using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;

namespace BannerAndBarrow.Simulation.Economy;

/// <summary>A Storehouse's request for stock it is short of.</summary>
public sealed class Demand
{
    public int Id;
    public int Owner;
    public int RequesterId;
    public ResourceType Resource;
    /// <summary>Amount not yet covered by a Shipment.</summary>
    public int Amount;
    public long CreatedTick;
}

/// <summary>
/// Reserved stock relayed Storehouse to Storehouse along <see cref="Path"/> (source first, requester last).
/// At each hop the next Storehouse's Carriers fetch it; spare Carrier capacity picks up extra surplus,
/// which serves local needs on the way or continues along the route.
/// </summary>
public sealed class Shipment
{
    public int Id;
    public int Owner;
    public ResourceType Resource;
    public List<int> Path = new();
    public int HopIndex;
    public int HolderReserved;
    public int HolderExtra;
    public int TransitReserved;
    public int TransitExtra;
    public int NextReserved;
    public int NextExtra;
    public long WaitingSinceTick = -1;

    public int HolderId => Path[HopIndex];
    public int NextId => Path[HopIndex + 1];
    public int RequesterId => Path[^1];
    public int HolderTotal => HolderReserved + HolderExtra;
}

public sealed class LogisticsState
{
    public SortedDictionary<int, Demand> Demands { get; } = new();
    public SortedDictionary<int, Shipment> Shipments { get; } = new();
    public long NextRunTick;
    internal int NextId = 1;
}

public static class LogisticsSystem
{
    public static void Update(GameState state)
    {
        var logistics = state.Logistics;
        if (state.Tick < logistics.NextRunTick) return;
        logistics.NextRunTick = state.Tick + state.Config.SecondsToTicks(state.Config.Economy.LogisticsIntervalSeconds);

        CreateMinimumStockDemands(state);
        ResolveDemands(state);
        AssignCarriers(state);
    }

    public static void RequestDemand(GameState state, Building storehouse, ResourceType resource, int amount)
    {
        if (amount <= 0 || storehouse.Store == null) return;
        int alreadyComing = IncomingShipped(state, storehouse.Id, resource);
        amount -= alreadyComing;
        if (amount <= 0) return;
        foreach (var d in state.Logistics.Demands.Values)
        {
            if (d.RequesterId == storehouse.Id && d.Resource == resource)
            {
                d.Amount = System.Math.Max(d.Amount, amount);
                return;
            }
        }
        var demand = new Demand
        {
            Id = state.Logistics.NextId++,
            Owner = storehouse.Owner,
            RequesterId = storehouse.Id,
            Resource = resource,
            Amount = amount,
            CreatedTick = state.Tick,
        };
        state.Logistics.Demands[demand.Id] = demand;
    }

    public static int IncomingShipped(GameState state, int storehouseId, ResourceType resource)
    {
        int total = 0;
        foreach (var s in state.Logistics.Shipments.Values)
            if (s.RequesterId == storehouseId && s.Resource == resource)
                total += s.HolderTotal + s.TransitReserved + s.TransitExtra + s.NextReserved + s.NextExtra;
        return total;
    }

    private static void CreateMinimumStockDemands(GameState state)
    {
        foreach (var b in state.Buildings.Values)
        {
            if (b.Store == null || !b.IsActive) continue;
            for (int i = 0; i < Resources.Count; i++)
            {
                int min = b.Store.MinimumStock[i];
                if (min <= 0) continue;
                int deficit = min - b.Store.Stock[i];
                if (deficit > 0) RequestDemand(state, b, (ResourceType)i, deficit);
            }
        }
    }

