using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;

namespace BannerAndBarrow.Simulation.Economy;

/// <summary>
/// Worker jobs as small state machines:
/// - Builder: fetch materials from the nearest Storehouse to a site, then build.
/// - Producer: gather or produce at their workplace, carrying inputs from and outputs to the nearest Storehouse.
/// - Carrier: move Shipments between Storehouses (assigned by <see cref="LogisticsSystem"/>).
/// </summary>
public static class WorkerSystem
{
    private static readonly Fix AtBuildingDistance = Fix.FromRatio(6, 5);
    private const int MaxBuildersPerSite = 3;

    public static void Update(GameState state)
    {
        foreach (var w in state.Workers.Values.ToList())
        {
            if (w.Task == WorkerTask.GoToEat)
            {
                UpdateEating(state, w);
                continue;
            }
            if (w.IsHungry(state.Tick) && CanTakeMealBreak(w) && state.Tick >= w.NextMealTryTick && TryGoEat(state, w)) continue;
            switch (w.Job)
            {
                case WorkerJob.None: UpdateJobless(state, w); break;
                case WorkerJob.Builder: UpdateBuilder(state, w); break;
                case WorkerJob.Producer: UpdateProducer(state, w); break;
                case WorkerJob.Carrier: UpdateCarrier(state, w); break;
            }
        }
    }

    // ------------------------------------------------------------------ needs

    /// <summary>Only between tasks, holding nothing that is reserved or on its way somewhere.</summary>
    private static bool CanTakeMealBreak(Worker w) =>
        w.Task is WorkerTask.Idle or WorkerTask.Wait && w.CarryAmount == 0 && w.PickupAmount == 0 && w.ShipmentId == 0;

    private static bool TryGoEat(GameState state, Worker w)
    {
        var eco = state.Config.Economy;
        var store = StockOps.NearestWithAvailable(state, w.Owner, w.Position, ResourceType.Food);
        if (store == null || store.Store!.Available(ResourceType.Food) < eco.MealFood)
        {
            w.NextMealTryTick = state.Tick + state.Config.SecondsToTicks(Fix.FromInt(10));
            HungerAlert(state, w.Owner, "Workers are Hungry and work at half speed. Build Farms.", w.Position);
            return false;
        }
        store.Store.Reserved[(int)ResourceType.Food] += eco.MealFood;
        w.MealStorehouseId = store.Id;
        w.Task = WorkerTask.GoToEat;
        GoToBuilding(state, w, store);
        return true;
    }

    private static void UpdateEating(GameState state, Worker w)
    {
        var eco = state.Config.Economy;
        var store = state.GetBuilding(w.MealStorehouseId);
        if (store?.Store == null || !store.IsActive)
        {
            if (store?.Store != null) store.Store.Reserved[(int)ResourceType.Food] -= eco.MealFood;
            w.MealStorehouseId = 0;
            w.Task = WorkerTask.Idle;
            return;
        }
        if (!AtBuilding(w, store)) return;
        store.Store.Reserved[(int)ResourceType.Food] -= eco.MealFood;
        store.Store.Stock[(int)ResourceType.Food] -= eco.MealFood;
        w.MealStorehouseId = 0;
        w.HungryAtTick = state.Tick + state.Config.SecondsToTicks(eco.MealIntervalSeconds);
        w.Task = WorkerTask.Idle;
        w.ClearGoal();
    }

    public static void HungerAlert(GameState state, int player, string message, FixVec2 position)
    {
        var p = state.Players[player];
        if (state.Tick < p.NextHungerAlertTick) return;
        p.NextHungerAlertTick = state.Tick + state.Config.SecondsToTicks(Fix.FromInt(30));
        state.AddEvent(player, GameEventKind.Warning, message, position);
    }

    /// <summary>Ticks a job of <paramref name="seconds"/> takes this Worker; Hungry Workers are slower.</summary>
    private static long WorkTicks(GameState state, Worker w, Fix seconds)
    {
        if (w.IsHungry(state.Tick)) seconds = seconds / state.Config.Economy.HungryWorkSpeed;
        return state.Config.SecondsToTicks(seconds);
    }

    // ------------------------------------------------------------------ navigation helpers

    public static void GoToBuilding(GameState state, Worker w, Building b)
    {
        var tiles = b.GetAccessTiles(state.Map);
        var near = b.NearestAccessTile(state.Map, w.Position);
        w.TargetBuildingId = b.Id;
        w.SetFlowGoal(FlowGoalKey.ForEntity(b.Id, MovementClass.Civilian, w.Owner), tiles, near.Center);
    }

    private static void GoToTile(GameState state, Worker w, TileCoord tile)
    {
        w.TargetTile = tile;
        w.SetFlowGoal(FlowGoalKey.ForTile(tile, state.Map.Width, MovementClass.Civilian, w.Owner), new[] { tile }, tile.Center);
    }

    /// <summary>
    /// Close enough to use the building. A Worker that has stopped (crowded doorway, pushed aside) counts from a little
    /// further out, otherwise it would stand there forever: the solver won't re-seek a goal that close.
    /// </summary>
    private static bool AtBuilding(Worker w, Building b) =>
        b.DistanceToFootprint(w.Position) <= AtBuildingDistance || (w.Arrived && b.DistanceToFootprint(w.Position) <= Fix.FromInt(3));

    private static bool AtTile(Worker w, TileCoord t) =>
        w.Arrived || FixVec2.DistanceSquared(w.Position, t.Center) <= Fix.FromRatio(3, 4) * Fix.FromRatio(3, 4);

    private static void Wait(GameState state, Worker w, Fix seconds)
    {
        w.Task = WorkerTask.Wait;
        w.TaskEndTick = state.Tick + state.Config.SecondsToTicks(seconds);
    }

