namespace Application.Accounts;

public abstract record DeleteAccountResult
{
    // T-031: the account is closed, now or already (both 204).
    public sealed record Closed : DeleteAccountResult;
    public sealed record NotFound : DeleteAccountResult;
    public sealed record Forbidden : DeleteAccountResult;
    public sealed record HasBalance : DeleteAccountResult;
    // A pending top-up, or an expired one the provider may still pay (T-031.3, T-031.5).
    public sealed record PendingTopups : DeleteAccountResult;
    // The provider couldn't be asked, so the close can't be proven safe (T-031.6).
    public sealed record ProviderUnavailable : DeleteAccountResult;
    public sealed record Failed : DeleteAccountResult;
}
