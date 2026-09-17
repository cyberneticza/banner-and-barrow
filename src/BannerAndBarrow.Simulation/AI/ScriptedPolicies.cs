using BannerAndBarrow.Simulation.Commands;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Military;
using BannerAndBarrow.Simulation.Pathfinding;
using BannerAndBarrow.Simulation.Territory;

namespace BannerAndBarrow.Simulation.AI;

/// <summary>Follows the profile's build order, keeps housing ahead of demand and lays dirt roads.</summary>
public sealed class ScriptedEconomyPolicy : IEconomyPolicy
{
    public void Decide(AiContext ctx)
    {
        ApplyMinimumStock(ctx);
        BuyCarriersWhereStarved(ctx);
        if (ctx.Profile.BuildRoads && ctx.Memory.DecisionCount % 5 == 0) BuildRoads(ctx);

        if (ctx.ActiveSites >= ctx.Profile.MaxConcurrentSites) return;

        if (ctx.WorkerRoom - ctx.Workers <= ctx.Profile.HouseWhenFreeRoomBelow && !HasSiteOf(ctx, BuildingType.House))
        {
            if (TryPlace(ctx, BuildingType.House)) return;
        }

        // Workers eat: keep Farms in step with the population.
        int wantedFarms = 1 + ctx.Workers / System.Math.Max(1, ctx.Profile.WorkersPerFarm);
        if (ctx.Count(BuildingType.Farm) < wantedFarms && !HasSiteOf(ctx, BuildingType.Farm) && TryPlace(ctx, BuildingType.Farm)) return;

        foreach (var step in ctx.BuildOrder)
        {
            if (ctx.Count(step.Building) >= step.Count) continue;
            if (TryPlace(ctx, step.Building)) return;
        }

        // Build order done: keep wood and food flowing, add a Smithy while its goods are backlogged, and turn
        // a food surplus into more recruitment capacity.
        if (ctx.Stock[(int)ResourceType.Wood] < 30 && ctx.Count(BuildingType.Woodcutter) < 6 && TryPlace(ctx, BuildingType.Woodcutter)) return;
        if (ctx.Stock[(int)ResourceType.Food] < 60 && ctx.Count(BuildingType.Farm) < 6 && TryPlace(ctx, BuildingType.Farm)) return;
        if (ctx.Stock[(int)ResourceType.Iron] >= 60 && SmithyBacklog(ctx) >= 12 && ctx.Count(BuildingType.Smithy) < ctx.Profile.MaxSmithies && TryPlace(ctx, BuildingType.Smithy)) return;
        if (ctx.Stock[(int)ResourceType.Food] >= ctx.Profile.ExtraBarracksFood && ctx.Count(BuildingType.Barracks) < ctx.Profile.MaxBarracks)
            TryPlace(ctx, BuildingType.Barracks);
    }

    private static int SmithyBacklog(AiContext ctx)
    {
        var orders = Manufacturing.Orders(ctx.State, ctx.Player);
        var pipeline = Manufacturing.Pipeline(ctx.State, ctx.Player);
        int backlog = 0;
        foreach (var r in new[] { ResourceType.Swords, ResourceType.Pikes, ResourceType.Armour })
            backlog += System.Math.Max(0, orders[(int)r] - pipeline[(int)r]);
        return backlog;
    }

    private static bool HasSiteOf(AiContext ctx, BuildingType type) =>
        ctx.State.Buildings.Values.Any(b => b.Owner == ctx.Player && b.Type == type && b.NeedsConstruction);

