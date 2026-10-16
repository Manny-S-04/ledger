using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Npgsql;

namespace Infrastructure.User;

public sealed class UserRepository : IUserRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public UserRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<DbResult<UserId>> Create(Domain.Users.User user)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("INSERT INTO Users (email, password_hash) VALUES (@email, @password_hash) RETURNING id", conn);
            cmd.Parameters.AddWithValue("@email", user.Email.EmailAddress);
            cmd.Parameters.AddWithValue("@password_hash", user.PasswordHash);

            var userIdObj = await cmd.ExecuteScalarAsync();

            if (userIdObj is not int userId)
                return new DbResult<UserId>.Failure(DbError.Unknown);

            var userIdResult = UserId.TryCreate(userId);
            if (userIdResult is null)
                return new DbResult<UserId>.Failure(DbError.Unknown);

            return new DbResult<UserId>.Success(userIdResult.Value);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // unique_violation
        {
            return new DbResult<UserId>.Failure(DbError.ConstraintViolation);
        }
        catch (Exception)
        {
            return new DbResult<UserId>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<bool>> Exists(Email email)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM Users WHERE email = @email)", conn);
            cmd.Parameters.AddWithValue("@email", email.EmailAddress);

            var exists = await cmd.ExecuteScalarAsync();

            if (exists is bool b)
                return new DbResult<bool>.Success(b);

            return new DbResult<bool>.Failure(DbError.Unknown);
        }
        catch (Exception)
        {
            return new DbResult<bool>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<IEnumerable<Domain.Users.User>>> GetAllUsers()
    {
        List<Domain.Users.User> users = [];

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, email, password_hash, created_at FROM Users", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var user = Domain.Users.User.Create(
                    reader.GetInt32(0),
                    Email.Create(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetDateTime(3)
                );

                users.Add(user);
            }

            return new DbResult<IEnumerable<Domain.Users.User>>.Success(users);
        }
        catch (Exception)
        {
            return new DbResult<IEnumerable<Domain.Users.User>>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<Domain.Users.User>> GetUserByEmail(Email email)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, email, password_hash, created_at FROM Users WHERE email = @email", conn);
            cmd.Parameters.AddWithValue("@email", email.EmailAddress);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return new DbResult<Domain.Users.User>.Failure(DbError.NotFound);

            Domain.Users.User user = Domain.Users.User.Create(
                reader.GetInt32(0),
                Email.Create(reader.GetString(1)),
                reader.GetString(2),
                reader.GetDateTime(3));

            return new DbResult<Domain.Users.User>.Success(user);
        }
        catch (Exception)
        {
            return new DbResult<Domain.Users.User>.Failure(DbError.Unknown);
        }
    }

    public async Task<DbResult<Domain.Users.User>> GetUser(Email email, string passwordHash)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, email, password_hash, created_at FROM Users WHERE email = @email AND password_hash = @password_hash", conn);
            cmd.Parameters.AddWithValue("@email", email.EmailAddress);
            cmd.Parameters.AddWithValue("@password_hash", passwordHash);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var retrievedUser = Domain.Users.User.Create(
                        reader.GetInt32(0),
                        Email.Create(reader.GetString(1)),
                        reader.GetString(2),
                        reader.GetDateTime(3)
                        );

                return new DbResult<Domain.Users.User>.Success(retrievedUser);
            }

            return new DbResult<Domain.Users.User>.Failure(DbError.Unknown);
        }
        catch (Exception)
        {
            return new DbResult<Domain.Users.User>.Failure(DbError.Unknown);
        }
    }
}
