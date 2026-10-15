using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Datatypes;

namespace Application.Accounts;

public class UpdateAccountHandler
{
    private readonly IAccountRepository _accountRepository;

    public UpdateAccountHandler(IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    public async Task<UpdateAccountResult> Handle(UpdateAccountCommand command)
    {
        if (!Amount.TryCreate(command.Balance, out Amount balance))
        {
            return new UpdateAccountResult.InvalidAmount();
        }

        DateTime now = DateTime.UtcNow;
        Account account = Account.Create(command.AccountId, command.UserId, balance, command.Currency, now, now);

        DbResult<bool> updated = await _accountRepository.UpdateAccountAsync(account);
        return updated switch
        {
            DbResult<bool>.Success { Value: true } => new UpdateAccountResult.Updated(),
            _ => new UpdateAccountResult.Failed()
        };
    }
}
