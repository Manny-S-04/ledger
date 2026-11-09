namespace FakeProvider.Data;

// A charge the provider has accepted. Persisted in the provider's own database (T-024.2 b).
// Status is the outcome decided at creation; it is reported as Processing until OutcomeAt.
public sealed record Charge(
    string ProviderReference,
    string Reference,
    long AmountPence,
    string Currency,
    string Scenario,
    string Status,
    string? FailureReason,
    long Sequence,
    DateTime OutcomeAt,
    DateTime CreatedAt);

// One scheduled send of a callback. A duplicate is a second row with the same EventId and Payload.
public sealed record NewDelivery(string EventId, string Payload, DateTime NextAttemptAt);

public sealed record Delivery(
    long Id,
    string EventId,
    string ProviderReference,
    string Payload,
    string Status,
    int Attempts,
    DateTime NextAttemptAt,
    string? LastError);

public static class DeliveryStatus
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string GaveUp = "gave_up";
}

public interface IChargeStore
{
    // The charge and its scheduled callbacks are saved together, so a restart never loses a pending callback.
    public Task SaveAsync(Charge charge, IReadOnlyList<NewDelivery> deliveries, CancellationToken cancellationToken = default);
    public Task<Charge?> GetAsync(string providerReference, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<Delivery>> GetDeliveriesAsync(string providerReference, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<Delivery>> GetDueDeliveriesAsync(DateTime now, int limit, CancellationToken cancellationToken = default);
    public Task MarkDeliveredAsync(long deliveryId, int attempts, CancellationToken cancellationToken = default);
    // nextAttemptAt null = no retries left (gave_up).
    public Task RecordFailedAttemptAsync(long deliveryId, int attempts, DateTime? nextAttemptAt, string error, CancellationToken cancellationToken = default);
}

public static class ChargeSchema
{
    // The provider's own database (`fakeprovider`), separate from the ledger's.
    public static readonly string[] CreateTables =
    [
        """
        CREATE TABLE IF NOT EXISTS charges (
            provider_reference TEXT PRIMARY KEY,
            reference TEXT NOT NULL,
            amount_pence BIGINT NOT NULL,
            currency TEXT NOT NULL,
            scenario TEXT NOT NULL,
            status TEXT NOT NULL,
            failure_reason TEXT NULL,
            sequence BIGINT NOT NULL DEFAULT 0,
            outcome_at TIMESTAMPTZ NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS callback_deliveries (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            event_id TEXT NOT NULL,
            provider_reference TEXT NOT NULL REFERENCES charges(provider_reference),
            payload TEXT NOT NULL,
            status TEXT NOT NULL,
            attempts INTEGER NOT NULL DEFAULT 0,
            next_attempt_at TIMESTAMPTZ NOT NULL,
            last_error TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT ck_callback_deliveries_status CHECK (status IN ('pending', 'delivered', 'gave_up'))
        )
        """,
        """
        CREATE INDEX IF NOT EXISTS ix_callback_deliveries_pending_next_attempt_at
            ON callback_deliveries (next_attempt_at) WHERE status = 'pending'
        """,
    ];
}
