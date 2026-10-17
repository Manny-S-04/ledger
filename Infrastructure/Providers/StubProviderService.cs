using Application.Interfaces;
using Application.Providers;

namespace Infrastructure.Providers;

// Placeholder until the HTTP client to FakeProvider is written: accepts every charge and reports it as
// still processing. No network calls.
public sealed class StubProviderService : IProviderService
{
    public Task<ProviderChargeResult> RequestChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<ProviderChargeResult>(new ProviderChargeResult.Accepted($"stub-{request.Reference}"));

    public Task<ProviderChargeStatus> GetChargeStatusAsync(string providerReference, CancellationToken cancellationToken = default) =>
        Task.FromResult<ProviderChargeStatus>(new ProviderChargeStatus.Processing());
}
