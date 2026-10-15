namespace Application.Accounts;

public abstract record DeleteAccountResult
{
    public sealed record Deleted : DeleteAccountResult;
    public sealed record Failed : DeleteAccountResult;
}