    public static bool TryPlace(AiContext ctx, BuildingType type)
    {
        var state = ctx.State;
        var keepTile = ctx.Keep.CenterTile;
        TileCoord near = keepTile;
        int minNodes = 0;

        if (type is BuildingType.Woodcutter or BuildingType.Quarry or BuildingType.IronMine)
        {
            // Put gatherers where a Storehouse has resources but few gatherers already, so outlying
            // Storehouses actually get used and trips stay short.
            TileCoord? best = null;
            int bestScore = int.MaxValue;
            foreach (var store in state.StorehousesOf(ctx.Player))
            {
                var t = PlacementFinder.NearestResourceTile(state, type, store.CenterTile, store.Store!.Reach + 4);
                if (!t.HasValue) continue;
                var reach = Fix.FromInt(store.Store.Reach + 4);
                int crowding = state.Buildings.Values.Count(b => b.Owner == ctx.Player && b.Type == type && FixVec2.DistanceSquared(b.Center, store.Center) <= reach * reach);
                int score = crowding * 400 + TileCoord.DistanceSquared(t.Value, store.CenterTile);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            if (best == null) return false;
            near = best.Value;
            minNodes = type == BuildingType.Woodcutter ? 6 : 1;
        }
        else if (type is BuildingType.Barracks or BuildingType.Archery or BuildingType.Stable)
        {
            near = (ctx.Keep.Center + ctx.TowardEnemy * 8).ToTile();
        }
        else if (type == BuildingType.Farm)
        {
            near = (ctx.Keep.Center - ctx.TowardEnemy * 8).ToTile();
            minNodes = 24;
        }

        var spot = PlacementFinder.Find(state, ctx.Player, type, near, maxRadius: 14, minNodes);
        if (spot == null && minNodes > 0) spot = PlacementFinder.Find(state, ctx.Player, type, near, maxRadius: 8, minNodes: 1);
        if (spot == null) return false;
        ctx.Issue(new PlaceBuildingCommand(ctx.Player, type, spot.Value));
        ctx.ActiveSites++;
        ctx.BuildingCounts[type] = ctx.Count(type) + 1;
        return true;
    }

    private static void ApplyMinimumStock(AiContext ctx)
    {
        foreach (var store in ctx.State.StorehousesOf(ctx.Player))
        {
            if (store.Store!.IsKeep) continue;
            foreach (var (resource, amount) in ctx.Profile.StorehouseMinimumStock)
                if (store.Store.MinimumStock[(int)resource] != amount)
                    ctx.Issue(new SetMinimumStockCommand(ctx.Player, store.Id, resource, amount));
        }
    }

    private static void BuyCarriersWhereStarved(AiContext ctx)
    {
        foreach (var store in ctx.State.StorehousesOf(ctx.Player))
            if (store.Store!.Starved && store.Store.CarriersPurchased < ctx.State.Config.Economy.StorehouseMaxCarriers)
                ctx.Issue(new BuyCarriersCommand(ctx.Player, store.Id));
    }

    /// <summary>Traces the civilian flow field from a building to its nearest Storehouse and paves it with dirt.</summary>
    private static void BuildRoads(AiContext ctx)
    {
        var state = ctx.State;
        foreach (var b in state.Buildings.Values)
        {
            if (b.Owner != ctx.Player || !b.IsActive || b.IsStorehouseLike || b.IsFort) continue;
            if (ctx.Memory.RoadedBuildingIds.Contains(b.Id)) continue;
            var store = StockOps.NearestStorehouse(state, ctx.Player, b.Center);
            if (store == null) continue;
            ctx.Memory.RoadedBuildingIds.Add(b.Id);

            var field = state.FlowFields.Get(FlowGoalKey.ForEntity(store.Id, MovementClass.Civilian, ctx.Player), store.GetAccessTiles(state.Map));
            var tile = b.NearestAccessTile(state.Map, store.Center);
            var tiles = new List<TileCoord>();
            for (int step = 0; step < 64 && tiles.Count < ctx.Profile.RoadTilesPerDecision * 4; step++)
            {
                if (state.Map.Roads[state.Map.Index(tile)] == RoadKind.None) tiles.Add(tile);
                if (field.IsGoal(tile.X, tile.Y)) break;
                int d = field.Direction[state.Map.Index(tile)];
                if (d < 0) break;
                var (dx, dy) = FlowField.Offsets8[d];
                tile = new TileCoord(tile.X + dx, tile.Y + dy);
            }
            if (tiles.Count > 0) ctx.Issue(new BuildRoadCommand(ctx.Player, RoadKind.Dirt, tiles.ToArray()));
            return; // one road per decision
        }
    }
}

/// <summary>Expands with Storehouses towards resources and fortifies the chokepoint nearest home.</summary>
public sealed class ScriptedTerritoryPolicy : ITerritoryPolicy
{
    public void Decide(AiContext ctx)
    {
        var state = ctx.State;
        var profile = ctx.Profile;

        RepairRuins(ctx);
        UpgradeFullStorehouses(ctx);

        if (state.Tick >= ctx.Memory.NextExpansionTick && ctx.SecondsElapsed > Fix.FromInt(90))
        {
            ctx.Memory.NextExpansionTick = state.Tick + ctx.SecondsToTicks(profile.ExpansionIntervalSeconds);
            // A Storehouse has two jobs: shortening supply trips for existing production, and pushing Territory
            // towards new resources. Fix the logistics first; expand when trips are already short.
            if (!TryPlaceLogisticsStorehouse(ctx)) TryExpand(ctx);
        }

        var forts = state.Buildings.Values.Where(b => b.Owner == ctx.Player && b.IsFort).ToList();
        if (forts.Count == 0 && ctx.SecondsElapsed >= profile.OutpostAfterSeconds && state.Tick >= ctx.Memory.NextOutpostTick)
        {
            // Don't re-place a lost Outpost every second in front of an enemy army.
            ctx.Memory.NextOutpostTick = state.Tick + ctx.SecondsToTicks(Fix.FromInt(120));
            TryPlaceOutpost(ctx);
        }

        foreach (var fort in forts)
        {
            if (fort.Type == BuildingType.Outpost && fort.IsActive && ctx.SecondsElapsed >= profile.StrongholdAfterSeconds &&
                StockOps.CanAfford(state, ctx.Player, StockOps.ToArray(state.Config.Forts.StrongholdUpgradeCost)))
            {
                ctx.Issue(new UpgradeOutpostCommand(ctx.Player, fort.Id));
            }
            if (fort.Type == BuildingType.Stronghold && fort.IsActive && fort.Fort!.StationedRegimentIds.Count < profile.RegimentsToStation)
            {
                var candidate = ctx.Regiments.FirstOrDefault(r =>
                    !r.IsStationed && r.Order == RegimentOrder.Idle && !ctx.Memory.WaveRegimentIds.Contains(r.Id) &&
                    state.Config.Soldier(r.Type).Ranged != null);
                if (candidate != null) ctx.Issue(new StationCommand(ctx.Player, new[] { candidate.Id }, fort.Id));
            }
        }
    }

