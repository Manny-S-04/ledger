using FakeProvider.Contracts;

namespace FakeProvider.Endpoints;

// Skeleton only (T-024): fixed answers, no storage, no background work, no callbacks. T-022 adds the logic.
public static class ChargeEndpoints
{
    public static IEndpointRouteBuilder MapChargeEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/charges");

        group.MapPost("/", (ChargeRequest request) =>
            Results.Accepted(value: new ChargeAccepted($"prov-{Guid.NewGuid():N}")));

        group.MapGet("/{providerReference}", (string providerReference) =>
            Results.Ok(new ChargeStatusResponse(providerReference, "Processing", null)));

        return app;
    }
}
