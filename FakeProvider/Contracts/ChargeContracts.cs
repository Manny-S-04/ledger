namespace FakeProvider.Contracts;

// The provider's public HTTP contract. Deliberately duplicated from the ledger, not shared:
// FakeProvider stands in for another company and references none of the ledger's projects.

// Ledger -> provider: please charge this amount. Reference = the ledger's id for the top-up.
public sealed record ChargeRequest(string Reference, decimal Amount, string Currency);

// Provider -> ledger, immediate answer to ChargeRequest.
public sealed record ChargeAccepted(string ProviderReference);

// Provider -> ledger, answer to GET /charges/{providerReference}. Status: Processing | Succeeded | Failed.
public sealed record ChargeStatusResponse(string ProviderReference, string Status, string? FailureReason);

// Provider -> ledger callback (webhook), POSTed to the configured ledger callback URL.
// Status: Succeeded | Failed. FailureReason: InsufficientFunds | CardDeclined | ProcessingError.
// Sequence: per-charge event counter, so the ledger can order callbacks.
public sealed record CallbackPayload(
    string ProviderReference,
    string Reference,
    string Status,
    string? FailureReason,
    decimal Amount,
    string Currency,
    long Sequence);
