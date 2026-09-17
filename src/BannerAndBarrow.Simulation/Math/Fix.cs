using System.Globalization;

namespace BannerAndBarrow.Simulation.Math;

/// <summary>
/// Deterministic fixed-point number: a <see cref="long"/> with 16 fractional bits.
/// All simulation math uses this type; float/double are banned from the simulation assembly
/// (enforced by NoFloatingPointTests). World distances are measured in Tiles.
/// </summary>
public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
{
    public const int FractionBits = 16;
    public const long OneRaw = 1L << FractionBits;

    public readonly long Raw;

    private Fix(long raw) => Raw = raw;

    public static readonly Fix Zero = new(0);
    public static readonly Fix One = new(OneRaw);
    public static readonly Fix Half = new(OneRaw / 2);
    public static readonly Fix MaxValue = new(long.MaxValue);
    public static readonly Fix MinValue = new(long.MinValue);
    public static readonly Fix Epsilon = new(1);

    public static Fix FromRaw(long raw) => new(raw);
    public static Fix FromInt(int value) => new(value * OneRaw);
    public static Fix FromRatio(long numerator, long denominator) => new(numerator * OneRaw / denominator);
    public static Fix FromDecimal(decimal value) => new((long)decimal.Round(value * OneRaw));

    public static implicit operator Fix(int value) => FromInt(value);

    public static Fix operator +(Fix a, Fix b) => new(a.Raw + b.Raw);
    public static Fix operator -(Fix a, Fix b) => new(a.Raw - b.Raw);
    public static Fix operator -(Fix a) => new(-a.Raw);
    public static Fix operator *(Fix a, Fix b) => new((long)(((Int128)a.Raw * b.Raw) >> FractionBits));
    public static Fix operator *(Fix a, int b) => new(a.Raw * b);
    public static Fix operator /(Fix a, int b) => new(a.Raw / b);

    public static Fix operator /(Fix a, Fix b)
    {
        if (b.Raw == 0) throw new DivideByZeroException("Fix division by zero");
        return new((long)(((Int128)a.Raw << FractionBits) / b.Raw));
    }

    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

    /// <summary>Rounds toward negative infinity.</summary>
    public int FloorToInt() => (int)(Raw >> FractionBits);

    public int RoundToInt() => (int)((Raw + OneRaw / 2) >> FractionBits);

    public int CeilToInt() => (int)((Raw + OneRaw - 1) >> FractionBits);

    public decimal ToDecimal() => (decimal)Raw / OneRaw;

    public static Fix Abs(Fix a) => a.Raw < 0 ? new(-a.Raw) : a;
    public static Fix Min(Fix a, Fix b) => a.Raw < b.Raw ? a : b;
    public static Fix Max(Fix a, Fix b) => a.Raw > b.Raw ? a : b;
    public static Fix Clamp(Fix v, Fix min, Fix max) => v.Raw < min.Raw ? min : v.Raw > max.Raw ? max : v;
    public static Fix Lerp(Fix a, Fix b, Fix t) => a + (b - a) * t;

    public static Fix Sqrt(Fix a)
    {
        if (a.Raw <= 0) return Zero;
        // sqrt(raw / 2^16) * 2^16 == sqrt(raw * 2^16)
        return new((long)IntMath.Sqrt((UInt128)(ulong)a.Raw << FractionBits));
    }

    public bool Equals(Fix other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix f && f.Raw == Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);
    public override string ToString() => ToDecimal().ToString("0.###", CultureInfo.InvariantCulture);
}

public static class IntMath
{
    public static ulong Sqrt(UInt128 value)
    {
        if (value == 0) return 0;
        UInt128 op = value;
        UInt128 res = 0;
        UInt128 one = (UInt128)1 << 126;
        while (one > op) one >>= 2;
        while (one != 0)
        {
            if (op >= res + one)
            {
                op -= res + one;
                res = (res >> 1) + one;
            }
            else
            {
                res >>= 1;
            }
            one >>= 2;
        }
        return (ulong)res;
    }

    public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
}
