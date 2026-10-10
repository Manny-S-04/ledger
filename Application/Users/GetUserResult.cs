using Domain.Users;

namespace Application.Users;

public abstract record GetUserResult
{
    public sealed record Success(IEnumerable<User> Users) : GetUserResult;
    public sealed record FailedWithError(DbResult.DbError Error) : GetUserResult;
    public sealed record Failed : GetUserResult;
}