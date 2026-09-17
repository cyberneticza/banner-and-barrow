using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Military;
using BannerAndBarrow.Simulation.Territory;

namespace BannerAndBarrow.Simulation.Commands;

public readonly record struct CommandResult(bool Ok, string Message)
{
    public static readonly CommandResult Success = new(true, "");
    public static CommandResult Fail(string message) => new(false, message);
}

/// <summary>Queues commands and applies them at the start of the next Tick, validating every one.</summary>
public sealed class CommandQueue
{
    private readonly List<GameCommand> _pending = new();

    public void Enqueue(GameCommand command) => _pending.Add(command);

    public int Count => _pending.Count;

    public void ApplyAll(GameState state)
    {
        if (_pending.Count == 0) return;
        var batch = _pending.ToList();
        _pending.Clear();
        foreach (var command in batch)
        {
            var result = CommandProcessor.Apply(state, command);
            if (!result.Ok && !state.Players[command.Player].IsAi)
                state.AddEvent(command.Player, GameEventKind.CommandRejected, result.Message);
        }
    }
}

public static class CommandProcessor
{
    public static CommandResult Apply(GameState state, GameCommand command)
    {
        if (command.Player < 0 || command.Player >= state.Players.Length) return CommandResult.Fail("Unknown player.");
        if (state.Players[command.Player].Defeated || state.IsOver) return CommandResult.Fail("The match is over.");

        return command switch
        {
            PlaceBuildingCommand c => PlaceBuilding(state, c),
            BuildRoadCommand c => BuildRoad(state, c),
            CancelConstructionCommand c => CancelConstruction(state, c),
            DemolishBuildingCommand c => DemolishBuilding(state, c),
            DemolishRoadsCommand c => DemolishRoads(state, c),
            PlaceWallsCommand c => PlaceWalls(state, c),
            CancelRecruitCommand c => CancelRecruit(state, c),
            SetRallyPointCommand c => SetRallyPoint(state, c),
            MoveRegimentsCommand c => MoveRegiments(state, c),
            SetFormationCommand c => ForRegiments(state, c.Player, c.RegimentIds, r => RegimentSystem.SetFormation(state, r, c.Formation)),
            MergeRegimentsCommand c => MergeRegiments(state, c),
            UpgradeRegimentsCommand c => UpgradeRegiments(state, c),
            SetStanceCommand c => ForRegiments(state, c.Player, c.RegimentIds, r => r.HoldGround = c.HoldGround),
            AttackRegimentCommand c => AttackRegiment(state, c),
            AttackBuildingCommand c => AttackBuilding(state, c),
            FormBattleLineCommand c => FormBattleLine(state, c),
            StopCommand c => ForRegiments(state, c.Player, c.RegimentIds, r =>
            {
                BattleGroupSystem.Leave(state, r);
                RegimentSystem.OrderStop(state, r);
            }),
            RecruitCommand c => Recruit(state, c),
            StationCommand c => Station(state, c),
            UnstationCommand c => Unstation(state, c),
            UpgradeOutpostCommand c => UpgradeOutpost(state, c),
            UpgradeStorehouseCommand c => UpgradeStorehouse(state, c),
            BuyCarriersCommand c => BuyCarriers(state, c),
            SetMinimumStockCommand c => SetMinimumStock(state, c),
            RepairRuinCommand c => RepairRuin(state, c),
            _ => CommandResult.Fail($"Unhandled command {command.GetType().Name}"),
        };
    }

    private static CommandResult PlaceBuilding(GameState state, PlaceBuildingCommand c)
    {
        var check = BuildRules.CanPlaceBuilding(state, c.Player, c.Type, c.Origin);
        if (!check.Ok) return CommandResult.Fail(check.Reason);
        var cost = BuildRules.GetCost(state, c.Player, c.Type, c.Origin);
        EntityOps.CreateBuilding(state, c.Player, c.Type, c.Origin, underConstruction: true, cost);
        return CommandResult.Success;
    }

