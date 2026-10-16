using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;

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
        DbResult<UserId> owner = await _accountRepository.GetAccountOwnerAsync(command.AccountId);
        switch (owner)
        {
            case DbResult<UserId>.Failure { Error: DbError.NotFound }:
                return new DeleteAccountResult.NotFound();
            case DbResult<UserId>.Success success when success.Value.Value != command.RequestingUserId:
                return new DeleteAccountResult.Forbidden();
            case DbResult<UserId>.Success:
                break;
            case var _:
                return new DeleteAccountResult.Failed();
        }

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
