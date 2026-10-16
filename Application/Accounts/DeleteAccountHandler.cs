using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Datatypes;

namespace Application.Accounts;

public class DeleteAccountHandler
{
    private readonly IAccountRepository _accountRepository;

    public DeleteAccountHandler(IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    public async Task<DeleteAccountResult> Handle(DeleteAccountCommand command)
    {
        // The repository deletes by Id only; the other fields are placeholders.
        Account account = Account.Create(command.AccountId, 0, default(Amount), null, DateTime.UtcNow, DateTime.UtcNow);

        DbResult<bool> deleted = await _accountRepository.DeleteAccountAsync(account);
        return deleted switch
        {
            DbResult<bool>.Success { Value: true } => new DeleteAccountResult.Deleted(),
            DbResult<bool>.Success { Value: false } => new DeleteAccountResult.NotFound(),
            _ => new DeleteAccountResult.Failed()
        };
    }
}
