using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Commands;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Tests;

public class LogisticsTests
{
    [Fact]
    public void Minimum_stock_is_relayed_across_storehouses_that_are_not_direct_neighbours()
    {
        // Keep --20 tiles-- Storehouse B --20 tiles-- Storehouse C. Keep and C are 40 apart (> 24 neighbour radius).
        var state = TestWorld.FlatState(80, 24, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(4, 10));
        keep.Store!.Stock[(int)ResourceType.Wood] = 200;
        var b = TestWorld.AddActive(state, 0, BuildingType.Storehouse, new TileCoord(25, 10));
        var c = TestWorld.AddActive(state, 0, BuildingType.Storehouse, new TileCoord(45, 10));
        c.Store!.MinimumStock[(int)ResourceType.Wood] = 20;

        var match = TestWorld.MatchFor(state);
        match.Run(20 * 240);

        Assert.True(c.Store.Stock[(int)ResourceType.Wood] >= 20, $"C has {c.Store.Stock[(int)ResourceType.Wood]} wood");
        Assert.True(keep.Store.Stock[(int)ResourceType.Wood] < 200);
        Assert.True(b.Store!.Reserved.All(r => r >= 0));
    }
}

public class ConstructionTests
{
    [Fact]
    public void Builders_deliver_materials_and_finish_a_building()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        keep.Store!.Stock[(int)ResourceType.Wood] = 100;
        keep.Store.Stock[(int)ResourceType.Stone] = 100;
        var match = TestWorld.MatchFor(state);
        // Roads make civilians fast enough for a short test.
        match.Commands.Enqueue(new BuildRoadCommand(0, RoadKind.Dirt, Enumerable.Range(10, 10).Select(x => new TileCoord(x, 15)).ToArray()));
        match.Commands.Enqueue(new PlaceBuildingCommand(0, BuildingType.House, new TileCoord(20, 14)));
        match.Run(20 * 120);

        var house = state.Buildings.Values.Single(b => b.Type == BuildingType.House);
        Assert.Equal(BuildingState.Active, house.State);
        Assert.Equal(80, keep.Store.Stock[(int)ResourceType.Wood]);
    }
}

public class FortTests
{
    [Fact]
    public void Regiment_stations_in_outpost_and_is_ejected_when_released()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        TestWorld.AddKeep(state, 0, new TileCoord(4, 4));
        var outpost = TestWorld.AddActive(state, 0, BuildingType.Outpost, new TileCoord(30, 20));
        var regiment = EntityOps.CreateRegiment(state, 0, SoldierType.Archers, new FixVec2(Fix.FromInt(24), Fix.FromInt(21)), FixVec2.North);
        var match = TestWorld.MatchFor(state);

        match.Commands.Enqueue(new StationCommand(0, new[] { regiment.Id }, outpost.Id));
        match.Run(20 * 20);
        Assert.True(regiment.IsStationed);
        Assert.All(regiment.SoldierIds, id => Assert.True(state.Soldiers[id].Stationed));

        match.Commands.Enqueue(new UnstationCommand(0, outpost.Id));
        match.Run(2);
        Assert.False(regiment.IsStationed);
    }

    [Fact]
    public void Stronghold_garrison_shoots_enemies_in_range()
    {
        var state = TestWorld.FlatState(60, 40);
        TestWorld.AddKeep(state, 0, new TileCoord(2, 2));
        TestWorld.AddKeep(state, 1, new TileCoord(54, 34));
        var fort = TestWorld.AddActive(state, 0, BuildingType.Outpost, new TileCoord(30, 20));
        fort.State = BuildingState.Upgrading;
        fort.Fort!.UpgradeCompleteTick = 1;
        var enemy = EntityOps.CreateRegiment(state, 1, SoldierType.Spearmen, new FixVec2(Fix.FromInt(31), Fix.FromInt(28)), FixVec2.North);
        int before = enemy.SoldierIds.Count;

        var match = TestWorld.MatchFor(state);
        match.Run(20 * 60);

        Assert.Equal(BuildingType.Stronghold, fort.Type);
        Assert.True(!state.Regiments.ContainsKey(enemy.Id) || enemy.SoldierIds.Count < before, "Garrison should have killed someone");
    }
}

public class CombatSimulationTests
{
    [Fact]
    public void Melee_regiments_fight_until_one_side_loses_soldiers()
    {
        var state = TestWorld.FlatState(60, 40);
        TestWorld.AddKeep(state, 0, new TileCoord(2, 2));
        TestWorld.AddKeep(state, 1, new TileCoord(54, 34));
        var a = EntityOps.CreateRegiment(state, 0, SoldierType.MenAtArms, new FixVec2(Fix.FromInt(25), Fix.FromInt(20)), new FixVec2(Fix.One, Fix.Zero));
        var b = EntityOps.CreateRegiment(state, 1, SoldierType.Spearmen, new FixVec2(Fix.FromInt(30), Fix.FromInt(20)), new FixVec2(-Fix.One, Fix.Zero));
        var match = TestWorld.MatchFor(state);
        // Fog of war: the target must be in sight to be attacked.
        BannerAndBarrow.Simulation.Territory.PresenceSystem.Recompute(state);
        BannerAndBarrow.Simulation.Territory.VisionSystem.Recompute(state);
        match.Commands.Enqueue(new AttackRegimentCommand(0, new[] { a.Id }, b.Id));
        match.Run(20 * 90);

        int aLeft = state.Regiments.ContainsKey(a.Id) ? a.SoldierIds.Count : 0;
        int bLeft = state.Regiments.ContainsKey(b.Id) ? b.SoldierIds.Count : 0;
        Assert.True(aLeft < 16 || bLeft < 16, "No casualties after 90 seconds of fighting");
        Assert.True(aLeft > bLeft, $"Men-at-Arms ({aLeft}) should beat Spearmen ({bLeft}) head-on");
    }
}

public class MatchTests
{
    [Fact]
    public void Ai_versus_ai_runs_and_builds_an_economy()
    {
        var match = Match.Create(TestWorld.Config(), seed: 4242, aiForBothPlayers: true);
        match.Run(20 * 60 * 4);
        foreach (var p in match.State.Players)
        {
            Assert.True(match.State.Buildings.Values.Count(b => b.Owner == p.Index && b.IsActive) >= 5, $"Player {p.Index} built too little");
            Assert.True(match.State.WorkerCount(p.Index) > 5);
        }
    }

    [Fact]
    public void Same_seed_produces_identical_simulation()
    {
        static long Fingerprint(int ticks)
        {
            var match = Match.Create(TestWorld.Config(), seed: 99, aiForBothPlayers: true);
            match.Run(ticks);
            var s = match.State;
            long hash = s.Tick;
            foreach (var soldier in s.Soldiers.Values) hash = hash * 31 + soldier.Position.X.Raw ^ soldier.Position.Y.Raw;
            foreach (var worker in s.Workers.Values) hash = hash * 31 + worker.Position.X.Raw ^ worker.Position.Y.Raw;
            foreach (var b in s.Buildings.Values) hash = hash * 31 + b.Id + (long)b.Type;
            return hash;
        }

        Assert.Equal(Fingerprint(20 * 90), Fingerprint(20 * 90));
    }
}
