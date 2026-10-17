using Application.Accounts;
using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ledger.Tests.Accounts;

// CreateAccountHandler. Repository mocked; covers user input only, not DB failures (T-016).
// Ownership (token userId == body userId -> 403) is checked in the endpoint, not here.
public class CreateAccountHandlerTests
{
    private readonly Mock<IAccountRepository> _repository = new();

    public CreateAccountHandlerTests()
    {
        _repository.Setup(r => r.AccountExistsAsync(It.IsAny<UserId>(), It.IsAny<Currency>()))
            .ReturnsAsync(new DbResult<bool>.Success(false));
        _repository.Setup(r => r.CreateAccountAsync(It.IsAny<Account>()))
            .ReturnsAsync((Account a) => new DbResult<Account>.Success(
                Account.Create(99, a.UserId, a.Balance, a.Currency, a.CreatedAt, a.UpdatedAt)));
    }

    private Task<CreateAccountResult> Handle(int userId, string? currency) =>
        new CreateAccountHandler(_repository.Object, NullLogger<CreateAccountHandler>.Instance)
            .Handle(new CreateAccountCommand(userId, currency));

    // ---- success ----

    [Fact]
    public async Task Handle_ValidUserAndCurrency_ReturnsCreatedAccount()
    {
        CreateAccountResult result = await Handle(1, "GBP");

        CreateAccountResult.Created created = Assert.IsType<CreateAccountResult.Created>(result);
        Assert.Equal(99, created.Account.Id);
        Assert.Equal(1, created.Account.UserId);
        Assert.Equal("GBP", created.Account.Currency.Code);
    }

    [Fact]
    public async Task Handle_NewAccount_OpensWithZeroBalance()
    {
        await Handle(1, "GBP");

        _repository.Verify(r => r.CreateAccountAsync(It.Is<Account>(a => a.Balance.Pence == 0)), Times.Once);
    }

    [Theory]
    [InlineData("USD")] [InlineData("EUR")] [InlineData("GBP")] [InlineData("JPY")] [InlineData("CAD")]
    [InlineData("AUD")] [InlineData("CHF")] [InlineData("SEK")] [InlineData("NOK")] [InlineData("DKK")]
    public async Task Handle_EachSupportedCurrency_ReturnsCreated(string currency)
    {
        CreateAccountResult result = await Handle(1, currency);

        Assert.IsType<CreateAccountResult.Created>(result);
    }

    // ---- invalid input ----

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Handle_NonPositiveUserId_ReturnsInvalidUserId(int userId)
    {
        CreateAccountResult result = await Handle(userId, "GBP");

        Assert.IsType<CreateAccountResult.InvalidUserId>(result);
        _repository.Verify(r => r.CreateAccountAsync(It.IsAny<Account>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gbp")]        // codes are upper-case only
    [InlineData("Gbp")]
    [InlineData(" GBP")]
    [InlineData("GBP ")]
    [InlineData("XYZ")]        // well-formed but unsupported
    [InlineData("GB")]
    [InlineData("GBPP")]
    [InlineData("123")]
    [InlineData("'; DROP TABLE accounts; --")]
    public async Task Handle_InvalidCurrency_ReturnsInvalidCurrency(string? currency)
    {
        CreateAccountResult result = await Handle(1, currency);

        Assert.IsType<CreateAccountResult.InvalidCurrency>(result);
        _repository.Verify(r => r.CreateAccountAsync(It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidUserIdAndInvalidCurrency_ReportsUserIdFirst()
    {
        CreateAccountResult result = await Handle(0, "XYZ");

        Assert.IsType<CreateAccountResult.InvalidUserId>(result);
    }

    // ---- one account per currency per user (T-008) ----

    [Fact]
    public async Task Handle_UserAlreadyHasAccountInCurrency_ReturnsAlreadyExistsWithoutInserting()
    {
        _repository.Setup(r => r.AccountExistsAsync(
                It.Is<UserId>(u => u.Value == 1), It.Is<Currency>(c => c.Code == "GBP")))
            .ReturnsAsync(new DbResult<bool>.Success(true));

        CreateAccountResult result = await Handle(1, "GBP");

        Assert.IsType<CreateAccountResult.AlreadyExists>(result);
        _repository.Verify(r => r.CreateAccountAsync(It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConcurrentDuplicateRejectedByUniqueConstraint_ReturnsAlreadyExists()
    {
        // The early check passed, but another request inserted the same currency first.
        _repository.Setup(r => r.CreateAccountAsync(It.IsAny<Account>()))
            .ReturnsAsync(new DbResult<Account>.Failure(DbError.ConstraintViolation));

        CreateAccountResult result = await Handle(1, "GBP");

        Assert.IsType<CreateAccountResult.AlreadyExists>(result);
    }

    [Fact]
    public async Task Handle_UserHasAccountInOtherCurrency_StillCreatesNewCurrency()
    {
        _repository.Setup(r => r.AccountExistsAsync(It.IsAny<UserId>(), It.Is<Currency>(c => c.Code == "EUR")))
            .ReturnsAsync(new DbResult<bool>.Success(true));

        CreateAccountResult result = await Handle(1, "GBP");

        Assert.IsType<CreateAccountResult.Created>(result);
    }
}
