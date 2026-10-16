namespace Application.Auth;

public abstract record AuthenticateResult
{
    public sealed record Found(AuthToken Token) : AuthenticateResult;
    public sealed record NotFound : AuthenticateResult;
    public sealed record Failed : AuthenticateResult;
}
