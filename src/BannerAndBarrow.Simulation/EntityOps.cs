using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Military;
using BannerAndBarrow.Simulation.Pathfinding;

namespace BannerAndBarrow.Simulation;

/// <summary>Creation and removal of entities, keeping map occupancy, territory and back-references consistent.</summary>
public static class EntityOps
{
    public static Building CreateBuilding(GameState state, int owner, BuildingType type, TileCoord origin, bool underConstruction, int[]? cost = null)
    {
        var def = state.Config.Building(type);
        var b = new Building
        {
            Id = state.NextId(),
            Owner = owner,
            Type = type,
            Origin = origin,
            Size = def.Size,
            MaxHp = Fix.FromInt(def.Hp),
            State = underConstruction ? BuildingState.UnderConstruction : BuildingState.Active,
            WorkRequired = def.BuildSeconds,
        };
        b.Hp = underConstruction ? b.MaxHp / 4 : b.MaxHp;
        if (cost != null)
            for (int i = 0; i < Resources.Count; i++) b.Required[i] = cost[i];
        if (!underConstruction)
        {
            for (int i = 0; i < Resources.Count; i++) b.Delivered[i] = b.Required[i];
            b.WorkDone = b.WorkRequired;
        }

        if (def.Size == 1 && state.Map.TerrainAt(origin) == Terrain.Forest)
        {
            // A wall through the woods clears the trees on its tile.
            state.Map.FellTree(origin, 0);
            state.Saplings.Remove(state.Map.Index(origin));
        }

        InitComponents(state, b);
        state.Buildings[b.Id] = b;
        state.Map.SetBuilding(origin, def.Size, b.Id, gateOwner: type == BuildingType.Gate ? (sbyte)owner : (sbyte)-1);
        state.Territory.Dirty = true;
        // Planned roads under the new building can never be laid: drop them so Builders don't wait on them.
        foreach (var site in state.RoadSites.Values.Where(r => b.Covers(r.Tile)).ToList())
        {
            state.RoadSites.Remove(site.Id);
            WorkerSystem.OnSiteRemoved(state, site);
        }
        return b;
    }

    private static void InitComponents(GameState state, Building b)
    {
        var cfg = state.Config;
        var def = cfg.Building(b.Type);
        if (b.IsStorehouseLike && b.Store == null)
        {
            b.Store = new StorehouseData
            {
                IsKeep = b.Type == BuildingType.Keep,
                Capacity = def.StorageCapacity,
                Reach = b.Type == BuildingType.Keep ? cfg.Territory.KeepReach : cfg.Territory.StorehouseReach,
                CarriersPurchased = cfg.Economy.StorehouseStartCarriers,
            };
        }
        if (b.IsFort && b.Fort == null) b.Fort = new FortData();
        if (b.Type is BuildingType.Barracks or BuildingType.Archery or BuildingType.Stable && b.Recruitment == null) b.Recruitment = new RecruitmentData();
    }

    public static void CompleteConstruction(GameState state, Building b)
    {
        b.State = BuildingState.Active;
        b.Hp = b.MaxHp;
        b.IsRepair = false;
        b.WorkDone = b.WorkRequired;
        state.Territory.Dirty = true;
        state.AddEvent(b.Owner, GameEventKind.Info, $"{b.Type} completed.", b.Center);
    }

    public static void DamageBuilding(GameState state, Building b, Fix amount, int attackerOwner)
    {
        if (b.State == BuildingState.Ruin || amount.Raw <= 0) return;
        b.LastAttackedTick = state.Tick;
        b.Hp -= amount;
        if (state.CollectStats) state.AddStat($"P{b.Owner} building-damage", amount);
        if (b.Hp.Raw <= 0) DestroyBuilding(state, b, allowRuin: true);
    }

    /// <summary>
    /// Destroys a building. Storehouses leave a Ruin (unless <paramref name="allowRuin"/> is false); losing the Keep loses the Match.
    /// </summary>
    public static void DestroyBuilding(GameState state, Building b, bool allowRuin)
    {
        if (!state.Buildings.ContainsKey(b.Id)) return;

        foreach (var wid in b.WorkerIds.ToList())
        {
            var w = state.GetWorker(wid);
            if (w != null) WorkerSystem.MakeJobless(state, w);
        }
        b.WorkerIds.Clear();

        if (b.Store != null)
        {
            foreach (var cid in b.Store.CarrierIds.ToList())
            {
                var c = state.GetWorker(cid);
                if (c != null) WorkerSystem.MakeJobless(state, c);
            }
            b.Store.CarrierIds.Clear();
            LogisticsSystem.OnStorehouseLost(state, b);
        }

        if (b.Fort != null)
        {
            foreach (var rid in b.Fort.StationedRegimentIds.ToList())
            {
                var r = state.GetRegiment(rid);
                if (r != null) FortSystem.Eject(state, b, r);
            }
        }

        WorkerSystem.OnSiteRemoved(state, b);

        if (b.Type == BuildingType.Keep)
        {
            state.Players[b.Owner].Defeated = true;
            state.AddEvent(b.Owner, GameEventKind.MatchOver, $"{state.Players[b.Owner].Name}'s Keep has fallen.", b.Center);
        }

        if (allowRuin && b.Type == BuildingType.Storehouse && b.State != BuildingState.UnderConstruction)
        {
            b.State = BuildingState.Ruin;
            b.Hp = Fix.Zero;
            b.IsBurning = false;
            Array.Clear(b.Store!.Stock);
            Array.Clear(b.Store.Reserved);
            Array.Clear(b.Store.Incoming);
            state.AddEvent(b.Owner, GameEventKind.Burning, "Storehouse destroyed! Repair the Ruin before its buildings burn down.", b.Center);
        }
        else
        {
            state.Buildings.Remove(b.Id);
            state.Map.SetBuilding(b.Origin, b.Size, 0);
        }
        state.Territory.Dirty = true;
    }