    private static void ResolveDemands(GameState state)
    {
        var cfg = state.Config.Territory;
        var expireTicks = state.Config.SecondsToTicks(Fix.FromInt(60));
        foreach (var demand in state.Logistics.Demands.Values.ToList())
        {
            var requester = state.GetBuilding(demand.RequesterId);
            if (requester?.Store == null || !requester.IsActive || demand.Amount <= 0 || state.Tick - demand.CreatedTick > expireTicks)
            {
                state.Logistics.Demands.Remove(demand.Id);
                continue;
            }

            // Breadth-first search over neighbouring Storehouses, never revisiting, limited hops.
            var parent = new Dictionary<int, int> { [requester.Id] = 0 };
            var frontier = new List<Building> { requester };
            for (int hop = 1; hop <= cfg.DemandMaxHops && demand.Amount > 0 && frontier.Count > 0; hop++)
            {
                var next = new List<Building>();
                foreach (var node in frontier)
                {
                    foreach (var neighbour in Neighbours(state, node))
                    {
                        if (parent.ContainsKey(neighbour.Id)) continue;
                        parent[neighbour.Id] = node.Id;
                        next.Add(neighbour);
                    }
                }
                foreach (var source in next)
                {
                    if (demand.Amount <= 0) break;
                    int surplus = source.Store!.Surplus(demand.Resource);
                    if (surplus <= 0) continue;
                    int take = System.Math.Min(surplus, demand.Amount);
                    source.Store.Reserved[(int)demand.Resource] += take;
                    demand.Amount -= take;

                    var path = new List<int>();
                    for (int id = source.Id; id != 0; id = parent[id]) path.Add(id);
                    var shipment = new Shipment
                    {
                        Id = state.Logistics.NextId++,
                        Owner = demand.Owner,
                        Resource = demand.Resource,
                        Path = path,
                        HolderReserved = take,
                    };
                    state.Logistics.Shipments[shipment.Id] = shipment;
                }
                frontier = next;
            }

            if (demand.Amount <= 0) state.Logistics.Demands.Remove(demand.Id);
        }
    }

    public static IEnumerable<Building> Neighbours(GameState state, Building storehouse)
    {
        var radius = Fix.FromInt(state.Config.Territory.StorehouseNeighbourRadius);
        return state.StorehousesOf(storehouse.Owner)
            .Where(b => b.Id != storehouse.Id && FixVec2.DistanceSquared(b.Center, storehouse.Center) <= radius * radius)
            .OrderBy(b => FixVec2.DistanceSquared(b.Center, storehouse.Center))
            .ThenBy(b => b.Id);
    }

    private static void AssignCarriers(GameState state)
    {
        var starvedTicks = state.Config.SecondsToTicks(state.Config.Economy.StarvedAfterSeconds);
        foreach (var b in state.Buildings.Values)
            if (b.Store != null) b.Store.Starved = false;

        foreach (var shipment in state.Logistics.Shipments.Values.ToList())
        {
            var holder = state.GetBuilding(shipment.HolderId);
            var next = state.GetBuilding(shipment.NextId);
            if (holder?.Store == null || next?.Store == null || !holder.IsActive || !next.IsActive)
            {
                CancelShipment(state, shipment);
                continue;
            }
            if (shipment.HolderTotal <= 0)
            {
                shipment.WaitingSinceTick = -1;
                continue;
            }

            int assignedCapacity = 0;
            foreach (var cid in next.Store.CarrierIds)
            {
                var c = state.GetWorker(cid);
                if (c != null && c.ShipmentId == shipment.Id && c.Task == WorkerTask.GoToPickup) assignedCapacity += state.Config.Economy.CarrierCapacity;
            }

            foreach (var cid in next.Store.CarrierIds)
            {
                if (assignedCapacity >= shipment.HolderTotal) break;
                var c = state.GetWorker(cid);
                if (c == null || c.Task != WorkerTask.Idle || c.CarryAmount > 0) continue;
                c.ShipmentId = shipment.Id;
                c.Task = WorkerTask.GoToPickup;
                WorkerSystem.GoToBuilding(state, c, holder);
                assignedCapacity += state.Config.Economy.CarrierCapacity;
            }

            if (assignedCapacity == 0)
            {
                if (shipment.WaitingSinceTick < 0) shipment.WaitingSinceTick = state.Tick;
                if (state.Tick - shipment.WaitingSinceTick >= starvedTicks)
                {
                    if (next.Store.StarvedSinceTick < 0)
                    {
                        next.Store.StarvedSinceTick = state.Tick;
                        state.AddEvent(next.Owner, GameEventKind.Starved, "Supply route starved: a Storehouse needs more Carriers.", next.Center);
                    }
                    next.Store.Starved = true;
                }
            }
            else
            {
                shipment.WaitingSinceTick = -1;
            }
        }

        foreach (var b in state.Buildings.Values)
            if (b.Store != null && !b.Store.Starved) b.Store.StarvedSinceTick = -1;
    }