    // ------------------------------------------------------------------ job assignment

    public static void AssignJob(GameState state, Worker w, WorkerJob job, Building? workplace)
    {
        MakeJobless(state, w);
        w.Job = job;
        w.WorkplaceId = workplace?.Id ?? 0;
        if (job == WorkerJob.Producer) workplace?.WorkerIds.Add(w.Id);
        if (job == WorkerJob.Carrier) workplace?.Store?.CarrierIds.Add(w.Id);
        w.Task = WorkerTask.Idle;
    }

    /// <summary>Drops the current job, releasing every reservation the Worker holds.</summary>
    public static void MakeJobless(GameState state, Worker w)
    {
        if (w.Task == WorkerTask.GoToEat && state.GetBuilding(w.MealStorehouseId)?.Store is { } meal)
            meal.Reserved[(int)ResourceType.Food] -= state.Config.Economy.MealFood;
        w.MealStorehouseId = 0;

        if (w.ReservedNodeIndex >= 0) state.ReservedNodes.Remove(w.ReservedNodeIndex);
        w.ReservedNodeIndex = -1;

        var pickup = state.GetBuilding(w.PickupStorehouseId);
        if (w.Job == WorkerJob.Builder)
        {
            var site = GetSite(state, w.TargetSiteId);
            if (w.Task == WorkerTask.GoToPickup)
            {
                if (pickup?.Store != null) pickup.Store.Reserved[(int)w.CarryType] -= w.PickupAmount;
                if (site != null) site.Incoming[(int)w.CarryType] -= w.PickupAmount;
            }
            else if (w.Task == WorkerTask.GoToSite && w.CarryAmount > 0 && site != null)
            {
                site.Incoming[(int)w.CarryType] -= w.CarryAmount;
            }
        }
        if (w.Task is WorkerTask.GoToRepair or WorkerTask.Repair)
        {
            w.TargetBuildingId = 0;
        }
        if (w.Job == WorkerJob.Producer)
        {
            if (pickup?.Store != null)
                for (int i = 0; i < Resources.Count; i++) pickup.Store.Reserved[i] -= w.ReservedInputs[i];
            if (w.Task == WorkerTask.GoToDeliver)
            {
                var target = state.GetBuilding(w.TargetBuildingId);
                if (target?.Store != null) target.Store.Incoming[(int)w.CarryType] -= w.CarryAmount;
            }
        }
        if (w.Job == WorkerJob.Carrier && w.CarryAmount > 0 && state.Logistics.Shipments.TryGetValue(w.ShipmentId, out var shipment))
        {
            shipment.TransitReserved -= w.CarryReserved;
            shipment.TransitExtra -= w.CarryExtra;
        }

        var workplace = state.GetBuilding(w.WorkplaceId);
        workplace?.WorkerIds.Remove(w.Id);
        workplace?.Store?.CarrierIds.Remove(w.Id);

        Array.Clear(w.ReservedInputs);
        Array.Clear(w.CarriedInputs);
        w.ActiveRecipe = -1;
        w.Sowing = false;
        w.Job = WorkerJob.None;
        w.WorkplaceId = 0;
        w.Task = WorkerTask.Idle;
        w.CarryAmount = 0;
        w.CarryReserved = 0;
        w.CarryExtra = 0;
        w.ShipmentId = 0;
        w.PickupStorehouseId = 0;
        w.PickupAmount = 0;
        w.TargetSiteId = 0;
        w.TargetBuildingId = 0;
        w.ClearGoal();
    }

    /// <summary>A construction site vanished: Builders heading to it drop their task.</summary>
    public static void OnSiteRemoved(GameState state, ConstructionTarget site)
    {
        foreach (var w in state.Workers.Values)
        {
            if (w.Job != WorkerJob.Builder || w.TargetSiteId != site.Id) continue;
            if (w.Task == WorkerTask.GoToPickup)
            {
                var pickup = state.GetBuilding(w.PickupStorehouseId);
                if (pickup?.Store != null) pickup.Store.Reserved[(int)w.CarryType] -= w.PickupAmount;
            }
            w.PickupAmount = 0;
            w.CarryAmount = 0;
            w.TargetSiteId = 0;
            w.Task = WorkerTask.Idle;
            w.ClearGoal();
        }
    }

    public static ConstructionTarget? GetSite(GameState state, int id)
    {
        if (id == 0) return null;
        if (state.Buildings.TryGetValue(id, out var b)) return b.IsWorkSite ? b : null;
        return state.RoadSites.TryGetValue(id, out var r) ? r : null;
    }

    // ------------------------------------------------------------------ jobless

    private static void UpdateJobless(GameState state, Worker w)
    {
        var keep = state.GetKeep(w.Owner);
        if (keep == null) return;
        if (w.GoalKind == NavGoalKind.None || (w.Arrived && !AtBuilding(w, keep))) GoToBuilding(state, w, keep);
    }

    // ------------------------------------------------------------------ builders

