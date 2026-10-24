namespace Application.Accounts;

public abstract record TopupAccountResult
{
    public sealed record Pending : TopupAccountResult;
    public sealed record InvalidCurrency : TopupAccountResult;
    public sealed record Failed(string Error) : TopupAccountResult;
}
