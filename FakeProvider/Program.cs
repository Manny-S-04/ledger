using System.Text;
using FakeProvider.Data;
using FakeProvider.Delivery;
using FakeProvider.Endpoints;
using FakeProvider.Scenarios;
using Npgsql;

namespace FakeProvider;

// A stand-in for an external payment provider (F2). Runs as its own process; shares no code with the ledger.
public static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        string connectionString =
            builder.Configuration.GetConnectionString("Provider")
            ?? throw new InvalidOperationException("Connection string 'Provider' is missing.");

        // Env vars win over appsettings (T-022: callback URL and secret from config / env, never from the request).
        string callbackUrl =
            builder.Configuration["FAKE_PROVIDER_CALLBACK_URL"]
            ?? builder.Configuration["Ledger:CallbackUrl"]
            ?? throw new InvalidOperationException("Config 'FAKE_PROVIDER_CALLBACK_URL' / 'Ledger:CallbackUrl' is missing.");
        string secret =
            builder.Configuration["FAKE_PROVIDER_SHARED_SECRET"]
            ?? builder.Configuration["Webhook:Secret"]
            ?? throw new InvalidOperationException("Config 'FAKE_PROVIDER_SHARED_SECRET' / 'Webhook:Secret' is missing.");
        TimeSpan callbackTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Callbacks:TimeoutSeconds", 10));
        TimeSpan pollInterval = TimeSpan.FromMilliseconds(builder.Configuration.GetValue("Callbacks:PollIntervalMilliseconds", 1000));

        NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddSingleton<IChargeStore, NpgsqlChargeStore>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<ScenarioOptions>(builder.Configuration.GetSection(ScenarioOptions.Section));
        builder.Services.AddSingleton(new CallbackSettings(new Uri(callbackUrl), Encoding.UTF8.GetBytes(secret), pollInterval));
        builder.Services.AddHttpClient(CallbackDispatcher.HttpClientName, client => client.Timeout = callbackTimeout);
        builder.Services.AddHostedService<CallbackDispatcher>();

        WebApplication app = builder.Build();

        await InitDb(dataSource);

        app.MapGet("/", () => "FakeProvider");
        app.MapGet("/health", () => Results.Ok("healthy"));
        app.MapChargeEndpoints();

        await app.RunAsync();
    }

    private static async Task InitDb(NpgsqlDataSource dataSource)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        foreach (string sql in ChargeSchema.CreateTables)
        {
            await using NpgsqlCommand command = new(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
