namespace Domain.Users;

// A plaintext password that satisfies the signup rules. Only ever hashed, never stored or logged.
public sealed class Password
{
    public const int MinLength = 3;
    public const int MaxLength = 8;

    private readonly string _value;

    private Password(string value) => _value = value;

    // Returns null with the broken rule in `error` when the value doesn't satisfy the rules.
    public static Password? TryCreate(string? value, out string? error)
    {
        if (string.IsNullOrEmpty(value))
        {
            error = "Password is required.";
            return null;
        }

        if (value.Length < MinLength || value.Length > MaxLength)
        {
            error = $"Password must be {MinLength}-{MaxLength} characters.";
            return null;
        }

        error = null;
        return new Password(value);
    }

    public string Hash(IPasswordHasher hasher) => hasher.Hash(_value);

    public override string ToString() => "********";
}
