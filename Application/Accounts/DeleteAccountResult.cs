namespace Application.Accounts;

public abstract record DeleteAccountResult
{
    public sealed record Deleted : DeleteAccountResult;
    public sealed record NotFound : DeleteAccountResult;
    public sealed record Forbidden : DeleteAccountResult;
    public sealed record HasBalance : DeleteAccountResult;
    public sealed record Failed : DeleteAccountResult;
}
