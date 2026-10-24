namespace Domain.Users;

public readonly record struct UserId
{
    public int Value { get; }

    private UserId(int value) => Value = value;

    public static UserId? TryCreate(int value)
        => value > 0 ? new UserId(value) : null;

    public static bool Equals(UserId left, UserId right) => left.Value == right.Value;
}