    private static void UpdateBuilder(GameState state, Worker w)
    {
        var cfg = state.Config;
        switch (w.Task)
        {
            case WorkerTask.Idle:
                FindBuilderTask(state, w);
                break;

            case WorkerTask.Wait:
                if (state.Tick >= w.TaskEndTick) w.Task = WorkerTask.Idle;
                break;

            case WorkerTask.GoToPickup:
            {
                var store = state.GetBuilding(w.PickupStorehouseId);
                var site = GetSite(state, w.TargetSiteId);
                if (store?.Store == null || !store.IsActive || site == null)
                {
                    if (store?.Store != null) store.Store.Reserved[(int)w.CarryType] -= w.PickupAmount;
                    if (site != null) site.Incoming[(int)w.CarryType] -= w.PickupAmount;
                    w.PickupAmount = 0;
                    w.Task = WorkerTask.Idle;
                    break;
                }
                if (!AtBuilding(w, store)) break;
                store.Store.Stock[(int)w.CarryType] -= w.PickupAmount;
                store.Store.Reserved[(int)w.CarryType] -= w.PickupAmount;
                w.CarryAmount = w.PickupAmount;
                w.PickupAmount = 0;
                w.PickupStorehouseId = 0;
                GoToSite(state, w, site);
                break;
            }

            case WorkerTask.GoToSite:
            {
                var site = GetSite(state, w.TargetSiteId);
                if (site == null)
                {
                    w.CarryAmount = 0;
                    w.Task = WorkerTask.Idle;
                    break;
                }
                if (w.CarryAmount == 0 && state.Tick % 20 == w.Id % 20 && !Workable(state, site, w.Owner))
                {
                    w.TargetSiteId = 0;
                    w.Task = WorkerTask.Idle;
                    break;
                }
                if (!AtSite(w, site)) break;
                if (w.CarryAmount > 0)
                {
                    site.Delivered[(int)w.CarryType] += w.CarryAmount;
                    site.Incoming[(int)w.CarryType] -= w.CarryAmount;
                    w.CarryAmount = 0;
                }
                w.Task = site.MaterialsComplete ? WorkerTask.Build : WorkerTask.Idle;
                break;
            }

            case WorkerTask.GoToRepair:
            case WorkerTask.Repair:
                UpdateRepair(state, w);
                break;

            case WorkerTask.Build:
            {
                var site = GetSite(state, w.TargetSiteId);
                if (site == null || !site.MaterialsComplete)
                {
                    w.Task = WorkerTask.Idle;
                    break;
                }
                if (site is Building building && UnderEnemyPresence(state, building, w.Owner))
                {
                    // Construction pauses under enemy Presence: find other work instead of hammering at nothing.
                    Wait(state, w, Fix.FromInt(2));
                    break;
                }
                var rate = cfg.Economy.BuildWorkPerSecond * state.Dt;
                if (w.IsHungry(state.Tick)) rate = rate * cfg.Economy.HungryWorkSpeed;
                site.WorkDone += rate;
                if (site.WorkDone >= site.WorkRequired) CompleteSite(state, site);
                break;
            }
        }
    }

    private static bool AtSite(Worker w, ConstructionTarget site) => site switch
    {
        Building b => AtBuilding(w, b),
        RoadSite r => AtTile(w, r.Tile),
        _ => true,
    };

    private static void GoToSite(GameState state, Worker w, ConstructionTarget site)
    {
        w.TargetSiteId = site.Id;
        w.Task = WorkerTask.GoToSite;
        if (site is Building b) GoToBuilding(state, w, b);
        else if (site is RoadSite r) GoToTile(state, w, r.Tile);
    }

    private static void CompleteSite(GameState state, ConstructionTarget site)
    {
        if (site is Building { Demolishing: true } demolished)
        {
            var store = StockOps.NearestStorehouse(state, demolished.Owner, demolished.Center);
            if (store?.Store != null)
                foreach (var (r, amount) in state.Config.Building(demolished.Type).Cost)
                    store.Store.Stock[(int)r] += (Fix.FromInt(amount) * state.Config.Economy.DemolishRefundFraction).FloorToInt();
            state.AddEvent(demolished.Owner, GameEventKind.Info, $"{demolished.Type} demolished.", demolished.Center);
            EntityOps.DestroyBuilding(state, demolished, allowRuin: false);
        }
        else if (site is Building b)
        {
            EntityOps.CompleteConstruction(state, b);
        }
        else if (site is RoadSite r)
        {
            state.RoadSites.Remove(r.Id);
            if (state.Map.BuildingIds[state.Map.Index(r.Tile)] == 0) state.Map.SetRoad(r.Tile, r.Kind);
        }
        foreach (var other in state.Workers.Values)
            if (other.Job == WorkerJob.Builder && other.TargetSiteId == site.Id && other.Task == WorkerTask.Build)
                other.Task = WorkerTask.Idle;
    }

    private static IEnumerable<ConstructionTarget> SitesOf(GameState state, int player)
    {
        foreach (var b in state.Buildings.Values)
            if (b.Owner == player && b.IsWorkSite) yield return b;
        foreach (var r in state.RoadSites.Values)
            if (r.Owner == player) yield return r;
    }

    private static bool UnderEnemyPresence(GameState state, Building b, int owner) =>
        b.Footprint().Any(t => state.Presence.HasEnemyPresence(t, owner));

    /// <summary>Sites Builders can do something about now: reachable, and not paused by enemy Presence.</summary>
    private static bool Workable(GameState state, ConstructionTarget site, int owner) => site switch
    {
        Building b => !UnderEnemyPresence(state, b, owner) && state.CanWorkersReach(owner, b),
        RoadSite r => state.CanWorkersReach(owner, r.Tile),
        _ => true,
    };

    private static int BuildersOn(GameState state, int siteId, int exceptWorkerId)
    {
        int count = 0;
        foreach (var o in state.Workers.Values)
            if (o.Job == WorkerJob.Builder && o.TargetSiteId == siteId && o.Id != exceptWorkerId) count++;
        return count;
    }

