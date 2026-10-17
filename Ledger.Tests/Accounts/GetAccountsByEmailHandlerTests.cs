using Application.Accounts;
using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ledger.Tests.Accounts;

// GetAccountsByEmailHandler. Repository mocked (T-016).
public class GetAccountsByEmailHandlerTests
{
    private readonly Mock<IAccountRepository> _repository = new();

    private Task<GetAccountsByEmailResult> Handle(string? email) =>
        new GetAccountsByEmailHandler(_repository.Object, NullLogger<GetAccountsByEmailHandler>.Instance)
            .Handle(new GetAccountsByEmailQuery(email!));

    private void RepositoryReturns(params Account[] accounts) =>
        _repository.Setup(r => r.GetAccountsByEmailAsync(It.IsAny<Email>()))
            .ReturnsAsync(new DbResult<IEnumerable<Account>>.Success(accounts));

    [Fact]
    public async Task Handle_UserWithAccounts_ReturnsFoundWithAllOfThem()
    {
        RepositoryReturns(AccountTestData.Account(1, 1, "GBP"), AccountTestData.Account(2, 1, "EUR"));

        GetAccountsByEmailResult result = await Handle("user1@example.com");

        GetAccountsByEmailResult.Found found = Assert.IsType<GetAccountsByEmailResult.Found>(result);
        Assert.Equal(2, found.Accounts.Count());
    }

    [Fact]
    public async Task Handle_UserWithNoAccounts_ReturnsFoundWithEmptyList()
    {
        RepositoryReturns();

        GetAccountsByEmailResult result = await Handle("user1@example.com");

        GetAccountsByEmailResult.Found found = Assert.IsType<GetAccountsByEmailResult.Found>(result);
        Assert.Empty(found.Accounts);
    }

    [Fact]
    public async Task Handle_EmailWithNoUser_ReturnsNotFound()
    {
        _repository.Setup(r => r.GetAccountsByEmailAsync(It.IsAny<Email>()))
            .ReturnsAsync(new DbResult<IEnumerable<Account>>.Failure(DbError.NotFound));

        GetAccountsByEmailResult result = await Handle("nobody@example.com");

        Assert.IsType<GetAccountsByEmailResult.NotFound>(result);
    }

    [Fact]
    public async Task Handle_MixedCaseEmail_QueriesNormalisedEmail()
    {
        RepositoryReturns();

        await Handle("  User1@EXAMPLE.com ");

        _repository.Verify(r => r.GetAccountsByEmailAsync(It.Is<Email>(e => e.EmailAddress == "user1@example.com")), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("user@")]
    [InlineData("'; DROP TABLE accounts; --")]
    public async Task Handle_MalformedEmail_ReturnsInvalidEmailWithoutQuerying(string? email)
    {
        GetAccountsByEmailResult result = await Handle(email);

        Assert.IsType<GetAccountsByEmailResult.InvalidEmail>(result);
        _repository.Verify(r => r.GetAccountsByEmailAsync(It.IsAny<Email>()), Times.Never);
    }
}
