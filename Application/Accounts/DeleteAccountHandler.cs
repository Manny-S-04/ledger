using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Application.Accounts;

public class DeleteAccountHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<DeleteAccountHandler> _logger;

    public DeleteAccountHandler(IAccountRepository accountRepository, ILogger<DeleteAccountHandler> logger)
    {
        _logger = logger;
        _accountRepository = accountRepository;
    }

    public async Task<DeleteAccountResult> Handle(DeleteAccountCommand command)
    {
        _logger.Info($"hit accountId={command.AccountId} requestingUserId={command.RequestingUserId}");
        DbResult<UserId> owner = await _accountRepository.GetAccountOwnerAsync(command.AccountId);
        switch (owner)
        {
            case DbResult<UserId>.Failure { Error: DbError.NotFound }:
                _logger.Info("result=NotFound");
                return new DeleteAccountResult.NotFound();
            case DbResult<UserId>.Success success when success.Value.Value != command.RequestingUserId:
                _logger.Info("result=Forbidden");
                return new DeleteAccountResult.Forbidden();
            case DbResult<UserId>.Success:
                break;
            case var _:
                _logger.Info("result=Failed");
                return new DeleteAccountResult.Failed();
        }

        DbResult<bool> deleted = await _accountRepository.DeleteAccountAsync(command.AccountId);
        DeleteAccountResult result = deleted switch
        {
            DbResult<bool>.Success { Value: true } => new DeleteAccountResult.Deleted(),
            DbResult<bool>.Success { Value: false } => new DeleteAccountResult.NotFound(),
            _ => new DeleteAccountResult.Failed()
        };
        _logger.Info($"result={result.GetType().Name}");
        return result;
    }
}
