using Application.Accounts;
using Application.DbResult;
using Application.Interfaces;
using Application.Providers;
using Domain.Accounts;
using Domain.Datatypes;
using Domain.Users;
using Moq;

namespace Ledger.Tests.Accounts;

// T-033: the request's CancellationToken reaches the provider call, so a client that disconnects while the
// provider hangs doesn't leave the handler waiting.
public class TopupAccountHandlerCancellationTests
{
    [Fact]
    public async Task Handle_TokenCancelledWhileProviderHangs_StopsWaiting()
    {
        UserId owner = UserId.TryCreate(1)!.Value;
        Mock<IAccountRepository> repository = new();
        repository.Setup(r => r.GetAccountOwnerAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<UserId>.Success(owner));
        repository.Setup(r => r.RecordTopupAttempt(It.IsAny<AccountId>(), It.IsAny<Amount>(), It.IsAny<Currency>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DbResult<TransactionId>.Success(TransactionId.TryCreate(1)!.Value));

        // A provider that never answers on its own: it only returns when the token it was given is cancelled.
        Mock<IProviderService> provider = new();
        provider.Setup(p => p.RequestChargeAsync(It.IsAny<ProviderChargeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProviderChargeRequest _, CancellationToken token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new ProviderChargeResult.Accepted("never");
            });

        TopupAccountHandler handler = new(repository.Object, provider.Object);
        TopupAccountCommand command = new(owner, AccountId.TryCreate(1)!.Value, Amount.TryCreate(10m)!.Value, Currency.TryCreate("GBP")!);

        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));
        Task<TopupAccountResult> handle = handler.Handle(command, cts.Token);

        Task finished = await Task.WhenAny(handle, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(handle, finished);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle);
    }
}