    private static bool StorehouseSpotAllowed(AiContext ctx, TileCoord origin)
    {
        var state = ctx.State;
        var size = state.Config.Building(BuildingType.Storehouse).Size;
        var center = new FixVec2(Fix.FromInt(origin.X) + Fix.FromRatio(size, 2), Fix.FromInt(origin.Y) + Fix.FromRatio(size, 2));
        var spacing = ctx.Profile.StorehouseSpacing;
        return !state.Buildings.Values.Any(b => b.Owner == ctx.Player && b.IsStorehouseLike &&
                                                 FixVec2.DistanceSquared(b.Center, center) < spacing * spacing);
    }

    private static TileCoord? FindStorehouseSpot(AiContext ctx, TileCoord near, int radius)
    {
        var state = ctx.State;
        for (int r = 0; r <= radius; r++)
        {
            var spot = PlacementFinder.Find(state, ctx.Player, BuildingType.Storehouse, near, maxRadius: r);
            if (spot == null) continue;
            if (StorehouseSpotAllowed(ctx, spot.Value)) return spot;
        }
        return null;
    }

    /// <summary>Drop a Storehouse beside production buildings whose Workers walk too far to deliver.</summary>
    private static bool TryPlaceLogisticsStorehouse(AiContext ctx)
    {
        var state = ctx.State;
        int storehouses = state.Buildings.Values.Count(b => b.Owner == ctx.Player && b.Type == BuildingType.Storehouse);
        if (storehouses >= ctx.Profile.MaxStorehouses) return false;

        var far = state.Buildings.Values
            .Where(b => b.Owner == ctx.Player && b.IsActive && state.Config.Building(b.Type).WorkerSlots > 0)
            .Where(b => StockOps.NearestStorehouse(state, ctx.Player, b.Center) is { } s &&
                        FixVec2.Distance(s.Center, b.Center) > ctx.Profile.LogisticsDistance)
            .ToList();
        if (far.Count < 2) return false;

        var centroid = far.Aggregate(FixVec2.Zero, (acc, b) => acc + b.Center) / far.Count;
        var spot = FindStorehouseSpot(ctx, centroid.ToTile(), 6);
        if (spot == null) return false;
        var cost = BuildRules.GetCost(state, ctx.Player, BuildingType.Storehouse, spot.Value);
        if (!StockOps.CanAfford(state, ctx.Player, cost)) return false;
        ctx.Issue(new PlaceBuildingCommand(ctx.Player, BuildingType.Storehouse, spot.Value));
        return true;
    }

    private static void RepairRuins(AiContext ctx)
    {
        foreach (var ruin in ctx.State.Buildings.Values.Where(b => b.Owner == ctx.Player && b.State == BuildingState.Ruin))
            if (BuildRules.CanRepairRuin(ctx.State, ctx.Player, ruin).Ok)
                ctx.Issue(new RepairRuinCommand(ctx.Player, ruin.Id));
    }

