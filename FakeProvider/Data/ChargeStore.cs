namespace FakeProvider.Data;

// A charge the provider has accepted. Persisted in the provider's own database (T-024.2 b).
public sealed record Charge(
    string ProviderReference,
    string Reference,
    decimal Amount,
    string Currency,
    string Status,
    string? FailureReason,
    long Sequence,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Storage for charges. Implemented against the provider's database in T-022.
public interface IChargeStore
{
    public Task SaveAsync(Charge charge);
    public Task<Charge?> GetAsync(string providerReference);
    public Task UpdateStatusAsync(string providerReference, string status, string? failureReason, long sequence);
}

public static class ChargeSchema
{
    // The provider's own database (`fakeprovider`), separate from the ledger's.
    public const string CreateTables = """
        CREATE TABLE IF NOT EXISTS charges (
            provider_reference TEXT PRIMARY KEY,
            reference TEXT NOT NULL,
            amount NUMERIC(15, 2) NOT NULL,
            currency VARCHAR(3) NOT NULL,
            status TEXT NOT NULL,
            failure_reason TEXT NULL,
            sequence BIGINT NOT NULL DEFAULT 0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        )
        """;
}
