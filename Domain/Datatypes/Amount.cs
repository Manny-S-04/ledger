using System.Globalization;

namespace Domain.Datatypes;

public readonly record struct Amount
{
    public long Pence { get; }

    private Amount(long pence) => Pence = pence;

    public static Amount? TryCreate(decimal pounds)
    {
        if (pounds < 0)
            return null;

        // Checked before scaling: pounds * 100 overflows decimal for very large values.
        if (pounds > long.MaxValue / 100m)
            return null;
        var scaled = pounds * 100m;

        if (scaled != decimal.Truncate(scaled))
            return null;

        if (scaled > long.MaxValue)
            return null;

        return new Amount((long)scaled);
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
