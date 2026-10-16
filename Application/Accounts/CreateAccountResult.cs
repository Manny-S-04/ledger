using Domain.Accounts;

namespace Application.Accounts;

public abstract record CreateAccountResult
{
    public sealed record Created(Account Account) : CreateAccountResult;
    public sealed record InvalidUserId : CreateAccountResult;
    public sealed record InvalidCurrency : CreateAccountResult;
    public sealed record Failed : CreateAccountResult;
}
