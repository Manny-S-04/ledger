using Api.Auth;
using Api.EndPoints;
using Npgsql;
using Application.Accounts;
using Application.Auth;
using Application.Interfaces;
using Application.Users;
using Infrastructure.Account;
using Infrastructure.Auth;
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
        var jwtKey =
            builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Config 'Jwt:Key' is missing.");
        var jwtExpiryMinutes = builder.Configuration.GetValue("Jwt:ExpiryMinutes", 15);

        builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        builder.Services.AddSingleton<ITokenService>(_ =>
            new HmacJwtTokenService(System.Text.Encoding.UTF8.GetBytes(jwtKey), TimeSpan.FromMinutes(jwtExpiryMinutes)));
        builder.Services
            .AddAuthentication(TokenAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TokenAuthenticationHandler>(
                TokenAuthenticationHandler.SchemeName, null);
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<GetUserHandler>();
        builder.Services.AddScoped<CreateUserHandler>();
        builder.Services.AddScoped<AuthenticateHandler>();
        builder.Services.AddScoped<IAccountRepository, AccountRepository>();
        builder.Services.AddScoped<CreateAccountHandler>();
        builder.Services.AddScoped<DeleteAccountHandler>();
        builder.Services.AddScoped<GetAccountsByEmailHandler>();
        builder.Services.AddScoped<GetAccountsByUserIdHandler>();
        
        var app = builder.Build();
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapUserEndpoints();
        app.MapAccountEndpoints();
        
        var source = app.Services.GetRequiredService<NpgsqlDataSource>();
        await InitDb(source, app.Services.GetRequiredService<IPasswordHasher>());
        
        return app;
    }

    public static async Task InitDb(NpgsqlDataSource dataSource, IPasswordHasher passwordHasher)
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
        await SeedDb(dataSource, passwordHasher);
    }

    private static async Task SeedDb(NpgsqlDataSource dataSource, IPasswordHasher passwordHasher)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        await using var countCommand = new NpgsqlCommand("SELECT COUNT(*) FROM users", connection);
        object? countObj = await countCommand.ExecuteScalarAsync();
        if(countObj is not long count) throw new Exception("Failed to execute count in seeddb");

        if (count == 0)
        {
            var insertCommand = new NpgsqlCommand(@"
                    INSERT INTO users (id, email, password_hash, created_at) 
                    VALUES 
                    (1, 'user1@example.com', @hash1, '2023-01-01T00:00:00Z'),
                    (2, 'user2@example.com', @hash2, '2023-01-01T00:00:00Z'),
                    (3, 'user3@example.com', @hash3, '2023-01-01T00:00:00Z'),
                    (4, 'user4@example.com', @hash4, '2023-01-01T00:00:00Z'),
                    (5, 'user5@example.com', @hash5, '2023-01-01T00:00:00Z'),
                    (6, 'user6@example.com', @hash6, '2023-01-01T00:00:00Z'),
                    (7, 'user7@example.com', @hash7, '2023-01-01T00:00:00Z'),
                    (8, 'user8@example.com', @hash8, '2023-01-01T00:00:00Z'),
                    (9, 'user9@example.com', @hash9, '2023-01-01T00:00:00Z'),
                    (10, 'user10@example.com', @hash10, '2023-01-01T00:00:00Z')
                    ", connection);

            for (int i = 1; i <= 10; i++)
            {
                insertCommand.Parameters.AddWithValue($"@hash{i}", passwordHasher.Hash($"password{i}"));
            }

            await insertCommand.ExecuteNonQueryAsync();
        }
        
        await using var accountCountCommand = new NpgsqlCommand("SELECT COUNT(*) FROM accounts", connection);
        object? accountCountObj = await accountCountCommand.ExecuteScalarAsync();
        if(accountCountObj is not long accountCount) throw new Exception("Failed to execute count in seeddb for accounts");

        if (accountCount == 0)
        {
            var insertAccountsCommand = new NpgsqlCommand(@"
                    INSERT INTO accounts (user_id, balance, currency) 
                    VALUES 
                    (1, 100.00, 'USD'),
                    (2, 250.50, 'EUR'),
                    (3, 500.00, 'GBP'),
                    (4, 75.25, 'JPY'),
                    (5, 1200.00, 'CAD'),
                    (6, 300.75, 'AUD'),
                    (7, 950.25, 'CHF'),
                    (8, 425.00, 'SEK'),
                    (9, 675.50, 'NOK'),
                    (10, 800.00, 'DKK')
                    ", connection);

            await insertAccountsCommand.ExecuteNonQueryAsync();
        }
    }
}

/*
Project 1: Ledger

A wallet platform where users hold balances, top up from an external provider (you write a fake provider you can make misbehave), and transfer money.

Functional requirements

F1. Users can create accounts, one currency.
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
