using Domain.Users;

namespace Ledger.Tests.Domain;

// UserId.TryCreate (T-018). Positive integers only.
public class UserIdTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void TryCreate_Positive_ReturnsUserIdWithThatValue(int value)
    {
        UserId? id = UserId.TryCreate(value);

        Assert.NotNull(id);
        Assert.Equal(value, id.Value.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void TryCreate_ZeroOrNegative_ReturnsNull(int value) =>
        Assert.Null(UserId.TryCreate(value));
}
