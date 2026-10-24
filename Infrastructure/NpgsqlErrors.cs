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
}
