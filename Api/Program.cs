using Api.EndPoints;
using Npgsql;
using Application.Interfaces;
using Application.Users;
using Infrastructure.User;

namespace Api;

//A wallet platform where users hold balances,
//top up from an external provider
//(you write a fake provider you can make misbehave),
//and transfer money.
public class Program
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
        
        await using var command = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS users (
                id SERIAL PRIMARY KEY,
                email VARCHAR(255) UNIQUE NOT NULL,
                password_hash TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )", 
            connection);
        
        await command.ExecuteNonQueryAsync();
    }
}
