using Domain.Datatypes;

namespace Ledger.Tests.Domain;

// Amount (T-018). Stored as whole pence; zero or positive; at most 2 decimal places.
public class AmountTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.01, 1)]
    [InlineData(1, 100)]
    [InlineData(12.34, 1234)]
    [InlineData(100.5, 10050)]
    public void TryCreate_ValidPounds_ReturnsAmountWithPence(decimal pounds, long expectedPence)
    {
        Amount? amount = Amount.TryCreate(pounds);

        Assert.NotNull(amount);
        Assert.Equal(expectedPence, amount.Value.Pence);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    public void TryCreate_Negative_ReturnsNull(decimal pounds) =>
        Assert.Null(Amount.TryCreate(pounds));

    [Theory]
    [InlineData(0.001)]
    [InlineData(1.005)]
    [InlineData(12.345)]
    public void TryCreate_MoreThanTwoDecimalPlaces_ReturnsNull(decimal pounds) =>
        Assert.Null(Amount.TryCreate(pounds));

    [Fact]
    public void TryCreate_TooLargeForPence_ReturnsNull() =>
        Assert.Null(Amount.TryCreate(decimal.MaxValue));

    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(1, "1.00")]
    [InlineData(12.3, "12.30")]
    [InlineData(1234.56, "1234.56")]
    public void ToString_AlwaysTwoDecimalPlacesWithDotSeparator(decimal pounds, string expected) =>
        Assert.Equal(expected, Amount.TryCreate(pounds)!.Value.ToString());

    [Fact]
    public void Add_TwoAmounts_SumsPence() =>
        Assert.Equal(1235, (Amount.TryCreate(12.34m)!.Value + Amount.TryCreate(0.01m)!.Value).Pence);

    [Fact]
    public void Add_Overflow_ThrowsInsteadOfWrappingAround()
    {
        Amount max = Amount.TryCreate(long.MaxValue / 100)!.Value;

        Assert.Throws<OverflowException>(() => max + max);
    }

    [Fact]
    public void Comparisons_CompareByValue()
    {
        Amount small = Amount.TryCreate(1m)!.Value;
        Amount large = Amount.TryCreate(2m)!.Value;

        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= Amount.TryCreate(1m)!.Value);
        Assert.True(small >= Amount.TryCreate(1m)!.Value);
        Assert.Equal(small, Amount.TryCreate(1.00m)!.Value);
    }
}
