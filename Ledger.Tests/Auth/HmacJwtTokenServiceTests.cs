using System.Text;
using Application.Auth;
using Domain.Users;
using Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ledger.Tests.Auth;

// HmacJwtTokenService (T-018, pure-ish: depends on the clock).
public class HmacJwtTokenServiceTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("test-signing-key-0123456789abcdef-0123");
    private static readonly UserId User = UserId.TryCreate(7)!.Value;
    private static readonly Email Mail = Email.TryCreate("user7@example.com")!;

    private static HmacJwtTokenService Service(TimeSpan? lifetime = null, byte[]? key = null) =>
        new(key ?? Key, lifetime ?? TimeSpan.FromMinutes(15), NullLogger<HmacJwtTokenService>.Instance);

    [Fact]
    public void CreateThenValidate_ReturnsTheSameUserIdAndEmail()
    {
        HmacJwtTokenService service = Service();

        TokenClaims? claims = service.Validate(service.Create(User, Mail).Value);

        Assert.NotNull(claims);
        Assert.Equal(7, claims.UserId);
        Assert.Equal("user7@example.com", claims.Email);
    }

    [Fact]
    public void Create_ExpiresAtIsNowPlusLifetime()
    {
        AuthToken token = Service(TimeSpan.FromMinutes(15)).Create(User, Mail);

        Assert.InRange(token.ExpiresAt, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public void Create_ProducesThreeDotSeparatedParts() =>
        Assert.Equal(3, Service().Create(User, Mail).Value.Split('.').Length);

    [Fact]
    public void Validate_ExpiredToken_ReturnsNull()
    {
        HmacJwtTokenService service = Service(TimeSpan.FromSeconds(-1));

        Assert.Null(service.Validate(service.Create(User, Mail).Value));
    }

    [Fact]
    public void Validate_TokenSignedWithDifferentKey_ReturnsNull()
    {
        string token = Service(key: Encoding.UTF8.GetBytes("another-key-another-key-another-key!!")).Create(User, Mail).Value;

        Assert.Null(Service().Validate(token));
    }

    [Fact]
    public void Validate_PayloadSwappedToAnotherUserWithOriginalSignature_ReturnsNull()
    {
        HmacJwtTokenService service = Service();
        string[] parts = service.Create(User, Mail).Value.Split('.');
        string forgedPayload = Base64Url("{\"sub\":\"1\",\"email\":\"user1@example.com\",\"iat\":1,\"exp\":9999999999}");

        Assert.Null(service.Validate($"{parts[0]}.{forgedPayload}.{parts[2]}"));
    }

    [Fact]
    public void Validate_AlgNoneWithEmptySignature_ReturnsNull()
    {
        HmacJwtTokenService service = Service();
        string payload = service.Create(User, Mail).Value.Split('.')[1];

        Assert.Null(service.Validate($"{Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{payload}."));
    }

    [Fact]
    public void Validate_SignatureWithOneCharacterChanged_ReturnsNull()
    {
        HmacJwtTokenService service = Service();
        string token = service.Create(User, Mail).Value;
        string tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.Null(service.Validate(tampered));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("...")]
    public void Validate_Malformed_ReturnsNull(string token) =>
        Assert.Null(Service().Validate(token));

    [Fact]
    public void Constructor_KeyShorterThan32Bytes_Throws() =>
        Assert.Throws<ArgumentException>(() => Service(key: Encoding.UTF8.GetBytes("short")));

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
