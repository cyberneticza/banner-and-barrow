using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Pathfinding;

/// <summary>
/// Integration field + per-tile direction towards a set of goal tiles. One field serves every agent
/// heading to the same goal, which is what makes group movement cheap compared with per-unit A*.
/// </summary>
public sealed class FlowField
{
    public const int Unreachable = int.MaxValue;
    public const sbyte NoDirection = -1;

    // Order: E, SE, S, SW, W, NW, N, NE
    public static readonly (int Dx, int Dy)[] Offsets8 =
    {
        (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
    };

    private static readonly Fix Diag = Fix.FromRaw(46341); // 1/sqrt(2)

    public static readonly FixVec2[] Directions8 =
    {
        new(Fix.One, Fix.Zero), new(Diag, Diag), new(Fix.Zero, Fix.One), new(-Diag, Diag),
        new(-Fix.One, Fix.Zero), new(-Diag, -Diag), new(Fix.Zero, -Fix.One), new(Diag, -Diag),
    };

    public int Width { get; }
    public int Height { get; }
    /// <summary>Whose Gates count as open in this field (-1: none).</summary>
    public int Player { get; internal set; } = -1;

    public MovementClass MovementClass { get; }
    public int[] Integration { get; }
    public sbyte[] Direction { get; }
    public TileCoord[] Goals { get; internal set; } = Array.Empty<TileCoord>();
    public int MajorVersion { get; internal set; } = -1;
    public int MinorVersion { get; internal set; } = -1;
    public long BuiltAtTick { get; internal set; }

    public FlowField(int width, int height, MovementClass movementClass)
    {
        Width = width;
        Height = height;
        MovementClass = movementClass;
        Integration = new int[width * height];
        Direction = new sbyte[width * height];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int GetIntegration(int x, int y) => InBounds(x, y) ? Integration[y * Width + x] : Unreachable;

    public bool IsReachable(int x, int y) => GetIntegration(x, y) != Unreachable;

    public bool IsGoal(int x, int y) => GetIntegration(x, y) == 0;

    public FixVec2 GetDirection(int x, int y)
    {
        if (!InBounds(x, y)) return FixVec2.Zero;
        var d = Direction[y * Width + x];
        return d < 0 ? FixVec2.Zero : Directions8[d];
    }

    /// <summary>
    /// Bilinear blend of the four nearest tile directions, so agents turn smoothly instead of snapping
    /// to 45-degree headings at tile borders. Falls back to the agent's own tile near walls.
    /// </summary>
    public FixVec2 Sample(FixVec2 position)
    {
        int tx = position.X.FloorToInt(), ty = position.Y.FloorToInt();
        var own = GetDirection(tx, ty);

        var local = position - new FixVec2(Fix.Half, Fix.Half);
        int x0 = local.X.FloorToInt(), y0 = local.Y.FloorToInt();
        Fix fx = local.X - Fix.FromInt(x0), fy = local.Y - Fix.FromInt(y0);

        var sum = FixVec2.Zero;
        Fix weightSum = Fix.Zero;
        Accumulate(x0, y0, (Fix.One - fx) * (Fix.One - fy), ref sum, ref weightSum);
        Accumulate(x0 + 1, y0, fx * (Fix.One - fy), ref sum, ref weightSum);
        Accumulate(x0, y0 + 1, (Fix.One - fx) * fy, ref sum, ref weightSum);
        Accumulate(x0 + 1, y0 + 1, fx * fy, ref sum, ref weightSum);

        if (weightSum.Raw == 0 || sum.IsZero) return own;
        var blended = sum.Normalized();
        // Never let blending point against the tile's own direction (would oscillate at wall corners).
        if (!own.IsZero && FixVec2.Dot(blended, own) < Fix.FromRatio(1, 4)) return own;
        return blended;
    }

    private void Accumulate(int x, int y, Fix weight, ref FixVec2 sum, ref Fix weightSum)
    {
        if (weight.Raw == 0 || !InBounds(x, y)) return;
        int i = y * Width + x;
        if (Integration[i] == Unreachable || Direction[i] < 0) return;
        sum += Directions8[Direction[i]] * weight;
        weightSum += weight;
    }
}
