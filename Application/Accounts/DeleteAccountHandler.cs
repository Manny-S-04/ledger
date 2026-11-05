using Application.DbResult;
using Application.Interfaces;
using Application.Providers;
using Domain.Accounts;
using Domain.Users;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Application.Accounts;

public class DeleteAccountHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly IProviderService _providerService;
    private readonly ILogger<DeleteAccountHandler> _logger;

    public DeleteAccountHandler(IAccountRepository accountRepository, IProviderService providerService, ILogger<DeleteAccountHandler> logger)
    {
        _providerService = providerService;
        _logger = logger;
        _accountRepository = accountRepository;
    }

    public async Task<DeleteAccountResult> Handle(DeleteAccountCommand command)
    {
        _logger.Info($"Deleting accountId={command.AccountId} requested by userId={command.RequestingUserId}");
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

        // T-031.12: the provider is asked before the account row is locked, never while holding the lock.
        DbResult<CloseBlockers> blockersResult = await _accountRepository.GetCloseBlockersAsync(command.AccountId);
        CloseBlockers blockers;
        switch (blockersResult)
        {
            case DbResult<CloseBlockers>.Success success: blockers = success.Value; break;
            case DbResult<CloseBlockers>.Failure { Error: DbError.NotFound }:
                _logger.Info("result=NotFound");
                return new DeleteAccountResult.NotFound();
            default:
                _logger.Info("result=Failed");
                return new DeleteAccountResult.Failed();
        }

        if (blockers.AlreadyClosed)
        {
            _logger.Info("result=Closed (already closed)");
            return new DeleteAccountResult.Closed();
        }

        if (blockers.HasPending)
        {
            _logger.Info("result=PendingTopups (pending)");
            return new DeleteAccountResult.PendingTopups();
        }

        HashSet<TransactionId> cleared = new HashSet<TransactionId>();
        foreach (ExpiredTopup expired in blockers.Expired)
        {
            if (expired.ProviderReference is null)
            {
                _logger.Info($"result=PendingTopups (expired transactionId={expired.Id.Value} has no provider reference)");
                return new DeleteAccountResult.PendingTopups();
            }

            ProviderChargeStatus status = await _providerService.GetChargeStatusAsync(expired.ProviderReference);
            switch (status)
            {
                case ProviderChargeStatus.Failed or ProviderChargeStatus.NotFound:
                    cleared.Add(expired.Id);
                    break;
                case ProviderChargeStatus.Unavailable:
                    _logger.Info($"result=ProviderUnavailable transactionId={expired.Id.Value}");
                    return new DeleteAccountResult.ProviderUnavailable();
                default:
                    _logger.Info($"result=PendingTopups (expired transactionId={expired.Id.Value} provider status={status.GetType().Name})");
                    return new DeleteAccountResult.PendingTopups();
            }
        }

        DbResult<CloseAccountOutcome> closed = await _accountRepository.CloseAccountAsync(command.AccountId, cleared);
        DeleteAccountResult result = closed switch
        {
            DbResult<CloseAccountOutcome>.Success { Value: CloseAccountOutcome.Closed or CloseAccountOutcome.AlreadyClosed } => new DeleteAccountResult.Closed(),
            DbResult<CloseAccountOutcome>.Success { Value: CloseAccountOutcome.HasBalance } => new DeleteAccountResult.HasBalance(),
            DbResult<CloseAccountOutcome>.Success { Value: CloseAccountOutcome.PendingTopups } => new DeleteAccountResult.PendingTopups(),
            DbResult<CloseAccountOutcome>.Failure { Error: DbError.NotFound } => new DeleteAccountResult.NotFound(),
            _ => new DeleteAccountResult.Failed()
        };
        _logger.Info($"result={result.GetType().Name}");
        return result;
    }
}
