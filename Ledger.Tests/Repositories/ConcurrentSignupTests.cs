using Application.DbResult;
using Application.Interfaces;
using Application.Users;
using Domain.Users;
using Infrastructure.Auth;
using Infrastructure.User;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ledger.Tests.Repositories;

// T-030: two simultaneous sign-ups with one email. CreateUserHandler with the real UserRepository and
// ledger_test, so the unique constraint on users.email is what decides the loser.
[Collection("Database")]
public class ConcurrentSignupTests : IAsyncLifetime
{
    private const string RaceEmail = "race@example.com";
    private const string ValidPassword = "abc123";

    private readonly DatabaseFixture _db;

    public ConcurrentSignupTests(DatabaseFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static CreateUserHandler Handler(IUserRepository repository) =>
        new(repository, new Pbkdf2PasswordHasher(), NullLogger<CreateUserHandler>.Instance);

    private UserRepository Repository() => new(_db.DataSource, NullLogger<UserRepository>.Instance);

    // Deterministic loser: the winner's row is already in, but this sign-up's Exists check ran before
    // the winner committed (simulated by an Exists that answers false), so it reaches the INSERT.
    [Fact]
    public async Task Handle_ExistsCheckPassedButEmailInsertedMeanwhile_ReturnsAlreadyExists()
    {
        await _db.InsertUserAsync(RaceEmail);

        CreateUserResult result = await Handler(new StaleExistsRepository(Repository()))
            .Handle(new CreateUserCommand(RaceEmail, ValidPassword));

        Assert.IsType<CreateUserResult.AlreadyExists>(result);
        Assert.Equal(1L, await _db.ScalarAsync<long>($"SELECT count(*) FROM users WHERE email = '{RaceEmail}'"));
    }

    [Fact]
    public async Task Handle_SameEmailConcurrently_OneCreatedRestAlreadyExists()
    {
        CreateUserResult[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Handler(Repository()).Handle(new CreateUserCommand(RaceEmail, ValidPassword))));

        Assert.Single(results.OfType<CreateUserResult.Created>());
        Assert.All(results.Where(r => r is not CreateUserResult.Created), r => Assert.IsType<CreateUserResult.AlreadyExists>(r));
        Assert.Equal(1L, await _db.ScalarAsync<long>($"SELECT count(*) FROM users WHERE email = '{RaceEmail}'"));
    }

    private sealed class StaleExistsRepository : IUserRepository
    {
        private readonly IUserRepository _inner;

        public StaleExistsRepository(IUserRepository inner) => _inner = inner;

        public Task<DbResult<bool>> Exists(Email email) =>
            Task.FromResult<DbResult<bool>>(new DbResult<bool>.Success(false));

        public Task<DbResult<UserId>> Create(User user) => _inner.Create(user);

        public Task<DbResult<User>> GetUserByEmail(Email email) => _inner.GetUserByEmail(email);
    }
}
