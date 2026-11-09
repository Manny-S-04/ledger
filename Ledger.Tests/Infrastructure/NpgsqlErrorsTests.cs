using Application.DbResult;
using Infrastructure;
using Npgsql;

namespace Ledger.Tests.Infrastructure;

// T-032: exception -> DbError mapping used by every repository catch-all.
public class NpgsqlErrorsTests
{
    private static PostgresException Pg(string sqlState) => new("test", "ERROR", "ERROR", sqlState);

    [Theory]
    [InlineData("23505", DbError.ConstraintViolation)] // unique_violation
    [InlineData("23503", DbError.Unknown)] // foreign_key_violation
    [InlineData("23514", DbError.Unknown)] // check_violation
    [InlineData("23502", DbError.Unknown)] // not_null_violation
    [InlineData("40001", DbError.Conflict)]            // serialization_failure
    [InlineData("40P01", DbError.Conflict)]            // deadlock_detected
    [InlineData("57014", DbError.Timeout)]             // query_canceled (statement_timeout)
    [InlineData("55P03", DbError.Timeout)]             // lock_not_available
    [InlineData("53300", DbError.Unavailable)]         // too_many_connections
    [InlineData("08006", DbError.Unavailable)]         // connection_failure
    [InlineData("57P01", DbError.Unavailable)]         // admin_shutdown
    [InlineData("42P01", DbError.Unknown)]             // undefined_table
    [InlineData("22003", DbError.Unknown)]             // numeric_value_out_of_range
    public void Map_PostgresException_MapsBySqlState(string sqlState, DbError expected)
    {
        Assert.Equal(expected, NpgsqlErrors.Map(Pg(sqlState)));
    }

    [Fact]
    public void Map_OperationCanceled_ReturnsCancelled()
    {
        Assert.Equal(DbError.Cancelled, NpgsqlErrors.Map(new OperationCanceledException()));
        Assert.Equal(DbError.Cancelled, NpgsqlErrors.Map(new TaskCanceledException()));
    }

    [Fact]
    public void Map_NonPostgresNpgsqlException_ReturnsUnavailable()
    {
        Assert.Equal(DbError.Unavailable, NpgsqlErrors.Map(new NpgsqlException("connection dropped")));
    }

    [Fact]
    public void Map_OtherException_ReturnsUnknown()
    {
        Assert.Equal(DbError.Unknown, NpgsqlErrors.Map(new InvalidOperationException()));
    }
}