    private static CommandResult BuildRoad(GameState state, BuildRoadCommand c)
    {
        int placed = 0;
        string lastReason = "No road tiles given.";
        foreach (var tile in c.Tiles.Distinct())
        {
            var check = BuildRules.CanPlaceRoad(state, c.Player, tile, c.Kind);
            if (!check.Ok)
            {
                lastReason = check.Reason;
                continue;
            }
            var site = new RoadSite
            {
                Id = state.NextId(),
                Owner = c.Player,
                Tile = tile,
                Kind = c.Kind,
                WorkRequired = c.Kind == RoadKind.Stone ? Fix.FromInt(2) : Fix.One,
            };
            var cost = BuildRules.GetRoadCost(c.Kind);
            for (int i = 0; i < Resources.Count; i++) site.Required[i] = cost[i];
            state.RoadSites[site.Id] = site;
            placed++;
        }
        return placed > 0 ? CommandResult.Success : CommandResult.Fail(lastReason);
    }

    private static CommandResult CancelConstruction(GameState state, CancelConstructionCommand c)
    {
        var b = state.GetBuilding(c.BuildingId);
        if (b == null || b.Owner != c.Player || !b.NeedsConstruction) return CommandResult.Fail("Nothing to cancel.");
        // Refund what was already delivered to the nearest Storehouse.
        var store = StockOps.NearestStorehouse(state, c.Player, b.Center);
        if (store?.Store != null)
            for (int i = 0; i < Resources.Count; i++) store.Store.Stock[i] += b.Delivered[i];
        if (b.IsRepair)
        {
            b.State = BuildingState.Ruin;
            b.IsRepair = false;
            Array.Clear(b.Delivered);
            WorkerSystem.OnSiteRemoved(state, b);
            return CommandResult.Success;
        }
        EntityOps.DestroyBuilding(state, b, allowRuin: false);
        return CommandResult.Success;
    }

    private static CommandResult DemolishBuilding(GameState state, DemolishBuildingCommand c)
    {
        var b = state.GetBuilding(c.BuildingId);
        if (b == null || b.Owner != c.Player) return CommandResult.Fail("Select one of your buildings.");
        if (b.Type == BuildingType.Keep) return CommandResult.Fail("The Keep cannot be demolished.");
        if (b.NeedsConstruction) return CancelConstruction(state, new CancelConstructionCommand(c.Player, b.Id));
        if (b.State == BuildingState.Ruin)
        {
            EntityOps.DestroyBuilding(state, b, allowRuin: false);
            return CommandResult.Success;
        }
        if (b.Demolishing)
        {
            b.Demolishing = false;
            b.WorkDone = b.WorkRequired;
            WorkerSystem.OnSiteRemoved(state, b);
            state.Territory.Dirty = true;
            return CommandResult.Success;
        }

        b.Demolishing = true;
        b.WorkDone = Fix.Zero;
        b.WorkRequired = Fix.Max(Fix.FromInt(3), state.Config.Building(b.Type).BuildSeconds * state.Config.Economy.DemolishTimeFraction);
        foreach (var wid in b.WorkerIds.ToList())
            if (state.GetWorker(wid) is { } w) WorkerSystem.MakeJobless(state, w);
        if (b.Store != null)
        {
            foreach (var cid in b.Store.CarrierIds.ToList())
                if (state.GetWorker(cid) is { } carrier) WorkerSystem.MakeJobless(state, carrier);
            LogisticsSystem.OnStorehouseLost(state, b);
        }
        if (b.Fort != null) FortSystem.UnstationAll(state, b);
        return CommandResult.Success;
    }

    private static CommandResult PlaceWalls(GameState state, PlaceWallsCommand c)
    {
        if (!BuildRules.IsWallPiece(c.Type)) return CommandResult.Fail("Not a wall piece.");
        int placed = 0;
        string reason = "Nowhere to build there.";
        foreach (var tile in c.Tiles.Distinct())
        {
            var check = BuildRules.CanPlaceBuilding(state, c.Player, c.Type, tile);
            if (!check.Ok)
            {
                reason = check.Reason;
                continue;
            }
            var cost = BuildRules.GetCost(state, c.Player, c.Type, tile);
            EntityOps.CreateBuilding(state, c.Player, c.Type, tile, underConstruction: true, cost);
            placed++;
        }
        return placed > 0 ? CommandResult.Success : CommandResult.Fail(reason);
    }