    private static void FindBuilderTask(GameState state, Worker w)
    {
        var sites = SitesOf(state, w.Owner)
            .Where(s => Workable(state, s, w.Owner))
            .OrderBy(s => FixVec2.DistanceSquared(s.WorkPoint, w.Position))
            .ThenBy(s => s.Id)
            .ToList();

        // Work on sites whose materials are all there.
        foreach (var site in sites)
        {
            if (!site.MaterialsComplete || site.WorkDone >= site.WorkRequired) continue;
            if (BuildersOn(state, site.Id, w.Id) >= MaxBuildersPerSite) continue;
            GoToSite(state, w, site);
            return;
        }

        // Then repair a damaged building, if not too many Builders are repairing already.
        if (TryStartRepair(state, w, maxRepairersForPlayer: 2)) return;

        // Otherwise fetch materials: spread Builders over sites first, then let the rest help wherever it's needed.
        if (TryFetchMaterials(state, w, sites, spread: true) || TryFetchMaterials(state, w, sites, spread: false)) return;

        // Nothing to build: any damaged building will do.
        if (TryStartRepair(state, w, maxRepairersForPlayer: int.MaxValue)) return;

        Wait(state, w, Fix.One);
    }

    private static bool TryFetchMaterials(GameState state, Worker w, List<ConstructionTarget> sites, bool spread)
    {
        var carry = state.Config.Economy.WorkerCarryCapacity;
        foreach (var site in sites)
        {
            if (spread && sites.Count > 1 && BuildersOn(state, site.Id, w.Id) >= MaxBuildersPerSite) continue;
            for (int i = 0; i < Resources.Count; i++)
            {
                var r = (ResourceType)i;
                int outstanding = site.Outstanding(r);
                if (outstanding <= 0) continue;
                var store = StockOps.NearestWithAvailable(state, w.Owner, site.WorkPoint, r);
                if (store == null)
                {
                    var nearest = StockOps.NearestStorehouse(state, w.Owner, site.WorkPoint);
                    if (nearest != null) LogisticsSystem.RequestDemand(state, nearest, r, outstanding);
                    continue;
                }
                int amount = System.Math.Min(carry, System.Math.Min(outstanding, store.Store!.Available(r)));
                store.Store.Reserved[i] += amount;
                site.Incoming[i] += amount;
                w.CarryType = r;
                w.PickupAmount = amount;
                w.PickupStorehouseId = store.Id;
                w.TargetSiteId = site.Id;
                w.Task = WorkerTask.GoToPickup;
                GoToBuilding(state, w, store);
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ repairs

    private static bool NeedsRepair(GameState state, Building b, int owner)
    {
        if (b.Owner != owner || b.State is not (BuildingState.Active or BuildingState.Upgrading) || b.Demolishing) return false;
        if (b.Hp >= b.MaxHp && b.Fire.Raw <= 0) return false;
        var eco = state.Config.Economy;
        if (state.Tick - b.LastAttackedTick < state.Config.SecondsToTicks(eco.RepairDelaySeconds)) return false;
        return !UnderEnemyPresence(state, b, owner);
    }

    private static bool TryStartRepair(GameState state, Worker w, int maxRepairersForPlayer)
    {
        var eco = state.Config.Economy;
        int repairing = 0;
        foreach (var o in state.Workers.Values)
            if (o.Owner == w.Owner && o.Id != w.Id && o.Task is WorkerTask.GoToRepair or WorkerTask.Repair) repairing++;
        if (repairing >= maxRepairersForPlayer) return false;

        Building? best = null;
        Fix bestD = Fix.MaxValue;
        foreach (var b in state.Buildings.Values)
        {
            if (!NeedsRepair(state, b, w.Owner)) continue;
            int onIt = 0;
            foreach (var o in state.Workers.Values)
                if (o.Id != w.Id && o.TargetBuildingId == b.Id && o.Task is WorkerTask.GoToRepair or WorkerTask.Repair) onIt++;
            if (onIt >= eco.MaxRepairersPerBuilding || !state.CanWorkersReach(w.Owner, b)) continue;
            // Burning buildings first, then the nearest.
            var d = b.DistanceToFootprint(w.Position) - (b.Fire.Raw > 0 ? Fix.FromInt(1000) : Fix.Zero);
            if (d < bestD)
            {
                bestD = d;
                best = b;
            }
        }
        if (best == null) return false;
        w.Task = WorkerTask.GoToRepair;
        GoToBuilding(state, w, best);
        return true;
    }

    private static void UpdateRepair(GameState state, Worker w)
    {
        var b = state.GetBuilding(w.TargetBuildingId);
        if (b == null || !NeedsRepair(state, b, w.Owner))
        {
            w.TargetBuildingId = 0;
            w.Task = WorkerTask.Idle;
            w.ClearGoal();
            return;
        }
        if (w.Task == WorkerTask.GoToRepair)
        {
            if (!AtBuilding(w, b)) return;
            w.Task = WorkerTask.Repair;
            w.ClearGoal();
        }
        var eco = state.Config.Economy;
        var speed = w.IsHungry(state.Tick) ? eco.HungryWorkSpeed : Fix.One;
        b.Hp = Fix.Min(b.MaxHp, b.Hp + b.MaxHp * eco.RepairFractionPerSecond * state.Dt * speed);
        b.Fire = Fix.Max(Fix.Zero, b.Fire - eco.FireDousePerSecond * state.Dt * speed);
    }

    // ------------------------------------------------------------------ producers

    private static void UpdateProducer(GameState state, Worker w)
    {
        var workplace = state.GetBuilding(w.WorkplaceId);
        if (workplace == null || !workplace.IsActive)
        {
            MakeJobless(state, w);
            return;
        }
        var def = state.Config.Building(workplace.Type);

        switch (w.Task)
        {
            case WorkerTask.Idle:
                if (w.CarryAmount > 0)
                {
                    StartDelivery(state, w);
                    break;
                }
                StartProductionCycle(state, w, workplace, def);
                break;

            case WorkerTask.Wait:
                if (state.Tick >= w.TaskEndTick) w.Task = WorkerTask.Idle;
                break;

            case WorkerTask.GoToNode:
            {
                int index = state.Map.Index(w.TargetTile);
                bool available = workplace.Type == BuildingType.Farm
                    ? (w.Sowing ? IsSowable(state, index, w.Owner) : state.Map.IsRipe(index, state.Tick))
                    : IsNodeAvailable(state, workplace.Type, index);
                if (!available)
                {
                    ReleaseNode(state, w);
                    w.Task = WorkerTask.Idle;
                    break;
                }
                // Farmers stand on the Field itself; other gatherers work from next to the node.
                var reach = workplace.Type == BuildingType.Farm ? Fix.FromRatio(1, 2) : Fix.FromRatio(3, 2);
                if (!AtTile(w, w.TargetTile) && FixVec2.DistanceSquared(w.Position, w.TargetTile.Center) > reach * reach) break;
                w.Task = w.Sowing ? WorkerTask.Sow : WorkerTask.Harvest;
                w.TaskEndTick = state.Tick + WorkTicks(state, w, w.Sowing ? state.Config.Economy.SowSeconds : def.WorkSeconds);
                w.ClearGoal();
                break;
            }

            case WorkerTask.Sow:
            {
                if (state.Tick < w.TaskEndTick) break;
                int index = w.ReservedNodeIndex;
                ReleaseNode(state, w);
                w.Sowing = false;
                if (index >= 0 && IsSowable(state, index, w.Owner))
                    state.Map.Sow(index, state.Tick, state.Tick + state.Config.SecondsToTicks(state.Config.Economy.CropGrowSeconds));
                w.Task = WorkerTask.Idle;
                break;
            }

            case WorkerTask.Harvest:
                if (state.Tick < w.TaskEndTick) break;
                if (workplace.Type == BuildingType.Farm) HarvestField(state, w, workplace, def);
                else Harvest(state, w, workplace, def);
                break;

            case WorkerTask.GoToWorkplace:
                if (!AtBuilding(w, workplace)) break;
                if (w.CarryAmount > 0)
                {
                    // Back from a gathering trip: add it to the pile at the building.
                    var carried = w.CarryType;
                    int amount = w.CarryAmount;
                    w.CarryAmount = 0;
                    StoreOutput(state, w, workplace, def, carried, amount);
                    break;
                }
                w.Task = WorkerTask.Work;
                w.TaskEndTick = state.Tick + WorkTicks(state, w, WorkSecondsFor(w, def));
                w.ClearGoal();
                break;

            case WorkerTask.Work:
                if (state.Tick < w.TaskEndTick) break;
                FinishWork(state, w, workplace, def);
                break;

            case WorkerTask.GoToPickup:
            {
                var store = state.GetBuilding(w.PickupStorehouseId);
                if (store?.Store == null || !store.IsActive)
                {
                    Array.Clear(w.ReservedInputs);
                    w.PickupStorehouseId = 0;
                    w.Task = WorkerTask.Idle;
                    break;
                }
                if (!AtBuilding(w, store)) break;
                for (int i = 0; i < Resources.Count; i++)
                {
                    store.Store.Stock[i] -= w.ReservedInputs[i];
                    store.Store.Reserved[i] -= w.ReservedInputs[i];
                    w.CarriedInputs[i] += w.ReservedInputs[i];
                    w.ReservedInputs[i] = 0;
                }
                w.PickupStorehouseId = 0;
                w.Task = WorkerTask.GoToWorkplace;
                GoToBuilding(state, w, workplace);
                break;
            }

            case WorkerTask.GoToDeliver:
            {
                var store = state.GetBuilding(w.TargetBuildingId);
                if (store?.Store == null || !store.IsActive)
                {
                    w.Task = WorkerTask.Idle; // keep carrying; will pick another Storehouse
                    break;
                }
                if (!AtBuilding(w, store)) break;
                store.Store.Incoming[(int)w.CarryType] -= w.CarryAmount;
                store.Store.Stock[(int)w.CarryType] += w.CarryAmount;
                w.CarryAmount = 0;
                w.Task = WorkerTask.Idle;
                break;
            }
        }
    }

    private static ResourceType? GatheredResource(BuildingType type) => type switch
    {
        BuildingType.Woodcutter => ResourceType.Wood,
        BuildingType.Quarry => ResourceType.Stone,
        BuildingType.IronMine => ResourceType.Iron,
        _ => null,
    };

    private static void StartProductionCycle(GameState state, Worker w, Building workplace, Config.BuildingDef def)
    {
        if (workplace.Type == BuildingType.Farm)
        {
            StartFarmCycle(state, w, workplace, def);
            return;
        }
        var gathered = GatheredResource(workplace.Type);
        if (gathered.HasValue)
        {
            int node = FindNode(state, workplace, gathered.Value);
            if (node >= 0)
            {
                state.ReservedNodes.Add(node);
                w.ReservedNodeIndex = node;
                w.Task = WorkerTask.GoToNode;
                GoToTile(state, w, new TileCoord(node % state.Map.Width, node / state.Map.Width));
                return;
            }
            if (DeliverLeftovers(state, w, workplace)) return;
            if (workplace.Type == BuildingType.Woodcutter && PlantSapling(state, workplace))
            {
                Wait(state, w, def.WorkSeconds);
                return;
            }
            Wait(state, w, Fix.FromInt(5));
            return;
        }

        if (def.Recipes.Count > 0 && w.ActiveRecipe < 0)
        {
            // Make to order, then to stock; nothing needed means the workshop idles.
            w.ActiveRecipe = Manufacturing.ChooseRecipe(state, workplace);
            if (w.ActiveRecipe < 0)
            {
                if (!DeliverLeftovers(state, w, workplace)) Wait(state, w, Fix.FromInt(3));
                return;
            }
        }
        var inputs = InputsFor(w, def);
        if (inputs.Count > 0)
        {
            bool haveInputs = inputs.All(kv => w.CarriedInputs[(int)kv.Key] >= kv.Value);
            if (!haveInputs)
            {
                var store = StockOps.Nearest(state, w.Owner, workplace.Center,
                    b => inputs.All(kv => b.Store!.Available(kv.Key) >= kv.Value - w.CarriedInputs[(int)kv.Key]));
                if (store == null)
                {
                    var nearest = StockOps.NearestStorehouse(state, w.Owner, workplace.Center);
                    if (nearest != null)
                        foreach (var (r, amount) in inputs)
                            if (nearest.Store!.Available(r) < amount)
                                LogisticsSystem.RequestDemand(state, nearest, r, amount * 3);
                    w.ActiveRecipe = -1; // re-plan next time: another product may be makeable
                    Wait(state, w, Fix.FromInt(3));
                    return;
                }
                foreach (var (r, amount) in inputs)
                {
                    int need = System.Math.Max(0, amount - w.CarriedInputs[(int)r]);
                    store.Store!.Reserved[(int)r] += need;
                    w.ReservedInputs[(int)r] = need;
                }
                w.PickupStorehouseId = store.Id;
                w.Task = WorkerTask.GoToPickup;
                GoToBuilding(state, w, store);
                return;
            }
        }

        if (AtBuilding(w, workplace))
        {
            w.Task = WorkerTask.Work;
            w.TaskEndTick = state.Tick + WorkTicks(state, w, WorkSecondsFor(w, def));
            w.ClearGoal();
        }
        else
        {
            w.Task = WorkerTask.GoToWorkplace;
            GoToBuilding(state, w, workplace);
        }
    }

    // ------------------------------------------------------------------ farms

    /// <summary>Harvest a ripe Field if there is one, otherwise sow until the Farm has its Fields, otherwise rest.</summary>
    private static void StartFarmCycle(GameState state, Worker w, Building farm, Config.BuildingDef def)
    {
        int ripe = FindField(state, farm, w.Owner, sow: false);
        int target = ripe >= 0 ? ripe : CountCrops(state, farm) < state.Config.Economy.FarmFields ? FindField(state, farm, w.Owner, sow: true) : -1;
        if (target >= 0)
        {
            state.ReservedNodes.Add(target);
            w.ReservedNodeIndex = target;
            w.Sowing = ripe < 0;
            w.Task = WorkerTask.GoToNode;
            GoToTile(state, w, new TileCoord(target % state.Map.Width, target / state.Map.Width));
            return;
        }
        if (DeliverLeftovers(state, w, farm)) return;
        if (!AtBuilding(w, farm))
        {
            w.Task = WorkerTask.GoToWorkplace;
            GoToBuilding(state, w, farm);
            return;
        }
        Wait(state, w, Fix.FromInt(2));
    }

    /// <summary>Empty grass a Farmer may turn into a Field: own Territory, nothing built, planted or paved on it.</summary>
    private static bool IsSowable(GameState state, int index, int owner)
    {
        var map = state.Map;
        return map.Terrain[index] == Terrain.Grass && map.BuildingIds[index] == 0 && map.Roads[index] == RoadKind.None &&
               map.Deposits[index] == null && map.RegrowAtTick[index] == 0 && !map.HasCrop(index) &&
               state.Territory.Owner[index] == owner;
    }

    private static bool IsFieldTile(GameState state, Building farm, int x, int y)
    {
        int radius = state.Config.Economy.FarmFieldRadius;
        var c = farm.CenterTile;
        int dx = x - c.X, dy = y - c.Y;
        return state.Map.InBounds(x, y) && dx * dx + dy * dy <= radius * radius && !farm.Covers(new TileCoord(x, y));
    }

    private static int CountCrops(GameState state, Building farm)
    {
        int radius = state.Config.Economy.FarmFieldRadius, count = 0;
        var c = farm.CenterTile;
        for (int y = c.Y - radius; y <= c.Y + radius; y++)
        for (int x = c.X - radius; x <= c.X + radius; x++)
        {
            if (!IsFieldTile(state, farm, x, y)) continue;
            int i = state.Map.Index(x, y);
            if (state.Map.HasCrop(i) || (state.ReservedNodes.Contains(i) && !state.Map.HasCrop(i))) count++;
        }
        return count;
    }

    private static int FindField(GameState state, Building farm, int owner, bool sow)
    {
        int radius = state.Config.Economy.FarmFieldRadius;
        var c = farm.CenterTile;
        int best = -1, bestD = int.MaxValue;
        for (int y = c.Y - radius; y <= c.Y + radius; y++)
        for (int x = c.X - radius; x <= c.X + radius; x++)
        {
            if (!IsFieldTile(state, farm, x, y)) continue;
            int dx = x - c.X, dy = y - c.Y, d = dx * dx + dy * dy;
            // Sow close to the Farm first (compact fields); harvest the nearest ripe one.
            if (d >= bestD) continue;
            int i = state.Map.Index(x, y);
            if (state.ReservedNodes.Contains(i)) continue;
            if (sow ? !IsSowable(state, i, owner) : !state.Map.IsRipe(i, state.Tick)) continue;
            best = i;
            bestD = d;
        }
        return best;
    }

    private static void HarvestField(GameState state, Worker w, Building farm, Config.BuildingDef def)
    {
        int index = w.ReservedNodeIndex;
        ReleaseNode(state, w);
        if (index < 0 || !state.Map.IsRipe(index, state.Tick))
        {
            w.Task = WorkerTask.Idle;
            return;
        }
        state.Map.ClearCrop(index);
        def.Outputs.TryGetValue(ResourceType.Food, out int perField);
        w.CarryType = ResourceType.Food;
        w.CarryAmount = System.Math.Max(1, perField);
        w.Task = WorkerTask.GoToWorkplace;
        GoToBuilding(state, w, farm);
    }

    private static bool IsNodeAvailable(GameState state, BuildingType type, int index)
    {
        var map = state.Map;
        return type switch
        {
            BuildingType.Woodcutter => map.Terrain[index] == Terrain.Forest && map.ResourceAmount[index] > 0,
            BuildingType.Quarry => map.Deposits[index] == ResourceType.Stone && map.ResourceAmount[index] > 0,
            BuildingType.IronMine => map.Deposits[index] == ResourceType.Iron && map.ResourceAmount[index] > 0,
            _ => false,
        };
    }

    public static int CountNodesInRange(GameState state, BuildingType type, TileCoord center)
    {
        int radius = state.Config.Economy.ProductionRadius, count = 0;
        var map = state.Map;
        if (type == BuildingType.Farm)
        {
            // Room for Fields: grass that isn't built on, paved or planted (Territory is checked when sowing).
            radius = state.Config.Economy.FarmFieldRadius;
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
            for (int x = center.X - radius; x <= center.X + radius; x++)
            {
                if (!map.InBounds(x, y) || (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) > radius * radius) continue;
                if (System.Math.Abs(x - center.X) <= 1 && System.Math.Abs(y - center.Y) <= 1) continue;
                int i = map.Index(x, y);
                if (map.Terrain[i] == Terrain.Grass && map.BuildingIds[i] == 0 && map.Roads[i] == RoadKind.None && map.Deposits[i] == null && map.RegrowAtTick[i] == 0) count++;
            }
            return count;
        }
        for (int y = center.Y - radius; y <= center.Y + radius; y++)
        for (int x = center.X - radius; x <= center.X + radius; x++)
        {
            if (!map.InBounds(x, y)) continue;
            int dx = x - center.X, dy = y - center.Y;
            if (dx * dx + dy * dy > radius * radius) continue;
            if (IsNodeAvailable(state, type, map.Index(x, y))) count++;
        }
        return count;
    }

    private static int FindNode(GameState state, Building workplace, ResourceType resource)
    {
        var map = state.Map;
        int radius = state.Config.Economy.ProductionRadius;
        var c = workplace.CenterTile;
        int best = -1, bestD = int.MaxValue;
        for (int y = c.Y - radius; y <= c.Y + radius; y++)
        for (int x = c.X - radius; x <= c.X + radius; x++)
        {
            if (!map.InBounds(x, y)) continue;
            int dx = x - c.X, dy = y - c.Y, d = dx * dx + dy * dy;
            if (d > radius * radius || d >= bestD) continue;
            int i = map.Index(x, y);
            if (state.ReservedNodes.Contains(i) || !IsNodeAvailable(state, workplace.Type, i)) continue;
            best = i;
            bestD = d;
        }
        return best;
    }

    private static bool PlantSapling(GameState state, Building workplace)
    {
        var map = state.Map;
        int radius = state.Config.Economy.ProductionRadius;
        var c = workplace.CenterTile;
        for (int r = 2; r <= radius; r++)
        for (int y = c.Y - r; y <= c.Y + r; y++)
        for (int x = c.X - r; x <= c.X + r; x++)
        {
            if (!map.InBounds(x, y) || System.Math.Max(System.Math.Abs(x - c.X), System.Math.Abs(y - c.Y)) != r) continue;
            int i = map.Index(x, y);
            if (map.Terrain[i] != Terrain.Grass || map.BuildingIds[i] != 0 || map.Roads[i] != RoadKind.None ||
                map.Deposits[i] != null || map.RegrowAtTick[i] != 0 || map.HasCrop(i) || state.Saplings.Contains(i)) continue;
            state.Saplings.Add(i);
            map.PlantSapling(new TileCoord(x, y), state.Tick + state.Config.SecondsToTicks(state.Config.Economy.TreeRegrowSeconds));
            return true;
        }
        return false;
    }

    private static void ReleaseNode(GameState state, Worker w)
    {
        if (w.ReservedNodeIndex >= 0) state.ReservedNodes.Remove(w.ReservedNodeIndex);
        w.ReservedNodeIndex = -1;
    }

    private static void Harvest(GameState state, Worker w, Building workplace, Config.BuildingDef def)
    {
        var resource = GatheredResource(workplace.Type)!.Value;
        var map = state.Map;
        int node = w.ReservedNodeIndex;
        ReleaseNode(state, w);
        if (node < 0 || !IsNodeAvailable(state, workplace.Type, node))
        {
            w.Task = WorkerTask.Idle;
            return;
        }
        def.Outputs.TryGetValue(resource, out int perTrip);
        int amount = System.Math.Min(System.Math.Max(1, perTrip), map.ResourceAmount[node]);
        map.ResourceAmount[node] -= amount;
        if (map.ResourceAmount[node] <= 0)
        {
            if (resource == ResourceType.Wood)
                map.FellTree(new TileCoord(node % map.Width, node / map.Width), state.Tick + state.Config.SecondsToTicks(state.Config.Economy.TreeRegrowSeconds));
            else
                map.Deposits[node] = null;
        }
        // Take it back to the workplace; it only reaches a Storehouse once a full batch is ready.
        w.CarryType = resource;
        w.CarryAmount = amount;
        w.Task = WorkerTask.GoToWorkplace;
        GoToBuilding(state, w, workplace);
    }

    /// <summary>Adds output to the building's pile; when a batch is ready the Worker carries all of it to a Storehouse.</summary>
    private static void StoreOutput(GameState state, Worker w, Building workplace, Config.BuildingDef def, ResourceType resource, int amount)
    {
        int r = (int)resource;
        workplace.OutputStock[r] += amount;
        // Workshops rush ordered goods out straight away instead of waiting for a full batch.
        bool ordered = def.Recipes.Count > 0 && Manufacturing.Orders(state, w.Owner)[r] > state.TotalStock(w.Owner)[r];
        if (!ordered && workplace.OutputStock[r] < System.Math.Max(1, def.BatchSize))
        {
            w.Task = WorkerTask.Idle;
            return;
        }
        w.CarryType = resource;
        w.CarryAmount = workplace.OutputStock[r];
        workplace.OutputStock[r] = 0;
        StartDelivery(state, w);
    }

    /// <summary>Nothing left to produce: carry whatever has piled up rather than leaving it stranded.</summary>
    private static bool DeliverLeftovers(GameState state, Worker w, Building workplace)
    {
        for (int i = 0; i < Resources.Count; i++)
        {
            if (workplace.OutputStock[i] <= 0) continue;
            w.CarryType = (ResourceType)i;
            w.CarryAmount = workplace.OutputStock[i];
            workplace.OutputStock[i] = 0;
            StartDelivery(state, w);
            return true;
        }
        return false;
    }

    private static Dictionary<ResourceType, int> InputsFor(Worker w, Config.BuildingDef def) =>
        w.ActiveRecipe >= 0 && w.ActiveRecipe < def.Recipes.Count ? def.Recipes[w.ActiveRecipe].Inputs : def.Inputs;

    private static Fix WorkSecondsFor(Worker w, Config.BuildingDef def) =>
        w.ActiveRecipe >= 0 && w.ActiveRecipe < def.Recipes.Count ? def.Recipes[w.ActiveRecipe].WorkSeconds : def.WorkSeconds;

    private static void FinishWork(GameState state, Worker w, Building workplace, Config.BuildingDef def)
    {
        var inputs = InputsFor(w, def);
        if (inputs.Count > 0)
        {
            if (!inputs.All(kv => w.CarriedInputs[(int)kv.Key] >= kv.Value))
            {
                w.Task = WorkerTask.Idle;
                return;
            }
            foreach (var (input, needed) in inputs) w.CarriedInputs[(int)input] -= needed;
        }

        var recipe = w.ActiveRecipe >= 0 && w.ActiveRecipe < def.Recipes.Count ? def.Recipes[w.ActiveRecipe] : null;
        w.ActiveRecipe = -1;
        var resource = recipe?.Output ?? (def.Outputs.Count > 0 ? def.Outputs.First().Key : ResourceType.Food);
        int amount = recipe?.Amount ?? (def.Outputs.Count > 0 ? def.Outputs.First().Value : 0);
        if (workplace.Type == BuildingType.TaxOffice)
        {
            int houses = state.Buildings.Values.Count(b => b.Owner == w.Owner && b.Type == BuildingType.House && b.IsActive);
            amount = System.Math.Clamp(houses * state.Config.Economy.TaxGoldPerHouse, 1, state.Config.Economy.TaxGoldMaxPerCycle);
        }
        if (amount <= 0)
        {
            w.Task = WorkerTask.Idle;
            return;
        }
        StoreOutput(state, w, workplace, def, resource, amount);
    }

    private static void StartDelivery(GameState state, Worker w)
    {
        var store = StockOps.NearestWithCapacity(state, w.Owner, w.Position, w.CarryType, w.CarryAmount);
        if (store == null)
        {
            // No Storehouse can take it: the Worker stops working until one can.
            Wait(state, w, Fix.FromInt(2));
            return;
        }
        store.Store!.Incoming[(int)w.CarryType] += w.CarryAmount;
        w.Task = WorkerTask.GoToDeliver;
        GoToBuilding(state, w, store);
    }

    // ------------------------------------------------------------------ carriers

    private static void UpdateCarrier(GameState state, Worker w)
    {
        var home = state.GetBuilding(w.WorkplaceId);
        if (home?.Store == null || !home.IsActive)
        {
            MakeJobless(state, w);
            return;
        }

        switch (w.Task)
        {
            case WorkerTask.Idle:
                if (!AtBuilding(w, home) && (w.GoalKind == NavGoalKind.None || w.Arrived)) GoToBuilding(state, w, home);
                break;

            case WorkerTask.GoToPickup:
            {
                state.Logistics.Shipments.TryGetValue(w.ShipmentId, out var shipment);
                var holder = shipment == null ? null : state.GetBuilding(shipment.HolderId);
                if (shipment == null || holder?.Store == null || !holder.IsActive || shipment.NextId != home.Id)
                {
                    w.ShipmentId = 0;
                    w.Task = WorkerTask.Idle;
                    break;
                }
                if (!AtBuilding(w, holder)) break;
                LogisticsSystem.Pickup(state, w, shipment, holder);
                break;
            }

            case WorkerTask.GoToDeliver:
            {
                var target = state.GetBuilding(w.TargetBuildingId);
                if (target?.Store == null || !target.IsActive)
                {
                    w.CarryAmount = 0;
                    w.ShipmentId = 0;
                    w.Task = WorkerTask.Idle;
                    break;
                }
                if (!AtBuilding(w, target)) break;
                LogisticsSystem.Deliver(state, w, target);
                break;
            }

            default:
                w.Task = WorkerTask.Idle;
                break;
        }
    }
}
