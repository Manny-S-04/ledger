using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;

namespace Application.Accounts;

public class CreateAccountHandler
{
    private readonly IAccountRepository _accountRepository;

    public CreateAccountHandler(IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    public async Task<CreateAccountResult> Handle(CreateAccountCommand command)
    {
        UserId? userId = UserId.TryCreate(command.UserId);
        if (userId is null)
        {
            return new CreateAccountResult.InvalidUserId();
        }

        if (!Amount.TryCreate(command.Balance, out Amount balance))
        {
            return new CreateAccountResult.InvalidAmount();
        }

        DateTime now = DateTime.UtcNow;
        Account account = Account.Create(userId.Value.Value, balance, command.Currency, now, now);

        DbResult<Account> created = await _accountRepository.CreateAccountAsync(account);
        return created switch
        {
            DbResult<Account>.Success success => new CreateAccountResult.Created(success.Value),
            _ => new CreateAccountResult.Failed()
        };
    }
}
