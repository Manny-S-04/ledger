using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Users;

namespace Application.Accounts;

public class GetAccountsByEmailHandler
{
    private readonly IAccountRepository _accountRepository;

    public GetAccountsByEmailHandler(IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    public async Task<GetAccountsByEmailResult> Handle(GetAccountsByEmailQuery query)
    {
        Email email;
        try
        {
            email = Email.Create(query.Email);
        }
        catch (ArgumentException)
        {
            return new GetAccountsByEmailResult.InvalidEmail();
        }

        DbResult<IEnumerable<Account>> accounts = await _accountRepository.GetAccountsByEmailAsync(email);
        return accounts switch
        {
            DbResult<IEnumerable<Account>>.Success success when success.Value.Any() =>
                new GetAccountsByEmailResult.Found(success.Value),
            DbResult<IEnumerable<Account>>.Success => new GetAccountsByEmailResult.NotFound(),
            _ => new GetAccountsByEmailResult.Failed()
        };
    }
}
