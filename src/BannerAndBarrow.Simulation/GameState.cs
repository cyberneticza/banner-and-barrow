using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;
using BannerAndBarrow.Simulation.Territory;
using BannerAndBarrow.Simulation.World;

namespace BannerAndBarrow.Simulation;

public sealed class Player
{
    public int Index;
    public string Name = "";
    public bool IsAi;
    public int KeepId;
    public bool Defeated;
    public long NextWorkerSpawnTick;
    public long NextWorkerAttackedAlertTick;
    public long NextHungerAlertTick;
    /// <summary>Regiments ordered to upgrade that are waiting for goods (these count as manufacturing orders).</summary>
    public readonly List<int> PendingUpgrades = new();
    /// <summary>Enemy buildings as this Player last saw them (fog of war).</summary>
    public readonly SortedDictionary<int, BuildingSighting> KnownBuildings = new();
    /// <summary>Enemy Regiments that attacked this Player, visible until the tick given.</summary>
    public readonly SortedDictionary<int, long> RevealedRegiments = new();
}

public enum GameEventKind : byte
{
    Info,
    Warning,
    Alert,
    Burning,
    Starved,
    CommandRejected,
    MatchOver,
}

public readonly record struct GameEvent(long Tick, int Player, GameEventKind Kind, string Message, FixVec2? Position);

/// <summary>The complete simulation state. Systems read and mutate it once per Tick.</summary>
public sealed class GameState
{
    public GameConfig Config { get; }
    public TileMap Map { get; }
    public Player[] Players { get; }
    public DeterministicRandom Rng { get; }
    public long Tick { get; internal set; }

    public SortedDictionary<int, Building> Buildings { get; } = new();
    public SortedDictionary<int, RoadSite> RoadSites { get; } = new();
    public SortedDictionary<int, Regiment> Regiments { get; } = new();
    public SortedDictionary<int, Military.BattleGroup> BattleGroups { get; } = new();
    public SortedDictionary<int, Soldier> Soldiers { get; } = new();
    public SortedDictionary<int, Worker> Workers { get; } = new();
    public List<Projectile> Projectiles { get; } = new();

    public TerritoryMap Territory { get; }
    public PresenceMap Presence { get; }
    public VisionMap Vision { get; }
    /// <summary>Walkable regions of the map, for "can Workers reach this?" checks.</summary>
    public Connectivity Connectivity { get; }
    internal int VisionTerritoryVersion = -1;
    internal long VisionPresenceTick = -2;
    public LogisticsState Logistics { get; } = new();
    /// <summary>Resource tiles a Worker is currently heading to, so two Workers don't pick the same tree.</summary>
    public HashSet<int> ReservedNodes { get; } = new();
    /// <summary>Tiles with a planted sapling waiting to grow.</summary>
    public HashSet<int> Saplings { get; } = new();

    public FlowFieldCache FlowFields { get; }
    public MovementSolver Movement { get; }
    /// <summary>Soldiers on the map (not Stationed), rebuilt each tick for combat queries. Handles index <see cref="SoldierList"/>.</summary>
    public SpatialHash SoldierHash { get; }
    public List<Soldier> SoldierList { get; } = new();
    /// <summary>Workers on the map, rebuilt each tick so Soldiers can find and attack them.</summary>
    public SpatialHash WorkerHash { get; }
    public List<Worker> WorkerList { get; } = new();
    /// <summary>Scratch list for spatial queries. Per state (not static) so parallel matches never share it.</summary>
    internal List<int> QueryBuffer { get; } = new(64);

    public List<GameEvent> Events { get; } = new();
    public int? Winner { get; internal set; }
    /// <summary>Diagnostics for tuning (damage by source and victim owner). Not used by game rules.</summary>
    public Dictionary<string, Fix> Stats { get; } = new();
    /// <summary>Off in the game (it allocates per hit); the Sandbox turns it on.</summary>
    public bool CollectStats { get; set; }
    internal string DamageSource = "melee";

