namespace Application.Providers;

// What the provider reports for a charge when asked directly.
public abstract record ProviderChargeStatus
{
    public sealed record Processing : ProviderChargeStatus;
    public sealed record Succeeded : ProviderChargeStatus;
    public sealed record Failed(ProviderFailureReason Reason) : ProviderChargeStatus;
    public sealed record NotFound : ProviderChargeStatus;
    public sealed record Unavailable(string Reason) : ProviderChargeStatus;
}
