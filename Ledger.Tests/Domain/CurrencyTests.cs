using Domain.Datatypes;

namespace Ledger.Tests.Domain;

// Currency.TryCreate (T-018). Exactly one of the 10 supported upper-case codes.
public class CurrencyTests
{
    [Theory]
    [InlineData("USD")] [InlineData("EUR")] [InlineData("GBP")] [InlineData("JPY")] [InlineData("CAD")]
    [InlineData("AUD")] [InlineData("CHF")] [InlineData("SEK")] [InlineData("NOK")] [InlineData("DKK")]
    public void TryCreate_SupportedCode_ReturnsCurrencyWithThatCode(string code)
    {
        Currency? currency = Currency.TryCreate(code);

        Assert.NotNull(currency);
        Assert.Equal(code, currency.Code);
        Assert.Equal(code, currency.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gbp")]
    [InlineData("Gbp")]
    [InlineData(" GBP")]
    [InlineData("GBP ")]
    [InlineData("XYZ")]
    [InlineData("GB")]
    [InlineData("GBPP")]
    [InlineData("BTC")]
    [InlineData("'; DROP TABLE accounts; --")]
    public void TryCreate_UnsupportedOrMalformed_ReturnsNull(string? code) =>
        Assert.Null(Currency.TryCreate(code));

    [Fact]
    public void Supported_ContainsExactlyTheTenCodes() =>
        Assert.Equal(["USD", "EUR", "GBP", "JPY", "CAD", "AUD", "CHF", "SEK", "NOK", "DKK"], Currency.Supported);

    [Fact]
    public void TryCreate_SameCodeTwice_AreEqual() =>
        Assert.Equal(Currency.TryCreate("GBP"), Currency.TryCreate("GBP"));
}
