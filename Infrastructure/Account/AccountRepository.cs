using Npgsql;
using Application.Logging;
using Microsoft.Extensions.Logging;
using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Domain.Datatypes;

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
        _logger.Info($"hit userId={account.UserId}");
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
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<Domain.Accounts.Account>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<bool>> DeleteAccountAsync(int accountId)
    {
        _logger.Info($"hit accountId={accountId}");
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
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<bool>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId)
    {
        _logger.Info($"hit accountId={accountId}");
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
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<UserId>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<IEnumerable<Domain.Accounts.Account>>> GetAccountsByUserIdAsync(UserId userId)
    {
        _logger.Info($"hit userId={userId.Value}");
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

                var account = Domain.Accounts.Account.Create(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    Amount.Create(reader.GetDecimal(2)),
                    currency,
                    reader.GetDateTime(4),
                    reader.GetDateTime(5)
                );
            
                accounts.Add(account);
            }
        
            _logger.Info($"result=Success count={accounts.Count}");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Success(accounts);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<IEnumerable<Domain.Accounts.Account>>> GetAccountsByEmailAsync(Email email)
    {
        _logger.Info("hit");
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

                var account = Domain.Accounts.Account.Create(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    Amount.Create(reader.GetDecimal(2)),
                    currency,
                    reader.GetDateTime(4),
                    reader.GetDateTime(5)
                );
                accounts.Add(account);
            }
            _logger.Info($"result=Success count={accounts.Count}");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Success(accounts);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<bool>> UpdateAccountAsync(Domain.Accounts.Account account)
    {
        _logger.Info($"hit accountId={account.Id}");
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
}
