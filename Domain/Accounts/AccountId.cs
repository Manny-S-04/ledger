namespace Domain.Accounts;

public readonly record struct AccountId
{
    public int Value { get; }

    private AccountId(int value) => Value = value;

    public static AccountId? TryCreate(int value)
        => value > 0 ? new AccountId(value) : null;
}
