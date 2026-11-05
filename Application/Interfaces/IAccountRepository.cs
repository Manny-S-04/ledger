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
   // T-031: pending/expired top-ups and whether the account is already closed. NotFound when there is no account.
   Task<DbResult<CloseBlockers>> GetCloseBlockersAsync(int accountId);
   // T-031: locks the account row, rechecks, and closes. Expired top-ups not in clearedExpired block the close.
   Task<DbResult<CloseAccountOutcome>> CloseAccountAsync(int accountId, IReadOnlySet<TransactionId> clearedExpired);
   Task<DbResult<bool>> AccountExistsAsync(UserId userId, Currency currency);
   Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId);

   Task<DbResult<TopupCallbackOutcome>> TopUpAccountAsync(TransactionId transactionId, ProviderCallbackStatus status);
   Task<DbResult<RecordTopupOutcome>> RecordTopupAttempt(AccountId accountId, Amount amount, Currency currency);
   // T-031.4: the provider's charge id, written when the provider accepts the charge.
   Task<DbResult<bool>> SetTopupProviderReferenceAsync(TransactionId transactionId, string providerReference);
   // Marks every pending top-up created before the cutoff as expired. Success(n) = rows expired.
   Task<DbResult<int>> ExpireStalePendingAsync(DateTime createdBefore);
   Task<DbResult<bool>> UpdateAccountBalance(Account account);
}
