using Application.DbResult;
using Application.Interfaces;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Application.Accounts;

public sealed class TopupAccountCallbackHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<TopupAccountCallbackHandler> _logger;

    public TopupAccountCallbackHandler(IAccountRepository accountRepository, ILogger<TopupAccountCallbackHandler> logger)
    {
        _accountRepository = accountRepository;
        _logger = logger;
    }

    public async Task<TopupAccountCallbackResult> Handle(TopupAccountCallbackCommand command)
    {
        _logger.Info($"Processing topup callback for transactionId={command.TransactionId.Value}, status={command.Status}");

        DbResult<TopupCallbackOutcome> result = await _accountRepository.TopUpAccountAsync(command.TransactionId, command.Status);

        TopupAccountCallbackResult response = result switch
        {
            DbResult<TopupCallbackOutcome>.Success { Value: TopupCallbackOutcome.Credited } => new TopupAccountCallbackResult.Credited(),
            DbResult<TopupCallbackOutcome>.Success { Value: TopupCallbackOutcome.MarkedFailed } => new TopupAccountCallbackResult.MarkedFailed(),
            DbResult<TopupCallbackOutcome>.Success { Value: TopupCallbackOutcome.AlreadyProcessed } => new TopupAccountCallbackResult.AlreadyProcessed(),
            DbResult<TopupCallbackOutcome>.Success { Value: TopupCallbackOutcome.LateSuccessOnExpired } => new TopupAccountCallbackResult.LateSuccessOnExpired(),
            DbResult<TopupCallbackOutcome>.Failure { Error: DbError.NotFound } => new TopupAccountCallbackResult.NotFound(),
            _ => new TopupAccountCallbackResult.Failed(),
        };

        _logger.Info($"result={response.GetType().Name}");
        return response;
    }
}
