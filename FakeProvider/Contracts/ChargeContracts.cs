namespace FakeProvider.Contracts;

// The provider's public HTTP contract. Deliberately duplicated from the ledger, not shared:
// FakeProvider stands in for another company and references none of the ledger's projects.

// Ledger -> provider: please charge this amount. Reference = the ledger's id for the top-up.
// Scenario: test-only override of the random outcome; honoured only in Development/Test, ignored elsewhere.
public sealed record ChargeRequest(string? Reference, long AmountPence, string? Currency, string? Scenario);

// Provider -> ledger, immediate answer to ChargeRequest. ProviderReference = the provider-generated charge id.
public sealed record ChargeAccepted(string ProviderReference);

// Provider -> ledger, answer to GET /charges/{chargeId}. Status: Processing | Succeeded | Failed.
// The remaining fields are diagnostics.
public sealed record ChargeStatusResponse(
    string ProviderReference,
    string Status,
    string? FailureReason,
    string Reference,
    long AmountPence,
    string Currency,
    string Scenario,
    long Sequence,
    DateTime OutcomeAt,
    IReadOnlyList<DeliveryDiagnostics> Deliveries);

public sealed record DeliveryDiagnostics(string EventId, string Status, int Attempts, DateTime NextAttemptAt, string? LastError);

// Provider -> ledger callback (webhook), POSTed to the configured ledger callback URL.
// EventId: provider-generated per callback; every resend (retry or duplicate) reuses it.
// ProviderReference: the charge id returned by POST /charges. No amount: the ledger already holds it.
// Status: Succeeded | Failed. FailureReason: InsufficientFunds | CardDeclined | ProcessingError.
// Sequence: per-charge event counter, so the ledger can order callbacks.
public sealed record CallbackPayload(
    string EventId,
    string ProviderReference,
    string Reference,
    string Status,
    string? FailureReason,
    string Currency,
    long Sequence);
