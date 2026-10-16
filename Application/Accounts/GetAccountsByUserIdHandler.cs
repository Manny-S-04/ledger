using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Users;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Application.Accounts;

public class GetAccountsByUserIdHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<GetAccountsByUserIdHandler> _logger;

    public GetAccountsByUserIdHandler(IAccountRepository accountRepository, ILogger<GetAccountsByUserIdHandler> logger)
    {
        _logger = logger;
        _accountRepository = accountRepository;
    }

    public async Task<GetAccountsByUserIdResult> Handle(GetAccountsByUserIdQuery query)
    {
        _logger.Info($"Getting accounts for userId={query.UserId}");
        UserId? userId = UserId.TryCreate(query.UserId);
        if (userId is null)
        {
            _logger.Info("result=InvalidUserId");
            return new GetAccountsByUserIdResult.InvalidUserId();
        }

        DbResult<IEnumerable<Account>> accounts = await _accountRepository.GetAccountsByUserIdAsync(userId.Value);
        GetAccountsByUserIdResult result = accounts switch
        {
            DbResult<IEnumerable<Account>>.Success success => new GetAccountsByUserIdResult.Found(success.Value),
            _ => new GetAccountsByUserIdResult.Failed()
        };
        _logger.Info($"result={result.GetType().Name}");
        return result;
    }
}
