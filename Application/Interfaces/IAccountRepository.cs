using Application.DbResult;
using Domain.Accounts;
using Domain.Users;

namespace Application.Interfaces;

public interface IAccountRepository 
{
   public Task<DbResult<IEnumerable<Account>>> GetAccountsByEmailAsync(Email email);
   public Task<DbResult<IEnumerable<Account>>> GetAccountsByUserIdAsync(UserId userId);
   public Task<DbResult<bool>> AccountExistsAsync(UserId userId, Domain.Datatypes.Currency currency);
   // Failure(ConstraintViolation) when the user already has an account in that currency.
   public Task<DbResult<Account>> CreateAccountAsync(Account account);
   public Task<DbResult<bool>> UpdateAccountAsync(Account account);
   // Deletes only when balance is 0. Success(true) = deleted, Success(false) = balance > 0, Failure(NotFound) = no such account.
   public Task<DbResult<bool>> DeleteAccountAsync(int accountId);
   public Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId);
}
