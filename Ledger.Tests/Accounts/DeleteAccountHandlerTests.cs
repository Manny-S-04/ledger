using Application.Accounts;
using Application.DbResult;
using Application.Interfaces;
using Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ledger.Tests.Accounts;

// DeleteAccountHandler. Repository mocked; covers user-facing outcomes, not DB failures (T-016).
public class DeleteAccountHandlerTests
{
    private const int AccountId = 5;
    private const int OwnerId = 1;
    private const int OtherUserId = 2;

    private readonly Mock<IAccountRepository> _repository = new();

    public DeleteAccountHandlerTests()
    {
        _repository.Setup(r => r.GetAccountOwnerAsync(AccountId))
            .ReturnsAsync(new DbResult<UserId>.Success(UserId.TryCreate(OwnerId)!.Value));
        _repository.Setup(r => r.GetAccountOwnerAsync(It.Is<int>(id => id != AccountId)))
            .ReturnsAsync(new DbResult<UserId>.Failure(DbError.NotFound));
        _repository.Setup(r => r.DeleteAccountAsync(AccountId))
            .ReturnsAsync(new DbResult<bool>.Success(true));
    }

    private Task<DeleteAccountResult> Handle(int accountId, int requestingUserId) =>
        new DeleteAccountHandler(_repository.Object, NullLogger<DeleteAccountHandler>.Instance)
            .Handle(new DeleteAccountCommand(accountId, requestingUserId));

    [Fact]
    public async Task Handle_OwnerDeletesEmptyAccount_ReturnsDeleted()
    {
        DeleteAccountResult result = await Handle(AccountId, OwnerId);

        Assert.IsType<DeleteAccountResult.Deleted>(result);
    }

    [Fact]
    public async Task Handle_AccountWithBalance_ReturnsHasBalance()
    {
        _repository.Setup(r => r.DeleteAccountAsync(AccountId))
            .ReturnsAsync(new DbResult<bool>.Success(false));

        DeleteAccountResult result = await Handle(AccountId, OwnerId);

        Assert.IsType<DeleteAccountResult.HasBalance>(result);
    }

    [Fact]
    public async Task Handle_SomeoneElsesAccount_ReturnsForbiddenAndNeverDeletes()
    {
        DeleteAccountResult result = await Handle(AccountId, OtherUserId);

        Assert.IsType<DeleteAccountResult.Forbidden>(result);
        _repository.Verify(r => r.DeleteAccountAsync(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Handle_AccountDoesNotExist_ReturnsNotFoundAndNeverDeletes(int accountId)
    {
        DeleteAccountResult result = await Handle(accountId, OwnerId);

        Assert.IsType<DeleteAccountResult.NotFound>(result);
        _repository.Verify(r => r.DeleteAccountAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AccountDeletedConcurrentlyAfterOwnerCheck_ReturnsNotFound()
    {
        _repository.Setup(r => r.DeleteAccountAsync(AccountId))
            .ReturnsAsync(new DbResult<bool>.Failure(DbError.NotFound));

        DeleteAccountResult result = await Handle(AccountId, OwnerId);

        Assert.IsType<DeleteAccountResult.NotFound>(result);
    }
}
