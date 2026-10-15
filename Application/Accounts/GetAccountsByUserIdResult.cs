using Domain.Accounts;

namespace Application.Accounts;

public abstract record GetAccountsByUserIdResult
{
    public sealed record Found(IEnumerable<Account> Accounts) : GetAccountsByUserIdResult;
    public sealed record NotFound : GetAccountsByUserIdResult;
    public sealed record InvalidUserId : GetAccountsByUserIdResult;
    public sealed record Failed : GetAccountsByUserIdResult;
}
