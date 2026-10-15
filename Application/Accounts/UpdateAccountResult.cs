namespace Application.Accounts;

public abstract record UpdateAccountResult
{
    public sealed record Updated : UpdateAccountResult;
    public sealed record InvalidAmount : UpdateAccountResult;
    public sealed record Failed : UpdateAccountResult;
}
