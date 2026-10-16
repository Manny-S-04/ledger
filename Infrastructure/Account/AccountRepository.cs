using Npgsql;
using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Domain.Datatypes;

namespace Infrastructure.Account;

public sealed class AccountRepository : IAccountRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public AccountRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<DbResult<Domain.Accounts.Account>> CreateAccountAsync(Domain.Accounts.Account account)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO accounts (user_id, balance, currency, created_at, updated_at) VALUES (@userId, @balance, @currency, @createdAt, @updatedAt) RETURNING id", conn);
        
            cmd.Parameters.AddWithValue("@userId", account.UserId);
            cmd.Parameters.AddWithValue("@balance", account.Balance.Pence / 100m);
            cmd.Parameters.AddWithValue("@currency", account.Currency ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@createdAt", account.CreatedAt);
            cmd.Parameters.AddWithValue("@updatedAt", account.UpdatedAt);
        
            object? id = await cmd.ExecuteScalarAsync();
        
            if (id == null || id == DBNull.Value)
                return new DbResult<Domain.Accounts.Account>.Failure(DbError.Unknown);
            
            var createdAccount = Domain.Accounts.Account.Create(
                (int)id, 
                account.UserId, 
                account.Balance, 
                account.Currency, 
                account.CreatedAt, 
                account.UpdatedAt);
            
            return new DbResult<Domain.Accounts.Account>.Success(createdAccount);
        }
        catch (Exception)
        {
            return new DbResult<Domain.Accounts.Account>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<bool>> DeleteAccountAsync(Domain.Accounts.Account account)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM accounts WHERE id = @id", conn);
        
            cmd.Parameters.AddWithValue("@id", account.Id);
        
            var rowsAffected = await cmd.ExecuteNonQueryAsync();
        
            return new DbResult<bool>.Success(rowsAffected > 0);
        }
        catch (Exception)
        {
            return new DbResult<bool>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT user_id FROM accounts WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", accountId);

            object? ownerObj = await cmd.ExecuteScalarAsync();
            if (ownerObj is not int owner)
                return new DbResult<UserId>.Failure(DbError.NotFound);

            UserId? ownerId = UserId.TryCreate(owner);
            if (ownerId is null)
                return new DbResult<UserId>.Failure(DbError.Unknown);

            return new DbResult<UserId>.Success(ownerId.Value);
        }
        catch (Exception)
        {
            return new DbResult<UserId>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<IEnumerable<Domain.Accounts.Account>>> GetAccountsByUserIdAsync(UserId userId)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, user_id, balance, currency, created_at, updated_at FROM accounts WHERE user_id = @userId", conn);
        
            cmd.Parameters.AddWithValue("@userId", userId.Value);
        
            await using var reader = await cmd.ExecuteReaderAsync();
            var accounts = new List<Domain.Accounts.Account>();
        
            while (await reader.ReadAsync())
            {
                var account = Domain.Accounts.Account.Create(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    Amount.Create(reader.GetDecimal(2)),
                    reader.GetString(3),
                    reader.GetDateTime(4),
                    reader.GetDateTime(5)
                );
            
                accounts.Add(account);
            }
        
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Success(accounts);
        }
        catch (Exception)
        {
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<IEnumerable<Domain.Accounts.Account>>> GetAccountsByEmailAsync(Email email)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();

            await using var userCmd = new NpgsqlCommand(
                    "SELECT id FROM users WHERE email = @email", conn);
            userCmd.Parameters.AddWithValue("@email", email.EmailAddress);

            var userIdObj = await userCmd.ExecuteScalarAsync();
            if (userIdObj == null || userIdObj == DBNull.Value)
            {
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
                var account = Domain.Accounts.Account.Create(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    Amount.Create(reader.GetDecimal(2)),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetDateTime(4),
                    reader.GetDateTime(5)
                );
                accounts.Add(account);
            }
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Success(accounts);
        }
        catch (Exception)
        {
            return new DbResult<IEnumerable<Domain.Accounts.Account>>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<bool>> UpdateAccountAsync(Domain.Accounts.Account account)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE accounts SET balance = @balance, currency = @currency, updated_at = @updatedAt WHERE id = @id", conn);
    
            cmd.Parameters.AddWithValue("@id", account.Id);
            cmd.Parameters.AddWithValue("@balance", account.Balance);
            cmd.Parameters.AddWithValue("@currency", account.Currency ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@updatedAt", account.UpdatedAt);
    
            var rowsAffected = await cmd.ExecuteNonQueryAsync();
    
            if (rowsAffected > 0)
                return new DbResult<bool>.Success(true);
            else
                return new DbResult<bool>.Failure(DbError.Unknown);
        }
        catch (Exception)
        {
            return new DbResult<bool>.Failure(DbError.Unknown);
        }
    }
}
