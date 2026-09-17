using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;

namespace BannerAndBarrow.Tests;

public class FlowFieldTests
{
    private static readonly TestWorld.AsciiGrid WallWithGap = new(
        "..........",
        "..........",
        "#####.####",
        "..........",
        "..........");

    [Fact]
    public void Every_reachable_tile_flows_to_the_goal()
    {
        var goal = new TileCoord(1, 4);
        var field = FlowFieldBuilder.Build(WallWithGap, MovementClass.Military, new[] { goal });

        for (int y = 0; y < WallWithGap.Height; y++)
        for (int x = 0; x < WallWithGap.Width; x++)
        {
            if (!WallWithGap.IsPassable(x, y)) continue;
            int cx = x, cy = y, steps = 0;
            while (!field.IsGoal(cx, cy))
            {
                var d = field.Direction[cy * field.Width + cx];
                Assert.True(d >= 0, $"Tile ({cx},{cy}) has no direction");
                cx += FlowField.Offsets8[d].Dx;
                cy += FlowField.Offsets8[d].Dy;
                Assert.True(WallWithGap.IsPassable(cx, cy), $"Flow walked into a wall at ({cx},{cy})");
                Assert.True(++steps < 50, "Flow did not converge");
            }
        }
    }

    [Fact]
    public void Top_half_is_routed_through_the_gap()
    {
        var field = FlowFieldBuilder.Build(WallWithGap, MovementClass.Military, new[] { new TileCoord(1, 4) });
        // Directly above the wall at x=1 the only way is sideways towards the gap at x=5.
        var dir = field.GetDirection(1, 1);
        Assert.True(dir.X > Fix.Zero, $"Expected eastward flow, got {dir}");
    }

    [Fact]
    public void Enclosed_tiles_are_unreachable()
    {
        var grid = new TestWorld.AsciiGrid(
            ".....",
            ".###.",
            ".#.#.",
            ".###.",
            ".....");
        var field = FlowFieldBuilder.Build(grid, MovementClass.Civilian, new[] { new TileCoord(0, 0) });
        Assert.False(field.IsReachable(2, 2));
        Assert.True(field.IsReachable(4, 4));
    }

    [Fact]
    public void Diagonals_do_not_cut_wall_corners()
    {
        var grid = new TestWorld.AsciiGrid(
            "...",
            ".#.",
            "...");
        var field = FlowFieldBuilder.Build(grid, MovementClass.Military, new[] { new TileCoord(2, 2) });
        // From (1,0) the diagonal to (2,1) is fine; (0,1) must not go diagonally through the wall corner to (1,2)... it may go down.
        var d = field.Direction[1 * grid.Width + 0];
        Assert.NotEqual(1, (int)d); // 1 = SE, which would clip the wall at (1,1)
    }

    [Fact]
    public void Cache_serves_stale_fields_until_rebuild_budget_allows()
    {
        var grid = new MutableGrid(20, 20);
        var cache = new FlowFieldCache(grid, capacity: 8, rebuildBudgetPerTick: 1, minorStaleTicks: 10);
        var goals = new[] { new TileCoord(0, 0) };
        var a = cache.Get(new FlowGoalKey(1, MovementClass.Military), goals);
        var b = cache.Get(new FlowGoalKey(2, MovementClass.Military), new[] { new TileCoord(19, 19) });
        Assert.Equal(2, cache.TotalBuilds);

        grid.MajorVersion++;
        cache.Get(new FlowGoalKey(1, MovementClass.Military), goals);
        cache.Get(new FlowGoalKey(2, MovementClass.Military), new[] { new TileCoord(19, 19) });
        Assert.Equal(2, cache.TotalBuilds); // queued, not rebuilt synchronously

        cache.BeginTick(1);
        Assert.Equal(3, cache.TotalBuilds); // budget of one per tick
        cache.BeginTick(2);
        Assert.Equal(4, cache.TotalBuilds);
        Assert.Equal(grid.MajorVersion, a.MajorVersion);
        Assert.Equal(grid.MajorVersion, b.MajorVersion);
    }

    private sealed class MutableGrid : INavGrid
    {
        public MutableGrid(int w, int h)
        {
            Width = w;
            Height = h;
        }

        public int Width { get; }
        public int Height { get; }
        public int MajorVersion { get; set; } = 1;
        public int MinorVersion => 1;
        public bool IsPassable(int x, int y) => true;
        public Fix GetSpeedMultiplier(int x, int y, MovementClass movementClass) => Fix.One;
    }
}

