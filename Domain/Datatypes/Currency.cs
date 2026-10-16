namespace Domain.Datatypes;

// An account's currency: a 3-letter code from the supported list. TryCreate returns null otherwise.
public sealed record Currency
{
    public static readonly IReadOnlyList<string> Supported =
        ["USD", "EUR", "GBP", "JPY", "CAD", "AUD", "CHF", "SEK", "NOK", "DKK"];

    public string Code { get; }

    private Currency(string code) => Code = code;

    public static Currency? TryCreate(string? code) =>
        code is not null && Supported.Contains(code) ? new Currency(code) : null;

    public override string ToString() => Code;
}
