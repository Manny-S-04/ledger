using Application.Accounts;
using Application.DbResult;
using Application.Providers;
using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;

namespace Application.Interfaces;

public interface IAccountRepository 
{
   Task<DbResult<Account>> CreateAccountAsync(Account account, CancellationToken cancellationToken = default);
   Task<DbResult<IEnumerable<Account>>> GetAccountsByEmailAsync(Email email, CancellationToken cancellationToken = default);
   Task<DbResult<IEnumerable<Account>>> GetAccountsByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);
   Task<DbResult<bool>> UpdateAccountAsync(Account account, CancellationToken cancellationToken = default);
   Task<DbResult<bool>> DeleteAccountAsync(int accountId, CancellationToken cancellationToken = default);
   Task<DbResult<bool>> AccountExistsAsync(UserId userId, Currency currency, CancellationToken cancellationToken = default);
   Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId, CancellationToken cancellationToken = default);

   Task<DbResult<TopupCallbackOutcome>> TopUpAccountAsync(TransactionId transactionId, ProviderCallbackStatus status, CancellationToken cancellationToken = default);
   Task<DbResult<TransactionId>> RecordTopupAttempt(AccountId accountId, Amount amount, Currency currency, CancellationToken cancellationToken = default);
   // Marks every pending top-up created before the cutoff as expired. Success(n) = rows expired.
   Task<DbResult<int>> ExpireStalePendingAsync(DateTime createdBefore, CancellationToken cancellationToken = default);
   Task<DbResult<bool>> UpdateAccountBalance(Account account, CancellationToken cancellationToken = default);
}
