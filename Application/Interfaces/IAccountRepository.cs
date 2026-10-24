using Application.Accounts;
using Application.DbResult;
using Application.Providers;
using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;

namespace Application.Interfaces;

public interface IAccountRepository 
{
   Task<DbResult<Account>> CreateAccountAsync(Account account);
   Task<DbResult<IEnumerable<Account>>> GetAccountsByEmailAsync(Email email);
   Task<DbResult<IEnumerable<Account>>> GetAccountsByUserIdAsync(UserId userId);
   Task<DbResult<bool>> UpdateAccountAsync(Account account);
   Task<DbResult<bool>> DeleteAccountAsync(int accountId);
   Task<DbResult<bool>> AccountExistsAsync(UserId userId, Currency currency);
   Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId);

   Task<DbResult<TopupCallbackOutcome>> TopUpAccountAsync(TransactionId transactionId, ProviderCallbackStatus status);
   Task<DbResult<TransactionId>> RecordTopupAttempt(AccountId accountId, Amount amount, Currency currency);
   // Marks every pending top-up created before the cutoff as expired. Success(n) = rows expired.
   Task<DbResult<int>> ExpireStalePendingAsync(DateTime createdBefore);
   Task<DbResult<bool>> UpdateAccountBalance(Account account);
}
