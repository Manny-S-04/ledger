using Domain.Users;

namespace Ledger.Tests.Domain;

// Email.TryCreate (T-018). Returns null for anything that isn't a valid address; otherwise the
// stored value is the trimmed, lower-cased address (the type is honest about its format).
public class EmailTests
{
    [Theory]
    [InlineData("user@example.com", "user@example.com")]
    [InlineData("User@Example.COM", "user@example.com")]
    [InlineData("  user@example.com  ", "user@example.com")]
    [InlineData("first.last@example.co.uk", "first.last@example.co.uk")]
    [InlineData("user+tag@example.com", "user+tag@example.com")]
    [InlineData("user_name-1@sub-domain.example.com", "user_name-1@sub-domain.example.com")]
    [InlineData("a@b.co", "a@b.co")]
    public void TryCreate_ValidAddress_ReturnsNormalisedEmail(string input, string expected)
    {
        Email? email = Email.TryCreate(input);

        Assert.NotNull(email);
        Assert.Equal(expected, email.EmailAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void TryCreate_NullOrBlank_ReturnsNull(string? input) =>
        Assert.Null(Email.TryCreate(input));

    [Theory]
    [InlineData("plainaddress")]                 // no @
    [InlineData("two@@example.com")]             // two @
    [InlineData("a@b@example.com")]
    [InlineData("@example.com")]                 // empty local part
    [InlineData("user@")]                        // empty domain
    [InlineData("user@example")]                 // domain without a dot
    [InlineData("user@.example.com")]            // domain starts with a dot
    [InlineData("user@example.com.")]            // domain ends with a dot
    [InlineData("user@example..com")]            // consecutive dots in domain
    [InlineData("user..name@example.com")]       // consecutive dots in local part
    [InlineData("user name@example.com")]        // space
    [InlineData("user@exa mple.com")]
    [InlineData("o'brien@example.com")]          // apostrophe not allowed
    [InlineData("user<script>@example.com")]
    [InlineData("user@example.com;DROP TABLE users")]
    [InlineData("user@ex_ample.com")]            // underscore not allowed in domain
    public void TryCreate_MalformedAddress_ReturnsNull(string input) =>
        Assert.Null(Email.TryCreate(input));

    [Fact]
    public void TryCreate_Exactly254Characters_ReturnsEmail()
    {
        string address = MakeAddress(254);

        Assert.NotNull(Email.TryCreate(address));
    }

    [Fact]
    public void TryCreate_LongerThan254Characters_ReturnsNull()
    {
        // 254 is the maximum length of an address (T-018.1). No such rule exists yet, so this fails until it's added.
        string address = MakeAddress(255);

        Assert.Null(Email.TryCreate(address));
    }

    [Fact]
    public void TryCreate_SameAddressDifferentCase_AreEqual() =>
        Assert.Equal(Email.TryCreate("User@Example.com"), Email.TryCreate("user@example.com"));

    private static string MakeAddress(int length)
    {
        const string suffix = "@example.com";
        return new string('a', length - suffix.Length) + suffix;
    }
}
