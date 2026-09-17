using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Military;

namespace BannerAndBarrow.Simulation.Economy;

/// <summary>Spawns Workers at the Keep when a job is open and Houses have room; reuses jobless Workers first.</summary>
public static class WorkerSpawnSystem
{
    public static void Update(GameState state)
    {
        foreach (var player in state.Players)
        {
            if (player.Defeated || state.Tick < player.NextWorkerSpawnTick) continue;
            var keep = state.GetKeep(player.Index);
            if (keep == null || RecruitmentSystem.UnderSiege(state, keep)) continue;

            var job = FindOpenJob(state, player.Index, out var workplace);
            if (job == WorkerJob.None) continue;

            var worker = state.Workers.Values.FirstOrDefault(w => w.Owner == player.Index && w.Job == WorkerJob.None);
            if (worker == null)
            {
                if (state.WorkerCount(player.Index) >= state.WorkerRoom(player.Index)) continue;
                var food = new int[Resources.Count];
                food[(int)ResourceType.Food] = state.Config.Economy.WorkerSpawnFood;
                if (!StockOps.TryWithdraw(state, player.Index, keep.Center, food))
                {
                    WorkerSystem.HungerAlert(state, player.Index, "No Food: new Workers can't arrive. Build Farms.", keep.Center);
                    continue;
                }
                var spawn = keep.NearestAccessTile(state.Map, keep.Center + new FixVec2(Fix.Zero, Fix.FromInt(keep.Size))).Center;
                worker = EntityOps.SpawnWorker(state, player.Index, spawn);
            }
            // One Worker per interval, whether newly spawned or re-assigned, so jobs fill up gradually.
            player.NextWorkerSpawnTick = state.Tick + state.Config.SecondsToTicks(state.Config.Economy.WorkerSpawnIntervalSeconds);
            WorkerSystem.AssignJob(state, worker, job, workplace);
        }
    }

    public static int DesiredBuilders(GameState state, int player)
    {
        var eco = state.Config.Economy;
        int sites = state.Buildings.Values.Count(b => b.Owner == player && b.IsWorkSite)
                    + (state.RoadSites.Values.Any(r => r.Owner == player) ? 1 : 0);
        return System.Math.Min(eco.BuildersMax, eco.BuildersBase + sites * eco.BuildersPerSite);
    }

    private static WorkerJob FindOpenJob(GameState state, int player, out Building? workplace)
    {
        workplace = null;
        int builders = state.Workers.Values.Count(w => w.Owner == player && w.Job == WorkerJob.Builder);
        bool hasSites = state.Buildings.Values.Any(b => b.Owner == player && b.IsWorkSite) || state.RoadSites.Values.Any(r => r.Owner == player);
        if (builders < (hasSites ? DesiredBuilders(state, player) : state.Config.Economy.BuildersBase)) return WorkerJob.Builder;

        foreach (var b in state.Buildings.Values)
        {
            if (b.Owner != player || !b.IsActive) continue;
            var def = state.Config.Building(b.Type);
            if (b.WorkerIds.Count < def.WorkerSlots)
            {
                workplace = b;
                return WorkerJob.Producer;
            }
        }
        foreach (var b in state.Buildings.Values)
        {
            if (b.Owner != player || !b.IsActive || b.Store == null) continue;
            if (b.Store.CarrierIds.Count < b.Store.CarriersPurchased)
            {
                workplace = b;
                return WorkerJob.Carrier;
            }
        }
        return WorkerJob.None;
    }
}

/// <summary>Felled forest and planted saplings grow back into trees.</summary>
public static class RegrowthSystem
{
    public static void Update(GameState state)
    {
        if (state.Tick % state.Config.Simulation.TicksPerSecond != 0) return;
        var map = state.Map;
        int wood = state.Config.Economy.WoodPerForestTile;
        for (int i = 0; i < map.RegrowAtTick.Length; i++)
        {
            long at = map.RegrowAtTick[i];
            if (at == 0 || state.Tick < at) continue;
            state.Saplings.Remove(i);
            if (map.BuildingIds[i] != 0 || map.Roads[i] != RoadKind.None)
            {
                map.RegrowAtTick[i] = 0;
                continue;
            }
            map.RegrowTree(i, wood);
        }
    }
}

/// <summary>Barracks and Stables turn queued orders into Regiments once the cost can be paid.</summary>
public static class RecruitmentSystem
{
    /// <summary>Enemy Presence on any footprint tile: a besieged building can't train or spawn.</summary>
    public static bool UnderSiege(GameState state, Building b) =>
        b.Footprint().Any(t => state.Presence.HasEnemyPresence(t, b.Owner));

    public static void Update(GameState state)
    {
        var cfg = state.Config;
        foreach (var b in state.Buildings.Values.ToList())
        {
            var rec = b.Recruitment;
            if (rec == null || !b.IsActive || rec.Queue.Count == 0) continue;
            if (UnderSiege(state, b))
            {
                // Besieged: training stalls until the enemy is driven off.
                if (rec.Paid) rec.CompleteTick++;
                continue;
            }
            var type = rec.Queue[0];
            var def = cfg.Soldier(type);

            if (!rec.Paid)
            {
                if (state.SoldierCount(b.Owner) + def.RegimentSize > cfg.Simulation.MaxSoldiersPerPlayer) continue;
                var cost = StockOps.ToArray(def.RecruitCost);
                if (StockOps.TryWithdraw(state, b.Owner, b.Center, cost))
                {
                    rec.Paid = true;
                    rec.CompleteTick = state.Tick + cfg.SecondsToTicks(def.RecruitSeconds);
                }
                else if (state.Tick >= rec.NextDemandTick)
                {
                    rec.NextDemandTick = state.Tick + cfg.SecondsToTicks(Fix.FromInt(5));
                    var nearest = StockOps.NearestStorehouse(state, b.Owner, b.Center);
                    var missing = StockOps.Missing(state, b.Owner, cost);
                    if (nearest != null)
                        for (int i = 0; i < Resources.Count; i++)
                            if (missing[i] > 0) LogisticsSystem.RequestDemand(state, nearest, (ResourceType)i, missing[i]);
                }
                continue;
            }

            if (state.Tick < rec.CompleteTick) continue;
            var keep = state.GetKeep(b.Owner);
            var awayFromKeep = keep == null ? new FixVec2(Fix.Zero, Fix.One) : (b.Center - keep.Center).Normalized();
            if (awayFromKeep.IsZero) awayFromKeep = new FixVec2(Fix.Zero, Fix.One);
            var spawn = b.NearestAccessTile(state.Map, b.Center + awayFromKeep * b.Size).Center;
            var regiment = EntityOps.CreateRegiment(state, b.Owner, type, spawn, awayFromKeep);
            if (b.RallyPoint is { } rally)
            {
                // Gather at the rally point, facing the way they walked.
                var facing = (rally - spawn).Normalized();
                RegimentSystem.OrderMove(state, regiment, rally, facing.IsZero ? awayFromKeep : facing, attackMove: true);
            }
            else
            {
                RegimentSystem.OrderMove(state, regiment, spawn + awayFromKeep * 4, awayFromKeep, attackMove: false);
            }
            rec.Queue.RemoveAt(0);
            rec.Paid = false;
            state.AddEvent(b.Owner, GameEventKind.Info, $"{type} Regiment ready.", spawn);
        }
    }
}
