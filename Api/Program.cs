using Api.EndPoints;
using Npgsql;
using Application.Interfaces;
using Application.Users;
using Infrastructure.User;

namespace Api;

public static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplication app = await GenerateApp(args);
        
        app.MapGet("/", (HttpContext httpContext) =>
        {
            return Results.Ok("Hello World!");
        });
        
        app.Run();
    }
    
    private static async Task<WebApplication> GenerateApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var connectionString =
            builder.Configuration.GetConnectionString("Ledger")
            ?? throw new InvalidOperationException(
                "Connection string 'Ledger' is missing.");

        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<GetUserHandler>();
        builder.Services.AddScoped<CreateUserHandler>();
        
        var app = builder.Build();
        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapUserEndpoints();
        
        var source = app.Services.GetRequiredService<NpgsqlDataSource>();
        await InitDb(source);
        
        return app;
    }

    public static async Task InitDb(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        
        var commands = new[]
        {
            @"
                CREATE TABLE IF NOT EXISTS users (
                    id SERIAL PRIMARY KEY,
                    email VARCHAR(255) UNIQUE NOT NULL,
                    password_hash TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                )",
            @"
            CREATE TABLE IF NOT EXISTS accounts (
                id SERIAL PRIMARY KEY,
                user_id INTEGER NOT NULL,
                balance DECIMAL(15, 2) NOT NULL,
                currency VARCHAR(3),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT fk_user_id FOREIGN KEY (user_id) REFERENCES users(id)
            )",
        };

        foreach (var commandText in commands)
        {
            await using var command = new NpgsqlCommand(commandText, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

/*
Project 1: Ledger

A wallet platform where users hold balances, top up from an external provider (you write a fake provider you can make misbehave), and transfer money.

Functional requirements

next> F1. Users can create accounts, one currency.
implement endpoints so that users can access their accounts and read the information
F2. Top-up starts from the client and completes when the provider calls back. The provider may call back late, twice, out of order, or never.
F3. A transfer between two accounts is atomic, and no account may go negative. Concurrent transfers on one account must not break this.
F4. Balance and a paginated statement can be queried, and a statement can be reproduced as of any past moment.
F5. Every state change is attributable (who, what, when) and provably unaltered afterwards.
F6. Users are notified of transfer outcomes. Notification failure never affects the transfer.
F7. Any client request may be retried any number of times, including after a timeout, with no duplicate effect.
F8. External partners can subscribe to events and receive them reliably even when they are slow or down. They can verify the events are authentic.
F9. A daily reconciliation against the provider's file produces a discrepancy report, and the system stays available while it runs.

Invariants (automated checker running during every load test)

I1. Total money in the system changes only through top-ups.
I2. No balance is below zero.
I3. Every client request has at most one effect.

Load profile

Steady: 100 transfers/s over 10k accounts, with 10 reads per write.
Hot: 30% of transfers touch the same 10 accounts.
Spike: 5× for 60 seconds, then back to normal.
Latency: reads p99 < 100 ms. For writes, define what "acknowledged" means to the user, then hold p99 < 500 ms to that definition.
A transfer the system has acknowledged is never lost.

Failure and operations

Killing any single component leaves the system self-recovering to a correct state.
One-command reproducible deploy to the VM.
A deploy with a schema change causes no downtime.
Within 60 seconds of a failure, telemetry alone tells you which component failed and why.
Restore drill: lose the datastore disk, restore, and state and measure your RPO and RTO.

Stage gates

Correct single-threaded.
20 rps concurrent with invariants green.
Full load profile.
Failure injection.
Deploy and restore drill.
*/
