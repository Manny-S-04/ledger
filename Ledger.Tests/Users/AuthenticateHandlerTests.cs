using Application.Auth;
using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ledger.Tests.Users;

// AuthenticateHandler (login). Repository, hasher and token service are mocked: these tests cover
// the handler's decisions for user input only, not database failures (T-015).
public class AuthenticateHandlerTests
{
    private const string UserEmail = "user1@example.com";
    private const string UserPassword = "pass1";
    private const string StoredHash = "stored-hash";

    private static readonly AuthToken Token = new("token-value", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private readonly Mock<IUserRepository> _repository = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();

    public AuthenticateHandlerTests()
    {
        User existing = User.Create(1, Email.TryCreate(UserEmail)!, StoredHash, DateTime.UtcNow);
        _repository.Setup(r => r.GetUserByEmail(It.Is<Email>(e => e.EmailAddress == UserEmail)))
            .ReturnsAsync(new DbResult<User>.Success(existing));
        _repository.Setup(r => r.GetUserByEmail(It.Is<Email>(e => e.EmailAddress != UserEmail)))
            .ReturnsAsync(new DbResult<User>.Failure(DbError.NotFound));
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        _hasher.Setup(h => h.Verify(UserPassword, StoredHash)).Returns(true);
        _tokens.Setup(t => t.Create(It.IsAny<UserId>(), It.IsAny<Email>())).Returns(Token);
    }

    private Task<AuthenticateResult> Handle(string? email, string? password) =>
        new AuthenticateHandler(_repository.Object, _hasher.Object, _tokens.Object, NullLogger<AuthenticateHandler>.Instance)
            .Handle(new AuthenticateCommand(email!, password!));

    // ---- success ----

    [Fact]
    public async Task Handle_CorrectEmailAndPassword_ReturnsFoundWithToken()
    {
        AuthenticateResult result = await Handle(UserEmail, UserPassword);

        AuthenticateResult.Found found = Assert.IsType<AuthenticateResult.Found>(result);
        Assert.Equal(Token, found.Token);
    }

    [Fact]
    public async Task Handle_CorrectCredentials_IssuesTokenForThatUsersIdAndEmail()
    {
        await Handle(UserEmail, UserPassword);

        _tokens.Verify(t => t.Create(
            It.Is<UserId>(id => id.Value == 1),
            It.Is<Email>(e => e.EmailAddress == UserEmail)), Times.Once);
    }

    [Theory]
    [InlineData("USER1@EXAMPLE.COM")]
    [InlineData("User1@Example.com")]
    [InlineData("  user1@example.com  ")]
    public async Task Handle_EmailInDifferentCaseOrWithSpaces_StillLogsIn(string email)
    {
        AuthenticateResult result = await Handle(email, UserPassword);

        Assert.IsType<AuthenticateResult.Found>(result);
    }

    // ---- failures that must all look the same (401) ----

    [Fact]
    public async Task Handle_WrongPassword_ReturnsNotFound()
    {
        AuthenticateResult result = await Handle(UserEmail, "wrong");

        Assert.IsType<AuthenticateResult.NotFound>(result);
    }

    [Theory]
    [InlineData("PASS1")]   // passwords are case-sensitive
    [InlineData("pass1 ")]  // and not trimmed
    [InlineData("")]
    [InlineData(null)]
    public async Task Handle_PasswordNotExactlyMatching_ReturnsNotFound(string? password)
    {
        AuthenticateResult result = await Handle(UserEmail, password);

        Assert.IsType<AuthenticateResult.NotFound>(result);
    }

    [Fact]
    public async Task Handle_UnknownEmail_ReturnsNotFound()
    {
        AuthenticateResult result = await Handle("nobody@example.com", UserPassword);

        Assert.IsType<AuthenticateResult.NotFound>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("'; DROP TABLE users; --")]
    public async Task Handle_MalformedEmail_ReturnsNotFoundWithoutQueryingTheRepository(string? email)
    {
        AuthenticateResult result = await Handle(email, UserPassword);

        Assert.IsType<AuthenticateResult.NotFound>(result);
        _repository.Verify(r => r.GetUserByEmail(It.IsAny<Email>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AnyFailure_NeverIssuesAToken()
    {
        await Handle(UserEmail, "wrong");
        await Handle("nobody@example.com", UserPassword);
        await Handle("not-an-email", UserPassword);

        _tokens.Verify(t => t.Create(It.IsAny<UserId>(), It.IsAny<Email>()), Times.Never);
    }

    // ---- login does not apply signup's password rules (T-007.3b) ----

    [Fact]
    public async Task Handle_StoredPasswordLongerThanSignupMaximum_StillLogsIn()
    {
        _hasher.Setup(h => h.Verify("averylongpassword", StoredHash)).Returns(true);

        AuthenticateResult result = await Handle(UserEmail, "averylongpassword");

        Assert.IsType<AuthenticateResult.Found>(result);
    }
}
