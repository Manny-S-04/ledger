using Application.DbResult;
using Domain.Users;
using Infrastructure.User;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ledger.Tests.Repositories;

// UserRepository against a real ledger_test database (T-017).
[Collection("Database")]
public class UserRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db;

    public UserRepositoryTests(DatabaseFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private UserRepository Repository() => new(_db.DataSource, NullLogger<UserRepository>.Instance);

    private static UserRepository UnreachableRepository() =>
        new(NpgsqlDataSource.Create(DatabaseFixture.UnreachableConnectionString), NullLogger<UserRepository>.Instance);

    private static Email Email(string value) => global::Domain.Users.Email.TryCreate(value)!;

    // ---- Create ----

    [Fact]
    public async Task Create_NewUser_ReturnsSuccessWithGeneratedIdAndPersistsRow()
    {
        DbResult<UserId> result = await Repository().Create(User.Create(Email("new@example.com"), "hash"));

        DbResult<UserId>.Success success = Assert.IsType<DbResult<UserId>.Success>(result);
        Assert.Equal(1, success.Value.Value);
        Assert.Equal("hash", await _db.ScalarAsync<string>("SELECT password_hash FROM users WHERE email = 'new@example.com'"));
    }

    [Fact]
    public async Task Create_EmailAlreadyExists_ReturnsConstraintViolation()
    {
        await _db.InsertUserAsync("taken@example.com");

        DbResult<UserId> result = await Repository().Create(User.Create(Email("taken@example.com"), "hash"));

        DbResult<UserId>.Failure failure = Assert.IsType<DbResult<UserId>.Failure>(result);
        Assert.Equal(DbError.ConstraintViolation, failure.Error);
    }

    [Fact]
    public async Task Create_SameEmailConcurrently_OnlyOneSucceeds()
    {
        UserRepository repository = Repository();
        DbResult<UserId>[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            repository.Create(User.Create(Email("race@example.com"), "hash"))));

        Assert.Single(results.OfType<DbResult<UserId>.Success>());
        Assert.All(results.OfType<DbResult<UserId>.Failure>(), f => Assert.Equal(DbError.ConstraintViolation, f.Error));
        Assert.Equal(1L, await _db.ScalarAsync<long>("SELECT count(*) FROM users WHERE email = 'race@example.com'"));
    }

    [Fact]
    public async Task Create_SqlLikeEmailContent_IsStoredLiterallyNotExecuted()
    {
        await Repository().Create(User.Create(Email("obrien+test@example.com"), "x'); DROP TABLE users; --"));

        Assert.Equal("x'); DROP TABLE users; --",
            await _db.ScalarAsync<string>("SELECT password_hash FROM users WHERE email = 'obrien+test@example.com'"));
    }

    [Fact]
    public async Task Create_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<UserId> result = await UnreachableRepository().Create(User.Create(Email("new@example.com"), "hash"));

        DbResult<UserId>.Failure failure = Assert.IsType<DbResult<UserId>.Failure>(result);
        Assert.Equal(DbError.Unavailable, failure.Error);
    }

    // ---- Exists ----

    [Fact]
    public async Task Exists_RegisteredEmail_ReturnsTrue()
    {
        await _db.InsertUserAsync("user@example.com");

        DbResult<bool> result = await Repository().Exists(Email("user@example.com"));

        Assert.True(Assert.IsType<DbResult<bool>.Success>(result).Value);
    }

    [Fact]
    public async Task Exists_UnknownEmail_ReturnsFalse()
    {
        DbResult<bool> result = await Repository().Exists(Email("nobody@example.com"));

        Assert.False(Assert.IsType<DbResult<bool>.Success>(result).Value);
    }

    [Fact]
    public async Task Exists_EmailGivenInDifferentCase_ReturnsTrue()
    {
        await _db.InsertUserAsync("user@example.com");

        DbResult<bool> result = await Repository().Exists(Email("USER@Example.com"));

        Assert.True(Assert.IsType<DbResult<bool>.Success>(result).Value);
    }

    [Fact]
    public async Task Exists_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<bool> result = await UnreachableRepository().Exists(Email("user@example.com"));

        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<bool>.Failure>(result).Error);
    }

    // ---- GetUserByEmail ----

    [Fact]
    public async Task GetUserByEmail_RegisteredEmail_ReturnsUserWithIdEmailAndHash()
    {
        int id = await _db.InsertUserAsync("user@example.com", "stored-hash");

        DbResult<User> result = await Repository().GetUserByEmail(Email("user@example.com"));

        User user = Assert.IsType<DbResult<User>.Success>(result).Value;
        Assert.Equal(id, user.Id);
        Assert.Equal("user@example.com", user.Email.EmailAddress);
        Assert.Equal("stored-hash", user.PasswordHash);
    }

    [Fact]
    public async Task GetUserByEmail_UnknownEmail_ReturnsNotFound()
    {
        DbResult<User> result = await Repository().GetUserByEmail(Email("nobody@example.com"));

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<User>.Failure>(result).Error);
    }

    [Fact]
    public async Task GetUserByEmail_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<User> result = await UnreachableRepository().GetUserByEmail(Email("user@example.com"));

        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<User>.Failure>(result).Error);
    }
}