public class MovementTests
{
    [Fact]
    public void Workers_take_the_road_detour_instead_of_cutting_across_grass()
    {
        var state = TestWorld.FlatState(40, 20, players: 1);
        var map = state.Map;
        // Road: up from (4,14) to (4,4), across to (32,4), down to (32,14). Straight grass line is 28 tiles.
        var road = new List<TileCoord>();
        for (int y = 14; y >= 4; y--) road.Add(new TileCoord(4, y));
        for (int x = 5; x <= 32; x++) road.Add(new TileCoord(x, 4));
        for (int y = 5; y <= 14; y++) road.Add(new TileCoord(32, y));
        foreach (var t in road) map.SetRoad(t, BannerAndBarrow.Simulation.Data.RoadKind.Dirt);

        var worker = BannerAndBarrow.Simulation.EntityOps.SpawnWorker(state, 0, new TileCoord(4, 14).Center);
        var goal = new TileCoord(32, 14);
        worker.SetFlowGoal(FlowGoalKey.ForTile(goal, map.Width, MovementClass.Civilian), new[] { goal }, goal.Center);

        bool usedRoad = false;
        for (int tick = 0; tick < 20 * 90 && !worker.Arrived; tick++)
        {
            state.FlowFields.BeginTick(tick);
            state.Movement.Step(state.Agents, Fix.FromRatio(1, 20));
            if (worker.Position.Y < Fix.FromInt(6)) usedRoad = true;
        }
        Assert.True(worker.Arrived, $"Worker did not arrive, at {worker.Position}");
        Assert.True(usedRoad, "Worker cut across the grass instead of following the road");
    }

    [Fact]
    public void Group_squeezes_through_a_chokepoint_without_jamming()
    {
        // 40 agents start on the left and must pass a 3-tile gap to reach the right side.
        var rows = new List<string>();
        for (int y = 0; y < 21; y++)
        {
            var row = new char[40];
            for (int x = 0; x < 40; x++) row[x] = x == 20 && (y < 9 || y > 11) ? '#' : '.';
            rows.Add(new string(row));
        }
        var grid = new TestWorld.AsciiGrid(rows.ToArray());
        var cache = new FlowFieldCache(grid, 16, 4, 100);
        var solver = new MovementSolver(grid, cache, new SteeringSettings());

        var goalTile = new TileCoord(34, 10);
        var goals = new[] { goalTile };
        var key = new FlowGoalKey(1, MovementClass.Military);
        var agents = new List<NavAgent>();
        for (int i = 0; i < 40; i++)
        {
            var a = new NavAgent
            {
                Id = i + 1,
                Position = new FixVec2(Fix.FromInt(3 + i % 8) + Fix.Half, Fix.FromInt(4 + i / 8 * 3) + Fix.Half),
                MaxSpeed = Fix.FromInt(2),
                Radius = Fix.FromRatio(3, 10),
                MovementClass = MovementClass.Military,
                ArrivalRadius = Fix.FromInt(5),
            };
            a.SetFlowGoal(key, goals, goalTile.Center);
            agents.Add(a);
        }

        var dt = Fix.FromRatio(1, 20);
        int tick = 0;
        for (; tick < 20 * 60 && agents.Any(a => a.Position.X < Fix.FromInt(26)); tick++)
        {
            cache.BeginTick(tick);
            solver.Step(agents, dt);
        }

        Assert.True(agents.All(a => a.Position.X >= Fix.FromInt(21)), $"Agents stuck left of the gap after {tick} ticks");
        foreach (var a in agents)
        {
            var t = a.Position.ToTile();
            Assert.True(grid.IsPassable(t.X, t.Y), $"Agent {a.Id} ended inside a wall at {a.Position}");
        }
    }

    [Fact]
    public void Separation_pushes_overlapping_agents_apart()
    {
        var grid = new TestWorld.AsciiGrid("..........", "..........", "..........", "..........");
        var cache = new FlowFieldCache(grid, 4, 1, 10);
        var solver = new MovementSolver(grid, cache, new SteeringSettings());
        var a = new NavAgent { Id = 1, Position = new FixVec2(Fix.FromInt(5), Fix.FromInt(2)) };
        var b = new NavAgent { Id = 2, Position = new FixVec2(Fix.FromInt(5) + Fix.FromRatio(1, 20), Fix.FromInt(2)) };
        var agents = new List<NavAgent> { a, b };
        for (int i = 0; i < 40; i++) solver.Step(agents, Fix.FromRatio(1, 20));
        Assert.True(FixVec2.Distance(a.Position, b.Position) >= a.Radius + b.Radius - Fix.FromRatio(1, 20));
    }
}
