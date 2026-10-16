namespace Application.DbResult;

public abstract record DbResult
{
    public sealed record Success : DbResult;
    public sealed record Failure(DbError Error) : DbResult;
}

public abstract record DbResult<T>
{
    public sealed record Success(T Value) : DbResult<T>;
    public sealed record Failure(DbError Error) : DbResult<T>;
}

public enum DbError
{
    Unavailable,
    ConstraintViolation,
    NotFound,
    Unknown
}
