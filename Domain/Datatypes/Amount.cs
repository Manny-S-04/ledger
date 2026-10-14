using System.Globalization;

namespace Domain.Datatypes;

public readonly record struct Amount
{
    public long Pence { get; }

    private Amount(long pence) => Pence = pence;

    public static Amount Create(decimal pounds) =>
        TryCreate(pounds, out var amount)
            ? amount
            : throw new ArgumentOutOfRangeException(
                nameof(pounds),
                "Amount must be positive with at most 2 decimal places.");

    public static bool TryCreate(decimal pounds, out Amount amount)
    {
        amount = default;

        if (pounds <= 0)
            return false;

        var scaled = pounds * 100m;

        if (scaled != decimal.Truncate(scaled))
            return false;

        if (scaled > long.MaxValue)
            return false;

        amount = new Amount((long)scaled);
        return true;
    }

    public static Amount operator +(Amount left, Amount right) =>
        new(checked(left.Pence + right.Pence));

    public static bool operator >(Amount left, Amount right) => left.Pence > right.Pence;
    public static bool operator <(Amount left, Amount right) => left.Pence < right.Pence;
    public static bool operator >=(Amount left, Amount right) => left.Pence >= right.Pence;
    public static bool operator <=(Amount left, Amount right) => left.Pence <= right.Pence;

    public override string ToString() =>
        (Pence / 100m).ToString("F2", CultureInfo.InvariantCulture);
}
