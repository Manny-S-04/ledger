using Application.DbResult;
using Domain.Accounts;
using Domain.Users;

namespace Application.Interfaces;

public interface IAccountRepository 
{
   public Task<DbResult<IEnumerable<Account>>> GetAccountsByEmailAsync(Email email);
   public Task<DbResult<IEnumerable<Account>>> GetAccountsByUserIdAsync(UserId userId);
   public Task<DbResult<Account>> CreateAccountAsync(Account account);
   public Task<DbResult<bool>> UpdateAccountAsync(Account account);
   public Task<DbResult<bool>> DeleteAccountAsync(int accountId);
   public Task<DbResult<UserId>> GetAccountOwnerAsync(int accountId);
}