    /// <summary>Turns a Ruin into a repair construction site.</summary>
    public static void StartRepair(GameState state, Building ruin)
    {
        var cfg = state.Config;
        var baseCost = cfg.Building(BuildingType.Storehouse).Cost;
        Array.Clear(ruin.Required);
        Array.Clear(ruin.Delivered);
        Array.Clear(ruin.Incoming);
        foreach (var (r, amount) in baseCost)
            ruin.Required[(int)r] = (Fix.FromInt(amount) * cfg.Territory.RuinRepairCostFraction).CeilToInt();
        ruin.State = BuildingState.UnderConstruction;
        ruin.IsRepair = true;
        ruin.WorkDone = Fix.Zero;
        ruin.WorkRequired = cfg.Building(BuildingType.Storehouse).BuildSeconds * cfg.Territory.RuinRepairCostFraction;
        ruin.Hp = ruin.MaxHp / 4;
    }

    public static Worker SpawnWorker(GameState state, int owner, FixVec2 position)
    {
        var cfg = state.Config.Movement;
        var w = new Worker
        {
            Id = state.NextId(),
            Owner = owner,
            Position = position,
            PreviousPosition = position,
            MaxSpeed = cfg.WorkerSpeed,
            Radius = cfg.WorkerRadius,
            MovementClass = MovementClass.Civilian,
            NavOwner = owner,
            ArrivalRadius = Fix.FromRatio(1, 2),
            Hp = state.Config.Economy.WorkerHp,
            HungryAtTick = state.Tick + state.Config.SecondsToTicks(state.Config.Economy.MealIntervalSeconds),
            // Workers walk through their own side's Soldiers (Squad -1 matches no Regiment).
            PassGroup = owner + 1,
            Squad = -1,
        };
        state.Workers[w.Id] = w;
        state.MarkAgentsDirty();
        return w;
    }

    public static void KillWorker(GameState state, Worker w)
    {
        if (!state.Workers.ContainsKey(w.Id)) return;
        WorkerSystem.MakeJobless(state, w);
        if (state.CollectStats) state.AddStat($"P{w.Owner} workers-killed", Fix.One);
        state.Workers.Remove(w.Id);
        state.MarkAgentsDirty();
        var player = state.Players[w.Owner];
        if (state.Tick >= player.NextWorkerAttackedAlertTick)
        {
            player.NextWorkerAttackedAlertTick = state.Tick + state.Config.SecondsToTicks(Fix.FromInt(15));
            state.AddEvent(w.Owner, GameEventKind.Alert, "Your Workers are being killed!", w.Position);
        }
    }

    public static Regiment CreateRegiment(GameState state, int owner, SoldierType type, FixVec2 position, FixVec2 facing, int? size = null)
    {
        var cfg = state.Config;
        var def = cfg.Soldier(type);
        var r = new Regiment
        {
            Id = state.NextId(),
            Owner = owner,
            Type = type,
            Formation = def.Formations.Count > 0 ? def.Formations[0] : FormationType.Line,
            Anchor = position,
            Facing = facing,
            Destination = position,
            DestinationFacing = facing,
            HoldPoint = position,
            Cohesion = cfg.Combat.CohesionMax,
        };
        int count = size ?? def.RegimentSize;
        r.StartingSize = count;
        var formation = cfg.Formation(r.Formation);
        for (int i = 0; i < count; i++)
        {
            var slot = position + FixVec2.LocalToWorld(FormationLayout.SlotOffset(formation, i, count), facing);
            var s = new Soldier
            {
                Id = state.NextId(),
                Owner = owner,
                Type = type,
                RegimentId = r.Id,
                Hp = def.Hp,
                Position = slot,
                PreviousPosition = slot,
                MaxSpeed = def.Speed,
                Radius = def.Radius,
                MovementClass = MovementClass.Military,
                PassGroup = owner + 1,
                Squad = r.Id,
            };
            state.Soldiers[s.Id] = s;
            r.SoldierIds.Add(s.Id);
        }
        state.Regiments[r.Id] = r;
        RegimentSystem.SetDestination(state, r, position, facing);
        state.MarkAgentsDirty();
        return r;
    }

    public static void KillSoldier(GameState state, Soldier s)
    {
        if (!state.Soldiers.Remove(s.Id)) return;
        state.MarkAgentsDirty();
        var r = state.GetRegiment(s.RegimentId);
        if (r == null) return;
        r.SoldierIds.Remove(s.Id);
        var cfg = state.Config;
        r.Cohesion = Fix.Max(Fix.Zero, r.Cohesion - cfg.Combat.CohesionLossPerCasualty * cfg.Formation(r.Formation).CohesionLossMultiplier);
        if (r.SoldierIds.Count == 0) RemoveRegiment(state, r);
    }

    public static void RemoveRegiment(GameState state, Regiment r)
    {
        BattleGroupSystem.Leave(state, r);
        foreach (var id in r.SoldierIds.ToList())
            state.Soldiers.Remove(id);
        r.SoldierIds.Clear();
        if (r.IsStationed)
        {
            var fort = state.GetBuilding(r.StationedInBuildingId);
            fort?.Fort?.StationedRegimentIds.Remove(r.Id);
        }
        state.Regiments.Remove(r.Id);
        state.MarkAgentsDirty();
    }
}
