using Application.Accounts;
using Application.DbResult;
using Application.Providers;
using Domain.Accounts;
using Domain.Datatypes;
using Infrastructure.Account;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

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
        _accountId = await _db.InsertAccountAsync(userId, "GBP", 5m);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AccountRepository Repository() => new(_db.DataSource, NullLogger<AccountRepository>.Instance);

    private AccountId Account => AccountId.TryCreate(_accountId)!.Value;
    private static Currency Gbp => Currency.TryCreate("GBP")!;

    private async Task<TransactionId> RecordAsync(decimal pounds)
    {
        DbResult<TransactionId> result = await Repository().RecordTopupAttempt(Account, Amount.TryCreate(pounds)!.Value, Gbp);
        return Assert.IsType<DbResult<TransactionId>.Success>(result).Value;
    }

    private Task<decimal> BalanceAsync() => _db.ScalarAsync<decimal>($"SELECT balance FROM accounts WHERE id = {_accountId}");

    private Task<string?> StatusAsync(TransactionId id) =>
        _db.ScalarAsync<string>($"SELECT status FROM account_transactions WHERE id = {id.Value}");

    // A callback with a fresh eventId (T-034), so only the status logic is exercised.
    private Task<DbResult<TopupCallbackOutcome>> CallbackAsync(TransactionId id, ProviderCallbackStatus status) =>
        Repository().TopUpAccountAsync(id, status, $"evt_{Guid.NewGuid():N}", 1);

    private static TopupCallbackOutcome OutcomeOf(DbResult<TopupCallbackOutcome> result) =>
        Assert.IsType<DbResult<TopupCallbackOutcome>.Success>(result).Value;

    private static AccountRepository UnreachableRepository() =>
        new(NpgsqlDataSource.Create(DatabaseFixture.UnreachableConnectionString), NullLogger<AccountRepository>.Instance);

    // ---- T-032: exception mapping ----

    [Fact]
    public async Task RecordTopupAttempt_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<TransactionId> result = await UnreachableRepository().RecordTopupAttempt(Account, Amount.TryCreate(1m)!.Value, Gbp);
        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<TransactionId>.Failure>(result).Error);
    }

    [Fact]
    public async Task RecordTopupAttempt_NoSuchAccount_ReturnsUnknown()
    {
        AccountId missing = AccountId.TryCreate(_accountId + 1000)!.Value;
        DbResult<TransactionId> result = await Repository().RecordTopupAttempt(missing, Amount.TryCreate(1m)!.Value, Gbp);
        Assert.Equal(DbError.Unknown, Assert.IsType<DbResult<TransactionId>.Failure>(result).Error);
    }

    [Fact]
    public async Task TopUpAccountAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<TopupCallbackOutcome> result = await UnreachableRepository().TopUpAccountAsync(TransactionId.TryCreate(1)!.Value, ProviderCallbackStatus.Succeeded, "evt_unreachable", 1);
        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<TopupCallbackOutcome>.Failure>(result).Error);
    }

    [Fact]
    public async Task ExpireStalePendingAsync_DatabaseUnreachable_ReturnsUnavailable()
    {
        DbResult<int> result = await UnreachableRepository().ExpireStalePendingAsync(DateTime.UtcNow);
        Assert.Equal(DbError.Unavailable, Assert.IsType<DbResult<int>.Failure>(result).Error);
    }

    // ---- RecordTopupAttempt ----

    [Fact]
    public async Task RecordTopupAttempt_InsertsPendingRowWithAmountAndReturnsItsId()
    {
        TransactionId id = await RecordAsync(12.34m);

        Assert.Equal("pending", await StatusAsync(id));
        Assert.Equal(12.34m, await _db.ScalarAsync<decimal>($"SELECT amount FROM account_transactions WHERE id = {id.Value}"));
        Assert.Equal(_accountId, await _db.ScalarAsync<int>($"SELECT account_id FROM account_transactions WHERE id = {id.Value}"));
        Assert.Equal(5m, await BalanceAsync());
    }

    // ---- TopUpAccountAsync (callback) ----

    [Fact]
    public async Task TopUpAccountAsync_Succeeded_CreditsStoredAmountAndMarksDone()
    {
        TransactionId id = await RecordAsync(12.34m);

        DbResult<TopupCallbackOutcome> result = await CallbackAsync(id, ProviderCallbackStatus.Succeeded);

        Assert.Equal(TopupCallbackOutcome.Credited, OutcomeOf(result));
        Assert.Equal(17.34m, await BalanceAsync());
        Assert.Equal("done", await StatusAsync(id));
    }

    [Fact]
    public async Task TopUpAccountAsync_SucceededTwice_SecondIsAlreadyProcessedAndCreditsOnce()
    {
        TransactionId id = await RecordAsync(10m);
        await CallbackAsync(id, ProviderCallbackStatus.Succeeded);

        DbResult<TopupCallbackOutcome> second = await CallbackAsync(id, ProviderCallbackStatus.Succeeded);

        Assert.Equal(TopupCallbackOutcome.AlreadyProcessed, OutcomeOf(second));
        Assert.Equal(15m, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_ConcurrentDuplicateCallbacks_CreditExactlyOnce()
    {
        TransactionId id = await RecordAsync(10m);

        DbResult<TopupCallbackOutcome>[] results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => CallbackAsync(id, ProviderCallbackStatus.Succeeded)));

        Assert.Equal(1, results.Count(r => OutcomeOf(r) == TopupCallbackOutcome.Credited));
        Assert.Equal(9, results.Count(r => OutcomeOf(r) == TopupCallbackOutcome.AlreadyProcessed));
        Assert.Equal(15m, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_Failed_MarksFailedWithoutCrediting()
    {
        TransactionId id = await RecordAsync(10m);

        DbResult<TopupCallbackOutcome> result = await CallbackAsync(id, ProviderCallbackStatus.Failed);

        Assert.Equal(TopupCallbackOutcome.MarkedFailed, OutcomeOf(result));
        Assert.Equal(5m, await BalanceAsync());
        Assert.Equal("failed", await StatusAsync(id));
    }

    [Fact]
    public async Task TopUpAccountAsync_FailedAfterSucceeded_SucceededIsFinal()
    {
        TransactionId id = await RecordAsync(10m);
        await CallbackAsync(id, ProviderCallbackStatus.Succeeded);

        DbResult<TopupCallbackOutcome> late = await CallbackAsync(id, ProviderCallbackStatus.Failed);

        Assert.Equal(TopupCallbackOutcome.AlreadyProcessed, OutcomeOf(late));
        Assert.Equal("done", await StatusAsync(id));
        Assert.Equal(15m, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_UnknownTransaction_ReturnsNotFound()
    {
        DbResult<TopupCallbackOutcome> result =
            await CallbackAsync(TransactionId.TryCreate(9999)!.Value, ProviderCallbackStatus.Succeeded);

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
        await CallbackAsync(oldButDone, ProviderCallbackStatus.Succeeded);
        await BackdateAsync(old, 60);
        await BackdateAsync(oldButDone, 60);

        DbResult<int> result = await Repository().ExpireStalePendingAsync(DateTime.UtcNow.AddMinutes(-30));

        Assert.Equal(1, Assert.IsType<DbResult<int>.Success>(result).Value);
        Assert.Equal("expired", await StatusAsync(old));
        Assert.Equal("pending", await StatusAsync(fresh));
        Assert.Equal("done", await StatusAsync(oldButDone));
    }

    private Task<bool> WasLateAsync(TransactionId id) =>
        _db.ScalarAsync<bool>($"SELECT was_late FROM account_transactions WHERE id = {id.Value}");

    private Task<long> LateAuditEventsAsync(TransactionId id) =>
        _db.ScalarAsync<long>($"SELECT COUNT(*) FROM audit_events WHERE event_type = 'topup_credited_late' AND account_transaction_id = {id.Value}");

    // T-027: a late success on an expired top-up is credited once, ends as done, and the lateness is recorded.
    [Fact]
    public async Task TopUpAccountAsync_SucceededAfterExpired_CreditsOnceMarksDoneAndRecordsLateness()
    {
        TransactionId id = await RecordAsync(10m);
        await BackdateAsync(id, 60);
        await Repository().ExpireStalePendingAsync(DateTime.UtcNow.AddMinutes(-30));

        DbResult<TopupCallbackOutcome> result = await CallbackAsync(id, ProviderCallbackStatus.Succeeded);

        Assert.Equal(TopupCallbackOutcome.LateSuccessOnExpired, OutcomeOf(result));
        Assert.Equal("done", await StatusAsync(id));
        Assert.True(await WasLateAsync(id));
        Assert.Equal(1L, await LateAuditEventsAsync(id));
        Assert.Equal(15m, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_SucceededWhilePending_IsNotLate()
    {
        TransactionId id = await RecordAsync(10m);

        await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, $"evt_{Guid.NewGuid():N}", 1);

        Assert.False(await WasLateAsync(id));
        Assert.Equal(0L, await LateAuditEventsAsync(id));
    }

    // T-027: the first top-up expires, a second one is started, then both succeed: each credits its own amount once.
    [Fact]
    public async Task TopUpAccountAsync_ExpiredThenNewTopup_BothSucceed_EachCreditedOnce()
    {
        TransactionId first = await RecordAsync(10m);
        await BackdateAsync(first, 60);
        await Repository().ExpireStalePendingAsync(DateTime.UtcNow.AddMinutes(-30));
        TransactionId second = await RecordAsync(3m);

        await Repository().TopUpAccountAsync(second, ProviderCallbackStatus.Succeeded, $"evt_{Guid.NewGuid():N}", 1);
        await Repository().TopUpAccountAsync(first, ProviderCallbackStatus.Succeeded, $"evt_{Guid.NewGuid():N}", 1);

        Assert.Equal("done", await StatusAsync(first));
        Assert.Equal("done", await StatusAsync(second));
        Assert.Equal(18m, await BalanceAsync());
    }

    // T-027.2: a success racing the expiry job is credited exactly once, whichever commits first.
    [Fact]
    public async Task TopUpAccountAsync_SucceededRacingExpiryJob_CreditedExactlyOnce()
    {
        decimal expected = 5m;
        for (int i = 0; i < 25; i++)
        {
            TransactionId id = await RecordAsync(1m);
            await BackdateAsync(id, 60);
            expected += 1m;

            Task<DbResult<int>> expire = Repository().ExpireStalePendingAsync(DateTime.UtcNow.AddMinutes(-30));
            Task<DbResult<TopupCallbackOutcome>> callback = Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, $"evt_{Guid.NewGuid():N}", 1);
            await Task.WhenAll(expire, callback);

            TopupCallbackOutcome outcome = OutcomeOf(await callback);
            Assert.True(outcome is TopupCallbackOutcome.Credited or TopupCallbackOutcome.LateSuccessOnExpired, $"iteration {i}: {outcome}");
            Assert.Equal("done", await StatusAsync(id));
            Assert.Equal(outcome == TopupCallbackOutcome.LateSuccessOnExpired, await WasLateAsync(id));
            Assert.Equal(expected, await BalanceAsync());
        }
    }

    [Fact]
    public async Task TopUpAccountAsync_FailedAfterExpired_ReturnsAlreadyProcessed()
    {
        TransactionId id = await RecordAsync(10m);
        await BackdateAsync(id, 60);
        await Repository().ExpireStalePendingAsync(DateTime.UtcNow.AddMinutes(-30));

        DbResult<TopupCallbackOutcome> result = await CallbackAsync(id, ProviderCallbackStatus.Failed);

        Assert.Equal(TopupCallbackOutcome.AlreadyProcessed, OutcomeOf(result));
        Assert.Equal("expired", await StatusAsync(id));
    }

    // ---- T-034: repeated and stale callbacks ----

    [Fact]
    public async Task TopUpAccountAsync_SameEventIdTwice_SecondIsDuplicateAndCreditsOnce()
    {
        TransactionId id = await RecordAsync(10m);

        DbResult<TopupCallbackOutcome> first = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, "evt_a", 1);
        DbResult<TopupCallbackOutcome> second = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, "evt_a", 1);

        Assert.Equal(TopupCallbackOutcome.Credited, OutcomeOf(first));
        Assert.Equal(TopupCallbackOutcome.DuplicateEvent, OutcomeOf(second));
        Assert.Equal(15m, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_ConcurrentSameEventId_OneAppliedRestDuplicate()
    {
        TransactionId id = await RecordAsync(10m);

        DbResult<TopupCallbackOutcome>[] results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, "evt_same", 1)));

        Assert.Equal(1, results.Count(r => OutcomeOf(r) == TopupCallbackOutcome.Credited));
        Assert.Equal(9, results.Count(r => OutcomeOf(r) == TopupCallbackOutcome.DuplicateEvent));
        Assert.Equal(1L, await _db.ScalarAsync<long>("SELECT COUNT(*) FROM processed_callbacks WHERE event_id = 'evt_same'"));
        Assert.Equal(15m, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_LowerSequenceThanLastReceived_IsStaleAndChangesNothing()
    {
        TransactionId id = await RecordAsync(10m);
        await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Failed, "evt_2", 2);

        DbResult<TopupCallbackOutcome> older = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, "evt_1", 1);

        Assert.Equal(TopupCallbackOutcome.StaleSequence, OutcomeOf(older));
        Assert.Equal("failed", await StatusAsync(id));
        Assert.Equal(2L, await _db.ScalarAsync<long>($"SELECT last_sequence FROM account_transactions WHERE id = {id.Value}"));
        Assert.Equal(5m, await BalanceAsync());
    }

    [Fact]
    public async Task TopUpAccountAsync_HigherSequence_IsProcessedAndBecomesLastReceived()
    {
        TransactionId id = await RecordAsync(10m);
        await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, "evt_1", 1);

        DbResult<TopupCallbackOutcome> newer = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Failed, "evt_2", 2);

        Assert.Equal(TopupCallbackOutcome.AlreadyProcessed, OutcomeOf(newer));
        Assert.Equal(2L, await _db.ScalarAsync<long>($"SELECT last_sequence FROM account_transactions WHERE id = {id.Value}"));
        Assert.Equal("evt_2", await _db.ScalarAsync<string>($"SELECT last_event_id FROM account_transactions WHERE id = {id.Value}"));
    }

    // T-034.3: same Sequence, different eventId: processed, not treated as stale.
    [Fact]
    public async Task TopUpAccountAsync_SameSequenceDifferentEventId_IsProcessed()
    {
        TransactionId id = await RecordAsync(10m);
        await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, "evt_a", 1);

        DbResult<TopupCallbackOutcome> other = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, "evt_b", 1);

        Assert.Equal(TopupCallbackOutcome.AlreadyProcessed, OutcomeOf(other));
        Assert.Equal("evt_b", await _db.ScalarAsync<string>($"SELECT last_event_id FROM account_transactions WHERE id = {id.Value}"));
        Assert.Equal(15m, await BalanceAsync());
    }

    // T-034.6: a callback for an unknown top-up counts as processed.
    [Fact]
    public async Task TopUpAccountAsync_UnknownTransaction_CountsAsProcessed()
    {
        TransactionId unknown = TransactionId.TryCreate(9999)!.Value;

        DbResult<TopupCallbackOutcome> first = await Repository().TopUpAccountAsync(unknown, ProviderCallbackStatus.Succeeded, "evt_x", 1);
        DbResult<TopupCallbackOutcome> again = await Repository().TopUpAccountAsync(unknown, ProviderCallbackStatus.Succeeded, "evt_x", 1);

        Assert.Equal(DbError.NotFound, Assert.IsType<DbResult<TopupCallbackOutcome>.Failure>(first).Error);
        Assert.Equal(TopupCallbackOutcome.DuplicateEvent, OutcomeOf(again));
    }

    // T-034.6: a DB failure does not count as processed (nothing reaches processed_callbacks).
    [Fact]
    public async Task TopUpAccountAsync_DbUnreachable_RecordsNothing()
    {
        TransactionId id = await RecordAsync(10m);
        await using Npgsql.NpgsqlDataSource unreachable = Npgsql.NpgsqlDataSource.Create(DatabaseFixture.UnreachableConnectionString);
        AccountRepository repository = new(unreachable, NullLogger<AccountRepository>.Instance);

        DbResult<TopupCallbackOutcome> result = await repository.TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, "evt_down", 1);

        Assert.IsType<DbResult<TopupCallbackOutcome>.Failure>(result);
        Assert.Equal(0L, await _db.ScalarAsync<long>("SELECT COUNT(*) FROM processed_callbacks WHERE event_id = 'evt_down'"));
        DbResult<TopupCallbackOutcome> retry = await Repository().TopUpAccountAsync(id, ProviderCallbackStatus.Succeeded, "evt_down", 1);
        Assert.Equal(TopupCallbackOutcome.Credited, OutcomeOf(retry));
    }
}
