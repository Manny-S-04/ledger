using Application.Accounts;
using Application.DbResult;
using Application.Providers;
using Domain.Accounts;
using Domain.Datatypes;
using Infrastructure.Account;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ledger.Tests.Repositories;

// Top up: RecordTopupAttempt, the callback (TopUpAccountAsync) and expiry, against ledger_test.
[Collection("Database")]
public class TopupRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db;
    private int _accountId;

    public TopupRepositoryTests(DatabaseFixture db) => _db = db;

    public async Task InitializeAsync()
    {
        await _db.ResetAsync();
        int userId = await _db.InsertUserAsync("owner@example.com");
        _accountId = await _db.InsertAccountAsync(userId, "GBP", 500);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AccountRepository Repository() => new(_db.DataSource, NullLogger<AccountRepository>.Instance);

    private AccountId Account => AccountId.TryCreate(_accountId)!.Value;
    private static Currency Gbp => Currency.TryCreate("GBP")!;

    private async Task<TransactionId> RecordAsync(decimal pounds)
    {
        DbResult<RecordTopupOutcome> result = await Repository().RecordTopupAttempt(Account, Amount.TryCreate(pounds)!.Value, Gbp);
        RecordTopupOutcome outcome = Assert.IsType<DbResult<RecordTopupOutcome>.Success>(result).Value;
        return Assert.IsType<RecordTopupOutcome.Recorded>(outcome).TransactionId;
    }

    private Task<long> BalanceAsync() => _db.ScalarAsync<long>($"SELECT balance FROM accounts WHERE id = {_accountId}");

    private Task<string?> StatusAsync(TransactionId id) =>
        _db.ScalarAsync<string>($"SELECT status FROM account_transactions WHERE id = {id.Value}");

    private static TopupCallbackOutcome OutcomeOf(DbResult<TopupCallbackOutcome> result) =>
        Assert.IsType<DbResult<TopupCallbackOutcome>.Success>(result).Value;

    // ---- RecordTopupAttempt ----

    [Fact]
    public async Task RecordTopupAttempt_ClosedAccount_ReturnsAccountClosedAndInsertsNothing()
    {
        await _db.ScalarAsync<int>($"UPDATE accounts SET balance = 0 WHERE id = {_accountId}");
        await Repository().CloseAccountAsync(_accountId, new HashSet<TransactionId>());

        DbResult<RecordTopupOutcome> result = await Repository().RecordTopupAttempt(Account, Amount.TryCreate(1m)!.Value, Gbp);

        Assert.IsType<RecordTopupOutcome.AccountClosed>(Assert.IsType<DbResult<RecordTopupOutcome>.Success>(result).Value);
        Assert.Equal(0L, await _db.ScalarAsync<long>($"SELECT count(*) FROM account_transactions WHERE account_id = {_accountId}"));
    }

    [Fact]
    public async Task SetTopupProviderReferenceAsync_StoresReferenceOnTheRow()
    {
        TransactionId id = await RecordAsync(1m);

        DbResult<bool> result = await Repository().SetTopupProviderReferenceAsync(id, "ch_42");

        Assert.IsType<DbResult<bool>.Success>(result);
        Assert.Equal("ch_42", await _db.ScalarAsync<string>($"SELECT provider_reference FROM account_transactions WHERE id = {id.Value}"));
    }


    [Fact]
    public async Task RecordTopupAttempt_InsertsPendingRowWithAmountAndReturnsItsId()
    {
        TransactionId id = await RecordAsync(12.34m);

        Assert.Equal("pending", await StatusAsync(id));
        Assert.Equal(1234L, await _db.ScalarAsync<long>($"SELECT amount FROM account_transactions WHERE id = {id.Value}"));
        Assert.Equal(_accountId, await _db.ScalarAsync<int>($"SELECT account_id FROM account_transactions WHERE id = {id.Value}"));
        Assert.Equal(500L, await BalanceAsync());
    }

    // ---- TopUpAccountAsync (callback) ----

    [Fact]
    public async Task TopUpAccountAsync_Succeeded_CreditsStoredAmountAndMarksDone()
    {
        TransactionId id = await RecordAsync(12.34m);

        DbResult<TopupCallbackOutcome> result = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded);

        Assert.Equal(TopupCallbackOutcome.Credited, OutcomeOf(result));
        Assert.Equal(1734L, await BalanceAsync());
        Assert.Equal("done", await StatusAsync(id));
    }

    [Fact]
    public async Task TopUpAccountAsync_SucceededTwice_SecondIsAlreadyProcessedAndCreditsOnce()
    {
        TransactionId id = await RecordAsync(10m);
        await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded);

        DbResult<TopupCallbackOutcome> second = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded);

        Assert.Equal(TopupCallbackOutcome.AlreadyProcessed, OutcomeOf(second));
        Assert.Equal(1500L, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_ConcurrentDuplicateCallbacks_CreditExactlyOnce()
    {
        TransactionId id = await RecordAsync(10m);

        DbResult<TopupCallbackOutcome>[] results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded)));

        Assert.Equal(1, results.Count(r => OutcomeOf(r) == TopupCallbackOutcome.Credited));
        Assert.Equal(9, results.Count(r => OutcomeOf(r) == TopupCallbackOutcome.AlreadyProcessed));
        Assert.Equal(1500L, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_Failed_MarksFailedWithoutCrediting()
    {
        TransactionId id = await RecordAsync(10m);

        DbResult<TopupCallbackOutcome> result = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Failed);

        Assert.Equal(TopupCallbackOutcome.MarkedFailed, OutcomeOf(result));
        Assert.Equal(500L, await BalanceAsync());
        Assert.Equal("failed", await StatusAsync(id));
    }

    [Fact]
    public async Task TopUpAccountAsync_FailedAfterSucceeded_SucceededIsFinal()
    {
        TransactionId id = await RecordAsync(10m);
        await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded);

        DbResult<TopupCallbackOutcome> late = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Failed);

        Assert.Equal(TopupCallbackOutcome.AlreadyProcessed, OutcomeOf(late));
        Assert.Equal("done", await StatusAsync(id));
        Assert.Equal(1500L, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_UnknownTransaction_ReturnsNotFound()
    {
        DbResult<TopupCallbackOutcome> result =
            await Repository().TopUpAccountAsync(TransactionId.TryCreate(9999)!.Value, ProviderCallbackStatus.Succeeded);

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<TopupCallbackOutcome>.Failure>(result).Error);
    }

    // ---- ExpireStalePendingAsync ----

    private Task BackdateAsync(TransactionId id, int minutes) =>
        _db.ScalarAsync<object>($"UPDATE account_transactions SET created_at = NOW() - INTERVAL '{minutes} minutes' WHERE id = {id.Value}");

    [Fact]
    public async Task ExpireStalePendingAsync_ExpiresOnlyPendingRowsOlderThanCutoff()
    {
        TransactionId old = await RecordAsync(1m);
        TransactionId fresh = await RecordAsync(2m);
        TransactionId oldButDone = await RecordAsync(3m);
        await Repository().TopUpAccountAsync(oldButDone, ProviderCallbackStatus.Succeeded);
        await BackdateAsync(old, 60);
        await BackdateAsync(oldButDone, 60);

        DbResult<int> result = await Repository().ExpireStalePendingAsync(DateTime.UtcNow.AddMinutes(-30));

        Assert.Equal(1, Assert.IsType<DbResult<int>.Success>(result).Value);
        Assert.Equal("expired", await StatusAsync(old));
        Assert.Equal("pending", await StatusAsync(fresh));
        Assert.Equal("done", await StatusAsync(oldButDone));
    }

    [Fact]
    public async Task TopUpAccountAsync_SucceededAfterExpired_ReturnsLateSuccessAndDoesNotCredit()
    {
        TransactionId id = await RecordAsync(10m);
        await BackdateAsync(id, 60);
        await Repository().ExpireStalePendingAsync(DateTime.UtcNow.AddMinutes(-30));

        DbResult<TopupCallbackOutcome> result = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded);

        Assert.Equal(TopupCallbackOutcome.LateSuccessOnExpired, OutcomeOf(result));
        Assert.Equal("expired", await StatusAsync(id));
        Assert.Equal(500L, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_FailedAfterExpired_ReturnsAlreadyProcessed()
    {
        TransactionId id = await RecordAsync(10m);
        await BackdateAsync(id, 60);
        await Repository().ExpireStalePendingAsync(DateTime.UtcNow.AddMinutes(-30));

        DbResult<TopupCallbackOutcome> result = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Failed);

        Assert.Equal(TopupCallbackOutcome.AlreadyProcessed, OutcomeOf(result));
        Assert.Equal("expired", await StatusAsync(id));
    }
}