    /// <summary>A Carrier arrived at the holding Storehouse.</summary>
    public static void Pickup(GameState state, Worker carrier, Shipment shipment, Building holder)
    {
        var store = holder.Store!;
        int r = (int)shipment.Resource;
        int capacity = state.Config.Economy.CarrierCapacity;

        int reserved = System.Math.Min(capacity, shipment.HolderReserved);
        shipment.HolderReserved -= reserved;
        int extra = System.Math.Min(capacity - reserved, shipment.HolderExtra);
        shipment.HolderExtra -= extra;
        store.Stock[r] -= reserved + extra;
        store.Reserved[r] -= reserved + extra;

        // Fill the rest of the load with this Storehouse's spare stock.
        int fill = System.Math.Min(capacity - reserved - extra, store.Surplus(shipment.Resource));
        store.Stock[r] -= fill;
        extra += fill;

        shipment.TransitReserved += reserved;
        shipment.TransitExtra += extra;
        carrier.CarryType = shipment.Resource;
        carrier.CarryReserved = reserved;
        carrier.CarryExtra = extra;
        carrier.CarryAmount = reserved + extra;

        var next = state.GetBuilding(shipment.NextId);
        if (carrier.CarryAmount == 0 || next == null)
        {
            carrier.Task = WorkerTask.Idle;
            carrier.ShipmentId = 0;
            return;
        }
        carrier.Task = WorkerTask.GoToDeliver;
        WorkerSystem.GoToBuilding(state, carrier, next);
    }

    /// <summary>A Carrier arrived at the next Storehouse on the route.</summary>
    public static void Deliver(GameState state, Worker carrier, Building target)
    {
        var store = target.Store;
        if (store == null)
        {
            carrier.CarryAmount = 0;
            carrier.Task = WorkerTask.Idle;
            return;
        }
        int r = (int)carrier.CarryType;
        state.Logistics.Shipments.TryGetValue(carrier.ShipmentId, out var shipment);

        if (shipment == null || shipment.NextId != target.Id)
        {
            store.Stock[r] += carrier.CarryAmount; // route gone: just store it here
        }
        else
        {
            shipment.TransitReserved -= carrier.CarryReserved;
            shipment.TransitExtra -= carrier.CarryExtra;
            bool final = shipment.HopIndex + 1 == shipment.Path.Count - 1;
            if (final)
            {
                store.Stock[r] += carrier.CarryAmount;
            }
            else
            {
                store.Stock[r] += carrier.CarryReserved;
                store.Reserved[r] += carrier.CarryReserved;
                shipment.NextReserved += carrier.CarryReserved;

                int deficit = System.Math.Max(0, store.MinimumStock[r] - store.Available((ResourceType)r));
                int local = System.Math.Min(carrier.CarryExtra, deficit);
                int onward = carrier.CarryExtra - local;
                store.Stock[r] += carrier.CarryExtra;
                store.Reserved[r] += onward;
                shipment.NextExtra += onward;
            }

            if (shipment.HolderTotal == 0 && shipment.TransitReserved == 0 && shipment.TransitExtra == 0)
            {
                if (final)
                {
                    state.Logistics.Shipments.Remove(shipment.Id);
                }
                else
                {
                    shipment.HopIndex++;
                    shipment.HolderReserved = shipment.NextReserved;
                    shipment.HolderExtra = shipment.NextExtra;
                    shipment.NextReserved = 0;
                    shipment.NextExtra = 0;
                    shipment.WaitingSinceTick = -1;
                }
            }
        }

        carrier.CarryAmount = 0;
        carrier.CarryReserved = 0;
        carrier.CarryExtra = 0;
        carrier.ShipmentId = 0;
        carrier.Task = WorkerTask.Idle;
    }

    public static void CancelShipment(GameState state, Shipment shipment)
    {
        var holder = state.GetBuilding(shipment.HolderId);
        if (holder?.Store != null) holder.Store.Reserved[(int)shipment.Resource] -= shipment.HolderTotal;
        if (shipment.HopIndex + 1 < shipment.Path.Count)
        {
            var next = state.GetBuilding(shipment.NextId);
            if (next?.Store != null) next.Store.Reserved[(int)shipment.Resource] -= shipment.NextReserved + shipment.NextExtra;
        }
        ClampReserved(holder);
        state.Logistics.Shipments.Remove(shipment.Id);
    }

    public static void OnStorehouseLost(GameState state, Building storehouse)
    {
        foreach (var s in state.Logistics.Shipments.Values.ToList())
            if (s.Path.Contains(storehouse.Id)) CancelShipment(state, s);
        foreach (var d in state.Logistics.Demands.Values.ToList())
            if (d.RequesterId == storehouse.Id) state.Logistics.Demands.Remove(d.Id);
    }

    private static void ClampReserved(Building? b)
    {
        if (b?.Store == null) return;
        for (int i = 0; i < Resources.Count; i++)
            b.Store.Reserved[i] = System.Math.Clamp(b.Store.Reserved[i], 0, System.Math.Max(0, b.Store.Stock[i]));
    }
}
