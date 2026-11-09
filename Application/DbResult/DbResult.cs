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
    Timeout,      // T-032: statement timeout / lock wait gave up (57014, 55P03)
    Conflict,     // T-032: serialization failure / deadlock, safe to retry (class 40)
    Cancelled,    // T-032: the caller cancelled the call (OperationCanceledException)
    Unknown
}