    public void AddStat(string key, Fix amount)
    {
        if (CollectStats) Stats[key] = (Stats.TryGetValue(key, out var v) ? v : Fix.Zero) + amount;
    }
    public bool IsOver => Winner.HasValue;

    private int _nextId = 1;
    private readonly List<NavAgent> _agents = new();
    private bool _agentsDirty = true;

    public GameState(GameConfig config, TileMap map, int playerCount, ulong seed)
    {
        Config = config;
        Map = map;
        Rng = new DeterministicRandom(seed);
        Players = new Player[playerCount];
        for (int i = 0; i < playerCount; i++) Players[i] = new Player { Index = i, Name = i == 0 ? "You" : "AI" };
        Territory = new TerritoryMap(map.Width, map.Height);
        Presence = new PresenceMap(map.Width, map.Height);
        Vision = new VisionMap(map.Width, map.Height);
        Connectivity = new Connectivity(map);
        var mv = config.Movement;
        FlowFields = new FlowFieldCache(map, mv.FlowFieldCacheCapacity, mv.FlowFieldRebuildsPerTick, mv.FlowFieldMinorStaleTicks);
        Movement = new MovementSolver(map, FlowFields, mv.Steering);
        SoldierHash = new SpatialHash(map.Width, map.Height, 4);
        WorkerHash = new SpatialHash(map.Width, map.Height, 4);
    }

    public int NextId() => _nextId++;

    public Fix Dt => Config.TickSeconds;

    public IReadOnlyList<NavAgent> Agents
    {
        get
        {
            if (_agentsDirty)
            {
                _agents.Clear();
                _agents.AddRange(Workers.Values);
                _agents.AddRange(Soldiers.Values);
                _agentsDirty = false;
            }
            return _agents;
        }
    }

    public void MarkAgentsDirty() => _agentsDirty = true;

    public void AddEvent(int player, GameEventKind kind, string message, FixVec2? position = null)
    {
        Events.Add(new GameEvent(Tick, player, kind, message, position));
        if (Events.Count > 300) Events.RemoveRange(0, Events.Count - 300);
    }

    public Building? GetBuilding(int id) => id != 0 && Buildings.TryGetValue(id, out var b) ? b : null;
    public Regiment? GetRegiment(int id) => id != 0 && Regiments.TryGetValue(id, out var r) ? r : null;
    public Soldier? GetSoldier(int id) => id != 0 && Soldiers.TryGetValue(id, out var s) ? s : null;
    public Worker? GetWorker(int id) => id != 0 && Workers.TryGetValue(id, out var w) ? w : null;

    public Building? GetKeep(int player) => GetBuilding(Players[player].KeepId);

    public bool AreEnemies(int a, int b) => a != b;

    public IEnumerable<Building> StorehousesOf(int player) =>
        Buildings.Values.Where(b => b.Owner == player && b.IsStorehouseLike && b.IsActive && b.Store != null);

    public int SoldierCount(int player)
    {
        int count = 0;
        foreach (var s in Soldiers.Values)
            if (s.Owner == player) count++;
        return count;
    }

    public int WorkerRoom(int player)
    {
        int room = 0;
        foreach (var b in Buildings.Values)
        {
            if (b.Owner != player || !b.IsActive) continue;
            room += Config.Building(b.Type).WorkerRoom;
        }
        return room;
    }

    /// <summary>Worker slots and Carrier places that nobody fills yet (a building is short of staff).</summary>
    public int MissingWorkers(Building b)
    {
        if (!b.IsActive) return 0;
        int missing = System.Math.Max(0, Config.Building(b.Type).WorkerSlots - b.WorkerIds.Count);
        if (b.Store != null) missing += System.Math.Max(0, b.Store.CarriersPurchased - b.Store.CarrierIds.Count);
        return missing;
    }

    public int UnfilledJobs(int player)
    {
        int total = 0;
        foreach (var b in Buildings.Values)
            if (b.Owner == player) total += MissingWorkers(b);
        return total;
    }

    public int HungryWorkers(int player)
    {
        int count = 0;
        foreach (var w in Workers.Values)
            if (w.Owner == player && w.IsHungry(Tick)) count++;
        return count;
    }

