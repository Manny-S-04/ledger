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
            return new DbResult<Domain.Accounts.Account>.Failure(DbError.Unknown);
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
            return new DbResult<bool>.Failure(DbError.Unknown);
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
            return new DbResult<bool>.Failure(DbError.Unknown);
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
            return new DbResult<UserId>.Failure(DbError.Unknown);
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
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
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
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
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
            return new DbResult<bool>.Failure(DbError.Unknown);
        }
    }

    // Applies a provider callback to the pending transaction it refers to. Only a row that is still pending
    // changes; on Succeeded the amount stored on that row is credited, in the same DB transaction.
    public async Task<DbResult<TopupCallbackOutcome>> TopUpAccountAsync(TransactionId transactionId, ProviderCallbackStatus status)
    {
        _logger.Info($"TopUpAccountAsync transactionId={transactionId.Value}, status={status}");
        string newStatus = status == ProviderCallbackStatus.Succeeded ? "done" : "failed";
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync();
            await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
            DateTime now = DateTime.UtcNow;

            int? accountId = null;
            decimal storedAmount = 0m;
            await using (NpgsqlCommand settle = new NpgsqlCommand(
                "UPDATE account_transactions SET status = @status, updated_at = @updatedAt WHERE id = @id AND status = 'pending' RETURNING account_id, amount", conn, tx))
            {
                settle.Parameters.AddWithValue("@id", transactionId.Value);
                settle.Parameters.AddWithValue("@status", newStatus);
                settle.Parameters.AddWithValue("@updatedAt", now);
                await using NpgsqlDataReader reader = await settle.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    accountId = reader.GetInt32(0);
                    storedAmount = reader.GetDecimal(1);
                }
            }

            if (accountId is null)
            {
                await tx.RollbackAsync();
                return await NotPendingOutcomeAsync(conn, transactionId, status);
            }

            if (status == ProviderCallbackStatus.Failed)
            {
                await tx.CommitAsync();
                _logger.Info("result=MarkedFailed");
                return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.MarkedFailed);
            }

            await using NpgsqlCommand credit = new NpgsqlCommand(
                "UPDATE accounts SET balance = balance + @amount, updated_at = @updatedAt WHERE id = @accountId", conn, tx);
            credit.Parameters.AddWithValue("@accountId", accountId.Value);
            credit.Parameters.AddWithValue("@amount", storedAmount);
            credit.Parameters.AddWithValue("@updatedAt", now);
            int credited = await credit.ExecuteNonQueryAsync();
            if (credited != 1)
            {
                await tx.RollbackAsync();
                _logger.Info("result=Failure(Unknown) - account row not updated");
                return new DbResult<TopupCallbackOutcome>.Failure(DbError.Unknown);
            }

            await tx.CommitAsync();
            _logger.Info("result=Credited");
            return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.Credited);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<TopupCallbackOutcome>.Failure(DbError.Unknown);
        }
    }

    // The settle UPDATE changed nothing: the transaction doesn't exist, or it is no longer pending.
    // Nothing goes back to the user for these; they are logged as warnings for the back end to look at.
    private async Task<DbResult<TopupCallbackOutcome>> NotPendingOutcomeAsync(
        NpgsqlConnection conn, TransactionId transactionId, ProviderCallbackStatus status)
    {
        await using NpgsqlCommand cmd = new NpgsqlCommand("SELECT status FROM account_transactions WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", transactionId.Value);
        object? current = await cmd.ExecuteScalarAsync();
        if (current is not string currentStatus)
        {
            _logger.Warn($"result=Failure(NotFound) transactionId={transactionId.Value}");
            return new DbResult<TopupCallbackOutcome>.Failure(DbError.NotFound);
        }

        if (currentStatus == "expired" && status == ProviderCallbackStatus.Succeeded)
        {
            _logger.Warn($"result=LateSuccessOnExpired transactionId={transactionId.Value} - provider took the money after the top up expired");
            return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.LateSuccessOnExpired);
        }

        _logger.Warn($"result=AlreadyProcessed transactionId={transactionId.Value} currentStatus={currentStatus} callbackStatus={status}");
        return new DbResult<TopupCallbackOutcome>.Success(TopupCallbackOutcome.AlreadyProcessed);
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
            return new DbResult<int>.Failure(DbError.Unknown);
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
            return new DbResult<bool>.Failure(DbError.Unknown);
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
            return new DbResult<TransactionId>.Failure(DbError.Unknown);
        }
    }
}
