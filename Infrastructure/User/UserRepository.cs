using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Npgsql;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Infrastructure.User;

public sealed class UserRepository : IUserRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(NpgsqlDataSource dataSource, ILogger<UserRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<DbResult<UserId>> Create(Domain.Users.User user)
    {
        _logger.Info("hit");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("INSERT INTO Users (email, password_hash) VALUES (@email, @password_hash) RETURNING id", conn);
            cmd.Parameters.AddWithValue("@email", user.Email.EmailAddress);
            cmd.Parameters.AddWithValue("@password_hash", user.PasswordHash);

            var userIdObj = await cmd.ExecuteScalarAsync();

            if (userIdObj is not int userId)
            {
                _logger.Info("result=Failure(Unknown)");
                return new DbResult<UserId>.Failure(DbError.Unknown);
            }

            var userIdResult = UserId.TryCreate(userId);
            if (userIdResult is null)
            {
                _logger.Info("result=Failure(Unknown)");
                return new DbResult<UserId>.Failure(DbError.Unknown);
            }

            _logger.Info("result=Success");
            return new DbResult<UserId>.Success(userIdResult.Value);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // unique_violation
        {
            _logger.Warn($"unique violation: {ex.ConstraintName}");
            return new DbResult<UserId>.Failure(DbError.ConstraintViolation);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<UserId>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<bool>> Exists(Email email)
    {
        _logger.Info("hit");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM Users WHERE email = @email)", conn);
            cmd.Parameters.AddWithValue("@email", email.EmailAddress);

            var exists = await cmd.ExecuteScalarAsync();

            if (exists is bool b)
            {
                _logger.Info("result=Success");
                return new DbResult<bool>.Success(b);
            }

            _logger.Info("result=Failure(Unknown)");
            return new DbResult<bool>.Failure(DbError.Unknown);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<bool>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<IEnumerable<Domain.Users.User>>> GetAllUsers()
    {
        _logger.Info("hit");
        List<Domain.Users.User> users = [];

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, email, password_hash, created_at FROM Users", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                Email? storedEmail = Email.TryCreate(reader.GetString(1));
                if (storedEmail is null)
                {
                    _logger.Warn($"stored email for user {reader.GetInt32(0)} is invalid");
                    return new DbResult<IEnumerable<Domain.Users.User>>.Failure(DbError.Unknown);
                }

                var user = Domain.Users.User.Create(
                    reader.GetInt32(0),
                    storedEmail,
                    reader.GetString(2),
                    reader.GetDateTime(3)
                );

                users.Add(user);
            }

            _logger.Info($"result=Success count={users.Count}");
            return new DbResult<IEnumerable<Domain.Users.User>>.Success(users);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<IEnumerable<Domain.Users.User>>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<Domain.Users.User>> GetUserByEmail(Email email)
    {
        _logger.Info("hit");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, email, password_hash, created_at FROM Users WHERE email = @email", conn);
            cmd.Parameters.AddWithValue("@email", email.EmailAddress);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                _logger.Info("result=Failure(NotFound)");
                return new DbResult<Domain.Users.User>.Failure(DbError.NotFound);
            }

            Email? storedEmail = Email.TryCreate(reader.GetString(1));
            if (storedEmail is null)
            {
                _logger.Warn($"stored email for user {reader.GetInt32(0)} is invalid");
                return new DbResult<Domain.Users.User>.Failure(DbError.Unknown);
            }

            Domain.Users.User user = Domain.Users.User.Create(
                reader.GetInt32(0),
                storedEmail,
                reader.GetString(2),
                reader.GetDateTime(3));

            _logger.Info("result=Success");
            return new DbResult<Domain.Users.User>.Success(user);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<Domain.Users.User>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<Domain.Users.User>> GetUser(Email email, string passwordHash)
    {
        _logger.Info("hit");
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, email, password_hash, created_at FROM Users WHERE email = @email AND password_hash = @password_hash", conn);
            cmd.Parameters.AddWithValue("@email", email.EmailAddress);
            cmd.Parameters.AddWithValue("@password_hash", passwordHash);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                Email? storedEmail = Email.TryCreate(reader.GetString(1));
                if (storedEmail is null)
                {
                    _logger.Warn($"stored email for user {reader.GetInt32(0)} is invalid");
                    return new DbResult<Domain.Users.User>.Failure(DbError.Unknown);
                }

                var retrievedUser = Domain.Users.User.Create(
                        reader.GetInt32(0),
                        storedEmail,
                        reader.GetString(2),
                        reader.GetDateTime(3)
                        );

                _logger.Info("result=Success");
                return new DbResult<Domain.Users.User>.Success(retrievedUser);
            }

            _logger.Info("result=Failure(Unknown)");
            return new DbResult<Domain.Users.User>.Failure(DbError.Unknown);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "failed");
            return new DbResult<Domain.Users.User>.Failure(DbError.Unknown);
        }
    }
}
