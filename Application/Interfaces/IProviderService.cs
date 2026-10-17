using Application.Providers;

namespace Application.Interfaces;

// The ledger's port to the external payment provider (T-024). The implementation in Infrastructure
// talks HTTP to FakeProvider; a real provider would be a different implementation of this interface.
public interface IProviderService
{
    // Ask the provider to charge the user's external funding source. The provider answers immediately
    // with Accepted/Rejected/Unavailable; the final outcome arrives later via the callback (ProviderCallback).
    public Task<ProviderChargeResult> RequestChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken = default);

    // Ask the provider what it currently thinks happened to a charge (for expiry checks / reconciliation).
    public Task<ProviderChargeStatus> GetChargeStatusAsync(string providerReference, CancellationToken cancellationToken = default);
}
