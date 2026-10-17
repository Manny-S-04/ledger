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
    public void TryCreate_ValidPounds_ReturnsTrueWithPence(decimal pounds, long expectedPence)
    {
        bool ok = Amount.TryCreate(pounds, out Amount amount);

        Assert.True(ok);
        Assert.Equal(expectedPence, amount.Pence);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    public void TryCreate_Negative_ReturnsFalse(decimal pounds) =>
        Assert.False(Amount.TryCreate(pounds, out _));

    [Theory]
    [InlineData(0.001)]
    [InlineData(1.005)]
    [InlineData(12.345)]
    public void TryCreate_MoreThanTwoDecimalPlaces_ReturnsFalse(decimal pounds) =>
        Assert.False(Amount.TryCreate(pounds, out _));

    [Fact]
    public void TryCreate_TooLargeForPence_ReturnsFalse() =>
        Assert.False(Amount.TryCreate(decimal.MaxValue, out _));

    [Fact]
    public void Create_InvalidValue_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Amount.Create(-1m));

    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(1, "1.00")]
    [InlineData(12.3, "12.30")]
    [InlineData(1234.56, "1234.56")]
    public void ToString_AlwaysTwoDecimalPlacesWithDotSeparator(decimal pounds, string expected) =>
        Assert.Equal(expected, Amount.Create(pounds).ToString());

    [Fact]
    public void Add_TwoAmounts_SumsPence() =>
        Assert.Equal(1235, (Amount.Create(12.34m) + Amount.Create(0.01m)).Pence);

    [Fact]
    public void Add_Overflow_ThrowsInsteadOfWrappingAround()
    {
        Amount max = Amount.Create(long.MaxValue / 100);

        Assert.Throws<OverflowException>(() => max + max);
    }

    [Fact]
    public void Comparisons_CompareByValue()
    {
        Amount small = Amount.Create(1m);
        Amount large = Amount.Create(2m);

        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= Amount.Create(1m));
        Assert.True(small >= Amount.Create(1m));
        Assert.Equal(small, Amount.Create(1.00m));
    }
}
