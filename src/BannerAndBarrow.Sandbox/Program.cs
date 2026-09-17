using System.Diagnostics;
using System.Text;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;
using BannerAndBarrow.Simulation.World;

// Headless tools for iterating on the simulation without the game window.
//   dotnet run --project src/BannerAndBarrow.Sandbox -- match [minutes] [seed] [profile]   AI vs AI, prints a timeline
//   dotnet run --project src/BannerAndBarrow.Sandbox -- map [seed]                         ASCII map dump
//   dotnet run --project src/BannerAndBarrow.Sandbox -- flowbench [seed]                   flow field build timings

var configDir = Environment.GetEnvironmentVariable("SANDBOX_CONFIG")
                ?? ConfigLoader.FindConfigDirectory(AppContext.BaseDirectory) ?? ConfigLoader.FindConfigDirectory(Directory.GetCurrentDirectory());
if (configDir == null)
{
    Console.Error.WriteLine("Could not find config/balance.json");
    return 1;
}
var config = ConfigLoader.LoadFromDirectory(configDir);
var mode = args.Length > 0 ? args[0] : "match";

switch (mode)
{
    case "map":
        DumpMap(config, args.Length > 1 ? int.Parse(args[1]) : config.Map.Seed);
        break;
    case "flowbench":
        FlowBench(config, args.Length > 1 ? int.Parse(args[1]) : config.Map.Seed);
        break;
    default:
        RunMatch(config,
            args.Length > 1 ? int.Parse(args[1]) : 20,
            args.Length > 2 ? int.Parse(args[2]) : config.Map.Seed,
            args.Length > 3 ? args[3] : null);
        break;
}
return 0;

