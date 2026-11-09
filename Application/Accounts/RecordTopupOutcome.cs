using Domain.Accounts;

namespace Application.Accounts;

// Result of recording a top-up attempt; the account's status is checked under the row lock (T-031.12).
public abstract record RecordTopupOutcome
{
    public sealed record Recorded(TransactionId TransactionId) : RecordTopupOutcome;
    public sealed record AccountClosed : RecordTopupOutcome;
}