    private static CommandResult DemolishRoads(GameState state, DemolishRoadsCommand c)
    {
        int queued = 0;
        foreach (var tile in c.Tiles.Distinct())
        {
            if (!state.Map.InBounds(tile)) continue;
            var planned = state.RoadSites.Values.FirstOrDefault(r => r.Tile == tile && r.Owner == c.Player);
            if (planned != null)
            {
                bool wasRemoval = planned.IsRemoval;
                state.RoadSites.Remove(planned.Id);
                WorkerSystem.OnSiteRemoved(state, planned);
                queued++;
                if (wasRemoval) continue; // clicking a queued removal again cancels it
                if (state.Map.Roads[state.Map.Index(tile)] == RoadKind.None) continue;
            }
            if (state.Map.Roads[state.Map.Index(tile)] == RoadKind.None) continue;
            if (state.Territory.IsEnemyOwned(tile, c.Player)) continue;
            var site = new RoadSite { Id = state.NextId(), Owner = c.Player, Tile = tile, Kind = RoadKind.None, WorkRequired = Fix.Half };
            state.RoadSites[site.Id] = site;
            queued++;
        }
        return queued > 0 ? CommandResult.Success : CommandResult.Fail("No roads there to remove.");
    }

    private static CommandResult SetRallyPoint(GameState state, SetRallyPointCommand c)
    {
        var b = state.GetBuilding(c.BuildingId);
        if (b?.Recruitment == null || b.Owner != c.Player) return CommandResult.Fail("Select your Barracks, Archery or Stable to set where its Regiments gather.");
        if (c.Point is not { } point)
        {
            b.RallyPoint = null;
            return CommandResult.Success;
        }
        var tile = point.ToTile();
        if (!state.Map.InBounds(tile)) return CommandResult.Fail("That is off the map.");
        b.RallyPoint = RegimentSystem.ClampToPassable(state, point);
        return CommandResult.Success;
    }

    private static CommandResult CancelRecruit(GameState state, CancelRecruitCommand c)
    {
        var b = state.GetBuilding(c.BuildingId);
        var rec = b?.Recruitment;
        if (b == null || rec == null || b.Owner != c.Player) return CommandResult.Fail("Select one of your Barracks or Stables.");
        if (c.QueueIndex < 0 || c.QueueIndex >= rec.Queue.Count) return CommandResult.Fail("Nothing queued there.");
        if (c.QueueIndex == 0 && rec.Paid)
        {
            var store = StockOps.NearestStorehouse(state, c.Player, b.Center);
            if (store?.Store != null)
            {
                var cost = StockOps.ToArray(state.Config.Soldier(rec.Queue[0]).RecruitCost);
                for (int i = 0; i < Resources.Count; i++) store.Store.Stock[i] += cost[i];
            }
            rec.Paid = false;
        }
        rec.Queue.RemoveAt(c.QueueIndex);
        return CommandResult.Success;
    }

    private static CommandResult UpgradeRegiments(GameState state, UpgradeRegimentsCommand c)
    {
        var player = state.Players[c.Player];
        string lastReason = "Select Spearmen to upgrade.";
        int handled = 0;
        foreach (var id in c.RegimentIds.Distinct())
        {
            var r = state.GetRegiment(id);
            if (r == null || r.Owner != c.Player || r.IsStationed || Manufacturing.UpgradeTarget(state, r.Type) == null) continue;
            if (UpgradeSystem.TryUpgrade(state, r, out var reason))
            {
                handled++;
                continue;
            }
            lastReason = reason;
            // Short of goods but next to the building: the order waits, and workshops make the goods.
            if (reason.StartsWith("Waiting") && !player.PendingUpgrades.Contains(r.Id))
            {
                player.PendingUpgrades.Add(r.Id);
                state.AddEvent(c.Player, GameEventKind.Info, $"Upgrade ordered: {reason}", state.RegimentCentroid(r));
                handled++;
            }
        }
        return handled > 0 ? CommandResult.Success : CommandResult.Fail(lastReason);
    }

