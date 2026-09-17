using BannerAndBarrow.Simulation.Commands;
using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.AI;

/// <summary>
/// Decision layers. v1 ships scripted implementations; swap any of them for a utility-scoring or adaptive
/// policy without touching the controller, the command API or the simulation.
/// </summary>
public interface IEconomyPolicy
{
    void Decide(AiContext ctx);
}

public interface ITerritoryPolicy
{
    void Decide(AiContext ctx);
}

public interface IMilitaryPolicy
{
    void Decide(AiContext ctx);
}

/// <summary>Long-lived AI state between decisions.</summary>
public sealed class AiMemory
{
    public long NextWaveTick = -1;
    public string StrategyName = "";
    public Fix Provocation;
    public long WaveWaitStartTick = -1;
    public bool WaveStaging;
    public long NextOutpostTick;
    public FixVec2 StagePoint;
    public long StageDeadlineTick;
    public int WavesSent;
    public long NextExpansionTick;
    public readonly HashSet<int> WaveRegimentIds = new();
    public readonly HashSet<int> RoadedBuildingIds = new();
    public int ScoutRegimentId;
    public long NextScoutTick;
    public int DecisionCount;
}

/// <summary>Facts gathered once per decision so policies don't each rescan the world.</summary>
public sealed class AiContext
{
    public required GameState State { get; init; }
    public required int Player { get; init; }
    public required AiProfile Profile { get; init; }
    public required CommandQueue Commands { get; init; }
    public required AiMemory Memory { get; init; }

    public Building Keep { get; set; } = null!;
    /// <summary>The enemy Keep, only once this AI has seen it (fog of war).</summary>
    public Building? EnemyKeep { get; set; }
    /// <summary>Where the enemy town is: the seen Keep, or a guess (the map corner mirroring our own Keep).</summary>
    public FixVec2 EnemyTown { get; set; }
    /// <summary>Enemy buildings this AI has seen that still stand (it may be out of date about their state).</summary>
    public List<Building> KnownEnemyBuildings { get; } = new();
    public int[] Stock { get; set; } = new int[Resources.Count];
    public Dictionary<BuildingType, int> BuildingCounts { get; } = new();
    public int ActiveSites { get; set; }
    public int Workers { get; set; }
    public int WorkerRoom { get; set; }
    public List<Regiment> Regiments { get; } = new();
    public List<Regiment> EnemyRegiments { get; } = new();
    public Fix SecondsElapsed { get; set; }
    public AiStrategy Strategy { get; set; } = new();

    /// <summary>The strategy's build order if it has one, otherwise the profile's.</summary>
    public List<BuildStep> BuildOrder => Strategy.BuildOrder.Count > 0 ? Strategy.BuildOrder : Profile.BuildOrder;

    public int Count(BuildingType type) => BuildingCounts.TryGetValue(type, out var c) ? c : 0;

    public void Issue(GameCommand command) => Commands.Enqueue(command);

    public long SecondsToTicks(Fix seconds) => State.Config.SecondsToTicks(seconds);

    /// <summary>Direction from our Keep towards the enemy.</summary>
    public FixVec2 TowardEnemy
    {
        get
        {
            var d = (EnemyTown - Keep.Center).Normalized();
            return d.IsZero ? FixVec2.North : d;
        }
    }
}

public sealed class AiController
{
    private readonly AiMemory _memory = new();
    private long _nextDecisionTick;

    public int Player { get; }
    public AiProfile Profile { get; }
    public IEconomyPolicy Economy { get; set; } = new ScriptedEconomyPolicy();
    public ITerritoryPolicy Territory { get; set; } = new ScriptedTerritoryPolicy();
    public IMilitaryPolicy Military { get; set; } = new ScriptedMilitaryPolicy();

    public AiController(int player, AiProfile profile)
    {
        Player = player;
        Profile = profile;
    }

    public void Update(GameState state, CommandQueue commands)
    {
        if (state.Tick < _nextDecisionTick || state.Players[Player].Defeated) return;
        _nextDecisionTick = state.Tick + state.Config.SecondsToTicks(Profile.DecisionIntervalSeconds);

        var keep = state.GetKeep(Player);
        if (keep == null) return;

        if (_memory.StrategyName.Length == 0) PickStrategy(state);
        var ctx = BuildContext(state, commands, keep);
        _memory.DecisionCount++;
        Economy.Decide(ctx);
        Territory.Decide(ctx);
        Military.Decide(ctx);
    }

    public string StrategyName => _memory.StrategyName;
    public Fix Provocation => _memory.Provocation;

    /// <summary>Weighted, seeded pick of an opening strategy (e.g. Economy boom vs early Rush).</summary>
    private void PickStrategy(GameState state)
    {
        var options = Profile.Strategies.Where(kv => kv.Value.Weight > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        if (options.Count == 0)
        {
            _memory.StrategyName = "Default";
            return;
        }
        int roll = state.Rng.Next(options.Sum(kv => kv.Value.Weight));
        foreach (var (name, strategy) in options)
        {
            if (roll < strategy.Weight)
            {
                _memory.StrategyName = name;
                break;
            }
            roll -= strategy.Weight;
        }
        state.AddEvent(Player, GameEventKind.Info, $"AI strategy: {_memory.StrategyName}.");
    }

    private AiContext BuildContext(GameState state, CommandQueue commands, Building keep)
    {
        var ctx = new AiContext
        {
            State = state,
            Player = Player,
            Profile = Profile,
            Commands = commands,
            Memory = _memory,
            Keep = keep,
            Strategy = Profile.Strategies.TryGetValue(_memory.StrategyName, out var strategy) ? strategy : new AiStrategy(),
            SecondsElapsed = Fix.FromRatio(state.Tick, state.Config.Simulation.TicksPerSecond),
        };
        foreach (var p in state.Players)
        {
            if (p.Index == Player || p.Defeated) continue;
            var enemyKeep = state.GetKeep(p.Index);
            if (enemyKeep != null && state.KnownBuilding(Player, enemyKeep.Id) != null) ctx.EnemyKeep ??= enemyKeep;
        }
        ctx.EnemyTown = ctx.EnemyKeep?.Center ??
                        new FixVec2(Fix.FromInt(state.Map.Width) - keep.Center.X, Fix.FromInt(state.Map.Height) - keep.Center.Y);
        foreach (var id in state.Players[Player].KnownBuildings.Keys)
            if (state.KnownBuilding(Player, id) is { } known && known.Owner != Player) ctx.KnownEnemyBuildings.Add(known);

        for (int i = 0; i < Resources.Count; i++) ctx.Stock[i] = Economy_TotalAvailable(state, (ResourceType)i);
        foreach (var b in state.Buildings.Values)
        {
            if (b.Owner != Player || b.State == BuildingState.Ruin) continue;
            ctx.BuildingCounts[b.Type] = ctx.Count(b.Type) + 1;
            if (b.NeedsConstruction) ctx.ActiveSites++;
        }
        ctx.Workers = state.WorkerCount(Player);
        ctx.WorkerRoom = state.WorkerRoom(Player);
        foreach (var r in state.Regiments.Values)
        {
            if (r.Owner == Player) ctx.Regiments.Add(r);
            else if (state.CanSee(Player, r)) ctx.EnemyRegiments.Add(r);
        }
        return ctx;
    }

    private int Economy_TotalAvailable(GameState state, ResourceType r) => StockOps.TotalAvailable(state, Player, r);
}
