using Ledger.Tests.Repositories;
using Migrator;
using Npgsql;

namespace Ledger.Tests.Migrator;

// MigrationRunner (T-035) against its own database, with migration files in a temp folder.
public sealed class MigrationRunnerTests : IAsyncLifetime
{
    private const string Database = "ledger_migrator_test";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ledger-migrations-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _output = new StringWriter();
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        NpgsqlConnectionStringBuilder builder = new(DatabaseFixture.ConnectionString) { Database = "postgres" };
        await using (NpgsqlConnection admin = new(builder.ConnectionString))
        {
            await admin.OpenAsync();
            await using NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS {Database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            await using NpgsqlCommand create = new($"CREATE DATABASE {Database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        builder.Database = Database;
        _connectionString = builder.ConnectionString;
        Directory.CreateDirectory(_directory);
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        NpgsqlConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    private MigrationRunner Runner() => new(_connectionString, _directory, _output);

    private void Write(string id, string name, string up, string? down = null)
    {
        File.WriteAllText(Path.Combine(_directory, $"{id}_{name}.up.sql"), up);
        if (down is not null)
        {
            File.WriteAllText(Path.Combine(_directory, $"{id}_{name}.down.sql"), down);
        }
    }

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using NpgsqlConnection conn = new(_connectionString);
        await conn.OpenAsync();
        await using NpgsqlCommand cmd = new(sql, conn);
        object? value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private Task<bool> TableExistsAsync(string table) => ScalarAsync<bool>($"SELECT to_regclass('{table}') IS NOT NULL");

    private Task<string?> AppliedIdsAsync() =>
        ScalarAsync<string>($"SELECT string_agg(id, ',' ORDER BY id) FROM schema_migrations WHERE id <> '{MigrationRunner.LockId}'");

    private void WriteThree()
    {
        Write("20261101000000", "a", "CREATE TABLE IF NOT EXISTS a (id int)", "DROP TABLE a");
        Write("20261102000000", "b", "CREATE TABLE IF NOT EXISTS b (id int)", "DROP TABLE b");
        Write("20261103000000", "c", "CREATE TABLE IF NOT EXISTS c (id int)", "DROP TABLE c");
    }

    [Fact]
    public async Task Up_AppliesAllInIdOrderAndRecordsThem()
    {
        Write("20261102000000", "second", "ALTER TABLE first ADD COLUMN x int");
        Write("20261101000000", "first", "CREATE TABLE IF NOT EXISTS first (id int)");

        await Runner().UpAsync();

        Assert.Equal("20261101000000,20261102000000", await AppliedIdsAsync());
        Assert.True(await ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'first' AND column_name = 'x')"));
    }

    [Fact]
    public async Task Up_Twice_SecondRunAppliesNothing()
    {
        WriteThree();
        await Runner().UpAsync();

        await Runner().UpAsync();

        Assert.EndsWith("Nothing to apply." + Environment.NewLine, _output.ToString());
    }

    [Fact]
    public async Task Up_FailingMigration_RollsItBackAndStops()
    {
        Write("20261101000000", "ok", "CREATE TABLE IF NOT EXISTS ok (id int)");
        Write("20261102000000", "bad", "CREATE TABLE half (id int); SELECT * FROM no_such_table");
        Write("20261103000000", "later", "CREATE TABLE IF NOT EXISTS later (id int)");

        await Assert.ThrowsAsync<MigrationException>(() => Runner().UpAsync());

        Assert.Equal("20261101000000", await AppliedIdsAsync());
        Assert.True(await TableExistsAsync("ok"));
        Assert.False(await TableExistsAsync("half"));
        Assert.False(await TableExistsAsync("later"));
    }

    [Fact]
    public async Task Up_NewMigrationAddedLater_IsApplied()
    {
        Write("20261101000000", "a", "CREATE TABLE IF NOT EXISTS a (id int)");
        await Runner().UpAsync();
        Write("20261102000000", "b", "CREATE TABLE IF NOT EXISTS b (id int)");

        await Runner().UpAsync();

        Assert.Equal("20261101000000,20261102000000", await AppliedIdsAsync());
    }

    [Fact]
    public async Task Down_UndoesOnlyTheLatest()
    {
        WriteThree();
        await Runner().UpAsync();

        await Runner().DownAsync(null);

        Assert.Equal("20261101000000,20261102000000", await AppliedIdsAsync());
        Assert.False(await TableExistsAsync("c"));
        Assert.True(await TableExistsAsync("b"));
    }

    [Fact]
    public async Task DownTo_UndoesEverythingAfterTheTarget()
    {
        WriteThree();
        await Runner().UpAsync();

        await Runner().DownAsync("20261101000000");

        Assert.Equal("20261101000000", await AppliedIdsAsync());
        Assert.True(await TableExistsAsync("a"));
        Assert.False(await TableExistsAsync("b"));
        Assert.False(await TableExistsAsync("c"));
    }

    [Fact]
    public async Task DownTo_UnknownId_FailsAndUndoesNothing()
    {
        WriteThree();
        await Runner().UpAsync();

        await Assert.ThrowsAsync<MigrationException>(() => Runner().DownAsync("20990101000000"));

        Assert.Equal("20261101000000,20261102000000,20261103000000", await AppliedIdsAsync());
    }

    [Fact]
    public async Task Down_MissingDownScript_FailsAndKeepsTheMigration()
    {
        Write("20261101000000", "a", "CREATE TABLE IF NOT EXISTS a (id int)");
        await Runner().UpAsync();

        await Assert.ThrowsAsync<MigrationException>(() => Runner().DownAsync(null));

        Assert.Equal("20261101000000", await AppliedIdsAsync());
    }

    [Fact]
    public async Task Status_ListsAppliedAndPending()
    {
        Write("20261101000000", "a", "CREATE TABLE IF NOT EXISTS a (id int)");
        await Runner().UpAsync();
        Write("20261102000000", "b", "CREATE TABLE IF NOT EXISTS b (id int)");
        _output.GetStringBuilder().Clear();

        await Runner().StatusAsync();

        string[] lines = _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("applied  20261101000000_a", lines[0]);
        Assert.Equal("pending  20261102000000_b", lines[1]);
    }

    [Fact]
    public async Task Run_WhileAnotherHoldsTheLock_FailsFastWithoutApplying()
    {
        WriteThree();
        await Runner().StatusAsync(); // creates the tracking table and lock row
        await using NpgsqlConnection holder = new(_connectionString);
        await holder.OpenAsync();
        await using NpgsqlTransaction tx = await holder.BeginTransactionAsync();
        await using (NpgsqlCommand hold = new($"SELECT id FROM schema_migrations WHERE id = '{MigrationRunner.LockId}' FOR UPDATE", holder, tx))
        {
            await hold.ExecuteScalarAsync();
        }

        MigrationException ex = await Assert.ThrowsAsync<MigrationException>(() => Runner().UpAsync());

        Assert.Equal("another run in progress", ex.Message);
        Assert.Null(await AppliedIdsAsync());
    }

    [Fact]
    public async Task Run_LockHolderConnectionDies_LockIsReleased()
    {
        WriteThree();
        await Runner().StatusAsync();
        NpgsqlConnection holder = new(new NpgsqlConnectionStringBuilder(_connectionString) { Pooling = false }.ConnectionString);
        await holder.OpenAsync();
        NpgsqlTransaction tx = await holder.BeginTransactionAsync();
        await using (NpgsqlCommand hold = new($"SELECT id FROM schema_migrations WHERE id = '{MigrationRunner.LockId}' FOR UPDATE", holder, tx))
        {
            await hold.ExecuteScalarAsync();
        }

        await holder.DisposeAsync(); // the "crash": connection gone, Postgres ends the transaction

        await Runner().UpAsync();
        Assert.Equal("20261101000000,20261102000000,20261103000000", await AppliedIdsAsync());
    }

    [Fact]
    public void Load_BadFileName_Throws()
    {
        File.WriteAllText(Path.Combine(_directory, "add_things.up.sql"), "SELECT 1");

        Assert.Throws<InvalidOperationException>(() => MigrationFile.Load(_directory));
    }

    [Fact]
    public void Load_DownWithoutUp_Throws()
    {
        File.WriteAllText(Path.Combine(_directory, "20261101000000_a.down.sql"), "SELECT 1");

        Assert.Throws<InvalidOperationException>(() => MigrationFile.Load(_directory));
    }
}
