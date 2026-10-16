using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Users;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Application.Accounts;

public class GetAccountsByEmailHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<GetAccountsByEmailHandler> _logger;

    public GetAccountsByEmailHandler(IAccountRepository accountRepository, ILogger<GetAccountsByEmailHandler> logger)
    {
        _logger = logger;
        _accountRepository = accountRepository;
    }

    public async Task<GetAccountsByEmailResult> Handle(GetAccountsByEmailQuery query)
    {
        _logger.Info("hit");
        Email? email = Email.TryCreate(query.Email);
        if (email is null)
        {
            _logger.Info("result=InvalidEmail");
            return new GetAccountsByEmailResult.InvalidEmail();
        }

        DbResult<IEnumerable<Account>> accounts = await _accountRepository.GetAccountsByEmailAsync(email);
        GetAccountsByEmailResult result = accounts switch
        {
            DbResult<IEnumerable<Account>>.Success success => new GetAccountsByEmailResult.Found(success.Value),
            DbResult<IEnumerable<Account>>.Failure { Error: DbError.NotFound } => new GetAccountsByEmailResult.NotFound(),
            _ => new GetAccountsByEmailResult.Failed()
        };
        _logger.Info($"result={result.GetType().Name}");
        return result;
    }
}