static void RunMatch(GameConfig config, int minutes, int seed, string? profile)
{
    var match = Match.Create(config, seed, aiForBothPlayers: true, aiProfile: profile);
    var state = match.State;
    state.CollectStats = true;
    int tps = config.Simulation.TicksPerSecond;
    long totalTicks = minutes * 60L * tps;
    var sw = Stopwatch.StartNew();
    long worstTickMs = 0;
    int eventCursor = 0;
    var checks = Environment.GetEnvironmentVariable("SANDBOX_CHECK") == "1" ? new ConstructionChecks() : null;

    for (long t = 0; t < totalTicks && !state.IsOver; t++)
    {
        var tickStart = sw.ElapsedMilliseconds;
        match.Step();
        worstTickMs = Math.Max(worstTickMs, sw.ElapsedMilliseconds - tickStart);

        for (; eventCursor < state.Events.Count; eventCursor++)
        {
            var e = state.Events[eventCursor];
            if (e.Kind is GameEventKind.Info && !e.Message.Contains("AI ") && !e.Message.Contains("Stronghold")) continue;
            Console.WriteLine($"[{Clock(e.Tick, tps)}] P{e.Player} {e.Kind}: {e.Message}");
        }
        if (state.Events.Count > 250)
        {
            state.Events.Clear();
            eventCursor = 0;
        }

        if (checks != null && state.Tick % (5 * tps) == 0) checks.Run(state);

        if (state.Tick % (60 * tps) == 0)
        {
            foreach (var p in state.Players)
            {
                var stock = state.TotalStock(p.Index);
                int buildings = state.Buildings.Values.Count(b => b.Owner == p.Index);
                int regiments = state.Regiments.Values.Count(r => r.Owner == p.Index);
                Console.WriteLine($"[{Clock(state.Tick, tps)}] P{p.Index} buildings={buildings} workers={state.WorkerCount(p.Index)}/{state.WorkerRoom(p.Index)} hungry={state.HungryWorkers(p.Index)} " +
                                  $"regiments={regiments} soldiers={state.SoldierCount(p.Index)} " +
                                  string.Join(" ", Resources.All.Select(r => $"{r}={stock[(int)r]}")));
            }
            if (Environment.GetEnvironmentVariable("SANDBOX_TRACE") == "1")
                foreach (var r in state.Regiments.Values)
                {
                    var enemyKeep = state.GetKeep(1 - r.Owner);
                    var c = state.RegimentCentroid(r);
                    var dist = enemyKeep == null ? Fix.Zero : FixVec2.Distance(c, enemyKeep.Center);
                    Console.WriteLine($"          P{r.Owner} {r.Type} n={r.SoldierIds.Count} {r.Order} at {c.ToTile()} keepDist={dist.RoundToInt()} " +
                                      $"engR={r.EngagedRegimentId} engB={r.EngagedBuildingId} tgtB={r.TargetBuildingId} arrived={r.AnchorArrived} lag={r.AverageLag} coh={r.Cohesion.RoundToInt()} " +
                                      $"eng=[{(state.GetRegiment(r.EngagedRegimentId) is {} er ? $"P{er.Owner} {er.Type} n={er.SoldierIds.Count} at {state.RegimentCentroid(er).ToTile()} stationed={er.IsStationed}" : "-")}] " +
                                      $"anchor={r.Anchor.ToTile()} dest={r.Destination.ToTile()} reachable={(state.FlowFields.TryPeek(r.DestinationKey, out var ff) && ff != null ? ff.IsReachable(c.ToTile().X, c.ToTile().Y).ToString() : "nofield")}");
                }
            Console.WriteLine("        damage taken: " + string.Join("  ", state.Stats.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value.RoundToInt()}")));
            state.Stats.Clear();
            Console.WriteLine($"        flowfields={state.FlowFields.Count} builds={state.FlowFields.TotalBuilds} projectiles={state.Projectiles.Count} " +
                              $"avgTick={sw.ElapsedMilliseconds / (double)state.Tick:0.00}ms worstTick={worstTickMs}ms");
        }
    }

    DumpStatus(state);
    Console.WriteLine(state.IsOver ? $"Winner: Player {state.Winner}" : "No winner within the time limit.");
    Console.WriteLine($"Simulated {state.Tick} ticks in {sw.Elapsed.TotalSeconds:0.0}s.");
}

static void DumpStatus(GameState state)
{
    foreach (var p in state.Players)
    {
        Console.WriteLine($"--- Player {p.Index}");
        foreach (var b in state.Buildings.Values.Where(b => b.Owner == p.Index))
        {
            var extra = b.NeedsConstruction
                ? $" needs {string.Join(",", Resources.All.Where(r => b.Required[(int)r] > 0).Select(r => $"{r}:{b.Delivered[(int)r]}/{b.Required[(int)r]}(+{b.Incoming[(int)r]})"))} work {b.WorkDone}/{b.WorkRequired}"
                : "";
            var store = b.Store != null ? $" stock[{string.Join(",", b.Store.Stock)}] res[{string.Join(",", b.Store.Reserved)}] carriers {b.Store.CarrierIds.Count}/{b.Store.CarriersPurchased}" : "";
            Console.WriteLine($"  {b.Type} #{b.Id} {b.State} workers={b.WorkerIds.Count}{extra}{store}{(b.IsBurning ? " BURNING" : "")}");
        }
        foreach (var g in state.Workers.Values.Where(w => w.Owner == p.Index).GroupBy(w => (w.Job, w.Task)))
            Console.WriteLine($"  workers {g.Key.Job}/{g.Key.Task}: {g.Count()}");
        Console.WriteLine($"  road sites: {state.RoadSites.Values.Count(r => r.Owner == p.Index)}, roads built: {state.Map.Roads.Count(r => r != RoadKind.None)}");
    }
    Console.WriteLine($"demands={state.Logistics.Demands.Count} shipments={state.Logistics.Shipments.Count}");
}

static string Clock(long tick, int tps)
{
    long s = tick / tps;
    return $"{s / 60:00}:{s % 60:00}";
}

static void DumpMap(GameConfig config, int seed)
{
    var map = MapGenerator.Generate(config, seed);
    var sb = new StringBuilder();
    for (int y = 0; y < map.Height; y += 2)
    {
        for (int x = 0; x < map.Width; x++)
        {
            int i = map.Index(x, y);
            char c = map.Terrain[i] switch
            {
                Terrain.Water => '~',
                Terrain.Mountain => '^',
                Terrain.Hills => 'n',
                Terrain.Forest => 'T',
                _ => '.',
            };
            if (map.Deposits[i] == ResourceType.Stone) c = 'S';
            if (map.Deposits[i] == ResourceType.Iron) c = 'I';
            foreach (var k in map.KeepSites)
                if (x >= k.X && x < k.X + MapGenerator.KeepSize && y >= k.Y && y < k.Y + MapGenerator.KeepSize) c = 'K';
            foreach (var cp in map.Chokepoints)
                if (cp.X == x && Math.Abs(cp.Y - y) <= 1) c = 'X';
            sb.Append(c);
        }
        sb.AppendLine();
    }
    Console.WriteLine(sb.ToString());
    Console.WriteLine($"Seed {map.Seed}, chokepoints: {string.Join(", ", map.Chokepoints)}");
}

static void FlowBench(GameConfig config, int seed)
{
    var map = MapGenerator.Generate(config, seed);
    var goals = new[] { new TileCoord(map.KeepSites[1].X, map.KeepSites[1].Y - 1) };
    FlowFieldBuilder.Build(map, MovementClass.Military, goals); // warm-up
    var sw = Stopwatch.StartNew();
    const int runs = 50;
    for (int i = 0; i < runs; i++) FlowFieldBuilder.Build(map, i % 2 == 0 ? MovementClass.Military : MovementClass.Civilian, goals);
    Console.WriteLine($"{map.Width}x{map.Height} flow field: {sw.Elapsed.TotalMilliseconds / runs:0.00} ms per build");

    var cache = new FlowFieldCache(map, 128, 3, 400);
    var solver = new MovementSolver(map, cache, config.Movement.Steering);
    var agents = new List<NavAgent>();
    var start = map.KeepSites[0];
    for (int i = 0; i < 400; i++)
    {
        var a = new NavAgent
        {
            Id = i + 1,
            Position = new FixVec2(Fix.FromInt(start.X + 6 + i % 20), Fix.FromInt(start.Y + 6 + i / 20)),
            MovementClass = MovementClass.Military,
            MaxSpeed = Fix.FromInt(2),
        };
        a.SetFlowGoal(FlowGoalKey.ForTile(goals[0], map.Width, MovementClass.Military), goals, goals[0].Center);
        a.ArrivalRadius = Fix.FromInt(3);
        agents.Add(a);
    }
    sw.Restart();
    const int ticks = 200;
    for (int t = 0; t < ticks; t++)
    {
        cache.BeginTick(t);
        solver.Step(agents, Fix.FromRatio(1, 20));
    }
    Console.WriteLine($"400 agents movement: {sw.Elapsed.TotalMilliseconds / ticks:0.00} ms per tick");
}
