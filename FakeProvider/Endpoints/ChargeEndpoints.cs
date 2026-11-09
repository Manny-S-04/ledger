using System.Text.Json;
using FakeProvider.Contracts;
using FakeProvider.Data;
using FakeProvider.Scenarios;
using Microsoft.Extensions.Options;

namespace FakeProvider.Endpoints;

public static class ChargeEndpoints
{
    private static readonly JsonSerializerOptions CallbackJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapChargeEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/charges");

        group.MapPost("/", CreateCharge);
        group.MapGet("/{chargeId}", GetCharge);

        return app;
    }

    // Accepts the charge, decides its outcome now, and schedules its callback(s). The outcome reaches the ledger
    // only by callback. Immediate errors: 400 for an invalid request, 500 when the charge can't be stored.
    private static async Task<IResult> CreateCharge(
        ChargeRequest request,
        IChargeStore store,
        IOptions<ScenarioOptions> scenarioOptions,
        IHostEnvironment environment,
        TimeProvider timeProvider,
        ILogger<ChargeRequest> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reference)
            || request.AmountPence <= 0
            || string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Trim().Length != 3)
        {
            logger.LogInformation("Charge rejected: invalid request reference={Reference}", request.Reference);
            return Results.Problem(statusCode: 400, title: "invalid charge request");
        }

        ScenarioOptions options = scenarioOptions.Value;
        Scenario scenario;
        if (IsTestEnvironment(environment) && (request.Scenario ?? options.Force) is string named)
        {
            if (ScenarioNames.TryParse(named) is not Scenario parsed)
            {
                logger.LogInformation("Charge rejected: unknown scenario {Scenario}", named);
                return Results.Problem(statusCode: 400, title: "unknown scenario");
            }
            scenario = parsed;
        }
        else
        {
            // Outside Development/Test a named scenario is ignored (T-022.5).
            scenario = ScenarioPicker.PickRandom(options.Weights, Random.Shared);
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        string chargeId = $"ch_{Guid.NewGuid():N}";
        string reference = request.Reference.Trim();
        string currency = request.Currency.Trim().ToUpperInvariant();

        bool succeeds = scenario != Scenario.FailedCallback;
        string status = succeeds ? "Succeeded" : "Failed";
        string? failureReason = succeeds ? null : "InsufficientFunds";

        TimeSpan delay = scenario switch
        {
            Scenario.LateSuccess => TimeSpan.FromSeconds(options.LateSuccessDelaySeconds),
            Scenario.OutOfOrder => TimeSpan.FromSeconds(Random.Shared.Next(options.OutOfOrderMaxDelaySeconds + 1)),
            _ => TimeSpan.FromSeconds(options.ProcessingDelaySeconds),
        };
        DateTime outcomeAt = now + delay;

        // One callback event per charge, so Sequence is 1.
        long sequence = scenario == Scenario.Never ? 0 : 1;
        List<NewDelivery> deliveries = [];
        if (scenario != Scenario.Never)
        {
            string eventId = $"evt_{Guid.NewGuid():N}";
            CallbackPayload payload = new(eventId, chargeId, reference, status, failureReason, currency, sequence);
            string payloadJson = JsonSerializer.Serialize(payload, CallbackJson);

            deliveries.Add(new NewDelivery(eventId, payloadJson, outcomeAt));
            if (scenario == Scenario.Duplicate)
            {
                deliveries.Add(new NewDelivery(eventId, payloadJson, outcomeAt + TimeSpan.FromSeconds(options.DuplicateGapSeconds)));
            }
        }

        Charge charge = new(chargeId, reference, request.AmountPence, currency, ScenarioNames.ToName(scenario),
            status, failureReason, sequence, outcomeAt, now);
        try
        {
            await store.SaveAsync(charge, deliveries, cancellationToken);
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or InvalidOperationException)
        {
            logger.LogError(exception, "Charge could not be stored reference={Reference}", reference);
            return Results.Problem(statusCode: 500, title: "charge could not be created");
        }

        logger.LogInformation(
            "Charge accepted chargeId={ChargeId} reference={Reference} amountPence={AmountPence} scenario={Scenario}",
            chargeId, reference, request.AmountPence, charge.Scenario);
        return Results.Accepted(value: new ChargeAccepted(chargeId));
    }

    private static async Task<IResult> GetCharge(
        string chargeId,
        IChargeStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Charge? charge = await store.GetAsync(chargeId, cancellationToken);
        if (charge is null)
        {
            return Results.NotFound();
        }

        IReadOnlyList<Data.Delivery> deliveries = await store.GetDeliveriesAsync(chargeId, cancellationToken);
        bool decided = timeProvider.GetUtcNow().UtcDateTime >= charge.OutcomeAt;

        return Results.Ok(new ChargeStatusResponse(
            charge.ProviderReference,
            decided ? charge.Status : "Processing",
            decided ? charge.FailureReason : null,
            charge.Reference,
            charge.AmountPence,
            charge.Currency,
            charge.Scenario,
            charge.Sequence,
            charge.OutcomeAt,
            deliveries.Select(d => new DeliveryDiagnostics(d.EventId, d.Status, d.Attempts, d.NextAttemptAt, d.LastError)).ToList()));
    }

    private static bool IsTestEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Test");
}
