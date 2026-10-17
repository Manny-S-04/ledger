using Application.DbResult;
using Application.Interfaces;
using Application.Users;
using Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ledger.Tests.Users;

// CreateUserHandler (signup). The repository and hasher are mocked: these tests cover the
// handler's decisions for user input only, not database failures (T-015).
public class CreateUserHandlerTests
{
    private const string ValidEmail = "new@example.com";
    private const string ValidPassword = "abc123";

    private readonly Mock<IUserRepository> _repository = new();
    private readonly Mock<IPasswordHasher> _hasher = new();

    public CreateUserHandlerTests()
    {
        // Happy-path defaults; individual tests override what they need.
        _repository.Setup(r => r.Exists(It.IsAny<Email>()))
            .ReturnsAsync(new DbResult<bool>.Success(false));
        _repository.Setup(r => r.Create(It.IsAny<User>()))
            .ReturnsAsync(new DbResult<UserId>.Success(UserId.TryCreate(42)!.Value));
        _hasher.Setup(h => h.Hash(It.IsAny<string>()))
            .Returns((string plain) => $"hashed:{plain}");
    }

    private CreateUserHandler CreateHandler() =>
        new(_repository.Object, _hasher.Object, NullLogger<CreateUserHandler>.Instance);

    private Task<CreateUserResult> Handle(string? email, string? password) =>
        CreateHandler().Handle(new CreateUserCommand(email!, password!));

    // ---- success ----

    [Fact]
    public async Task Handle_ValidEmailAndPassword_ReturnsCreatedWithNewUserId()
    {
        CreateUserResult result = await Handle(ValidEmail, ValidPassword);

        CreateUserResult.Created created = Assert.IsType<CreateUserResult.Created>(result);
        Assert.Equal(42, created.Id);
    }

    [Fact]
    public async Task Handle_ValidInput_StoresTheHashNotThePlaintextPassword()
    {
        await Handle(ValidEmail, ValidPassword);

        _repository.Verify(r => r.Create(It.Is<User>(u =>
            u.PasswordHash == $"hashed:{ValidPassword}" && u.PasswordHash != ValidPassword)), Times.Once);
    }

    [Fact]
    public async Task Handle_MixedCaseEmailWithSpaces_ChecksAndStoresNormalisedEmail()
    {
        await Handle("  New@Example.COM ", ValidPassword);

        _repository.Verify(r => r.Exists(It.Is<Email>(e => e.EmailAddress == "new@example.com")), Times.Once);
        _repository.Verify(r => r.Create(It.Is<User>(u => u.Email.EmailAddress == "new@example.com")), Times.Once);
    }

    // ---- email validation ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing-at.example.com")]
    [InlineData("two@@example.com")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@example")]
    [InlineData("user@.example.com")]
    [InlineData("user@example.com.")]
    [InlineData("user..name@example.com")]
    [InlineData("user name@example.com")]
    [InlineData("user<script>@example.com")]
    [InlineData("'; DROP TABLE users; --@example.com")]
    public async Task Handle_InvalidEmail_ReturnsInvalidEmail(string? email)
    {
        CreateUserResult result = await Handle(email, ValidPassword);

        Assert.IsType<CreateUserResult.InvalidEmail>(result);
    }

    [Fact]
    public async Task Handle_InvalidEmail_NeverTouchesTheRepository()
    {
        await Handle("not-an-email", ValidPassword);

        _repository.Verify(r => r.Exists(It.IsAny<Email>()), Times.Never);
        _repository.Verify(r => r.Create(It.IsAny<User>()), Times.Never);
    }

    // ---- password validation (3-8 characters, no character rules) ----

    [Theory]
    [InlineData(null, "Password is required.")]
    [InlineData("", "Password is required.")]
    [InlineData("a", "Password must be 3-8 characters.")]
    [InlineData("ab", "Password must be 3-8 characters.")]
    [InlineData("abcdefghi", "Password must be 3-8 characters.")]
    [InlineData("averyveryverylongpassword", "Password must be 3-8 characters.")]
    public async Task Handle_InvalidPassword_ReturnsInvalidPasswordWithReason(string? password, string expectedReason)
    {
        CreateUserResult result = await Handle(ValidEmail, password);

        CreateUserResult.InvalidPassword invalid = Assert.IsType<CreateUserResult.InvalidPassword>(result);
        Assert.Equal(expectedReason, invalid.Reason);
    }

    [Theory]
    [InlineData("abc")]        // minimum length
    [InlineData("abcdefgh")]   // maximum length
    [InlineData("ABC")]        // capitals allowed
    [InlineData("a!@")]        // symbols allowed
    [InlineData("123")]        // digits only allowed
    [InlineData("   ")]        // no character rules: three spaces is 3 characters
    public async Task Handle_PasswordWithinRules_ReturnsCreated(string password)
    {
        CreateUserResult result = await Handle(ValidEmail, password);

        Assert.IsType<CreateUserResult.Created>(result);
    }

    [Fact]
    public async Task Handle_InvalidPassword_NeverHashesOrCreates()
    {
        await Handle(ValidEmail, "ab");

        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
        _repository.Verify(r => r.Create(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidEmailAndInvalidPassword_ReportsEmailFirst()
    {
        CreateUserResult result = await Handle("not-an-email", "ab");

        Assert.IsType<CreateUserResult.InvalidEmail>(result);
    }

    // ---- already exists ----

    [Fact]
    public async Task Handle_EmailAlreadyRegistered_ReturnsAlreadyExistsAndDoesNotCreate()
    {
        _repository.Setup(r => r.Exists(It.IsAny<Email>()))
            .ReturnsAsync(new DbResult<bool>.Success(true));

        CreateUserResult result = await Handle(ValidEmail, ValidPassword);

        Assert.IsType<CreateUserResult.AlreadyExists>(result);
        _repository.Verify(r => r.Create(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmailRegisteredWithDifferentCase_ReturnsAlreadyExists()
    {
        _repository.Setup(r => r.Exists(It.Is<Email>(e => e.EmailAddress == "taken@example.com")))
            .ReturnsAsync(new DbResult<bool>.Success(true));

        CreateUserResult result = await Handle("TAKEN@Example.com", ValidPassword);

        Assert.IsType<CreateUserResult.AlreadyExists>(result);
    }
}