    private static CommandResult MergeRegiments(GameState state, MergeRegimentsCommand c)
    {
        var regiments = c.RegimentIds.Distinct().Select(state.GetRegiment)
            .Where(r => r != null && r.Owner == c.Player && !r.IsStationed).Cast<Regiment>().ToList();
        int merged = 0;
        foreach (var byType in regiments.GroupBy(r => r.Type))
            merged += RegimentSystem.MergeAll(state, byType.ToList());
        return merged > 0 ? CommandResult.Success : CommandResult.Fail("Select two or more Regiments of the same type to merge.");
    }

    private static CommandResult MoveRegiments(GameState state, MoveRegimentsCommand c)
    {
        var regiments = c.RegimentIds.Distinct().Select(state.GetRegiment)
            .Where(r => r != null && r.Owner == c.Player).Cast<Regiment>().ToList();
        if (regiments.Count == 0) return CommandResult.Fail("No Regiments selected.");
        var facing = c.Facing.IsZero ? FixVec2.North : c.Facing;
        // Several Regiments move as a Battle Group, arranged in ranks by type.
        BattleGroupSystem.OrderGroupMove(state, regiments, c.Target, facing, c.AttackMove);
        return CommandResult.Success;
    }

    private static CommandResult ForRegiments(GameState state, int player, int[] ids, Action<Regiment> action)
    {
        int count = 0;
        foreach (var id in ids)
        {
            var r = state.GetRegiment(id);
            if (r == null || r.Owner != player) continue;
            action(r);
            count++;
        }
        return count > 0 ? CommandResult.Success : CommandResult.Fail("No Regiments selected.");
    }

    private static CommandResult AttackRegiment(GameState state, AttackRegimentCommand c)
    {
        var target = state.GetRegiment(c.TargetRegimentId);
        if (target == null || target.Owner == c.Player || !state.CanSee(c.Player, target)) return CommandResult.Fail("Invalid target.");
        return ForRegiments(state, c.Player, c.RegimentIds, r =>
        {
            BattleGroupSystem.Leave(state, r);
            RegimentSystem.OrderAttackRegiment(state, r, target);
        });
    }

    private static CommandResult AttackBuilding(GameState state, AttackBuildingCommand c)
    {
        var target = state.KnownBuilding(c.Player, c.TargetBuildingId);
        if (target == null || target.Owner == c.Player || target.State == BuildingState.Ruin) return CommandResult.Fail("Invalid target.");
        return ForRegiments(state, c.Player, c.RegimentIds, r =>
        {
            BattleGroupSystem.Leave(state, r);
            RegimentSystem.OrderAttackBuilding(state, r, target);
        });
    }

    private static CommandResult FormBattleLine(GameState state, FormBattleLineCommand c)
    {
        var leader = state.GetRegiment(c.InfantryRegimentId);
        if (leader == null || leader.Owner != c.Player) return CommandResult.Fail("Select an infantry Regiment to lead.");
        if (state.Config.Soldier(leader.Type).Ranged != null) return CommandResult.Fail("The leading Regiment must be infantry.");
        return ForRegiments(state, c.Player, c.RangedRegimentIds.Where(id => id != leader.Id).ToArray(), r =>
        {
            BattleGroupSystem.Leave(state, r);
            if (state.Config.Soldier(r.Type).Ranged != null) RegimentSystem.OrderFollow(state, r, leader);
        });
    }

    private static CommandResult Recruit(GameState state, RecruitCommand c)
    {
        var b = state.GetBuilding(c.BuildingId);
        if (b?.Recruitment == null || b.Owner != c.Player || !b.IsActive) return CommandResult.Fail("Select a finished Barracks, Archery or Stable.");
        var def = state.Config.Soldier(c.Type);
        if (def.RecruitedAt != b.Type) return CommandResult.Fail($"{c.Type} are recruited at a {def.RecruitedAt}.");
        if (b.Recruitment.Queue.Count >= 5) return CommandResult.Fail("Recruitment queue is full.");
        b.Recruitment.Queue.Add(c.Type);
        return CommandResult.Success;
    }

