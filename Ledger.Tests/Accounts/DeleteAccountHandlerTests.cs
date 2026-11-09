using Application.Accounts;
using Application.DbResult;
using Application.Interfaces;
using Application.Providers;
using Domain.Accounts;
using Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ledger.Tests.Accounts;

// DeleteAccountHandler closes the account (T-031). Repository and provider mocked.
public class DeleteAccountHandlerTests
{
    private const int AccountId = 5;
    private const int OwnerId = 1;
    private const int OtherUserId = 2;

    private readonly Mock<IAccountRepository> _repository = new();
    private readonly Mock<IProviderService> _provider = new();

    public DeleteAccountHandlerTests()
    {
        _repository.Setup(r => r.GetAccountOwnerAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<UserId>.Success(UserId.TryCreate(OwnerId)!.Value));
        _repository.Setup(r => r.GetAccountOwnerAsync(It.Is<int>(id => id != AccountId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<UserId>.Failure(DbError.NotFound));
        Blockers(new CloseBlockers(false, false, Array.Empty<ExpiredTopup>()));
        _repository.Setup(r => r.CloseAccountAsync(AccountId, It.IsAny<IReadOnlySet<TransactionId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<CloseAccountOutcome>.Success(CloseAccountOutcome.Closed));
    }

    private void Blockers(CloseBlockers blockers) =>
        _repository.Setup(r => r.GetCloseBlockersAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<CloseBlockers>.Success(blockers));

    private static TransactionId Tx(int id) => TransactionId.TryCreate(id)!.Value;

    private void ProviderSays(string reference, ProviderChargeStatus status) =>
        _provider.Setup(p => p.GetChargeStatusAsync(reference, It.IsAny<CancellationToken>())).ReturnsAsync(status);

    private Task<DeleteAccountResult> Handle(int accountId, int requestingUserId) =>
        new DeleteAccountHandler(_repository.Object, _provider.Object, NullLogger<DeleteAccountHandler>.Instance)
            .Handle(new DeleteAccountCommand(accountId, requestingUserId));

    private void VerifyNeverClosed() =>
        _repository.Verify(r => r.CloseAccountAsync(It.IsAny<int>(), It.IsAny<IReadOnlySet<TransactionId>>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Handle_OwnerClosesEmptyAccount_ReturnsClosed()
    {
        DeleteAccountResult result = await Handle(AccountId, OwnerId);

        Assert.IsType<DeleteAccountResult.Closed>(result);
    }

    [Fact]
    public async Task Handle_AlreadyClosed_ReturnsClosedWithoutAskingProviderOrClosingAgain()
    {
        Blockers(new CloseBlockers(true, false, new[] { new ExpiredTopup(Tx(3), "ch_3") }));

        DeleteAccountResult result = await Handle(AccountId, OwnerId);

        Assert.IsType<DeleteAccountResult.Closed>(result);
        _provider.VerifyNoOtherCalls();
        VerifyNeverClosed();
    }

    [Fact]
    public async Task Handle_CloseTransactionSeesAlreadyClosed_ReturnsClosed()
    {
        _repository.Setup(r => r.CloseAccountAsync(AccountId, It.IsAny<IReadOnlySet<TransactionId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<CloseAccountOutcome>.Success(CloseAccountOutcome.AlreadyClosed));

        Assert.IsType<DeleteAccountResult.Closed>(await Handle(AccountId, OwnerId));
    }

    [Fact]
    public async Task Handle_AccountWithBalance_ReturnsHasBalance()
    {
        _repository.Setup(r => r.CloseAccountAsync(AccountId, It.IsAny<IReadOnlySet<TransactionId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<CloseAccountOutcome>.Success(CloseAccountOutcome.HasBalance));

        Assert.IsType<DeleteAccountResult.HasBalance>(await Handle(AccountId, OwnerId));
    }

    [Fact]
    public async Task Handle_PendingTopup_ReturnsPendingTopupsWithoutAskingProvider()
    {
        Blockers(new CloseBlockers(false, true, Array.Empty<ExpiredTopup>()));

        Assert.IsType<DeleteAccountResult.PendingTopups>(await Handle(AccountId, OwnerId));
        _provider.VerifyNoOtherCalls();
        VerifyNeverClosed();
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("succeeded")]
    public async Task Handle_ExpiredTopupProviderMayStillPay_ReturnsPendingTopups(string providerStatus)
    {
        Blockers(new CloseBlockers(false, false, new[] { new ExpiredTopup(Tx(3), "ch_3") }));
        ProviderSays("ch_3", providerStatus == "processing"
            ? new ProviderChargeStatus.Processing()
            : new ProviderChargeStatus.Succeeded());

        Assert.IsType<DeleteAccountResult.PendingTopups>(await Handle(AccountId, OwnerId));
        VerifyNeverClosed();
    }

    [Fact]
    public async Task Handle_ExpiredTopupsProviderFailedOrNotFound_ClosesWithThoseCleared()
    {
        Blockers(new CloseBlockers(false, false, new[] { new ExpiredTopup(Tx(3), "ch_3"), new ExpiredTopup(Tx(4), "ch_4") }));
        ProviderSays("ch_3", new ProviderChargeStatus.Failed(default));
        ProviderSays("ch_4", new ProviderChargeStatus.NotFound());

        Assert.IsType<DeleteAccountResult.Closed>(await Handle(AccountId, OwnerId));
        _repository.Verify(r => r.CloseAccountAsync(AccountId,
            It.Is<IReadOnlySet<TransactionId>>(s => s.Count == 2 && s.Contains(Tx(3)) && s.Contains(Tx(4))), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExpiredTopupWithoutProviderReference_ReturnsPendingTopups()
    {
        Blockers(new CloseBlockers(false, false, new[] { new ExpiredTopup(Tx(3), null) }));

        Assert.IsType<DeleteAccountResult.PendingTopups>(await Handle(AccountId, OwnerId));
        VerifyNeverClosed();
    }

    [Fact]
    public async Task Handle_ProviderUnavailable_ReturnsProviderUnavailable()
    {
        Blockers(new CloseBlockers(false, false, new[] { new ExpiredTopup(Tx(3), "ch_3") }));
        ProviderSays("ch_3", new ProviderChargeStatus.Unavailable("timeout"));

        Assert.IsType<DeleteAccountResult.ProviderUnavailable>(await Handle(AccountId, OwnerId));
        VerifyNeverClosed();
    }

    [Fact]
    public async Task Handle_CloseTransactionFindsNewBlocker_ReturnsPendingTopups()
    {
        _repository.Setup(r => r.CloseAccountAsync(AccountId, It.IsAny<IReadOnlySet<TransactionId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<CloseAccountOutcome>.Success(CloseAccountOutcome.PendingTopups));

        Assert.IsType<DeleteAccountResult.PendingTopups>(await Handle(AccountId, OwnerId));
    }

    [Fact]
    public async Task Handle_SomeoneElsesAccount_ReturnsForbiddenAndNeverCloses()
    {
        DeleteAccountResult result = await Handle(AccountId, OtherUserId);

        Assert.IsType<DeleteAccountResult.Forbidden>(result);
        VerifyNeverClosed();
    }

    [Theory]
    [InlineData(999)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Handle_AccountDoesNotExist_ReturnsNotFoundAndNeverCloses(int accountId)
    {
        DeleteAccountResult result = await Handle(accountId, OwnerId);

        Assert.IsType<DeleteAccountResult.NotFound>(result);
        VerifyNeverClosed();
    }

    [Fact]
    public async Task Handle_AccountGoneAfterOwnerCheck_ReturnsNotFound()
    {
        _repository.Setup(r => r.CloseAccountAsync(AccountId, It.IsAny<IReadOnlySet<TransactionId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<CloseAccountOutcome>.Failure(DbError.NotFound));

        Assert.IsType<DeleteAccountResult.NotFound>(await Handle(AccountId, OwnerId));
    }
}
