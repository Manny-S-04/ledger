using Npgsql;

namespace Migrator;

public sealed class MigrationException : Exception
{
    public MigrationException(string message) : base(message) { }
    public MigrationException(string message, Exception inner) : base(message, inner) { }
}

// Applies and undoes migrations, tracked in schema_migrations (T-035).
public sealed class MigrationRunner
{
    // M.3 / M.6: the lock is a row in the tracking table. A run holds it with FOR UPDATE NOWAIT on its own
    // connection for the whole run, so a crash drops the connection and Postgres releases it.
    public const string LockId = "__lock__";
    private const string LockNotAvailable = "55P03";

    private readonly string _connectionString;
    private readonly string _migrationsDirectory;
    private readonly TextWriter _output;

    public MigrationRunner(string connectionString, string migrationsDirectory, TextWriter output)
    {
        _connectionString = connectionString;
        _migrationsDirectory = migrationsDirectory;
        _output = output;
    }

    public async Task UpAsync()
    {
        IReadOnlyList<MigrationFile> files = MigrationFile.Load(_migrationsDirectory);
        await using NpgsqlConnection lockConnection = await LockAsync();
        await using NpgsqlConnection conn = await OpenAsync();
        HashSet<string> applied = (await AppliedAsync(conn)).Select(a => a.Id).ToHashSet();

        List<MigrationFile> pending = files.Where(f => !applied.Contains(f.Id)).ToList();
        if (pending.Count == 0)
        {
            _output.WriteLine("Nothing to apply.");
            return;
        }

        foreach (MigrationFile migration in pending)
        {
            await RunInTransactionAsync(conn, $"{migration.Id}_{migration.Name} up", await File.ReadAllTextAsync(migration.UpPath),
                "INSERT INTO schema_migrations (id, name, applied_at) VALUES (@id, @name, now())", migration);
            _output.WriteLine($"Applied {migration.Id}_{migration.Name}");
        }
    }

    // M.7: no target undoes the latest applied migration; a target undoes everything applied after it.
    public async Task DownAsync(string? toId)
    {
        IReadOnlyList<MigrationFile> files = MigrationFile.Load(_migrationsDirectory);
        await using NpgsqlConnection lockConnection = await LockAsync();
        await using NpgsqlConnection conn = await OpenAsync();
        List<AppliedMigration> applied = await AppliedAsync(conn);
        if (applied.Count == 0)
        {
            _output.WriteLine("Nothing to undo.");
            return;
        }

        List<AppliedMigration> toUndo;
        if (toId is null)
        {
            toUndo = new List<AppliedMigration> { applied[^1] };
        }
        else
        {
            if (!applied.Any(a => a.Id == toId))
            {
                throw new MigrationException($"--to {toId}: no applied migration has that id");
            }
            toUndo = applied.Where(a => string.CompareOrdinal(a.Id, toId) > 0).ToList();
        }

        if (toUndo.Count == 0)
        {
            _output.WriteLine("Nothing to undo.");
            return;
        }

        foreach (AppliedMigration migration in Enumerable.Reverse(toUndo))
        {
            MigrationFile? file = files.FirstOrDefault(f => f.Id == migration.Id);
            if (file?.DownPath is null)
            {
                throw new MigrationException($"{migration.Id}_{migration.Name} has no .down.sql; stopped");
            }

            await RunInTransactionAsync(conn, $"{file.Id}_{file.Name} down", await File.ReadAllTextAsync(file.DownPath),
                "DELETE FROM schema_migrations WHERE id = @id", file);
            _output.WriteLine($"Undid {file.Id}_{file.Name}");
        }
    }

    public async Task StatusAsync()
    {
        IReadOnlyList<MigrationFile> files = MigrationFile.Load(_migrationsDirectory);
        await using NpgsqlConnection conn = await OpenAsync();
        await EnsureTrackingTableAsync(conn);
        Dictionary<string, AppliedMigration> applied = (await AppliedAsync(conn)).ToDictionary(a => a.Id);

        foreach (MigrationFile file in files)
        {
            _output.WriteLine(applied.TryGetValue(file.Id, out AppliedMigration? done)
                ? $"applied  {file.Id}_{file.Name}  {done.AppliedAt:O}"
                : $"pending  {file.Id}_{file.Name}");
        }

        foreach (AppliedMigration orphan in applied.Values.Where(a => files.All(f => f.Id != a.Id)))
        {
            _output.WriteLine($"applied  {orphan.Id}_{orphan.Name}  {orphan.AppliedAt:O}  (file missing)");
        }

        if (files.Count == 0 && applied.Count == 0)
        {
            _output.WriteLine("No migrations.");
        }
    }

    private sealed record AppliedMigration(string Id, string Name, DateTime AppliedAt);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task EnsureTrackingTableAsync(NpgsqlConnection conn)
    {
        await using NpgsqlCommand cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at TIMESTAMPTZ
            );
            INSERT INTO schema_migrations (id, name) VALUES (@lockId, 'run lock') ON CONFLICT (id) DO NOTHING;", conn);
        cmd.Parameters.AddWithValue("@lockId", LockId);
        await cmd.ExecuteNonQueryAsync();
    }

    // The returned connection holds the lock row until it is disposed (or the process dies).
    private async Task<NpgsqlConnection> LockAsync()
    {
        NpgsqlConnection conn = await OpenAsync();
        try
        {
            await EnsureTrackingTableAsync(conn);
            await using (NpgsqlCommand begin = new NpgsqlCommand("BEGIN", conn))
            {
                await begin.ExecuteNonQueryAsync();
            }
            await using NpgsqlCommand lockCmd = new NpgsqlCommand("SELECT id FROM schema_migrations WHERE id = @lockId FOR UPDATE NOWAIT", conn);
            lockCmd.Parameters.AddWithValue("@lockId", LockId);
            await lockCmd.ExecuteScalarAsync();
            return conn;
        }
        catch (PostgresException ex) when (ex.SqlState == LockNotAvailable)
        {
            await conn.DisposeAsync();
            throw new MigrationException("another run in progress");
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    private static async Task<List<AppliedMigration>> AppliedAsync(NpgsqlConnection conn)
    {
        await using NpgsqlCommand cmd = new NpgsqlCommand(
            "SELECT id, name, applied_at FROM schema_migrations WHERE id <> @lockId ORDER BY id", conn);
        cmd.Parameters.AddWithValue("@lockId", LockId);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        List<AppliedMigration> applied = new List<AppliedMigration>();
        while (await reader.ReadAsync())
        {
            applied.Add(new AppliedMigration(reader.GetString(0), reader.GetString(1), reader.GetDateTime(2)));
        }
        return applied;
    }

    // All-or-nothing: the script and its tracking row commit together or not at all.
    private static async Task RunInTransactionAsync(NpgsqlConnection conn, string label, string script, string trackingSql, MigrationFile migration)
    {
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
        try
        {
            await using (NpgsqlCommand run = new NpgsqlCommand(script, conn, tx))
            {
                await run.ExecuteNonQueryAsync();
            }
            await using (NpgsqlCommand track = new NpgsqlCommand(trackingSql, conn, tx))
            {
                track.Parameters.AddWithValue("@id", migration.Id);
                track.Parameters.AddWithValue("@name", migration.Name);
                await track.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch (PostgresException ex)
        {
            await tx.RollbackAsync();
            throw new MigrationException($"{label} failed and was rolled back: {ex.MessageText}", ex);
        }
    }
}
