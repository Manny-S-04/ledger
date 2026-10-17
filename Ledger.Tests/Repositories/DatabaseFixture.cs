using Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Ledger.Tests.Repositories;

// Real Postgres for repository tests (T-017): a separate `ledger_test` database, created if missing.
// The schema comes from the app's own InitDb (once per run); each test then empties the tables.
// Override the connection with the LEDGER_TEST_CONNECTION environment variable.
public sealed class DatabaseFixture : IAsyncLifetime
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("LEDGER_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=ledger_test;Username=manny;Password=bob";

    // Nothing listens on port 1: opening a connection is refused immediately (T-017.3 a).
    public const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=ledger_test;Username=manny;Password=bob;Timeout=2";

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        NpgsqlConnectionStringBuilder builder = new(ConnectionString);
        string database = builder.Database ?? "ledger_test";
        builder.Database = "postgres";

        await using (NpgsqlConnection admin = new(builder.ConnectionString))
        {
            await admin.OpenAsync();
            await using NpgsqlCommand exists = new("SELECT 1 FROM pg_database WHERE datname = @name", admin);
            exists.Parameters.AddWithValue("@name", database);
            if (await exists.ExecuteScalarAsync() is null)
            {
                await using NpgsqlCommand create = new($"CREATE DATABASE \"{database}\"", admin);
                await create.ExecuteNonQueryAsync();
            }
        }

        DataSource = NpgsqlDataSource.Create(ConnectionString);
        await Api.Program.InitDb(DataSource, new Pbkdf2PasswordHasher(), NullLogger.Instance);
    }

    public async Task DisposeAsync() => await DataSource.DisposeAsync();

    public async Task ResetAsync()
    {
        await using NpgsqlConnection conn = await DataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new("TRUNCATE accounts, users RESTART IDENTITY CASCADE", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> InsertUserAsync(string email, string passwordHash = "test-hash")
    {
        await using NpgsqlConnection conn = await DataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new(
            "INSERT INTO users (email, password_hash) VALUES (@email, @hash) RETURNING id", conn);
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@hash", passwordHash);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<int> InsertAccountAsync(int userId, string currency, decimal balance = 0m)
    {
        await using NpgsqlConnection conn = await DataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new(
            "INSERT INTO accounts (user_id, balance, currency) VALUES (@userId, @balance, @currency) RETURNING id", conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@balance", balance);
        cmd.Parameters.AddWithValue("@currency", currency);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using NpgsqlConnection conn = await DataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new(sql, conn);
        object? value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }
}

// Both repository test classes share one database, so they must not run in parallel.
[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}
