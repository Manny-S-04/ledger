using Application.Accounts;
using Application.DbResult;
using Application.Interfaces;
using Domain.Accounts;
using Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ledger.Tests.Accounts;

// GetAccountsByUserIdHandler. Repository mocked (T-016).
// Ownership (token userId == route userId -> 403) is checked in the endpoint, not here.
public class GetAccountsByUserIdHandlerTests
{
    private readonly Mock<IAccountRepository> _repository = new();

    private Task<GetAccountsByUserIdResult> Handle(int userId) =>
        new GetAccountsByUserIdHandler(_repository.Object, NullLogger<GetAccountsByUserIdHandler>.Instance)
            .Handle(new GetAccountsByUserIdQuery(userId));

    [Fact]
    public async Task Handle_UserWithAccounts_ReturnsFoundWithAllOfThem()
    {
        _repository.Setup(r => r.GetAccountsByUserIdAsync(It.Is<UserId>(u => u.Value == 3)))
            .ReturnsAsync(new DbResult<IEnumerable<Account>>.Success(
                [AccountTestData.Account(3, 3, "GBP", 500m), AccountTestData.Account(11, 3, "EUR")]));

        GetAccountsByUserIdResult result = await Handle(3);

        GetAccountsByUserIdResult.Found found = Assert.IsType<GetAccountsByUserIdResult.Found>(result);
        Assert.Equal([3, 11], found.Accounts.Select(a => a.Id));
    }

    [Fact]
    public async Task Handle_UserWithNoAccounts_ReturnsFoundWithEmptyList()
    {
        _repository.Setup(r => r.GetAccountsByUserIdAsync(It.IsAny<UserId>()))
            .ReturnsAsync(new DbResult<IEnumerable<Account>>.Success([]));

        GetAccountsByUserIdResult result = await Handle(999);

        GetAccountsByUserIdResult.Found found = Assert.IsType<GetAccountsByUserIdResult.Found>(result);
        Assert.Empty(found.Accounts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Handle_NonPositiveUserId_ReturnsInvalidUserIdWithoutQuerying(int userId)
    {
        GetAccountsByUserIdResult result = await Handle(userId);

        Assert.IsType<GetAccountsByUserIdResult.InvalidUserId>(result);
        _repository.Verify(r => r.GetAccountsByUserIdAsync(It.IsAny<UserId>()), Times.Never);
    }
}
