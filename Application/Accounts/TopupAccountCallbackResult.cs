namespace Application.Accounts;

public abstract record TopupAccountCallbackResult
{
    public sealed record Credited : TopupAccountCallbackResult;
    public sealed record MarkedFailed : TopupAccountCallbackResult;
    public sealed record AlreadyProcessed : TopupAccountCallbackResult;
    public sealed record LateSuccessOnExpired : TopupAccountCallbackResult;
    public sealed record NotFound : TopupAccountCallbackResult;
    public sealed record Failed : TopupAccountCallbackResult;
}
