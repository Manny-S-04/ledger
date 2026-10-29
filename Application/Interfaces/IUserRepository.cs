using Domain.Users;
using Application.DbResult;

namespace Application.Interfaces;

public interface IUserRepository
{
    public Task<DbResult<UserId>> Create(User user, CancellationToken cancellationToken = default);
    public Task<DbResult<bool>> Exists(Email email, CancellationToken cancellationToken = default);
    public Task<DbResult<User>> GetUserByEmail(Email email, CancellationToken cancellationToken = default);
}
