using Application.Accounts;
using Npgsql;
using Application.Logging;
using Microsoft.Extensions.Logging;
using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Domain.Datatypes;
using Application.Providers;
using Domain.Accounts;

namespace Infrastructure.Account;

public sealed class AccountRepository : IAccountRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<AccountRepository> _logger; 

    public AccountRepository(NpgsqlDataSource dataSource, ILogger<AccountRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<DbResult<Domain.Accounts.Account>> CreateAccountAsync(Domain.Accounts.Account account, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Inserting account for userId={account.UserId}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO accounts (user_id, balance, currency, created_at, updated_at) VALUES (@userId, @balance, @currency, @createdAt, @updatedAt) RETURNING id", conn);
        
            cmd.Parameters.AddWithValue("@userId", account.UserId);
            cmd.Parameters.AddWithValue("@balance", account.Balance.Pence);
            cmd.Parameters.AddWithValue("@currency", account.Currency.Code);
            cmd.Parameters.AddWithValue("@createdAt", account.CreatedAt);
            cmd.Parameters.AddWithValue("@updatedAt", account.UpdatedAt);
        
            object? id = await cmd.ExecuteScalarAsync(cancellationToken);
        
            if (id == null || id == DBNull.Value)
            {
                _logger.Info("result=Failure(Unknown)");
                return new DbResult<Domain.Accounts.Account>.Failure(DbError.Unknown);
            }
            
            var createdAccount = Domain.Accounts.Account.Create(
                (int)id, 
                account.UserId, 
                account.Balance, 
                account.Currency, 
                account.CreatedAt, 
                account.UpdatedAt);
            
            _logger.Info("result=Success");
            return new DbResult<Domain.Accounts.Account>.Success(createdAccount);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // unique_violation
        {
            _logger.Warn($"unique violation: {ex.ConstraintName}");
            return new DbResult<Domain.Accounts.Account>.Failure(DbError.ConstraintViolation);
        }
        catch (Exception ex) when (NpgsqlErrors.IsUnavailable(ex))
        {
            _logger.Error(ex, "unavailable");
            return new DbResult<Domain.Accounts.Account>.Failure(DbError.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<Domain.Accounts.Account>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<bool>> AccountExistsAsync(UserId userId, Domain.Datatypes.Currency currency, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Checking for an existing account userId={userId.Value} currency={currency.Code}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM accounts WHERE user_id = @userId AND currency = @currency AND status = 'open')", conn);
            cmd.Parameters.AddWithValue("@userId", userId.Value);
            cmd.Parameters.AddWithValue("@currency", currency.Code);

            bool exists = await cmd.ExecuteScalarAsync(cancellationToken) is true;
            _logger.Info($"result=Success exists={exists}");
            return new DbResult<bool>.Success(exists);
        }
        catch (Exception ex) when (NpgsqlErrors.IsUnavailable(ex))
        {
            _logger.Error(ex, "unavailable");
            return new DbResult<bool>.Failure(DbError.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<bool>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    private static AccountStatus? ParseStatus(string status) => status switch
    {
        "open" => AccountStatus.Open,
        "closed" => AccountStatus.Closed,
        _ => null
    };

    public async Task<DbResult<CloseBlockers>> GetCloseBlockersAsync(int accountId, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Reading close blockers for accountId={accountId}");
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand statusCmd = new NpgsqlCommand("SELECT status FROM accounts WHERE id = @id", conn);
            statusCmd.Parameters.AddWithValue("@id", accountId);
            if (await statusCmd.ExecuteScalarAsync(cancellationToken) is not string status)
            {
                _logger.Info("result=Failure(NotFound)");
                return new DbResult<CloseBlockers>.Failure(DbError.NotFound);
            }

            bool hasPending = false;
            List<ExpiredTopup> expired = new List<ExpiredTopup>();
            await using NpgsqlCommand topupCmd = new NpgsqlCommand(
                "SELECT id, status, provider_reference FROM account_transactions WHERE account_id = @id AND status IN ('pending', 'expired')", conn);
            topupCmd.Parameters.AddWithValue("@id", accountId);
            await using NpgsqlDataReader reader = await topupCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(1) == "pending")
                {
                    hasPending = true;
                    continue;
                }

                if (TransactionId.TryCreate(reader.GetInt32(0)) is not TransactionId id)
                {
                    _logger.Warn($"stored transaction id {reader.GetInt32(0)} is invalid");
                    return new DbResult<CloseBlockers>.Failure(DbError.Unknown);
                }
                expired.Add(new ExpiredTopup(id, reader.IsDBNull(2) ? null : reader.GetString(2)));
            }

            _logger.Info($"result=Success status={status} hasPending={hasPending} expired={expired.Count}");
            return new DbResult<CloseBlockers>.Success(new CloseBlockers(status == "closed", hasPending, expired));
        }
        catch (Exception ex) when (NpgsqlErrors.IsUnavailable(ex))
        {
            _logger.Error(ex, "unavailable");
            return new DbResult<CloseBlockers>.Failure(DbError.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<CloseBlockers>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    // T-031.12: the account row is locked for the whole check-and-close, so a top-up start (which takes the
    // same lock) either sees the account closed or is seen here as pending.
    public async Task<DbResult<CloseAccountOutcome>> CloseAccountAsync(int accountId, IReadOnlySet<TransactionId> clearedExpired, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Closing accountId={accountId} clearedExpired={clearedExpired.Count}");
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

            string status;
            long balance;
            await using (NpgsqlCommand lockCmd = new NpgsqlCommand(
                "SELECT status, balance FROM accounts WHERE id = @id FOR UPDATE", conn, tx))
            {
                lockCmd.Parameters.AddWithValue("@id", accountId);
                await using NpgsqlDataReader reader = await lockCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    _logger.Info("result=Failure(NotFound)");
                    return new DbResult<CloseAccountOutcome>.Failure(DbError.NotFound);
                }
                status = reader.GetString(0);
                balance = reader.GetInt64(1);
            }

            if (status == "closed")
            {
                _logger.Info("result=AlreadyClosed");
                return new DbResult<CloseAccountOutcome>.Success(CloseAccountOutcome.AlreadyClosed);
            }

            if (balance != 0)
            {
                _logger.Info("result=HasBalance");
                return new DbResult<CloseAccountOutcome>.Success(CloseAccountOutcome.HasBalance);
            }

            await using (NpgsqlCommand topupCmd = new NpgsqlCommand(
                "SELECT id, status FROM account_transactions WHERE account_id = @id AND status IN ('pending', 'expired')", conn, tx))
            {
                topupCmd.Parameters.AddWithValue("@id", accountId);
                await using NpgsqlDataReader reader = await topupCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    bool blocks = reader.GetString(1) == "pending"
                        || TransactionId.TryCreate(reader.GetInt32(0)) is not TransactionId id
                        || !clearedExpired.Contains(id);
                    if (blocks)
                    {
                        _logger.Info($"result=PendingTopups transactionId={reader.GetInt32(0)}");
                        return new DbResult<CloseAccountOutcome>.Success(CloseAccountOutcome.PendingTopups);
                    }
                }
            }

            await using (NpgsqlCommand closeCmd = new NpgsqlCommand(
                "UPDATE accounts SET status = 'closed', closed_at = @now, updated_at = @now WHERE id = @id", conn, tx))
            {
                closeCmd.Parameters.AddWithValue("@id", accountId);
                closeCmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                await closeCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            _logger.Info("result=Closed");
            return new DbResult<CloseAccountOutcome>.Success(CloseAccountOutcome.Closed);
        }
        catch (Exception ex) when (NpgsqlErrors.IsUnavailable(ex))
        {
            _logger.Error(ex, "unavailable");
            return new DbResult<CloseAccountOutcome>.Failure(DbError.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<CloseAccountOutcome>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Getting account owner for accountId={accountId}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT user_id FROM accounts WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", accountId);

            object? ownerObj = await cmd.ExecuteScalarAsync(cancellationToken);
            if (ownerObj is not int owner)
            {
                _logger.Info("result=Failure(NotFound)");
                return new DbResult<UserId>.Failure(DbError.NotFound);
            }

            UserId? ownerId = UserId.TryCreate(owner);
            if (ownerId is null)
            {
                _logger.Info("result=Failure(Unknown)");
                return new DbResult<UserId>.Failure(DbError.Unknown);
            }

            _logger.Info("result=Success");
            return new DbResult<UserId>.Success(ownerId.Value);
        }
        catch (Exception ex) when (NpgsqlErrors.IsUnavailable(ex))
        {
            _logger.Error(ex, "db unavailable");
            return new DbResult<UserId>.Failure(DbError.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<UserId>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<IEnumerable<Domain.Accounts.Account>>> GetAccountsByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Loading accounts for userId={userId.Value}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT id, user_id, balance, currency, created_at, updated_at, status, closed_at FROM accounts WHERE user_id = @userId", conn);
        
            cmd.Parameters.AddWithValue("@userId", userId.Value);
        
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var accounts = new List<Domain.Accounts.Account>();
        
            while (await reader.ReadAsync(cancellationToken))
            {
                Domain.Datatypes.Currency? currency = Domain.Datatypes.Currency.TryCreate(reader.GetString(3));
                if (currency is null)
                {
                    _logger.Warn($"stored currency for account {reader.GetInt32(0)} is invalid");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
                }

                Amount? amount = Amount.TryCreate(reader.GetInt64(2) / 100m);
                if(amount is null) 
                {
                    _logger.Warn("Failed to generate correct amount");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown); 
                }
                    
                AccountStatus? status = ParseStatus(reader.GetString(6));
                if (status is null)
                {
                    _logger.Warn($"stored status for account {reader.GetInt32(0)} is invalid");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
                }

                var account = Domain.Accounts.Account.Create(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    amount.Value,
                    currency,
                    reader.GetDateTime(4),
                    reader.GetDateTime(5),
                    status.Value,
                    reader.IsDBNull(7) ? null : reader.GetDateTime(7)
                );
            
                accounts.Add(account);
            }
        
            _logger.Info($"result=Success count={accounts.Count}");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Success(accounts);
        }
        catch (Exception ex) when (NpgsqlErrors.IsUnavailable(ex))
        {
            _logger.Error(ex, "unavailable");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<IEnumerable<Domain.Accounts.Account>>> GetAccountsByEmailAsync(Email email, CancellationToken cancellationToken = default)
    {
        _logger.Info("Loading accounts by email");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

            await using var userCmd = new NpgsqlCommand(
                    "SELECT id FROM users WHERE email = @email", conn);
            userCmd.Parameters.AddWithValue("@email", email.EmailAddress);

            var userIdObj = await userCmd.ExecuteScalarAsync(cancellationToken);
            if (userIdObj == null || userIdObj == DBNull.Value)
            {
                _logger.Info("result=Failure(NotFound)");
                return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.NotFound);
            }

            var userId = (int)userIdObj;

            await using var accountCmd = new NpgsqlCommand(
                    "SELECT id, user_id, balance, currency, created_at, updated_at, status, closed_at FROM accounts WHERE user_id = @userId", conn);
            accountCmd.Parameters.AddWithValue("@userId", userId);

            await using var reader = await accountCmd.ExecuteReaderAsync(cancellationToken);
            var accounts = new List<Domain.Accounts.Account>();

            while (await reader.ReadAsync(cancellationToken))
            {
                Domain.Datatypes.Currency? currency = Domain.Datatypes.Currency.TryCreate(reader.GetString(3));
                if (currency is null)
                {
                    _logger.Warn($"stored currency for account {reader.GetInt32(0)} is invalid");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
                }

                Amount? amount = Amount.TryCreate(reader.GetInt64(2) / 100m);
                if(amount is null) 
                {
                    _logger.Warn("Failed to generate correct amount");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown); 
                }


                AccountStatus? status = ParseStatus(reader.GetString(6));
                if (status is null)
                {
                    _logger.Warn($"stored status for account {reader.GetInt32(0)} is invalid");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
                }

                var account = Domain.Accounts.Account.Create(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    amount.Value,
                    currency,
                    reader.GetDateTime(4),
                    reader.GetDateTime(5),
                    status.Value,
                    reader.IsDBNull(7) ? null : reader.GetDateTime(7)
                );
                accounts.Add(account);
            }
            _logger.Info($"result=Success count={accounts.Count}");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Success(accounts);
        }
        catch (Exception ex) when (NpgsqlErrors.IsUnavailable(ex))
        {
            _logger.Error(ex, "unavailable");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<bool>> UpdateAccountAsync(Domain.Accounts.Account account, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Updating accountId={account.Id}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "UPDATE accounts SET balance = @balance, currency = @currency, updated_at = @updatedAt WHERE id = @id", conn);
    
            cmd.Parameters.AddWithValue("@id", account.Id);
            cmd.Parameters.AddWithValue("@balance", account.Balance);
            cmd.Parameters.AddWithValue("@currency", account.Currency.Code);
            cmd.Parameters.AddWithValue("@updatedAt", account.UpdatedAt);
    
            var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
    
            if (rowsAffected > 0)
            {
                _logger.Info("result=Success");
                return new DbResult<bool>.Success(true);
            }
            else
            {
                _logger.Info("result=Failure(Unknown)");
                return new DbResult<bool>.Failure(DbError.Unknown);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<bool>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    // Applies a provider callback to the transaction it refers to. A pending row takes either outcome; an expired
    // row takes only Succeeded (T-027: a late success is still credited, marked done, was_late set and an audit
    // event written). On Succeeded the amount stored on that row is credited, in the same DB transaction.
    // T-034: the eventId is recorded in processed_callbacks (unique) and the top-up row is locked, in that same
    // transaction. A repeated eventId or a Sequence lower than the last one received changes nothing. A callback for
    // an unknown top-up still counts as processed; a DB failure rolls everything back, so it does not.
    // Racing the expiry job: the row is locked FOR UPDATE, so the status read here is the one the settle acts on.
    public async Task<DbResult<TopupCallbackOutcome>> TopUpAccountAsync(TransactionId transactionId, ProviderCallbackStatus status, string eventId, long sequence, CancellationToken cancellationToken = default)
    {
        _logger.Info($"TopUpAccountAsync transactionId={transactionId.Value}, status={status}, eventId={eventId}, sequence={sequence}");
        string newStatus = status == ProviderCallbackStatus.Succeeded ? "done" : "failed";
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);
            DateTime now = DateTime.UtcNow;

            // A concurrent copy of the same eventId waits here on the unique key until this transaction ends.
            await using (NpgsqlCommand record = new NpgsqlCommand(
                "INSERT INTO processed_callbacks (event_id, account_transaction_id, sequence, processed_at) VALUES (@eventId, @id, @sequence, @processedAt) ON CONFLICT (event_id) DO NOTHING", conn, tx))
            {
                record.Parameters.AddWithValue("@eventId", eventId);
                record.Parameters.AddWithValue("@id", transactionId.Value);
                record.Parameters.AddWithValue("@sequence", sequence);
                record.Parameters.AddWithValue("@processedAt", now);
                if (await record.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    await tx.RollbackAsync(cancellationToken);
                    _logger.Warn($"result=DuplicateEvent eventId={eventId}");
                    return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.DuplicateEvent);
                }
            }

            string? currentStatus = null;
            long? lastSequence = null;
            await using (NpgsqlCommand lockRow = new NpgsqlCommand(
                "SELECT status, last_sequence FROM account_transactions WHERE id = @id FOR UPDATE", conn, tx))
            {
                lockRow.Parameters.AddWithValue("@id", transactionId.Value);
                await using NpgsqlDataReader reader = await lockRow.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    currentStatus = reader.GetString(0);
                    lastSequence = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                }
            }

            if (currentStatus is null)
            {
                await tx.CommitAsync(cancellationToken);
                _logger.Warn($"result=Failure(NotFound) transactionId={transactionId.Value}");
                return new DbResult<TopupCallbackOutcome>.Failure(DbError.NotFound);
            }

            if (lastSequence is long last && sequence < last)
            {
                await tx.CommitAsync(cancellationToken);
                _logger.Warn($"result=StaleSequence transactionId={transactionId.Value} sequence={sequence} lastSequence={last}");
                return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.StaleSequence);
            }

            await using (NpgsqlCommand received = new NpgsqlCommand(
                "UPDATE account_transactions SET last_sequence = @sequence, last_event_id = @eventId WHERE id = @id", conn, tx))
            {
                received.Parameters.AddWithValue("@id", transactionId.Value);
                received.Parameters.AddWithValue("@sequence", sequence);
                received.Parameters.AddWithValue("@eventId", eventId);
                await received.ExecuteNonQueryAsync(cancellationToken);
            }

            bool wasLate = currentStatus == "expired" && status == ProviderCallbackStatus.Succeeded;
            if (currentStatus != "pending" && !wasLate)
            {
                await tx.CommitAsync(cancellationToken);
                _logger.Warn($"result=AlreadyProcessed transactionId={transactionId.Value} currentStatus={currentStatus} callbackStatus={status}");
                return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.AlreadyProcessed);
            }

            int accountId;
            long storedAmount;
            await using (NpgsqlCommand settle = new NpgsqlCommand(
                "UPDATE account_transactions SET status = @status, was_late = @wasLate, updated_at = @updatedAt WHERE id = @id RETURNING account_id, amount", conn, tx))
            {
                settle.Parameters.AddWithValue("@id", transactionId.Value);
                settle.Parameters.AddWithValue("@status", newStatus);
                settle.Parameters.AddWithValue("@updatedAt", now);
                settle.Parameters.AddWithValue("@wasLate", wasLate);
                await using NpgsqlDataReader reader = await settle.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                accountId = reader.GetInt32(0);
                storedAmount = reader.GetInt64(1);
            }

            if (status == ProviderCallbackStatus.Failed)
            {
                await tx.CommitAsync(cancellationToken);
                _logger.Info("result=MarkedFailed");
                return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.MarkedFailed);
            }

            await using NpgsqlCommand credit = new NpgsqlCommand(
                "UPDATE accounts SET balance = balance + @amount, updated_at = @updatedAt WHERE id = @accountId", conn, tx);
            credit.Parameters.AddWithValue("@accountId", accountId);
            credit.Parameters.AddWithValue("@amount", storedAmount);
            credit.Parameters.AddWithValue("@updatedAt", now);
            int credited = await credit.ExecuteNonQueryAsync(cancellationToken);
            if (credited != 1)
            {
                await tx.RollbackAsync(cancellationToken);
                _logger.Info("result=Failure(Unknown) - account row not updated");
                return new DbResult<TopupCallbackOutcome>.Failure(DbError.Unknown);
            }

            if (wasLate)
            {
                await using NpgsqlCommand audit = new NpgsqlCommand(
                    "INSERT INTO audit_events (event_type, account_transaction_id, created_at) VALUES (@eventType, @transactionId, @createdAt)", conn, tx);
                audit.Parameters.AddWithValue("@eventType", "topup_credited_late");
                audit.Parameters.AddWithValue("@transactionId", transactionId.Value);
                audit.Parameters.AddWithValue("@createdAt", now);
                await audit.ExecuteNonQueryAsync(cancellationToken);

                await tx.CommitAsync(cancellationToken);
                _logger.Warn($"result=LateSuccessOnExpired transactionId={transactionId.Value} - credited after the top up expired");
                return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.LateSuccessOnExpired);
            }

            await tx.CommitAsync(cancellationToken);
            _logger.Info("result=Credited");
            return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.Credited);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<TopupCallbackOutcome>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<int>> ExpireStalePendingAsync(DateTime createdBefore, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Expiring pending top ups created before {createdBefore:O}");
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand cmd = new NpgsqlCommand(
                "UPDATE account_transactions SET status = 'expired', updated_at = @updatedAt WHERE status = 'pending' AND created_at < @createdBefore", conn);
            cmd.Parameters.AddWithValue("@createdBefore", createdBefore);
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
            int expired = await cmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.Info($"result=Success expired={expired}");
            return new DbResult<int>.Success(expired);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<int>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<bool>> UpdateAccountBalance(Domain.Accounts.Account account, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Updating account balance for accountId={account.Id}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "UPDATE accounts SET balance = @balance, updated_at = @updatedAt WHERE id = @id", conn);
    
            cmd.Parameters.AddWithValue("@id", account.Id);
            cmd.Parameters.AddWithValue("@balance", account.Balance);
            cmd.Parameters.AddWithValue("@updatedAt", account.UpdatedAt);
    
            var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
    
            if (rowsAffected > 0)
            {
                _logger.Info("result=Success");
                return new DbResult<bool>.Success(true);
            }
            else
            {
                _logger.Info("result=Failure(NotFound)");
                return new DbResult<bool>.Failure(DbError.NotFound);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<bool>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    // T-031.12: locks the account row and checks it is open before inserting the pending row, in one transaction.
    public async Task<DbResult<RecordTopupOutcome>> RecordTopupAttempt(AccountId accountId, Amount amount, Currency currency, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Recording topup attempt for accountId={accountId.Value}, amount={amount}");
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

            await using (NpgsqlCommand lockCmd = new NpgsqlCommand("SELECT status FROM accounts WHERE id = @id FOR UPDATE", conn, tx))
            {
                lockCmd.Parameters.AddWithValue("@id", accountId.Value);
                object? status = await lockCmd.ExecuteScalarAsync(cancellationToken);
                if (status is not string accountStatus)
                {
                    _logger.Info("result=Failure(NotFound)");
                    return new DbResult<RecordTopupOutcome>.Failure(DbError.NotFound);
                }
                if (accountStatus == "closed")
                {
                    _logger.Info("result=AccountClosed");
                    return new DbResult<RecordTopupOutcome>.Success(new RecordTopupOutcome.AccountClosed());
                }
            }

            await using NpgsqlCommand cmd = new NpgsqlCommand(
                "INSERT INTO account_transactions (account_id, amount, currency, created_at, updated_at, status) VALUES (@accountId, @amount, @currency, @createdAt, @updatedAt, @status) RETURNING id", conn, tx);
            DateTime now = DateTime.UtcNow;
            cmd.Parameters.AddWithValue("@accountId", accountId.Value);
            cmd.Parameters.AddWithValue("@amount", amount.Pence);
            cmd.Parameters.AddWithValue("@currency", currency.Code);
            cmd.Parameters.AddWithValue("@createdAt", now);
            cmd.Parameters.AddWithValue("@updatedAt", now);
            cmd.Parameters.AddWithValue("@status", "pending");

            object? id = await cmd.ExecuteScalarAsync(cancellationToken);
            if (id is int rowId && TransactionId.TryCreate(rowId) is TransactionId transactionId)
            {
                await tx.CommitAsync(cancellationToken);
                _logger.Info($"result=Success transactionId={transactionId.Value}");
                return new DbResult<RecordTopupOutcome>.Success(new RecordTopupOutcome.Recorded(transactionId));
            }

            _logger.Info("result=Failure(Unknown)");
            return new DbResult<RecordTopupOutcome>.Failure(DbError.Unknown);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<RecordTopupOutcome>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<bool>> SetTopupProviderReferenceAsync(TransactionId transactionId, string providerReference, CancellationToken cancellationToken = default)
    {
        _logger.Info($"Storing provider reference for transactionId={transactionId.Value}");
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand cmd = new NpgsqlCommand(
                "UPDATE account_transactions SET provider_reference = @reference, updated_at = @updatedAt WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", transactionId.Value);
            cmd.Parameters.AddWithValue("@reference", providerReference);
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
            if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                _logger.Info("result=Failure(NotFound)");
                return new DbResult<bool>.Failure(DbError.NotFound);
            }

            _logger.Info("result=Success");
            return new DbResult<bool>.Success(true);
        }
        catch (Exception ex) when (NpgsqlErrors.IsUnavailable(ex))
        {
            _logger.Error(ex, "unavailable");
            return new DbResult<bool>.Failure(DbError.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<bool>.Failure(NpgsqlErrors.Map(ex));
        }
    }
}
