namespace Application.Providers;

// The provider's immediate answer to a charge request (not the final outcome).
public abstract record ProviderChargeResult
{
    // Accepted for processing; the outcome will arrive via callback.
    public sealed record Accepted(string ProviderReference) : ProviderChargeResult;

    // Refused outright (e.g. invalid request); no callback will follow.
    public sealed record Rejected(string Reason) : ProviderChargeResult;

    // The provider couldn't be reached or timed out (T-019.7: the top-up becomes Failed, client gets 502/503).
    public sealed record Unavailable(string Reason) : ProviderChargeResult;
}
