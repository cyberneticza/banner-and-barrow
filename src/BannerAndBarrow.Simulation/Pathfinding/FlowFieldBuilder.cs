using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Pathfinding;

/// <summary>Builds a <see cref="FlowField"/> with a Dijkstra sweep outwards from the goal tiles.</summary>
public static class FlowFieldBuilder
{
    private const int OrthogonalStep = 1000;
    private const int DiagonalStep = 1414;
    private static readonly Fix CostScale = Fix.FromInt(100);

    public static FlowField Build(INavGrid grid, MovementClass movementClass, IReadOnlyList<TileCoord> goals, int player = -1)
    {
        var field = new FlowField(grid.Width, grid.Height, movementClass) { Player = player };
        Rebuild(field, grid, goals);
        return field;
    }

    public static void Rebuild(FlowField field, INavGrid grid, IReadOnlyList<TileCoord> goals)
    {
        int w = grid.Width, h = grid.Height, n = w * h;
        var integration = field.Integration;
        var cost = new int[n];
        Array.Fill(integration, FlowField.Unreachable);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            if (!grid.IsPassableSafe(x, y, field.Player))
            {
                cost[i] = 0;
                continue;
            }
            var mult = grid.GetSpeedMultiplier(x, y, field.MovementClass);
            if (mult.Raw <= 0) mult = Fix.FromRatio(1, 100);
            // Cost per tile is inversely proportional to speed: slow terrain is expensive.
            cost[i] = System.Math.Max(1, (CostScale / mult * grid.GetRouteCostMultiplier(x, y, field.MovementClass)).RoundToInt());
        }

        var open = new PriorityQueue<int, long>();
        foreach (var g in goals)
        {
            if (!grid.InBounds(g.X, g.Y)) continue;
            int gi = g.Y * w + g.X;
            if (cost[gi] == 0 || integration[gi] == 0) continue;
            integration[gi] = 0;
            open.Enqueue(gi, 0);
        }

        while (open.TryDequeue(out int current, out long priority))
        {
            if (priority != integration[current]) continue; // stale queue entry
            int cx = current % w, cy = current / w;
            for (int d = 0; d < 8; d++)
            {
                var (dx, dy) = FlowField.Offsets8[d];
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int ni = ny * w + nx;
                if (cost[ni] == 0) continue;
                bool diagonal = dx != 0 && dy != 0;
                if (diagonal && (cost[cy * w + nx] == 0 || cost[ny * w + cx] == 0)) continue; // no corner cutting
                long step = (long)(cost[current] + cost[ni]) * (diagonal ? DiagonalStep : OrthogonalStep) / 2000;
                long candidate = integration[current] + System.Math.Max(1, step);
                if (candidate < integration[ni])
                {
                    integration[ni] = (int)System.Math.Min(candidate, int.MaxValue - 1);
                    open.Enqueue(ni, integration[ni]);
                }
            }
        }

        ComputeDirections(field, cost);
        field.Goals = goals.ToArray();
        field.MajorVersion = grid.MajorVersion;
        field.MinorVersion = grid.MinorVersion;
    }

    private static void ComputeDirections(FlowField field, int[] cost)
    {
        int w = field.Width, h = field.Height;
        var integration = field.Integration;
        var direction = field.Direction;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            direction[i] = FlowField.NoDirection;
            if (integration[i] == FlowField.Unreachable || integration[i] == 0) continue;
            int best = integration[i];
            sbyte bestDir = FlowField.NoDirection;
            for (int d = 0; d < 8; d++)
            {
                var (dx, dy) = FlowField.Offsets8[d];
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                if (dx != 0 && dy != 0 && (cost[y * w + nx] == 0 || cost[ny * w + x] == 0)) continue;
                int v = integration[ny * w + nx];
                if (v < best)
                {
                    best = v;
                    bestDir = (sbyte)d;
                }
            }
            direction[i] = bestDir;
        }
    }
}
