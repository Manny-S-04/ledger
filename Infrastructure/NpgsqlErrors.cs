using Application.DbResult;
using Npgsql;

namespace Infrastructure;

internal static class NpgsqlErrors
{
    // T-025: connection-level failures map to DbError.Unavailable: connect refused, connect timeout,
    // command timeout, dropped connection (non-Postgres NpgsqlException), and server-side
    // connection errors / shutdown (SqlState class 08, 57P0x).
    public static bool IsUnavailable(Exception ex) => ex switch
    {
        PostgresException pg => pg.SqlState.StartsWith("08", StringComparison.Ordinal)
                                || pg.SqlState.StartsWith("57P0", StringComparison.Ordinal),
        NpgsqlException => true,
        _ => false,
    };

    // T-032: the catch-all in every repository method maps through here. Common errors get a semantic DbError,
    // grouped by SqlState class; anything else stays Unknown (500).
    public static DbError Map(Exception ex) => ex switch
    {
        OperationCanceledException => DbError.Cancelled,
        // Only unique_violation: handlers read ConstraintViolation as "already exists", and T-000.4 has a
        // foreign key miss surface as Failed, so the other class 23 codes stay Unknown.
        PostgresException { SqlState: "23505" } => DbError.ConstraintViolation,
        PostgresException pg when pg.SqlState.StartsWith("40", StringComparison.Ordinal) => DbError.Conflict,
        PostgresException { SqlState: "57014" or "55P03" } => DbError.Timeout,
        PostgresException pg when pg.SqlState.StartsWith("53", StringComparison.Ordinal) => DbError.Unavailable,
        _ when IsUnavailable(ex) => DbError.Unavailable,
        _ => DbError.Unknown,
    };
}
