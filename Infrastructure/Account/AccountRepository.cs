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

    public async Task<DbResult<Domain.Accounts.Account>> CreateAccountAsync(Domain.Accounts.Account account)
    {
        _logger.Info($"Inserting account for userId={account.UserId}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO accounts (user_id, balance, currency, created_at, updated_at) VALUES (@userId, @balance, @currency, @createdAt, @updatedAt) RETURNING id", conn);
        
            cmd.Parameters.AddWithValue("@userId", account.UserId);
            cmd.Parameters.AddWithValue("@balance", account.Balance.Pence / 100m);
            cmd.Parameters.AddWithValue("@currency", account.Currency.Code);
            cmd.Parameters.AddWithValue("@createdAt", account.CreatedAt);
            cmd.Parameters.AddWithValue("@updatedAt", account.UpdatedAt);
        
            object? id = await cmd.ExecuteScalarAsync();
        
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

    public async Task<DbResult<bool>> AccountExistsAsync(UserId userId, Domain.Datatypes.Currency currency)
    {
        _logger.Info($"Checking for an existing account userId={userId.Value} currency={currency.Code}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM accounts WHERE user_id = @userId AND currency = @currency)", conn);
            cmd.Parameters.AddWithValue("@userId", userId.Value);
            cmd.Parameters.AddWithValue("@currency", currency.Code);

            bool exists = await cmd.ExecuteScalarAsync() is true;
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

    public async Task<DbResult<bool>> DeleteAccountAsync(int accountId)
    {
        _logger.Info($"Looking up owner of accountId={accountId}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();

            // Single statement: the balance check and the delete happen atomically under the row lock,
            // so a concurrent balance change can't slip in between them.
            await using var cmd = new NpgsqlCommand("DELETE FROM accounts WHERE id = @id AND balance = 0", conn);
            cmd.Parameters.AddWithValue("@id", accountId);

            int rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected > 0)
            {
                _logger.Info("result=Success(deleted)");
                return new DbResult<bool>.Success(true);
            }

            // Nothing deleted: only to report why (balance > 0 vs. no such account).
            await using var existsCmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM accounts WHERE id = @id)", conn);
            existsCmd.Parameters.AddWithValue("@id", accountId);
            if (await existsCmd.ExecuteScalarAsync() is true)
            {
                _logger.Info("result=Success(not deleted: balance > 0)");
                return new DbResult<bool>.Success(false);
            }

            _logger.Info("result=Failure(NotFound)");
            return new DbResult<bool>.Failure(DbError.NotFound);
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

    public async Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId)
    {
        _logger.Info($"Deleting accountId={accountId} (only if balance is 0)");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT user_id FROM accounts WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", accountId);

            object? ownerObj = await cmd.ExecuteScalarAsync();
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
            _logger.Error(ex, "unavailable");
            return new DbResult<UserId>.Failure(DbError.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<UserId>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<IEnumerable<Domain.Accounts.Account>>> GetAccountsByUserIdAsync(UserId userId)
    {
        _logger.Info($"Loading accounts for userId={userId.Value}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, user_id, balance, currency, created_at, updated_at FROM accounts WHERE user_id = @userId", conn);
        
            cmd.Parameters.AddWithValue("@userId", userId.Value);
        
            await using var reader = await cmd.ExecuteReaderAsync();
            var accounts = new List<Domain.Accounts.Account>();
        
            while (await reader.ReadAsync())
            {
                Domain.Datatypes.Currency? currency = Domain.Datatypes.Currency.TryCreate(reader.GetString(3));
                if (currency is null)
                {
                    _logger.Warn($"stored currency for account {reader.GetInt32(0)} is invalid");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
                }

                Amount? amount = Amount.TryCreate(reader.GetDecimal(2));
                if(amount is null) 
                {
                    _logger.Warn("Failed to generate correct amount");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown); 
                }
                    
                var account = Domain.Accounts.Account.Create(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    amount.Value,
                    currency,
                    reader.GetDateTime(4),
                    reader.GetDateTime(5)
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

    public async Task<DbResult<IEnumerable<Domain.Accounts.Account>>> GetAccountsByEmailAsync(Email email)
    {
        _logger.Info("Loading accounts by email");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();

            await using var userCmd = new NpgsqlCommand(
                    "SELECT id FROM users WHERE email = @email", conn);
            userCmd.Parameters.AddWithValue("@email", email.EmailAddress);

            var userIdObj = await userCmd.ExecuteScalarAsync();
            if (userIdObj == null || userIdObj == DBNull.Value)
            {
                _logger.Info("result=Failure(NotFound)");
                return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.NotFound);
            }

            var userId = (int)userIdObj;

            await using var accountCmd = new NpgsqlCommand(
                    "SELECT id, user_id, balance, currency, created_at, updated_at FROM accounts WHERE user_id = @userId", conn);
            accountCmd.Parameters.AddWithValue("@userId", userId);

            await using var reader = await accountCmd.ExecuteReaderAsync();
            var accounts = new List<Domain.Accounts.Account>();

            while (await reader.ReadAsync())
            {
                Domain.Datatypes.Currency? currency = Domain.Datatypes.Currency.TryCreate(reader.GetString(3));
                if (currency is null)
                {
                    _logger.Warn($"stored currency for account {reader.GetInt32(0)} is invalid");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
                }

                Amount? amount = Amount.TryCreate(reader.GetDecimal(2));
                if(amount is null) 
                {
                    _logger.Warn("Failed to generate correct amount");
                    return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown); 
                }


                var account = Domain.Accounts.Account.Create(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    amount.Value,
                    currency,
                    reader.GetDateTime(4),
                    reader.GetDateTime(5)
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

    public async Task<DbResult<bool>> UpdateAccountAsync(Domain.Accounts.Account account)
    {
        _logger.Info($"Updating accountId={account.Id}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE accounts SET balance = @balance, currency = @currency, updated_at = @updatedAt WHERE id = @id", conn);
    
            cmd.Parameters.AddWithValue("@id", account.Id);
            cmd.Parameters.AddWithValue("@balance", account.Balance);
            cmd.Parameters.AddWithValue("@currency", account.Currency.Code);
            cmd.Parameters.AddWithValue("@updatedAt", account.UpdatedAt);
    
            var rowsAffected = await cmd.ExecuteNonQueryAsync();
    
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
    public async Task<DbResult<TopupCallbackOutcome>> TopUpAccountAsync(TransactionId transactionId, ProviderCallbackStatus status, string eventId, long sequence)
    {
        _logger.Info($"TopUpAccountAsync transactionId={transactionId.Value}, status={status}, eventId={eventId}, sequence={sequence}");
        string newStatus = status == ProviderCallbackStatus.Succeeded ? "done" : "failed";
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync();
            await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
            DateTime now = DateTime.UtcNow;

            // A concurrent copy of the same eventId waits here on the unique key until this transaction ends.
            await using (NpgsqlCommand record = new NpgsqlCommand(
                "INSERT INTO processed_callbacks (event_id, account_transaction_id, sequence, processed_at) VALUES (@eventId, @id, @sequence, @processedAt) ON CONFLICT (event_id) DO NOTHING", conn, tx))
            {
                record.Parameters.AddWithValue("@eventId", eventId);
                record.Parameters.AddWithValue("@id", transactionId.Value);
                record.Parameters.AddWithValue("@sequence", sequence);
                record.Parameters.AddWithValue("@processedAt", now);
                if (await record.ExecuteNonQueryAsync() == 0)
                {
                    await tx.RollbackAsync();
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
                await using NpgsqlDataReader reader = await lockRow.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    currentStatus = reader.GetString(0);
                    lastSequence = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                }
            }

            if (currentStatus is null)
            {
                await tx.CommitAsync();
                _logger.Warn($"result=Failure(NotFound) transactionId={transactionId.Value}");
                return new DbResult<TopupCallbackOutcome>.Failure(DbError.NotFound);
            }

            if (lastSequence is long last && sequence < last)
            {
                await tx.CommitAsync();
                _logger.Warn($"result=StaleSequence transactionId={transactionId.Value} sequence={sequence} lastSequence={last}");
                return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.StaleSequence);
            }

            await using (NpgsqlCommand received = new NpgsqlCommand(
                "UPDATE account_transactions SET last_sequence = @sequence, last_event_id = @eventId WHERE id = @id", conn, tx))
            {
                received.Parameters.AddWithValue("@id", transactionId.Value);
                received.Parameters.AddWithValue("@sequence", sequence);
                received.Parameters.AddWithValue("@eventId", eventId);
                await received.ExecuteNonQueryAsync();
            }

            bool wasLate = currentStatus == "expired" && status == ProviderCallbackStatus.Succeeded;
            if (currentStatus != "pending" && !wasLate)
            {
                await tx.CommitAsync();
                _logger.Warn($"result=AlreadyProcessed transactionId={transactionId.Value} currentStatus={currentStatus} callbackStatus={status}");
                return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.AlreadyProcessed);
            }

            int accountId;
            decimal storedAmount;
            await using (NpgsqlCommand settle = new NpgsqlCommand(
                "UPDATE account_transactions SET status = @status, was_late = @wasLate, updated_at = @updatedAt WHERE id = @id RETURNING account_id, amount", conn, tx))
            {
                settle.Parameters.AddWithValue("@id", transactionId.Value);
                settle.Parameters.AddWithValue("@status", newStatus);
                settle.Parameters.AddWithValue("@updatedAt", now);
                settle.Parameters.AddWithValue("@wasLate", wasLate);
                await using NpgsqlDataReader reader = await settle.ExecuteReaderAsync();
                await reader.ReadAsync();
                accountId = reader.GetInt32(0);
                storedAmount = reader.GetDecimal(1);
            }

            if (status == ProviderCallbackStatus.Failed)
            {
                await tx.CommitAsync();
                _logger.Info("result=MarkedFailed");
                return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.MarkedFailed);
            }

            await using NpgsqlCommand credit = new NpgsqlCommand(
                "UPDATE accounts SET balance = balance + @amount, updated_at = @updatedAt WHERE id = @accountId", conn, tx);
            credit.Parameters.AddWithValue("@accountId", accountId);
            credit.Parameters.AddWithValue("@amount", storedAmount);
            credit.Parameters.AddWithValue("@updatedAt", now);
            int credited = await credit.ExecuteNonQueryAsync();
            if (credited != 1)
            {
                await tx.RollbackAsync();
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
                await audit.ExecuteNonQueryAsync();

                await tx.CommitAsync();
                _logger.Warn($"result=LateSuccessOnExpired transactionId={transactionId.Value} - credited after the top up expired");
                return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.LateSuccessOnExpired);
            }

            await tx.CommitAsync();
            _logger.Info("result=Credited");
            return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.Credited);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<TopupCallbackOutcome>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<int>> ExpireStalePendingAsync(DateTime createdBefore)
    {
        _logger.Info($"Expiring pending top ups created before {createdBefore:O}");
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync();
            await using NpgsqlCommand cmd = new NpgsqlCommand(
                "UPDATE account_transactions SET status = 'expired', updated_at = @updatedAt WHERE status = 'pending' AND created_at < @createdBefore", conn);
            cmd.Parameters.AddWithValue("@createdBefore", createdBefore);
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
            int expired = await cmd.ExecuteNonQueryAsync();
            _logger.Info($"result=Success expired={expired}");
            return new DbResult<int>.Success(expired);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<int>.Failure(NpgsqlErrors.Map(ex));
        }
    }

    public async Task<DbResult<bool>> UpdateAccountBalance(Domain.Accounts.Account account)
    {
        _logger.Info($"Updating account balance for accountId={account.Id}");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE accounts SET balance = @balance, updated_at = @updatedAt WHERE id = @id", conn);
    
            cmd.Parameters.AddWithValue("@id", account.Id);
            cmd.Parameters.AddWithValue("@balance", account.Balance);
            cmd.Parameters.AddWithValue("@updatedAt", account.UpdatedAt);
    
            var rowsAffected = await cmd.ExecuteNonQueryAsync();
    
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

    public async Task<DbResult<TransactionId>> RecordTopupAttempt(AccountId accountId, Amount amount, Currency currency)
    {
        _logger.Info($"Recording topup attempt for accountId={accountId.Value}, amount={amount}");
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync();
            await using NpgsqlCommand cmd = new NpgsqlCommand(
                "INSERT INTO account_transactions (account_id, amount, currency, created_at, updated_at, status) VALUES (@accountId, @amount, @currency, @createdAt, @updatedAt, @status) RETURNING id", conn);
            DateTime now = DateTime.UtcNow;
            cmd.Parameters.AddWithValue("@accountId", accountId.Value);
            cmd.Parameters.AddWithValue("@amount", amount.Pence / 100m);
            cmd.Parameters.AddWithValue("@currency", currency.Code);
            cmd.Parameters.AddWithValue("@createdAt", now);
            cmd.Parameters.AddWithValue("@updatedAt", now);
            cmd.Parameters.AddWithValue("@status", "pending");

            object? id = await cmd.ExecuteScalarAsync();
            if (id is int rowId && TransactionId.TryCreate(rowId) is TransactionId transactionId)
            {
                _logger.Info($"result=Success transactionId={transactionId.Value}");
                return new DbResult<TransactionId>.Success(transactionId);
            }

            _logger.Info("result=Failure(Unknown)");
            return new DbResult<TransactionId>.Failure(DbError.Unknown);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<TransactionId>.Failure(NpgsqlErrors.Map(ex));
        }
    }
}
