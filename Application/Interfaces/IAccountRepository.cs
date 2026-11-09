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
   // T-031: pending/expired top-ups and whether the account is already closed. NotFound when there is no account.
   Task<DbResult<CloseBlockers>> GetCloseBlockersAsync(int accountId, CancellationToken cancellationToken = default);
   // T-031: locks the account row, rechecks, and closes. Expired top-ups not in clearedExpired block the close.
   Task<DbResult<CloseAccountOutcome>> CloseAccountAsync(int accountId, IReadOnlySet<TransactionId> clearedExpired, CancellationToken cancellationToken = default);
   Task<DbResult<bool>> AccountExistsAsync(UserId userId, Currency currency, CancellationToken cancellationToken = default);
   Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId, CancellationToken cancellationToken = default);

   Task<DbResult<TopupCallbackOutcome>> TopUpAccountAsync(TransactionId transactionId, ProviderCallbackStatus status, string eventId, long sequence, CancellationToken cancellationToken = default);
   Task<DbResult<RecordTopupOutcome>> RecordTopupAttempt(AccountId accountId, Amount amount, Currency currency, CancellationToken cancellationToken = default);
   // T-031.4: the provider's charge id, written when the provider accepts the charge.
   Task<DbResult<bool>> SetTopupProviderReferenceAsync(TransactionId transactionId, string providerReference, CancellationToken cancellationToken = default);
   // Marks every pending top-up created before the cutoff as expired. Success(n) = rows expired.
   Task<DbResult<int>> ExpireStalePendingAsync(DateTime createdBefore, CancellationToken cancellationToken = default);
   Task<DbResult<bool>> UpdateAccountBalance(Account account, CancellationToken cancellationToken = default);
}
