using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Application.Accounts;

public class CreateAccountHandler
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<CreateAccountHandler> _logger;

    public CreateAccountHandler(IAccountRepository accountRepository, ILogger<CreateAccountHandler> logger)
    {
        _logger = logger;
        _accountRepository = accountRepository;
    }

    public async Task<CreateAccountResult> Handle(CreateAccountCommand command)
    {
        _logger.Info($"hit userId={command.UserId} currency={command.Currency}");
        UserId? userId = UserId.TryCreate(command.UserId);
        if (userId is null)
        {
            _logger.Info("result=InvalidUserId");
            return new CreateAccountResult.InvalidUserId();
        }

        Currency? currency = Currency.TryCreate(command.Currency);
        if (currency is null)
        {
            _logger.Info("result=InvalidCurrency");
            return new CreateAccountResult.InvalidCurrency();
        }

        DateTime now = DateTime.UtcNow;
        Account account = Account.Create(userId.Value.Value, Amount.Create(0m), currency, now, now);

        DbResult<Account> created = await _accountRepository.CreateAccountAsync(account);
        CreateAccountResult result = created switch
        {
            DbResult<Account>.Success success => new CreateAccountResult.Created(success.Value),
            _ => new CreateAccountResult.Failed()
        };
        _logger.Info($"result={result.GetType().Name}");
        return result;
    }
}
