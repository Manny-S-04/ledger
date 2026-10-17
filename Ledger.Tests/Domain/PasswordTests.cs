using Domain.Users;
using Moq;

namespace Ledger.Tests.Domain;

// Password.TryCreate / Hash (T-018). Rules: 3-8 characters, no character requirements.
public class PasswordTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("abcdefgh")]
    [InlineData("ABC")]
    [InlineData("123")]
    [InlineData("!@#$%^&*")]
    [InlineData("   ")]
    [InlineData("pässwö")]   // non-ASCII is fine
    public void TryCreate_WithinRules_ReturnsPasswordAndNoError(string value)
    {
        Password? password = Password.TryCreate(value, out string? error);

        Assert.NotNull(password);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryCreate_Missing_ReturnsNullWithRequiredError(string? value)
    {
        Password? password = Password.TryCreate(value, out string? error);

        Assert.Null(password);
        Assert.Equal("Password is required.", error);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abcdefghi")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    public void TryCreate_OutsideLengthRange_ReturnsNullWithLengthError(string value)
    {
        Password? password = Password.TryCreate(value, out string? error);

        Assert.Null(password);
        Assert.Equal("Password must be 3-8 characters.", error);
    }

    [Fact]
    public void Hash_PassesTheExactPlaintextToTheHasherAndReturnsItsResult()
    {
        Mock<IPasswordHasher> hasher = new();
        hasher.Setup(h => h.Hash("abc123")).Returns("the-hash");

        string hash = Password.TryCreate("abc123", out _)!.Hash(hasher.Object);

        Assert.Equal("the-hash", hash);
        hasher.Verify(h => h.Hash("abc123"), Times.Once);
    }

    [Fact]
    public void ToString_NeverRevealsThePlaintext()
    {
        Password password = Password.TryCreate("secret1", out _)!;

        Assert.DoesNotContain("secret1", password.ToString());
        Assert.DoesNotContain("secret1", $"{password}");
    }
}
