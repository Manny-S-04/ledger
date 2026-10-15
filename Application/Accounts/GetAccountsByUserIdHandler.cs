using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Users;

namespace Application.Accounts;

public class GetAccountsByUserIdHandler
{
    private readonly IAccountRepository _accountRepository;

    public GetAccountsByUserIdHandler(IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    public async Task<GetAccountsByUserIdResult> Handle(GetAccountsByUserIdQuery query)
    {
        UserId? userId = UserId.TryCreate(query.UserId);
        if (userId is null)
        {
            return new GetAccountsByUserIdResult.InvalidUserId();
        }

        DbResult<IEnumerable<Account>> accounts = await _accountRepository.GetAccountsByUserIdAsync(userId.Value);
        return accounts switch
        {
            DbResult<IEnumerable<Account>>.Success success => new GetAccountsByUserIdResult.Found(success.Value),
            _ => new GetAccountsByUserIdResult.Failed()
        };
    }
}