    private static void UpgradeFullStorehouses(AiContext ctx)
    {
        var state = ctx.State;
        var eco = state.Config.Economy;
        foreach (var store in state.StorehousesOf(ctx.Player))
        {
            if (store.Store!.Upgrades >= eco.StorehouseMaxUpgrades) continue;
            bool nearlyFull = store.Store.Stock.Any(amount => amount * 10 >= store.Store.Capacity * 9);
            if (nearlyFull && StockOps.CanAfford(state, ctx.Player, StockOps.ToArray(eco.StorehouseUpgradeCost)))
                ctx.Issue(new UpgradeStorehouseCommand(ctx.Player, store.Id));
        }
    }

    private static void TryExpand(AiContext ctx)
    {
        var state = ctx.State;
        int storehouses = state.Buildings.Values.Count(b => b.Owner == ctx.Player && b.Type == BuildingType.Storehouse);
        if (storehouses >= ctx.Profile.MaxStorehouses) return;

        // Head for the nearest resource outside our Territory.
        TileCoord? target = null;
        int bestD = int.MaxValue;
        foreach (var type in new[] { BuildingType.Quarry, BuildingType.IronMine, BuildingType.Woodcutter })
        {
            var t = PlacementFinder.NearestResourceTile(state, type, ctx.Keep.CenterTile, 40);
            if (t == null) continue;
            // Skip resources already inside our Territory.
            var probe = FindOutsideResource(state, ctx, type);
            if (probe == null) continue;
            int d = TileCoord.DistanceSquared(probe.Value, ctx.Keep.CenterTile);
            if (d < bestD)
            {
                bestD = d;
                target = probe;
            }
        }
        if (target == null) return;

        // Walk back from the target towards the Keep until we're inside our Territory.
        var from = target.Value.Center;
        var dir = (ctx.Keep.Center - from).Normalized();
        for (int step = 0; step < 60; step++)
        {
            var p = from + dir * step;
            var tile = p.ToTile();
            if (state.Territory.OwnerAt(tile) != ctx.Player) continue;
            var inward = (p + dir * 2).ToTile();
            var spot = FindStorehouseSpot(ctx, inward, 5);
            if (spot == null) continue;
            var cost = BuildRules.GetCost(state, ctx.Player, BuildingType.Storehouse, spot.Value);
            if (!StockOps.CanAfford(state, ctx.Player, cost)) return;
            ctx.Issue(new PlaceBuildingCommand(ctx.Player, BuildingType.Storehouse, spot.Value));
            return;
        }
    }

    private static TileCoord? FindOutsideResource(GameState state, AiContext ctx, BuildingType type)
    {
        var map = state.Map;
        var keep = ctx.Keep.CenterTile;
        TileCoord? best = null;
        int bestD = int.MaxValue;
        int radius = 36;
        for (int y = keep.Y - radius; y <= keep.Y + radius; y += 2)
        for (int x = keep.X - radius; x <= keep.X + radius; x += 2)
        {
            if (!map.InBounds(x, y)) continue;
            if (state.Territory.OwnerAt(x, y) != Territory.TerritoryMap.None) continue;
            int i = map.Index(x, y);
            bool match = type switch
            {
                BuildingType.Woodcutter => map.Terrain[i] == Terrain.Forest,
                BuildingType.Quarry => map.Deposits[i] == ResourceType.Stone,
                BuildingType.IronMine => map.Deposits[i] == ResourceType.Iron,
                _ => false,
            };
            if (!match) continue;
            int d = TileCoord.DistanceSquared(new TileCoord(x, y), keep);
            if (d < bestD)
            {
                bestD = d;
                best = new TileCoord(x, y);
            }
        }
        return best;
    }

    private static void TryPlaceOutpost(AiContext ctx)
    {
        var state = ctx.State;
        if (state.Map.Chokepoints.Count == 0) return;
        if (!StockOps.CanAfford(state, ctx.Player, StockOps.ToArray(state.Config.Building(BuildingType.Outpost).Cost))) return;
        var choke = state.Map.Chokepoints.OrderBy(c => TileCoord.DistanceSquared(c, ctx.Keep.CenterTile)).First();
        var homeward = (ctx.Keep.Center - choke.Center).Normalized();
        var near = (choke.Center + homeward * 10).ToTile();
        var spot = PlacementFinder.Find(state, ctx.Player, BuildingType.Outpost, near, maxRadius: 8);
        if (spot != null) ctx.Issue(new PlaceBuildingCommand(ctx.Player, BuildingType.Outpost, spot.Value));
    }
}