    /// <summary>Hungry Workers who recently found no Food to eat (not just on their way to a meal).</summary>
    public int StarvingWorkers(int player)
    {
        int count = 0;
        foreach (var w in Workers.Values)
            if (w.Owner == player && w.IsHungry(Tick) && w.NextMealTryTick > Tick) count++;
        return count;
    }

    public int WorkerCount(int player)
    {
        int count = 0;
        foreach (var w in Workers.Values)
            if (w.Owner == player) count++;
        return count;
    }

    public int[] TotalStock(int player)
    {
        var total = new int[Resources.Count];
        foreach (var s in StorehousesOf(player))
            for (int i = 0; i < Resources.Count; i++)
                total[i] += s.Store!.Stock[i];
        return total;
    }

    /// <summary>True if Workers from the Player's Keep can walk to one of the building's edges.</summary>
    public bool CanWorkersReach(int player, Building b)
    {
        var keep = GetKeep(player);
        if (keep == null) return true;
        var home = new HashSet<int>();
        foreach (var t in keep.GetAccessTiles(Map)) home.Add(Connectivity.ComponentAt(t));
        foreach (var t in b.GetAccessTiles(Map))
            if (home.Contains(Connectivity.ComponentAt(t))) return true;
        return false;
    }

    public bool CanWorkersReach(int player, TileCoord tile)
    {
        var keep = GetKeep(player);
        if (keep == null) return true;
        int c = Connectivity.ComponentAt(tile);
        foreach (var t in keep.GetAccessTiles(Map))
            if (Connectivity.ComponentAt(t) == c) return true;
        return false;
    }

    // ------------------------------------------------------------------ fog of war

    public bool CanSee(int player, TileCoord t) => !Config.Territory.FogOfWar || Vision.IsVisible(t.X, t.Y, player);
    public bool CanSee(int player, FixVec2 p) => CanSee(player, p.ToTile());

    public bool CanSee(int player, Building b) => b.Owner == player || VisionSystem.AnyVisible(this, player, b.Origin, b.Size);

    /// <summary>A Regiment is visible when its own Player owns it, it recently attacked the Player, or any of its Soldiers stands in view.</summary>
    public bool CanSee(int player, Regiment r)
    {
        if (r.Owner == player || !Config.Territory.FogOfWar) return true;
        if (r.IsStationed) return false;
        if (IsRevealed(player, r.Id)) return true;
        foreach (var id in r.SoldierIds)
            if (GetSoldier(id) is { } s && CanSee(player, s.Position)) return true;
        return false;
    }

    public bool IsRevealed(int player, int regimentId) =>
        Players[player].RevealedRegiments.TryGetValue(regimentId, out var until) && Tick < until;

    /// <summary>An attacker gives itself away: its Regiment (or the fort it shoots from) becomes visible to the victim.</summary>
    public void RevealAttacker(int victim, Soldier? attacker, Building? fort = null)
    {
        if (victim < 0 || victim >= Players.Length) return;
        if (attacker != null && attacker.Owner != victim && attacker.RegimentId != 0 && !attacker.Stationed)
            Players[victim].RevealedRegiments[attacker.RegimentId] = Tick + Config.SecondsToTicks(Config.Territory.RevealAttackerSeconds);
        if (fort != null && fort.Owner != victim) VisionSystem.Remember(this, victim, fort);
    }

    /// <summary>A known enemy building that still exists (the Player may target it), or null.</summary>
    public Building? KnownBuilding(int player, int id) =>
        Players[player].KnownBuildings.ContainsKey(id) || !Config.Territory.FogOfWar ? GetBuilding(id) : null;

    public FixVec2 RegimentCentroid(Regiment r)
    {
        if (r.SoldierIds.Count == 0) return r.Anchor;
        var sum = FixVec2.Zero;
        int n = 0;
        foreach (var id in r.SoldierIds)
        {
            var s = GetSoldier(id);
            if (s == null) continue;
            sum += s.Position;
            n++;
        }
        return n == 0 ? r.Anchor : sum / n;
    }
}
