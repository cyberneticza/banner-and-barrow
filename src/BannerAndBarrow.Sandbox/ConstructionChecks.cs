using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

/// <summary>
/// SANDBOX_CHECK=1: watches construction for the ways it can silently stall and prints each problem once:
/// Incoming counters that no Builder accounts for, sites with no progress for two minutes, and Builders swinging
/// at a site that is not progressing.
/// </summary>
sealed class ConstructionChecks
{
    private readonly Dictionary<int, (long Tick, int Delivered, Fix Work)> _progress = new();
    private readonly Dictionary<int, (long Tick, Fix Work)> _builderProgress = new();
    private readonly HashSet<string> _reported = new();
    private readonly Dictionary<int, long> _eatSince = new();

    public void Run(GameState state)
    {
        int tps = state.Config.Simulation.TicksPerSecond;
        var sites = state.Buildings.Values.Where(b => b.IsWorkSite).Cast<ConstructionTarget>().Concat(state.RoadSites.Values).ToList();
        foreach (var site in sites)
        {
            // 1. Incoming must equal what Builders are fetching or carrying for the site.
            var expected = new int[Resources.Count];
            foreach (var w in state.Workers.Values)
            {
                if (w.Job != WorkerJob.Builder || w.TargetSiteId != site.Id) continue;
                if (w.Task == WorkerTask.GoToPickup) expected[(int)w.CarryType] += w.PickupAmount;
                else if (w.Task == WorkerTask.GoToSite) expected[(int)w.CarryType] += w.CarryAmount;
            }
            for (int i = 0; i < Resources.Count; i++)
                if (expected[i] != site.Incoming[i])
                    Report(state, $"incoming-{site.Id}-{i}", $"site #{site.Id} Incoming[{(ResourceType)i}]={site.Incoming[i]} but Builders carry {expected[i]}");

            // 2. No progress for two minutes.
            int delivered = site.Delivered.Sum();
            if (!_progress.TryGetValue(site.Id, out var last) || last.Delivered != delivered || last.Work != site.WorkDone)
            {
                _progress[site.Id] = (state.Tick, delivered, site.WorkDone);
            }
            else if (state.Tick - last.Tick > 120 * tps)
            {
                var missing = Resources.All.Where(r => site.Required[(int)r] > site.Delivered[(int)r])
                    .Select(r => $"{r} {site.Delivered[(int)r]}/{site.Required[(int)r]} (+{site.Incoming[(int)r]}) stock {StockOps.TotalAvailable(state, site.Owner, r)}");
                var builders = state.Workers.Values.Where(w => w.Owner == site.Owner && w.Job == WorkerJob.Builder)
                    .GroupBy(w => $"{w.Task}{(w.TargetSiteId == site.Id ? "*" : "")}").Select(g => $"{g.Key}x{g.Count()}");
                var access = site is Building b ? $" access={b.GetAccessTiles(state.Map).Length}" : "";
                var kind = site is Building bb ? $"{bb.Type}{(bb.Demolishing ? " demolishing" : "")}" : "road";
                var sample = state.Workers.Values.FirstOrDefault(w => w.Job == WorkerJob.Builder && w.TargetSiteId == site.Id);
                if (sample != null)
                {
                    var t = sample.Position.ToTile();
                    string reach = state.FlowFields.TryPeek(sample.FlowKey, out var ff) && ff != null ? ff.IsReachable(t.X, t.Y).ToString() : "nofield";
                    Console.WriteLine($"        sample Builder #{sample.Id} {sample.Task} at {t} goal {sample.GoalPoint.ToTile()} dist={FixVec2.Distance(sample.Position, site.WorkPoint)} " +
                                      $"arrived={sample.Arrived} goalKind={sample.GoalKind} reachable={reach} terrain={state.Map.Terrain[state.Map.Index(t)]} building={state.Map.BuildingIds[state.Map.Index(t)]}");
                }
                Report(state, $"stall-{site.Id}", $"P{site.Owner} site #{site.Id} {kind} stalled {(state.Tick - last.Tick) / tps}s: {string.Join("; ", missing)} work {site.WorkDone}/{site.WorkRequired}{access} builders[{string.Join(" ", builders)}]");
            }
        }

        // 2b. Workers on their way to eat for over a minute.
        foreach (var w in state.Workers.Values)
        {
            if (w.Task != WorkerTask.GoToEat) continue;
            var key = $"eat-{w.Id}";
            if (!_eatSince.TryGetValue(w.Id, out var since)) _eatSince[w.Id] = state.Tick;
            else if (state.Tick - since > 60 * tps)
            {
                var store = state.GetBuilding(w.MealStorehouseId);
                var t = w.Position.ToTile();
                string reach = state.FlowFields.TryPeek(w.FlowKey, out var ff) && ff != null ? ff.IsReachable(t.X, t.Y).ToString() : "nofield";
                var near = Fix.FromInt(2);
                var soldiers = state.Soldiers.Values.Where(o => FixVec2.DistanceSquared(o.Position, w.Position) <= near * near).GroupBy(o => o.Owner).Select(g => $"P{g.Key}x{g.Count()}");
                var workers = state.Workers.Values.Count(o => o.Id != w.Id && FixVec2.DistanceSquared(o.Position, w.Position) <= near * near);
                Console.WriteLine($"        velocity={w.Velocity} speed={w.EffectiveSpeed} stuck={w.StuckTicks} goalKind={w.GoalKind} goal={w.GoalPoint.ToTile()} soldiers[{string.Join(" ", soldiers)}] workers={workers} " +
                                  $"road={state.Map.Roads[state.Map.Index(t)]} terrain={state.Map.Terrain[state.Map.Index(t)]} presence={state.Presence.MaskAt(t.X, t.Y)} task={w.Task} hungry={w.IsHungry(state.Tick)}");
                Report(state, key + "-" + since, $"P{w.Owner} Worker #{w.Id} ({w.Job}) walking to eat for {(state.Tick - since) / tps}s at {t} to #{w.MealStorehouseId} {store?.Type} dist={(store == null ? Fix.Zero : store.DistanceToFootprint(w.Position))} arrived={w.Arrived} reachable={reach}");
            }
        }
        foreach (var id in _eatSince.Keys.ToList())
            if (state.GetWorker(id) is not { Task: WorkerTask.GoToEat }) _eatSince.Remove(id);

        // 3. Builders swinging at a site that is not getting any work done.
        foreach (var w in state.Workers.Values)
        {
            if (w.Job != WorkerJob.Builder || w.Task != WorkerTask.Build)
            {
                _builderProgress.Remove(w.Id);
                continue;
            }
            var site = WorkerSystem.GetSite(state, w.TargetSiteId);
            var work = site?.WorkDone ?? Fix.Zero;
            if (!_builderProgress.TryGetValue(w.Id, out var bp) || bp.Work != work) _builderProgress[w.Id] = (state.Tick, work);
            else if (state.Tick - bp.Tick > 30 * tps)
                Report(state, $"swing-{w.Id}-{w.TargetSiteId}", $"P{w.Owner} Builder #{w.Id} stuck in Build for {(state.Tick - bp.Tick) / tps}s at site #{w.TargetSiteId} " +
                       $"(exists={site != null} materials={site?.MaterialsComplete} work {work}/{site?.WorkRequired})");
        }
    }

    private void Report(GameState state, string key, string message)
    {
        if (!_reported.Add(key)) return;
        int tps = state.Config.Simulation.TicksPerSecond;
        long s = state.Tick / tps;
        Console.WriteLine($"[{s / 60:00}:{s % 60:00}] CHECK {message}");
    }
}
