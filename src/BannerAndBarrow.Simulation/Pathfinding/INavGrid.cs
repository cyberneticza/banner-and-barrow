using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Pathfinding;

/// <summary>Which movement rules an agent follows. Roads affect the two classes differently.</summary>
public enum MovementClass : byte
{
    Civilian = 0,
    Military = 1,
}

/// <summary>
/// The only view of the world the pathfinding module sees. Keeps flow fields, steering and avoidance
/// testable without a GameState.
/// </summary>
public interface INavGrid
{
    int Width { get; }
    int Height { get; }

    bool IsPassable(int x, int y);

    /// <summary>
    /// Passability for one Player. Gates are open to their owner and closed to everyone else; everything else is
    /// the same for all Players.
    /// </summary>
    bool IsPassableFor(int x, int y, int player) => IsPassable(x, y);

    /// <summary>Movement speed multiplier on a passable tile (roads, forest, hills).</summary>
    Fix GetSpeedMultiplier(int x, int y, MovementClass movementClass);

    /// <summary>
    /// Extra route-planning cost on top of speed (1 = none). Lets Workers strongly prefer roads: an off-road
    /// tile can cost more than its slowness alone would justify.
    /// </summary>
    Fix GetRouteCostMultiplier(int x, int y, MovementClass movementClass) => Fix.One;

    /// <summary>Bumped when passability or road layout changes. Flow fields built on an older version are rebuilt.</summary>
    int MajorVersion { get; }

    /// <summary>Bumped for cheap cosmetic cost changes (trees felled/regrown). Rebuilt lazily after a delay.</summary>
    int MinorVersion { get; }
}

public static class NavGridExtensions
{
    public static bool InBounds(this INavGrid grid, int x, int y) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height;

    public static bool IsPassableSafe(this INavGrid grid, int x, int y) => grid.InBounds(x, y) && grid.IsPassable(x, y);

    public static bool IsPassableSafe(this INavGrid grid, int x, int y, int player) =>
        grid.InBounds(x, y) && (player < 0 ? grid.IsPassable(x, y) : grid.IsPassableFor(x, y, player));

    /// <summary>Supercover line walk: true if every tile the segment touches is passable.</summary>
    public static bool HasLineOfSight(this INavGrid grid, FixVec2 from, FixVec2 to)
    {
        int x = from.X.FloorToInt(), y = from.Y.FloorToInt();
        int endX = to.X.FloorToInt(), endY = to.Y.FloorToInt();
        if (!grid.IsPassableSafe(x, y) || !grid.IsPassableSafe(endX, endY)) return false;

        var d = to - from;
        int stepX = d.X.Raw > 0 ? 1 : d.X.Raw < 0 ? -1 : 0;
        int stepY = d.Y.Raw > 0 ? 1 : d.Y.Raw < 0 ? -1 : 0;

        // Parametric distance (in units of the whole segment) to the next vertical/horizontal tile boundary.
        Fix tDeltaX = stepX == 0 ? Fix.MaxValue : Fix.One / Fix.Abs(d.X);
        Fix tDeltaY = stepY == 0 ? Fix.MaxValue : Fix.One / Fix.Abs(d.Y);
        Fix tMaxX = stepX == 0 ? Fix.MaxValue
            : (stepX > 0 ? (Fix.FromInt(x + 1) - from.X) : (from.X - Fix.FromInt(x))) * tDeltaX;
        Fix tMaxY = stepY == 0 ? Fix.MaxValue
            : (stepY > 0 ? (Fix.FromInt(y + 1) - from.Y) : (from.Y - Fix.FromInt(y))) * tDeltaY;

        int guard = 0;
        while ((x != endX || y != endY) && guard++ < 512)
        {
            if (tMaxX < tMaxY)
            {
                tMaxX += tDeltaX;
                x += stepX;
            }
            else if (tMaxY < tMaxX)
            {
                tMaxY += tDeltaY;
                y += stepY;
            }
            else
            {
                // Passing exactly through a corner: both neighbours must be open to avoid squeezing diagonally.
                if (!grid.IsPassableSafe(x + stepX, y) || !grid.IsPassableSafe(x, y + stepY)) return false;
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
                x += stepX;
                y += stepY;
            }
            if (!grid.IsPassableSafe(x, y)) return false;
        }
        return true;
    }
}
