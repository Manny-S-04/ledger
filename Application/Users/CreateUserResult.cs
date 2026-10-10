namespace Application.Users;

public abstract record CreateUserResult
{
    public sealed record Created(int Id) : CreateUserResult;
    public sealed record AlreadyExists : CreateUserResult;
    public sealed record InvalidEmail : CreateUserResult;
    public sealed record Failed : CreateUserResult;
}