namespace Domain.Accounts;

// The ledger's id for one top-up: the id of its account_transactions row. It is sent to the provider as
// the charge reference and comes back in the callback, so both sides talk about the same transaction.
public readonly record struct TransactionId
{
    public int Value { get; }

    private TransactionId(int value) => Value = value;

    public static TransactionId? TryCreate(int value)
        => value > 0 ? new TransactionId(value) : null;
}
