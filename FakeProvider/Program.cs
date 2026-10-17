using FakeProvider.Endpoints;

namespace FakeProvider;

// A stand-in for an external payment provider (F2). Runs as its own process; shares no code with the ledger.
public static class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        WebApplication app = builder.Build();

        app.MapGet("/", () => "FakeProvider");
        app.MapChargeEndpoints();

        app.Run();
    }
}
