using Microsoft.Extensions.Configuration;
using Migrator;

// T-035 (M.8): dotnet run --project Migrator -- up | down [--to <id>] | status
// Connection string: ConnectionStrings:Ledger from appsettings.json, overridden by env ConnectionStrings__Ledger.
const string Usage = "usage: Migrator up | down [--to <id>] | status";

IConfigurationRoot configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

string? connectionString = configuration.GetConnectionString("Ledger");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Connection string 'Ledger' is missing (appsettings.json or env ConnectionStrings__Ledger).");
    return 2;
}

MigrationRunner runner = new MigrationRunner(connectionString, Path.Combine(AppContext.BaseDirectory, "Migrations"), Console.Out);

try
{
    switch (args)
    {
        case ["up"]:
            await runner.UpAsync();
            return 0;
        case ["down"]:
            await runner.DownAsync(null);
            return 0;
        case ["down", "--to", string toId]:
            await runner.DownAsync(toId);
            return 0;
        case ["status"]:
            await runner.StatusAsync();
            return 0;
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (Exception ex) when (ex is MigrationException or InvalidOperationException or Npgsql.NpgsqlException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
