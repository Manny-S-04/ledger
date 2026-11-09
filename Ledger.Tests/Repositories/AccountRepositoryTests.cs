using Application.Accounts;
using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;
using Infrastructure.Account;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
        Account.Create(userId, Amount.TryCreate(balance)!.Value, Currency(currency), DateTime.UtcNow, DateTime.UtcNow);

    // ---- CreateAccountAsync ----

    [Fact]
    public async Task CreateAccountAsync_NewCurrency_ReturnsAccountWithIdAndPersistsZeroBalance()
    {
        DbResult<Account> result = await Repository().CreateAccountAsync(NewAccount(_userId, "GBP"));

        Account created = Assert.IsType<DbResult<Account>.Success>(result).Value;
        Assert.True(created.Id > 0);
        Assert.Equal(0L, await _db.ScalarAsync<long>($"SELECT balance FROM accounts WHERE id = {created.Id}"));
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
        int gbp = await _db.InsertAccountAsync(_userId, "GBP", 1234);
        int eur = await _db.InsertAccountAsync(_userId, "EUR");
        int other = await _db.InsertUserAsync("other@example.com");
        await _db.InsertAccountAsync(other, "GBP", 9900);

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

    // ---- Closing (T-031): GetCloseBlockersAsync, CloseAccountAsync ----

    private static TransactionId Tx(int id) => TransactionId.TryCreate(id)!.Value;
    private static readonly IReadOnlySet<TransactionId> NoneCleared = new HashSet<TransactionId>();

    private static CloseAccountOutcome CloseOutcome(DbResult<CloseAccountOutcome> result) =>
        Assert.IsType<DbResult<CloseAccountOutcome>.Success>(result).Value;

    [Fact]
    public async Task CloseAccountAsync_ZeroBalance_ClosesAndKeepsRowAndHistory()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        await _db.InsertTopupAsync(account, "done");

        CloseAccountOutcome outcome = CloseOutcome(await Repository().CloseAccountAsync(account, NoneCleared));

        Assert.Equal(CloseAccountOutcome.Closed, outcome);
        Assert.Equal("closed", await _db.ScalarAsync<string>($"SELECT status FROM accounts WHERE id = {account}"));
        Assert.True(await _db.ScalarAsync<bool>($"SELECT closed_at IS NOT NULL FROM accounts WHERE id = {account}"));
        Assert.Equal(1L, await _db.ScalarAsync<long>($"SELECT count(*) FROM account_transactions WHERE account_id = {account}"));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(10000L)]
    public async Task CloseAccountAsync_PositiveBalance_ReturnsHasBalanceAndStaysOpen(long balancePence)
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP", balancePence);

        Assert.Equal(CloseAccountOutcome.HasBalance, CloseOutcome(await Repository().CloseAccountAsync(account, NoneCleared)));
        Assert.Equal("open", await _db.ScalarAsync<string>($"SELECT status FROM accounts WHERE id = {account}"));
        Assert.Equal(balancePence, await _db.ScalarAsync<long>($"SELECT balance FROM accounts WHERE id = {account}"));
    }

    [Fact]
    public async Task CloseAccountAsync_AlreadyClosed_ReturnsAlreadyClosedAndKeepsClosedAt()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        await Repository().CloseAccountAsync(account, NoneCleared);
        DateTime closedAt = await _db.ScalarAsync<DateTime>($"SELECT closed_at FROM accounts WHERE id = {account}");

        Assert.Equal(CloseAccountOutcome.AlreadyClosed, CloseOutcome(await Repository().CloseAccountAsync(account, NoneCleared)));
        Assert.Equal(closedAt, await _db.ScalarAsync<DateTime>($"SELECT closed_at FROM accounts WHERE id = {account}"));
    }

    [Fact]
    public async Task CloseAccountAsync_PendingTopup_ReturnsPendingTopupsAndStaysOpen()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        await _db.InsertTopupAsync(account, "pending", "ch_1");

        Assert.Equal(CloseAccountOutcome.PendingTopups, CloseOutcome(await Repository().CloseAccountAsync(account, NoneCleared)));
        Assert.Equal("open", await _db.ScalarAsync<string>($"SELECT status FROM accounts WHERE id = {account}"));
    }

    [Fact]
    public async Task CloseAccountAsync_ExpiredTopupNotCleared_ReturnsPendingTopups()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        int cleared = await _db.InsertTopupAsync(account, "expired", "ch_1");
        await _db.InsertTopupAsync(account, "expired", "ch_2");

        HashSet<TransactionId> clearedSet = new HashSet<TransactionId> { Tx(cleared) };
        Assert.Equal(CloseAccountOutcome.PendingTopups, CloseOutcome(await Repository().CloseAccountAsync(account, clearedSet)));
    }

    [Fact]
    public async Task CloseAccountAsync_AllExpiredTopupsCleared_Closes()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        int first = await _db.InsertTopupAsync(account, "expired", "ch_1");
        int second = await _db.InsertTopupAsync(account, "expired", "ch_2");

        HashSet<TransactionId> clearedSet = new HashSet<TransactionId> { Tx(first), Tx(second) };
        Assert.Equal(CloseAccountOutcome.Closed, CloseOutcome(await Repository().CloseAccountAsync(account, clearedSet)));
    }

    [Fact]
    public async Task CloseAccountAsync_NoSuchAccount_ReturnsNotFound()
    {
        DbResult<CloseAccountOutcome> result = await Repository().CloseAccountAsync(9999, NoneCleared);

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<CloseAccountOutcome>.Failure>(result).Error);
    }

    [Fact]
    public async Task CloseAccountAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<CloseAccountOutcome> result = await UnreachableRepository().CloseAccountAsync(1, NoneCleared);

        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<CloseAccountOutcome>.Failure>(result).Error);
    }

    [Fact]
    public async Task GetCloseBlockersAsync_ReportsPendingAndExpiredWithReferences()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        await _db.InsertTopupAsync(account, "pending", "ch_1");
        int expiredWithRef = await _db.InsertTopupAsync(account, "expired", "ch_2");
        int expiredNoRef = await _db.InsertTopupAsync(account, "expired");
        await _db.InsertTopupAsync(account, "done", "ch_4");
        await _db.InsertTopupAsync(account, "failed", "ch_5");

        CloseBlockers blockers = Assert.IsType<DbResult<CloseBlockers>.Success>(await Repository().GetCloseBlockersAsync(account)).Value;

        Assert.False(blockers.AlreadyClosed);
        Assert.True(blockers.HasPending);
        Assert.Equal(2, blockers.Expired.Count);
        Assert.Contains(new ExpiredTopup(Tx(expiredWithRef), "ch_2"), blockers.Expired);
        Assert.Contains(new ExpiredTopup(Tx(expiredNoRef), null), blockers.Expired);
    }

    [Fact]
    public async Task GetCloseBlockersAsync_ClosedAccount_ReportsAlreadyClosed()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        await Repository().CloseAccountAsync(account, NoneCleared);

        CloseBlockers blockers = Assert.IsType<DbResult<CloseBlockers>.Success>(await Repository().GetCloseBlockersAsync(account)).Value;

        Assert.True(blockers.AlreadyClosed);
    }

    [Fact]
    public async Task GetCloseBlockersAsync_NoSuchAccount_ReturnsNotFound()
    {
        DbResult<CloseBlockers> result = await Repository().GetCloseBlockersAsync(9999);

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<CloseBlockers>.Failure>(result).Error);
    }

    [Fact]
    public async Task ClosedAccount_SameCurrencyCanBeOpenedAgain()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        await Repository().CloseAccountAsync(account, NoneCleared);

        DbResult<bool> exists = await Repository().AccountExistsAsync(UserIdOf(_userId), Currency("GBP"));
        DbResult<Account> created = await Repository().CreateAccountAsync(NewAccount(_userId, "GBP"));

        Assert.False(Assert.IsType<DbResult<bool>.Success>(exists).Value);
        Assert.IsType<DbResult<Account>.Success>(created);
    }

    [Fact]
    public async Task GetAccountsByUserIdAsync_ClosedAccountListedWithStatusAndClosedAt()
    {
        int closed = await _db.InsertAccountAsync(_userId, "GBP");
        int open = await _db.InsertAccountAsync(_userId, "EUR");
        await Repository().CloseAccountAsync(closed, NoneCleared);

        DbResult<IEnumerable<Account>> result = await Repository().GetAccountsByUserIdAsync(UserIdOf(_userId));

        List<Account> accounts = Assert.IsType<DbResult<IEnumerable<Account>>.Success>(result).Value.ToList();
        Account closedAccount = accounts.Single(a => a.Id == closed);
        Account openAccount = accounts.Single(a => a.Id == open);
        Assert.Equal(AccountStatus.Closed, closedAccount.Status);
        Assert.NotNull(closedAccount.ClosedAt);
        Assert.Equal(AccountStatus.Open, openAccount.Status);
        Assert.Null(openAccount.ClosedAt);
    }

    // T-031.12: close and top-up start race on the same account; exactly one of them wins.
    [Fact]
    public async Task CloseAndTopupStartConcurrently_EitherClosedWithNoTopupOrOpenWithPendingTopup()
    {
        Currency gbp = Currency("GBP");
        for (int i = 0; i < 25; i++)
        {
            await _db.ResetAsync();
            int user = await _db.InsertUserAsync($"race{i}@example.com");
            int account = await _db.InsertAccountAsync(user, "GBP");
            AccountRepository repository = Repository();

            Task<DbResult<CloseAccountOutcome>> close = repository.CloseAccountAsync(account, NoneCleared);
            Task<DbResult<RecordTopupOutcome>> topup = repository.RecordTopupAttempt(AccountId.TryCreate(account)!.Value, Amount.TryCreate(1m)!.Value, gbp);
            await Task.WhenAll(close, topup);

            CloseAccountOutcome closeOutcome = CloseOutcome(await close);
            RecordTopupOutcome topupOutcome = Assert.IsType<DbResult<RecordTopupOutcome>.Success>(await topup).Value;
            long pending = await _db.ScalarAsync<long>($"SELECT count(*) FROM account_transactions WHERE account_id = {account}");
            if (closeOutcome == CloseAccountOutcome.Closed)
            {
                Assert.IsType<RecordTopupOutcome.AccountClosed>(topupOutcome);
                Assert.Equal(0L, pending);
            }
            else
            {
                Assert.Equal(CloseAccountOutcome.PendingTopups, closeOutcome);
                Assert.IsType<RecordTopupOutcome.Recorded>(topupOutcome);
                Assert.Equal(1L, pending);
            }
        }
    }

    // ---- T-032: exception mapping ----

    [Fact]
    public async Task UpdateAccountAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<bool> result = await UnreachableRepository().UpdateAccountAsync(NewAccount(_userId, "GBP"));
        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<bool>.Failure>(result).Error);
    }

    [Fact]
    public async Task UpdateAccountBalance_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<bool> result = await UnreachableRepository().UpdateAccountBalance(NewAccount(_userId, "GBP"));
        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<bool>.Failure>(result).Error);
    }

    // ---- UpdateAccountAsync (currently unused by any handler) ----

    [Fact]
    public async Task UpdateAccountAsync_ExistingAccount_ReturnsTrueAndPersistsBalance()
    {
        int account = await _db.InsertAccountAsync(_userId, "GBP");
        Account updated = Account.Create(account, _userId, Amount.TryCreate(5m)!.Value, Currency("GBP"), DateTime.UtcNow, DateTime.UtcNow);

        DbResult<bool> result = await Repository().UpdateAccountAsync(updated);

        Assert.True(Assert.IsType<DbResult<bool>.Success>(result).Value);
        Assert.Equal(5m, await _db.ScalarAsync<decimal>($"SELECT balance FROM accounts WHERE id = {account}"));
    }

    [Fact]
    public async Task UpdateAccountAsync_NoSuchAccount_ReturnsNotFound()
    {
        Account missing = Account.Create(9999, _userId, Amount.TryCreate(5m)!.Value, Currency("GBP"), DateTime.UtcNow, DateTime.UtcNow);

        DbResult<bool> result = await Repository().UpdateAccountAsync(missing);

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<bool>.Failure>(result).Error);
    }
}
