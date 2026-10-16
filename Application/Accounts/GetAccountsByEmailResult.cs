using Domain.Accounts;

namespace Application.Accounts;

public abstract record GetAccountsByEmailResult
{
    public sealed record Found(IEnumerable<Account> Accounts) : GetAccountsByEmailResult;
    public sealed record NotFound : GetAccountsByEmailResult;
    public sealed record InvalidEmail : GetAccountsByEmailResult;
    public sealed record Failed : GetAccountsByEmailResult;
}
