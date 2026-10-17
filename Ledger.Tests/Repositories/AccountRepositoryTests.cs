using Application.DbResult;
using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;
using Infrastructure.Account;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ledger.Tests.Repositories;

// AccountRepository against a real ledger_test database (T-017).
[Collection("Database")]
public class AccountRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db;
    private int _userId;

    public AccountRepositoryTests(DatabaseFixture db) => _db = db;

    public async Task InitializeAsync()
    {
        await _db.ResetAsync();
        _userId = await _db.InsertUserAsync("owner@example.com");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AccountRepository Repository() => new(_db.DataSource, NullLogger<AccountRepository>.Instance);

    private static AccountRepository UnreachableRepository() =>
        new(NpgsqlDataSource.Create(DatabaseFixture.UnreachableConnectionString), NullLogger<AccountRepository>.Instance);

    private static Currency Currency(string code) => global::Domain.Datatypes.Currency.TryCreate(code)!;
    private static UserId UserIdOf(int id) => global::Domain.Users.UserId.TryCreate(id)!.Value;

    private static Account NewAccount(int userId, string currency, decimal balance = 0m) =>
        Account.Create(userId, Amount.Create(balance), Currency(currency), DateTime.UtcNow, DateTime.UtcNow);

    // ---- CreateAccountAsync ----

    [Fact]
    public async Task CreateAccountAsync_NewCurrency_ReturnsAccountWithIdAndPersistsZeroBalance()
    {
        DbResult<Account> result = await Repository().CreateAccountAsync(NewAccount(_userId, "GBP"));

        Account created = Assert.IsType<DbResult<Account>.Success>(result).Value;
        Assert.True(created.Id > 0);
        Assert.Equal(0m, await _db.ScalarAsync<decimal>($"SELECT balance FROM accounts WHERE id = {created.Id}"));
        Assert.Equal("GBP", await _db.ScalarAsync<string>($"SELECT currency FROM accounts WHERE id = {created.Id}"));
    }

    [Fact]
    public async Task CreateAccountAsync_SecondAccountInSameCurrency_ReturnsConstraintViolation()
    {
        await _db.InsertAccountAsync(_userId, "GBP");

        DbResult<Account> result = await Repository().CreateAccountAsync(NewAccount(_userId, "GBP"));

        Assert.Equal(DbError.ConstraintViolation, Assert.IsType<DbResult<Account>.Failure>(result).Error);
    }

    [Fact]
    public async Task CreateAccountAsync_SameCurrencyConcurrently_OnlyOneRowIsCreated()
    {
        AccountRepository repository = Repository();
        DbResult<Account>[] results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            repository.CreateAccountAsync(NewAccount(_userId, "CHF"))));

        Assert.Single(results.OfType<DbResult<Account>.Success>());
        Assert.Equal(1L, await _db.ScalarAsync<long>($"SELECT count(*) FROM accounts WHERE user_id = {_userId} AND currency = 'CHF'"));
    }

    [Fact]
    public async Task CreateAccountAsync_DifferentCurrencyForSameUser_Succeeds()
    {
        await _db.InsertAccountAsync(_userId, "GBP");

        DbResult<Account> result = await Repository().CreateAccountAsync(NewAccount(_userId, "EUR"));

        Assert.IsType<DbResult<Account>.Success>(result);
    }

    [Fact]
    public async Task CreateAccountAsync_UserDoesNotExist_ReturnsFailureUnknown()
    {
        // Foreign key violation. T-000.4 decided this surfaces as Failed, so the repository reports Unknown.
        DbResult<Account> result = await Repository().CreateAccountAsync(NewAccount(9999, "GBP"));

        Assert.Equal(DbError.Unknown, Assert.IsType<DbResult<Account>.Failure>(result).Error);
    }

    [Fact]
    public async Task CreateAccountAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<Account> result = await UnreachableRepository().CreateAccountAsync(NewAccount(_userId, "GBP"));

        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<Account>.Failure>(result).Error);
    }

    // ---- AccountExistsAsync ----

    [Fact]
    public async Task AccountExistsAsync_UserHasThatCurrency_ReturnsTrue()
    {
        await _db.InsertAccountAsync(_userId, "GBP");

        DbResult<bool> result = await Repository().AccountExistsAsync(UserIdOf(_userId), Currency("GBP"));

        Assert.True(Assert.IsType<DbResult<bool>.Success>(result).Value);
    }

    [Fact]
    public async Task AccountExistsAsync_UserHasOnlyOtherCurrency_ReturnsFalse()
    {
        await _db.InsertAccountAsync(_userId, "EUR");

        DbResult<bool> result = await Repository().AccountExistsAsync(UserIdOf(_userId), Currency("GBP"));

        Assert.False(Assert.IsType<DbResult<bool>.Success>(result).Value);
    }

    [Fact]
    public async Task AccountExistsAsync_OtherUserHasThatCurrency_ReturnsFalse()
    {
        int other = await _db.InsertUserAsync("other@example.com");
        await _db.InsertAccountAsync(other, "GBP");

        DbResult<bool> result = await Repository().AccountExistsAsync(UserIdOf(_userId), Currency("GBP"));

        Assert.False(Assert.IsType<DbResult<bool>.Success>(result).Value);
    }

    [Fact]
    public async Task AccountExistsAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<bool> result = await UnreachableRepository().AccountExistsAsync(UserIdOf(_userId), Currency("GBP"));

        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<bool>.Failure>(result).Error);
    }

    // ---- GetAccountsByUserIdAsync ----

    [Fact]
    public async Task GetAccountsByUserIdAsync_UserWithAccounts_ReturnsOnlyThatUsersAccountsWithBalances()
    {
        int gbp = await _db.InsertAccountAsync(_userId, "GBP", 12.34m);
        int eur = await _db.InsertAccountAsync(_userId, "EUR");
        int other = await _db.InsertUserAsync("other@example.com");
        await _db.InsertAccountAsync(other, "GBP", 99m);

        DbResult<IEnumerable<Account>> result = await Repository().GetAccountsByUserIdAsync(UserIdOf(_userId));

        List<Account> accounts = Assert.IsType<DbResult<IEnumerable<Account>>.Success>(result).Value.OrderBy(a => a.Id).ToList();
        Assert.Equal([gbp, eur], accounts.Select(a => a.Id));
        Assert.Equal(1234, accounts[0].Balance.Pence);
        Assert.Equal("GBP", accounts[0].Currency.Code);
    }

    [Fact]
    public async Task GetAccountsByUserIdAsync_UserWithNoAccounts_ReturnsEmptyList()
    {
        DbResult<IEnumerable<Account>> result = await Repository().GetAccountsByUserIdAsync(UserIdOf(_userId));

        Assert.Empty(Assert.IsType<DbResult<IEnumerable<Account>>.Success>(result).Value);
    }

    [Fact]
    public async Task GetAccountsByUserIdAsync_UnknownUser_ReturnsEmptyList()
    {
        DbResult<IEnumerable<Account>> result = await Repository().GetAccountsByUserIdAsync(UserIdOf(9999));

        Assert.Empty(Assert.IsType<DbResult<IEnumerable<Account>>.Success>(result).Value);
    }

    [Fact]
    public async Task GetAccountsByUserIdAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<IEnumerable<Account>> result = await UnreachableRepository().GetAccountsByUserIdAsync(UserIdOf(_userId));

        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<IEnumerable<Account>>.Failure>(result).Error);
    }

    // ---- GetAccountsByEmailAsync ----

    [Fact]
    public async Task GetAccountsByEmailAsync_UserWithAccounts_ReturnsTheirAccounts()
    {
        int gbp = await _db.InsertAccountAsync(_userId, "GBP");

        DbResult<IEnumerable<Account>> result = await Repository().GetAccountsByEmailAsync(Email.TryCreate("owner@example.com")!);

        Assert.Equal([gbp], Assert.IsType<DbResult<IEnumerable<Account>>.Success>(result).Value.Select(a => a.Id));
    }

    [Fact]
    public async Task GetAccountsByEmailAsync_UserWithNoAccounts_ReturnsEmptyList()
    {
        DbResult<IEnumerable<Account>> result = await Repository().GetAccountsByEmailAsync(Email.TryCreate("owner@example.com")!);

        Assert.Empty(Assert.IsType<DbResult<IEnumerable<Account>>.Success>(result).Value);
    }

    [Fact]
    public async Task GetAccountsByEmailAsync_UnknownEmail_ReturnsNotFound()
    {
        DbResult<IEnumerable<Account>> result = await Repository().GetAccountsByEmailAsync(Email.TryCreate("nobody@example.com")!);

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<IEnumerable<Account>>.Failure>(result).Error);
    }

    [Fact]
    public async Task GetAccountsByEmailAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<IEnumerable<Account>> result = await UnreachableRepository().GetAccountsByEmailAsync(Email.TryCreate("owner@example.com")!);

        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<IEnumerable<Account>>.Failure>(result).Error);
    }

    // ---- GetAccountOwnerAsync ----

    [Fact]
    public async Task GetAccountOwnerAsync_ExistingAccount_ReturnsOwnersUserId()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");

        DbResult<UserId> result = await Repository().GetAccountOwnerAsync(account);

        Assert.Equal(_userId, Assert.IsType<DbResult<UserId>.Success>(result).Value.Value);
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetAccountOwnerAsync_NoSuchAccount_ReturnsNotFound(int accountId)
    {
        DbResult<UserId> result = await Repository().GetAccountOwnerAsync(accountId);

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<UserId>.Failure>(result).Error);
    }

    [Fact]
    public async Task GetAccountOwnerAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<UserId> result = await UnreachableRepository().GetAccountOwnerAsync(1);

        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<UserId>.Failure>(result).Error);
    }

    // ---- DeleteAccountAsync (deletes only when balance is 0) ----

    [Fact]
    public async Task DeleteAccountAsync_ZeroBalance_ReturnsTrueAndRemovesRow()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");

        DbResult<bool> result = await Repository().DeleteAccountAsync(account);

        Assert.True(Assert.IsType<DbResult<bool>.Success>(result).Value);
        Assert.Equal(0L, await _db.ScalarAsync<long>($"SELECT count(*) FROM accounts WHERE id = {account}"));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(100)]
    public async Task DeleteAccountAsync_PositiveBalance_ReturnsFalseAndKeepsRowAndBalance(decimal balance)
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP", balance);

        DbResult<bool> result = await Repository().DeleteAccountAsync(account);

        Assert.False(Assert.IsType<DbResult<bool>.Success>(result).Value);
        Assert.Equal(balance, await _db.ScalarAsync<decimal>($"SELECT balance FROM accounts WHERE id = {account}"));
    }

    [Fact]
    public async Task DeleteAccountAsync_NoSuchAccount_ReturnsNotFound()
    {
        DbResult<bool> result = await Repository().DeleteAccountAsync(9999);

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<bool>.Failure>(result).Error);
    }

    [Fact]
    public async Task DeleteAccountAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<bool> result = await UnreachableRepository().DeleteAccountAsync(1);

        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<bool>.Failure>(result).Error);
    }

    // ---- UpdateAccountAsync (currently unused by any handler) ----

    [Fact]
    public async Task UpdateAccountAsync_ExistingAccount_ReturnsTrueAndPersistsBalance()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        Account updated = Account.Create(account, _userId, Amount.Create(5m), Currency("GBP"), DateTime.UtcNow, DateTime.UtcNow);

        DbResult<bool> result = await Repository().UpdateAccountAsync(updated);

        Assert.True(Assert.IsType<DbResult<bool>.Success>(result).Value);
        Assert.Equal(5m, await _db.ScalarAsync<decimal>($"SELECT balance FROM accounts WHERE id = {account}"));
    }

    [Fact]
    public async Task UpdateAccountAsync_NoSuchAccount_ReturnsNotFound()
    {
        Account missing = Account.Create(9999, _userId, Amount.Create(5m), Currency("GBP"), DateTime.UtcNow, DateTime.UtcNow);

        DbResult<bool> result = await Repository().UpdateAccountAsync(missing);

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<bool>.Failure>(result).Error);
    }
}
