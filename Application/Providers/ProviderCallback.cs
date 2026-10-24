using Domain.Accounts;
using Domain.Datatypes;

namespace Application.Providers;

// The provider's callback (webhook) telling the ledger the final outcome of a charge (handled in T-023).
// Sequence: the provider's per-charge event counter, used to order callbacks (T-023.3 b).
public sealed record ProviderCallback(
    string ProviderReference,
    TransactionId Reference,
    ProviderCallbackStatus Status,
    ProviderFailureReason? FailureReason,
    Amount Amount,
    Currency Currency,
    long Sequence);

public enum ProviderCallbackStatus
{
    Succeeded,
    Failed
}
