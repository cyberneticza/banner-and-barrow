using BannerAndBarrow.Simulation.AI;
using BannerAndBarrow.Simulation.Commands;
using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Military;
using BannerAndBarrow.Simulation.Territory;
using BannerAndBarrow.Simulation.World;

namespace BannerAndBarrow.Simulation;

/// <summary>
/// Owns a match and advances it one fixed Tick at a time. Has no dependency on rendering, so it runs
/// headless in tests and in the Sandbox tool.
/// </summary>
public sealed class Match
{
    public GameState State { get; }
    public CommandQueue Commands { get; } = new();
    public List<AiController> AiControllers { get; } = new();

    public Match(GameState state)
    {
        State = state;
    }

    /// <summary>Creates a 1v1 match: Player 0 is human unless <paramref name="aiForBothPlayers"/>, Player 1 is the AI.</summary>
    public static Match Create(GameConfig config, int seed, bool aiForBothPlayers = false, string? aiProfile = null)
    {
        var map = MapGenerator.Generate(config, seed);
        var state = new GameState(config, map, playerCount: 2, seed: (ulong)(uint)seed ^ 0xA5A5A5A5UL);
        var sim = new Match(state);

        for (int p = 0; p < 2; p++)
        {
            var player = state.Players[p];
            player.IsAi = p == 1 || aiForBothPlayers;
            if (player.IsAi) player.Name = p == 0 ? "AI West" : "AI";

            var keep = EntityOps.CreateBuilding(state, p, BuildingType.Keep, map.KeepSites[p], underConstruction: false);
            player.KeepId = keep.Id;
            foreach (var (r, amount) in config.Economy.StartingStock) keep.Store!.Stock[(int)r] = amount;

            // A starting Regiment of Spearmen in front of the Keep.
            var towardCenter = (new FixVec2(Fix.FromInt(map.Width / 2), Fix.FromInt(map.Height / 2)) - keep.Center).Normalized();
            var spawn = RegimentSystem.ClampToPassable(state, keep.Center + towardCenter * 6);
            EntityOps.CreateRegiment(state, p, SoldierType.Spearmen, spawn, towardCenter);

            if (player.IsAi) sim.AiControllers.Add(new AiController(p, config.Ai.Get(aiProfile)));
        }

        TerritorySystem.Recompute(state);
        PresenceSystem.Recompute(state);
        VisionSystem.Recompute(state);
        return sim;
    }

    public void Step()
    {
        var state = State;
        if (state.IsOver) return;
        state.Tick++;

        Commands.ApplyAll(state);
        foreach (var ai in AiControllers) ai.Update(state, Commands);

        state.FlowFields.BeginTick(state.Tick);

        TerritorySystem.Update(state);
        PresenceSystem.Update(state);
        VisionSystem.Update(state);

        WorkerSpawnSystem.Update(state);
        LogisticsSystem.Update(state);
        WorkerSystem.Update(state);
        RecruitmentSystem.Update(state);
        UpgradeSystem.Update(state);
        RegrowthSystem.Update(state);

        CombatSystem.RebuildSoldierIndex(state);
        BattleGroupSystem.Update(state);
        RegimentSystem.Update(state);
        CombatSystem.Update(state);
        FortSystem.Update(state);
        FireSystem.Update(state);
        ProjectileSystem.Update(state);

        state.Movement.Step(state.Agents, state.Dt);

        CheckVictory(state);
    }

    public void Run(int ticks)
    {
        for (int i = 0; i < ticks && !State.IsOver; i++) Step();
    }

    private static void CheckVictory(GameState state)
    {
        var alive = state.Players.Where(p => !p.Defeated).ToList();
        if (alive.Count == 1 && state.Players.Length > 1)
        {
            state.Winner = alive[0].Index;
            state.AddEvent(alive[0].Index, GameEventKind.MatchOver, $"{alive[0].Name} wins!");
        }
    }
}