    private static CommandResult Station(GameState state, StationCommand c)
    {
        var fort = state.GetBuilding(c.FortId);
        if (fort == null || !fort.IsFort || fort.Owner != c.Player) return CommandResult.Fail("Select one of your Outposts or Strongholds.");
        return ForRegiments(state, c.Player, c.RegimentIds, r =>
        {
            BattleGroupSystem.Leave(state, r);
            RegimentSystem.OrderStation(state, r, fort);
        });
    }

    private static CommandResult Unstation(GameState state, UnstationCommand c)
    {
        var fort = state.GetBuilding(c.FortId);
        if (fort == null || !fort.IsFort || fort.Owner != c.Player) return CommandResult.Fail("Not your fort.");
        FortSystem.UnstationAll(state, fort);
        return CommandResult.Success;
    }

    private static CommandResult UpgradeOutpost(GameState state, UpgradeOutpostCommand c)
    {
        var fort = state.GetBuilding(c.FortId);
        if (fort == null || fort.Owner != c.Player) return CommandResult.Fail("Not your Outpost.");
        return FortSystem.StartUpgrade(state, fort, out var reason) ? CommandResult.Success : CommandResult.Fail(reason);
    }

    private static CommandResult UpgradeStorehouse(GameState state, UpgradeStorehouseCommand c)
    {
        var b = state.GetBuilding(c.StorehouseId);
        if (b?.Store == null || b.Owner != c.Player || !b.IsActive) return CommandResult.Fail("Select a finished Storehouse.");
        var eco = state.Config.Economy;
        if (b.Store.Upgrades >= eco.StorehouseMaxUpgrades) return CommandResult.Fail("Already fully upgraded.");
        var cost = StockOps.ToArray(eco.StorehouseUpgradeCost);
        if (!StockOps.TryWithdraw(state, c.Player, b.Center, cost)) return CommandResult.Fail($"Need {StockOps.Describe(cost)}.");
        b.Store.Upgrades++;
        b.Store.Capacity = (Fix.FromInt(b.Store.Capacity) * eco.StorehouseUpgradeCapacityMultiplier).RoundToInt();
        return CommandResult.Success;
    }

    private static CommandResult BuyCarriers(GameState state, BuyCarriersCommand c)
    {
        var b = state.GetBuilding(c.StorehouseId);
        if (b?.Store == null || b.Owner != c.Player || !b.IsActive) return CommandResult.Fail("Select a finished Storehouse.");
        var eco = state.Config.Economy;
        if (b.Store.CarriersPurchased >= eco.StorehouseMaxCarriers) return CommandResult.Fail("Carrier limit reached.");
        var cost = StockOps.ToArray(eco.CarrierPurchaseCost);
        if (!StockOps.TryWithdraw(state, c.Player, b.Center, cost)) return CommandResult.Fail($"Need {StockOps.Describe(cost)}.");
        b.Store.CarriersPurchased = System.Math.Min(eco.StorehouseMaxCarriers, b.Store.CarriersPurchased + eco.CarriersPerPurchase);
        return CommandResult.Success;
    }

    private static CommandResult SetMinimumStock(GameState state, SetMinimumStockCommand c)
    {
        var b = state.GetBuilding(c.StorehouseId);
        if (b?.Store == null || b.Owner != c.Player) return CommandResult.Fail("Select a Storehouse.");
        b.Store.MinimumStock[(int)c.Resource] = System.Math.Clamp(c.Amount, 0, b.Store.Capacity);
        return CommandResult.Success;
    }

    private static CommandResult RepairRuin(GameState state, RepairRuinCommand c)
    {
        var b = state.GetBuilding(c.BuildingId);
        if (b == null) return CommandResult.Fail("Nothing to repair.");
        var check = BuildRules.CanRepairRuin(state, c.Player, b);
        if (!check.Ok) return CommandResult.Fail(check.Reason);
        EntityOps.StartRepair(state, b);
        return CommandResult.Success;
    }
}
