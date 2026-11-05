using Application.DbResult;
using Application.Interfaces;
using Application.Providers;
using Domain.Accounts;
using Domain.Users;

namespace Application.Accounts;

public class TopupAccountHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly IProviderService _providerService;
    
    public TopupAccountHandler(IAccountRepository accountRepository, IProviderService providerService)
    {
        _accountRepository = accountRepository;
        _providerService = providerService;
    }

    public async Task<TopupAccountResult> Handle(TopupAccountCommand command)
    {
        if (command.Amount.Pence <= 0)
        {
            return new TopupAccountResult.Failed("Amount must be positive");
        }

        UserId userId;
        DbResult<UserId> userIdResult = await _accountRepository.GetAccountOwnerAsync(command.AccountId.Value);

        switch(userIdResult) 
        {
            case DbResult<UserId>.Success success: userId = success.Value; break;
            case DbResult<UserId>.Failure { Error: DbError.NotFound }: return new TopupAccountResult.Failed("Account not found");
            default: return new TopupAccountResult.Failed("Failed to get account owner"); // perhaps bubble up the result error
        }

        if(!Equals(command.UserId.Value, userId.Value))
        {
            return new TopupAccountResult.Failed("User does not own the account");
        }

        DbResult<RecordTopupOutcome> recordResult = await _accountRepository.RecordTopupAttempt(command.AccountId, command.Amount, command.Currency);
        TransactionId transactionId;
        switch (recordResult)
        {
            case DbResult<RecordTopupOutcome>.Success { Value: RecordTopupOutcome.Recorded recorded }: transactionId = recorded.TransactionId; break;
            case DbResult<RecordTopupOutcome>.Success { Value: RecordTopupOutcome.AccountClosed }: return new TopupAccountResult.AccountClosed();
            default: return new TopupAccountResult.Failed("Failed to record the top up");
        }

        ProviderChargeRequest request = new(transactionId, command.Amount, command.Currency);

        ProviderChargeResult chargeResult = await _providerService.RequestChargeAsync(request);

        if (chargeResult is ProviderChargeResult.Accepted accepted
            && await _accountRepository.SetTopupProviderReferenceAsync(transactionId, accepted.ProviderReference) is not DbResult<bool>.Success)
        {
            return new TopupAccountResult.Failed("Failed to store the provider reference");
        }

        return chargeResult switch
        {
            ProviderChargeResult.Accepted => new TopupAccountResult.Pending(),
            ProviderChargeResult.Rejected rejected => new TopupAccountResult.Failed(rejected.Reason),
            ProviderChargeResult.Unavailable unavailable => new TopupAccountResult.Failed(unavailable.Reason),
            _ => new TopupAccountResult.Failed("Failed to contact provider, please try again"),
        };
    }
}
