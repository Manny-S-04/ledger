using Domain.Users;
using Application.DbResult;

namespace Application.Interfaces;

public interface IUserRepository
{
    public Task<DbResult<UserId>> Create(User user);
    public Task<DbResult<bool>> Exists(Email email);
    public Task<DbResult<IEnumerable<User>>> GetAllUsers();
    public Task<DbResult<User>> GetUserByEmail(Email email);
}
