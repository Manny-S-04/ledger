using Infrastructure.Auth;

namespace Ledger.Tests.Auth;

// Pbkdf2PasswordHasher (T-018, "pure-ish": no I/O, but random salts and deliberately slow).
public class Pbkdf2PasswordHasherTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = new();

    [Fact]
    public void Hash_UsesTheDocumentedFormat()
    {
        string[] parts = Hasher.Hash("abc").Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("600000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes() =>
        Assert.NotEqual(Hasher.Hash("abc"), Hasher.Hash("abc"));

    [Fact]
    public void Hash_DoesNotContainThePlaintext() =>
        Assert.DoesNotContain("abc123", Hasher.Hash("abc123"));

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue() =>
        Assert.True(Hasher.Verify("abc123", Hasher.Hash("abc123")));

    [Theory]
    [InlineData("abc124")]
    [InlineData("ABC123")]
    [InlineData("abc123 ")]
    [InlineData("")]
    public void Verify_DifferentPassword_ReturnsFalse(string attempt) =>
        Assert.False(Hasher.Verify(attempt, Hasher.Hash("abc123")));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$2b$12$examplehash1")]                         // the old fake seed values
    [InlineData("pbkdf2-sha256$600000$!!notbase64!!$AAAA")]
    [InlineData("pbkdf2-sha256$-5$AAAA$AAAA")]
    [InlineData("md5$1$AAAA$AAAA")]
    public void Verify_MalformedStoredHash_ReturnsFalseInsteadOfThrowing(string stored) =>
        Assert.False(Hasher.Verify("abc123", stored));
}
