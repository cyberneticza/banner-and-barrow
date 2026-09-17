namespace BannerAndBarrow.Simulation.Math;

/// <summary>2D fixed-point vector in Tile units. +X is east, +Y is south (screen space).</summary>
public readonly struct FixVec2 : IEquatable<FixVec2>
{
    public readonly Fix X;
    public readonly Fix Y;

    public FixVec2(Fix x, Fix y)
    {
        X = x;
        Y = y;
    }

    public static readonly FixVec2 Zero = new(Fix.Zero, Fix.Zero);
    public static readonly FixVec2 North = new(Fix.Zero, -Fix.One);

    public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static FixVec2 operator -(FixVec2 a) => new(-a.X, -a.Y);
    public static FixVec2 operator *(FixVec2 a, Fix s) => new(a.X * s, a.Y * s);
    public static FixVec2 operator *(Fix s, FixVec2 a) => new(a.X * s, a.Y * s);
    public static FixVec2 operator /(FixVec2 a, Fix s) => new(a.X / s, a.Y / s);
    public static FixVec2 operator /(FixVec2 a, int s) => new(a.X / s, a.Y / s);
    public static bool operator ==(FixVec2 a, FixVec2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(FixVec2 a, FixVec2 b) => !(a == b);

    public Fix LengthSquared => X * X + Y * Y;
    public Fix Length => Fix.Sqrt(LengthSquared);
    public bool IsZero => X.Raw == 0 && Y.Raw == 0;

    /// <summary>Perpendicular pointing to the right of this vector when it is used as a forward direction.</summary>
    public FixVec2 Right => new(-Y, X);

    public static Fix Dot(FixVec2 a, FixVec2 b) => a.X * b.X + a.Y * b.Y;
    public static Fix Cross(FixVec2 a, FixVec2 b) => a.X * b.Y - a.Y * b.X;
    public static Fix DistanceSquared(FixVec2 a, FixVec2 b) => (a - b).LengthSquared;
    public static Fix Distance(FixVec2 a, FixVec2 b) => (a - b).Length;

    public FixVec2 Normalized()
    {
        var len = Length;
        return len.Raw == 0 ? Zero : new FixVec2(X / len, Y / len);
    }

    public FixVec2 Truncated(Fix maxLength)
    {
        var lenSq = LengthSquared;
        if (lenSq <= maxLength * maxLength) return this;
        var len = Fix.Sqrt(lenSq);
        return len.Raw == 0 ? Zero : this * (maxLength / len);
    }

    /// <summary>Transforms a local offset (X = right, Y = forward) into world space for the given forward direction.</summary>
    public static FixVec2 LocalToWorld(FixVec2 local, FixVec2 forward) => forward.Right * local.X + forward * local.Y;

    public static FixVec2 Lerp(FixVec2 a, FixVec2 b, Fix t) => a + (b - a) * t;

    public TileCoord ToTile() => new(X.FloorToInt(), Y.FloorToInt());

    public bool Equals(FixVec2 other) => this == other;
    public override bool Equals(object? obj) => obj is FixVec2 v && v == this;
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}

public readonly record struct TileCoord(int X, int Y)
{
    public FixVec2 Center => new(Fix.FromInt(X) + Fix.Half, Fix.FromInt(Y) + Fix.Half);

    public static int ChebyshevDistance(TileCoord a, TileCoord b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    public static int DistanceSquared(TileCoord a, TileCoord b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
