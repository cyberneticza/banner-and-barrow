using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Commands;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Military;
using BannerAndBarrow.Simulation.Pathfinding;
using BannerAndBarrow.Simulation.Territory;
using BannerAndBarrow.Simulation.World;

namespace BannerAndBarrow.Tests;

public class WorkerCasualtyTests
{
    [Fact]
    public void Soldiers_kill_enemy_workers_and_the_job_is_restaffed()
    {
        var state = TestWorld.FlatState(80, 40);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 16));
        TestWorld.AddKeep(state, 1, new TileCoord(70, 16));
        var farm = TestWorld.AddActive(state, 0, BuildingType.Farm, new TileCoord(18, 16));
        var match = TestWorld.MatchFor(state);
        match.Run(20 * 15);
        var farmer = state.GetWorker(farm.WorkerIds.Single())!;

        var raiders = EntityOps.CreateRegiment(state, 1, SoldierType.MenAtArms, farmer.Position + new FixVec2(Fix.FromInt(2), Fix.Zero), FixVec2.North);
        match.Run(20 * 5);
        Assert.False(state.Workers.ContainsKey(farmer.Id), "Farmer should have been killed");

        RegimentSystem.OrderMove(state, raiders, new FixVec2(Fix.FromInt(60), Fix.FromInt(30)), FixVec2.North, attackMove: false);
        match.Run(20 * 40);
        Assert.Single(farm.WorkerIds);
        Assert.NotEqual(farmer.Id, farm.WorkerIds[0]);
        _ = keep;
    }
}

public class BatchProductionTests
{
    [Fact]
    public void Output_piles_up_at_the_building_until_a_full_batch_is_carried_off()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var farm = TestWorld.AddActive(state, 0, BuildingType.Farm, new TileCoord(18, 10));
        var def = state.Config.Building(BuildingType.Farm);
        var match = TestWorld.MatchFor(state);

        // The Farmer sows Fields, harvests them when ripe, piles the Food at the Farm and carries off full batches.
        bool sawPile = false, sawCrops = false, delivered = false;
        for (int t = 0; t < 20 * 400 && !delivered; t++)
        {
            int before = keep.Store!.Stock[(int)ResourceType.Food];
            int pileBefore = farm.OutputStock[(int)ResourceType.Food];
            match.Step();
            sawCrops |= state.Map.CropRipeTick.Any(c => c != 0);
            int pile = farm.OutputStock[(int)ResourceType.Food];
            if (pile > 0 && pile < def.BatchSize) sawPile = true;
            if (pileBefore == 0 && pile == 0 && keep.Store.Stock[(int)ResourceType.Food] >= before + def.BatchSize) delivered = true;
        }
        Assert.True(sawCrops, "No Fields were sown");
        Assert.True(sawPile, "Food never piled up at the Farm");
        Assert.True(delivered, "A full batch never reached the Keep");
    }

    [Fact]
    public void Buildings_report_missing_workers()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var farm = TestWorld.AddActive(state, 0, BuildingType.Farm, new TileCoord(16, 10));
        Assert.Equal(1, state.MissingWorkers(farm));
        TestWorld.MatchFor(state).Run(20 * 5);
        Assert.Equal(0, state.MissingWorkers(farm));
    }
}

public class DemolitionTests
{
    [Fact]
    public void Builders_demolish_a_building_and_half_its_cost_is_refunded()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var house = TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(16, 11));
        int woodBefore = keep.Store!.Stock[(int)ResourceType.Wood];
        var match = TestWorld.MatchFor(state);

        match.Commands.Enqueue(new DemolishBuildingCommand(0, house.Id));
        match.Run(20 * 60);

        Assert.False(state.Buildings.ContainsKey(house.Id));
        Assert.Equal(woodBefore + state.Config.Building(BuildingType.House).Cost[ResourceType.Wood] / 2, keep.Store.Stock[(int)ResourceType.Wood]);
    }

    [Fact]
    public void Demolishing_twice_cancels()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var house = TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(16, 11));
        var match = TestWorld.MatchFor(state);
        match.Commands.Enqueue(new DemolishBuildingCommand(0, house.Id));
        match.Run(1);
        Assert.True(house.Demolishing);
        match.Commands.Enqueue(new DemolishBuildingCommand(0, house.Id));
        match.Run(20 * 30);
        Assert.False(house.Demolishing);
        Assert.True(state.Buildings.ContainsKey(house.Id));
    }

    [Fact]
    public void Roads_can_be_torn_up()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var tiles = Enumerable.Range(14, 5).Select(x => new TileCoord(x, 15)).ToArray();
        foreach (var t in tiles) state.Map.SetRoad(t, RoadKind.Dirt);
        var match = TestWorld.MatchFor(state);

        match.Commands.Enqueue(new DemolishRoadsCommand(0, tiles));
        match.Run(20 * 60);

        Assert.All(tiles, t => Assert.Equal(RoadKind.None, state.Map.Roads[state.Map.Index(t)]));
        Assert.Empty(state.RoadSites);
    }
}

public class RecruitQueueTests
{
    [Fact]
    public void Cancelling_paid_training_refunds_the_cost()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        foreach (var (r, a) in state.Config.Economy.StartingStock) keep.Store!.Stock[(int)r] = a;
        var barracks = TestWorld.AddActive(state, 0, BuildingType.Barracks, new TileCoord(18, 10));
        var match = TestWorld.MatchFor(state);
        var goldBefore = keep.Store!.Stock[(int)ResourceType.Gold];

        match.Commands.Enqueue(new RecruitCommand(0, barracks.Id, SoldierType.MenAtArms));
        match.Commands.Enqueue(new RecruitCommand(0, barracks.Id, SoldierType.MenAtArms));
        match.Run(3);
        Assert.True(barracks.Recruitment!.Paid);
        Assert.True(keep.Store.Stock[(int)ResourceType.Gold] < goldBefore);

        match.Commands.Enqueue(new CancelRecruitCommand(0, barracks.Id, 0));
        match.Commands.Enqueue(new CancelRecruitCommand(0, barracks.Id, 0));
        match.Run(2);
        Assert.Empty(barracks.Recruitment.Queue);
        Assert.Equal(goldBefore, keep.Store.Stock[(int)ResourceType.Gold]);
    }
}

public class BattleGroupTests
{
    private static (GameState, Dictionary<SoldierType, Regiment>) Army()
    {
        var state = TestWorld.FlatState(120, 80);
        TestWorld.AddKeep(state, 0, new TileCoord(4, 4));
        TestWorld.AddKeep(state, 1, new TileCoord(110, 70));
        var regiments = new Dictionary<SoldierType, Regiment>();
        int x = 20;
        foreach (var type in Enum.GetValues<SoldierType>())
        {
            regiments[type] = EntityOps.CreateRegiment(state, 0, type, new FixVec2(Fix.FromInt(x), Fix.FromInt(40)), FixVec2.North);
            x += 10;
        }
        return (state, regiments);
    }

    [Fact]
    public void Group_move_puts_spears_in_front_then_men_at_arms_archers_and_knights_on_the_flanks()
    {
        var (state, regiments) = Army();
        BattleGroupSystem.OrderGroupMove(state, regiments.Values.ToList(), new FixVec2(Fix.FromInt(50), Fix.FromInt(30)), FixVec2.North, false);
        var group = state.BattleGroups.Values.Single();
        var slots = BattleGroupSystem.Layout(state, group);

        Fix Forward(SoldierType t) => slots[regiments[t].Id].Y;
        Assert.True(Forward(SoldierType.Spearmen) > Forward(SoldierType.MenAtArms));
        Assert.True(Forward(SoldierType.MenAtArms) > Forward(SoldierType.Archers));
        Assert.True(Forward(SoldierType.Archers) > Forward(SoldierType.Longbowmen));
        Assert.Equal(Forward(SoldierType.Spearmen), Forward(SoldierType.Knights));
        Assert.True(Fix.Abs(slots[regiments[SoldierType.Knights].Id].X) > Fix.Abs(slots[regiments[SoldierType.Spearmen].Id].X));
    }

    [Fact]
    public void Men_at_arms_step_forward_against_infantry_and_spears_against_knights()
    {
        var (state, regiments) = Army();
        var match = TestWorld.MatchFor(state);
        BattleGroupSystem.OrderGroupMove(state, regiments.Values.ToList(), new FixVec2(Fix.FromInt(50), Fix.FromInt(40)), FixVec2.North, false);
        var group = state.BattleGroups.Values.Single();
        Assert.Equal(SoldierType.Spearmen, group.FrontType);

        var enemyFoot = EntityOps.CreateRegiment(state, 1, SoldierType.MenAtArms, new FixVec2(Fix.FromInt(50), Fix.FromInt(22)), FixVec2.North);
        match.Run(20 * 2);
        Assert.Equal(SoldierType.MenAtArms, group.FrontType);

        EntityOps.RemoveRegiment(state, enemyFoot);
        EntityOps.CreateRegiment(state, 1, SoldierType.Knights, new FixVec2(Fix.FromInt(50), Fix.FromInt(22)), FixVec2.North);
        match.Run(20 * 2);
        Assert.Equal(SoldierType.Spearmen, group.FrontType);
    }

    [Fact]
    public void Individual_orders_take_a_regiment_out_of_its_group()
    {
        var (state, regiments) = Army();
        var match = TestWorld.MatchFor(state);
        match.Commands.Enqueue(new MoveRegimentsCommand(0, regiments.Values.Select(r => r.Id).ToArray(), new FixVec2(Fix.FromInt(50), Fix.FromInt(30)), FixVec2.North, false));
        match.Run(1);
        Assert.All(regiments.Values, r => Assert.NotEqual(0, r.GroupId));

        match.Commands.Enqueue(new StopCommand(0, new[] { regiments[SoldierType.Knights].Id }));
        match.Run(1);
        Assert.Equal(0, regiments[SoldierType.Knights].GroupId);
        Assert.NotEqual(0, regiments[SoldierType.Archers].GroupId);
    }
}

public class MergeAndKiteTests
{
    [Fact]
    public void Squads_of_the_same_type_merge_up_to_the_cap()
    {
        var state = TestWorld.FlatState(80, 40, players: 1);
        var squads = Enumerable.Range(0, 5)
            .Select(i => EntityOps.CreateRegiment(state, 0, SoldierType.Spearmen, new FixVec2(Fix.FromInt(10 + i * 4), Fix.FromInt(20)), FixVec2.North))
            .ToList();
        var archers = EntityOps.CreateRegiment(state, 0, SoldierType.Archers, new FixVec2(Fix.FromInt(40), Fix.FromInt(20)), FixVec2.North);
        var match = TestWorld.MatchFor(state);

        match.Commands.Enqueue(new MergeRegimentsCommand(0, squads.Select(r => r.Id).Append(archers.Id).ToArray()));
        match.Run(1);

        var spearRegiments = state.Regiments.Values.Where(r => r.Type == SoldierType.Spearmen).OrderByDescending(r => r.SoldierIds.Count).ToList();
        Assert.Equal(2, spearRegiments.Count); // 30 Spearmen with a cap of 24
        Assert.Equal(24, spearRegiments[0].SoldierIds.Count);
        Assert.Equal(6, spearRegiments[1].SoldierIds.Count);
        Assert.All(spearRegiments[0].SoldierIds, id => Assert.Equal(spearRegiments[0].Id, state.Soldiers[id].RegimentId));
        Assert.True(state.Regiments.ContainsKey(archers.Id), "Different types never merge");
    }

    [Fact]
    public void Archers_fall_back_from_approaching_infantry()
    {
        var state = TestWorld.FlatState(80, 40);
        var archers = EntityOps.CreateRegiment(state, 0, SoldierType.Archers, new FixVec2(Fix.FromInt(40), Fix.FromInt(20)), new FixVec2(-Fix.One, Fix.Zero));
        var enemy = EntityOps.CreateRegiment(state, 1, SoldierType.MenAtArms, new FixVec2(Fix.FromInt(25), Fix.FromInt(20)), new FixVec2(Fix.One, Fix.Zero));
        var match = TestWorld.MatchFor(state);
        RegimentSystem.OrderAttackRegiment(state, enemy, archers);
        var start = state.RegimentCentroid(archers);

        bool kited = false, shot = false;
        for (int t = 0; t < 20 * 20 && state.Regiments.ContainsKey(archers.Id); t++)
        {
            match.Step();
            if (state.RegimentCentroid(archers).X > start.X + Fix.FromInt(3)) kited = true;
            shot |= state.Projectiles.Count > 0 || enemy.SoldierIds.Count < enemy.StartingSize;
        }
        Assert.True(kited, "Archers never stepped back from the Men-at-Arms");
        Assert.True(shot, "Archers should still be shooting between fall-backs");
    }
}

public class AiEscalationTests
{
    private static BannerAndBarrow.Simulation.AI.AiContext Context(GameState state, int seconds, Fix provocation)
    {
        var profile = TestWorld.Config().Ai.Get("Normal");
        var memory = new BannerAndBarrow.Simulation.AI.AiMemory { Provocation = provocation };
        state.Tick = seconds * state.Config.Simulation.TicksPerSecond;
        return new BannerAndBarrow.Simulation.AI.AiContext
        {
            State = state, Player = 1, Profile = profile, Commands = new CommandQueue(), Memory = memory,
            SecondsElapsed = Fix.FromInt(seconds), Strategy = new BannerAndBarrow.Simulation.Config.AiStrategy(),
        };
    }

    [Fact]
    public void Attacks_grow_with_time_and_faster_when_provoked()
    {
        var state = TestWorld.FlatState();
        int early = BannerAndBarrow.Simulation.AI.ScriptedMilitaryPolicy.TargetWaveSoldiers(Context(state, 180, Fix.Zero));
        int later = BannerAndBarrow.Simulation.AI.ScriptedMilitaryPolicy.TargetWaveSoldiers(Context(state, 900, Fix.Zero));
        int provoked = BannerAndBarrow.Simulation.AI.ScriptedMilitaryPolicy.TargetWaveSoldiers(Context(state, 900, Fix.One));
        Assert.True(early < later, $"early {early} vs later {later}");
        Assert.True(later < provoked, $"later {later} vs provoked {provoked}");
        Assert.True(early <= 18, "The first attacks should be small raids");

        var shortGap = BannerAndBarrow.Simulation.AI.ScriptedMilitaryPolicy.WaveIntervalSeconds(Context(state, 1800, Fix.Zero));
        var longGap = BannerAndBarrow.Simulation.AI.ScriptedMilitaryPolicy.WaveIntervalSeconds(Context(state, 180, Fix.Zero));
        Assert.True(shortGap < longGap);
    }

    [Fact]
    public void Ai_does_not_attack_during_the_peace_window_and_picks_a_strategy()
    {
        var match = Match.Create(TestWorld.Config(), seed: 4242, aiForBothPlayers: true, aiProfile: "Normal");
        match.Run(20 * 115);
        var events = match.State.Events.Select(e => e.Message).ToList();
        Assert.Contains(events, m => m.StartsWith("AI strategy:"));
        Assert.DoesNotContain(events, m => m.StartsWith("AI raids") || m.StartsWith("AI attack wave"));
    }
}

public class StanceTests
{
    [Fact]
    public void Archers_holding_ground_outrange_and_outpace_archers_on_the_move()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var archers = EntityOps.CreateRegiment(state, 0, SoldierType.Archers, new FixVec2(Fix.FromInt(20), Fix.FromInt(20)), FixVec2.North);
        var soldier = state.Soldiers[archers.SoldierIds[0]];
        var bow = state.Config.Soldier(SoldierType.Archers).Ranged!;

        // Set and holding ground: longest range, fastest volleys.
        archers.AnchorArrived = true;
        archers.HoldGround = true;
        var steadyRange = CombatSystem.RangeOf(state, soldier, bow, archers);
        var steadyCooldown = CombatSystem.RangedCooldownTicks(state, bow, archers);

        // Just moved: range is cut until they settle again.
        archers.AnchorArrived = false;
        archers.LastMovedTick = state.Tick;
        var movingRange = CombatSystem.RangeOf(state, soldier, bow, archers);
        var movingCooldown = CombatSystem.RangedCooldownTicks(state, bow, archers);

        var settleTicks = state.Config.SecondsToTicks(state.Config.Combat.Stance.SettleSeconds);
        archers.LastMovedTick = state.Tick - settleTicks / 2;
        var halfSettledRange = CombatSystem.RangeOf(state, soldier, bow, archers);
        archers.LastMovedTick = state.Tick - settleTicks;
        var settledRange = CombatSystem.RangeOf(state, soldier, bow, archers);

        Assert.True(steadyRange > bow.Range && movingRange < bow.Range);
        Assert.True(movingRange < halfSettledRange && halfSettledRange < settledRange, "Range should come back as they stand still");
        // Settled but not holding ground: plain range. Holding ground adds its bonus on top.
        Assert.Equal(bow.Range, settledRange);
        Assert.Equal(bow.Range * state.Config.Combat.Stance.HoldRangeMultiplier, steadyRange);
        Assert.Equal(state.Config.Combat.Stance.MovedRangePenaltyTiles, settledRange - movingRange);
        Assert.True(steadyCooldown < movingCooldown);
        Assert.True(steadyCooldown <= 10, "Holding archers should loose a volley about every half second");

        var longbow = state.Config.Soldier(SoldierType.Longbowmen).Ranged!;
        Assert.True(longbow.Range > bow.Range && longbow.Damage > bow.Damage && longbow.Penetration > bow.Penetration);
    }
}

public class ManufacturingTests
{
    private static (GameState state, Building keep) Town()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        Array.Clear(keep.Store!.Stock);
        return (state, keep);
    }

    private static int RecipeFor(GameState state, Building workshop, ResourceType output) =>
        state.Config.Building(workshop.Type).Recipes.FindIndex(r => r.Output == output);

    [Fact]
    public void Workshop_makes_to_stock_up_to_the_target_then_idles()
    {
        var (state, keep) = Town();
        var smithy = TestWorld.AddActive(state, 0, BuildingType.Smithy, new TileCoord(18, 10));
        int target = state.Config.Economy.GoodsStockTarget;
        keep.Store!.Stock[(int)ResourceType.Swords] = target;
        keep.Store.Stock[(int)ResourceType.Armour] = target;
        keep.Store.Stock[(int)ResourceType.Pikes] = 3;
        Assert.Equal(RecipeFor(state, smithy, ResourceType.Pikes), Manufacturing.ChooseRecipe(state, smithy));

        keep.Store.Stock[(int)ResourceType.Pikes] = target;
        Assert.Equal(-1, Manufacturing.ChooseRecipe(state, smithy));
    }

    [Fact]
    public void Orders_come_before_stock()
    {
        var (state, keep) = Town();
        var smithy = TestWorld.AddActive(state, 0, BuildingType.Smithy, new TileCoord(18, 10));
        var barracks = TestWorld.AddActive(state, 0, BuildingType.Barracks, new TileCoord(24, 10));
        keep.Store!.Stock[(int)ResourceType.Swords] = 4; // well stocked by the stock rule's standards: Pikes are lowest
        barracks.Recruitment!.Queue.Add(SoldierType.MenAtArms);

        var orders = Manufacturing.Orders(state, 0);
        Assert.True(orders[(int)ResourceType.Swords] > 0);
        // Armour is ordered with nothing on hand: the biggest shortfall wins over the empty (unordered) Pikes.
        Assert.Equal(RecipeFor(state, smithy, ResourceType.Armour), Manufacturing.ChooseRecipe(state, smithy));

        keep.Store.Stock[(int)ResourceType.Armour] = orders[(int)ResourceType.Armour];
        Assert.Equal(RecipeFor(state, smithy, ResourceType.Swords), Manufacturing.ChooseRecipe(state, smithy));

        // Orders covered: the surplus stock rule tops up whatever has the least beyond what is ordered.
        keep.Store.Stock[(int)ResourceType.Swords] = orders[(int)ResourceType.Swords] + 5;
        keep.Store.Stock[(int)ResourceType.Armour] = orders[(int)ResourceType.Armour] + 5;
        Assert.Equal(RecipeFor(state, smithy, ResourceType.Pikes), Manufacturing.ChooseRecipe(state, smithy));
    }

    [Fact]
    public void Smithy_worker_produces_goods_and_stops_at_the_stock_target()
    {
        var (state, keep) = Town();
        keep.Store!.Stock[(int)ResourceType.Iron] = 150;
        keep.Store.Stock[(int)ResourceType.Wood] = 60;
        keep.Store.Stock[(int)ResourceType.Food] = 50;
        TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(10, 16));
        var smithy = TestWorld.AddActive(state, 0, BuildingType.Smithy, new TileCoord(15, 10));
        var match = TestWorld.MatchFor(state);
        match.Run(20 * 60 * 6);

        int target = state.Config.Economy.GoodsStockTarget;
        var pipeline = Manufacturing.Pipeline(state, 0);
        foreach (var r in new[] { ResourceType.Swords, ResourceType.Pikes, ResourceType.Armour })
        {
            Assert.True(pipeline[(int)r] >= target, $"{r} {pipeline[(int)r]}");
            Assert.True(pipeline[(int)r] < target + 2, $"{r} {pipeline[(int)r]}");
        }
        Assert.Equal(0, Manufacturing.Pipeline(state, 0)[(int)ResourceType.Bows]);
    }

    [Fact]
    public void Soldiers_are_recruited_at_their_own_building()
    {
        var (state, _) = Town();
        var barracks = TestWorld.AddActive(state, 0, BuildingType.Barracks, new TileCoord(18, 10));
        var archery = TestWorld.AddActive(state, 0, BuildingType.Archery, new TileCoord(24, 10));
        Assert.False(CommandProcessor.Apply(state, new RecruitCommand(0, barracks.Id, SoldierType.Archers)).Ok);
        Assert.True(CommandProcessor.Apply(state, new RecruitCommand(0, archery.Id, SoldierType.Archers)).Ok);
        Assert.True(CommandProcessor.Apply(state, new RecruitCommand(0, barracks.Id, SoldierType.Pikemen)).Ok);
        Assert.DoesNotContain(state.Config.Soldier(SoldierType.Spearmen).RecruitCost.Keys, Resources.IsGoods);
    }

    [Fact]
    public void Spearmen_upgrade_to_pikemen_next_to_a_barracks_and_wait_for_goods()
    {
        var (state, keep) = Town();
        TestWorld.AddActive(state, 0, BuildingType.Barracks, new TileCoord(18, 10));
        var near = EntityOps.CreateRegiment(state, 0, SoldierType.Spearmen, new FixVec2(Fix.FromInt(20), Fix.FromInt(18)), FixVec2.North);
        var far = EntityOps.CreateRegiment(state, 0, SoldierType.Spearmen, new FixVec2(Fix.FromInt(55), Fix.FromInt(36)), FixVec2.North);
        int n = near.SoldierIds.Count;

        Assert.False(CommandProcessor.Apply(state, new UpgradeRegimentsCommand(0, new[] { far.Id })).Ok);

        // No goods yet: the upgrade is ordered and shows up as demand for Pikes and Armour.
        Assert.True(CommandProcessor.Apply(state, new UpgradeRegimentsCommand(0, new[] { near.Id })).Ok);
        Assert.Contains(near.Id, state.Players[0].PendingUpgrades);
        Assert.Equal(n, Manufacturing.Orders(state, 0)[(int)ResourceType.Pikes]);
        Assert.Equal(SoldierType.Spearmen, near.Type);

        keep.Store!.Stock[(int)ResourceType.Pikes] = n;
        keep.Store.Stock[(int)ResourceType.Armour] = n + 2;
        TestWorld.MatchFor(state).Run(25);
        Assert.Equal(SoldierType.Pikemen, near.Type);
        Assert.All(near.SoldierIds, id => Assert.Equal(SoldierType.Pikemen, state.GetSoldier(id)!.Type));
        Assert.Empty(state.Players[0].PendingUpgrades);
        Assert.Equal(0, keep.Store.Stock[(int)ResourceType.Pikes]);
        Assert.Equal(2, keep.Store.Stock[(int)ResourceType.Armour]);
    }
}

public class FogOfWarTests
{
    private static (GameState state, Building keep0, Building keep1) TwoTowns()
    {
        var state = TestWorld.FlatState(120, 40);
        var keep0 = TestWorld.AddKeep(state, 0, new TileCoord(8, 16));
        var keep1 = TestWorld.AddKeep(state, 1, new TileCoord(108, 16));
        PresenceSystem.Recompute(state);
        VisionSystem.Recompute(state);
        return (state, keep0, keep1);
    }

    private static void Refresh(GameState state)
    {
        TerritorySystem.Recompute(state);
        PresenceSystem.Recompute(state);
        VisionSystem.Recompute(state);
    }

    [Fact]
    public void Players_see_their_territory_and_presence_only()
    {
        var (state, keep0, keep1) = TwoTowns();
        Assert.True(state.CanSee(0, keep0.CenterTile));
        Assert.False(state.CanSee(0, keep1.CenterTile));
        Assert.False(state.CanSee(0, keep1));
        Assert.DoesNotContain(keep1.Id, state.Players[0].KnownBuildings.Keys);

        var enemy = EntityOps.CreateRegiment(state, 1, SoldierType.Spearmen, new FixVec2(Fix.FromInt(60), Fix.FromInt(20)), FixVec2.North);
        Refresh(state);
        Assert.False(state.CanSee(0, enemy));
        Assert.False(CommandProcessor.Apply(state, new AttackBuildingCommand(0, new int[0], keep1.Id)).Ok);

        EntityOps.CreateRegiment(state, 0, SoldierType.Spearmen, new FixVec2(Fix.FromInt(62), Fix.FromInt(20)), FixVec2.North);
        Refresh(state);
        Assert.True(state.CanSee(0, enemy));
        Assert.True(state.CanSee(1, state.Regiments.Values.First(r => r.Owner == 0 && r.Id != enemy.Id)));
    }

    [Fact]
    public void Enemy_buildings_are_remembered_as_last_seen_until_the_spot_is_seen_again()
    {
        var (state, _, _) = TwoTowns();
        var farm = TestWorld.AddActive(state, 1, BuildingType.Farm, new TileCoord(96, 20));
        var scout = EntityOps.CreateRegiment(state, 0, SoldierType.Spearmen, new FixVec2(Fix.FromInt(94), Fix.FromInt(22)), FixVec2.North);
        Refresh(state);
        Assert.Contains(farm.Id, state.Players[0].KnownBuildings.Keys);
        Assert.NotNull(state.KnownBuilding(0, farm.Id));

        // Scout leaves; the farm is destroyed out of sight: still remembered.
        foreach (var id in scout.SoldierIds) state.GetSoldier(id)!.Position = new FixVec2(Fix.FromInt(30), Fix.FromInt(20));
        Refresh(state);
        EntityOps.DestroyBuilding(state, farm, allowRuin: false);
        Refresh(state);
        Assert.Contains(farm.Id, state.Players[0].KnownBuildings.Keys);

        // Seeing the spot again clears the memory.
        foreach (var id in scout.SoldierIds) state.GetSoldier(id)!.Position = new FixVec2(Fix.FromInt(94), Fix.FromInt(22));
        Refresh(state);
        Assert.DoesNotContain(farm.Id, state.Players[0].KnownBuildings.Keys);
    }

    [Fact]
    public void The_AI_starts_without_knowing_where_the_enemy_keep_is()
    {
        var config = TestWorld.Config();
        var match = Match.Create(config, seed: 7, aiForBothPlayers: true);
        match.Run(20);
        var state = match.State;
        var keep0 = state.GetKeep(0)!;
        Assert.DoesNotContain(keep0.Id, state.Players[1].KnownBuildings.Keys);
        Assert.False(state.CanSee(1, keep0));
        Assert.Contains(state.Events, e => e.Player == 1 && e.Message.Contains("scout"));
    }
}


public class WorkerNeedsTests
{
    [Fact]
    public void New_workers_cost_food_and_none_arrive_without_it()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        keep.Store!.Stock[(int)ResourceType.Food] = 2;
        TestWorld.AddActive(state, 0, BuildingType.Farm, new TileCoord(18, 10));
        TestWorld.MatchFor(state).Run(20 * 10);
        Assert.Equal(2, state.WorkerCount(0));
        Assert.Equal(0, keep.Store.Stock[(int)ResourceType.Food]);
    }

    [Fact]
    public void Hungry_workers_walk_to_a_storehouse_and_eat()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var worker = EntityOps.SpawnWorker(state, 0, new FixVec2(Fix.FromInt(30), Fix.FromInt(20)));
        worker.HungryAtTick = state.Tick;
        int food = keep.Store!.Stock[(int)ResourceType.Food];
        Assert.True(worker.IsHungry(state.Tick));

        var match = TestWorld.MatchFor(state);
        match.Run(20 * 30);
        Assert.False(worker.IsHungry(state.Tick));
        // Other Workers arriving for jobs cost Food too.
        int arrivals = (state.WorkerCount(0) - 1) * state.Config.Economy.WorkerSpawnFood;
        Assert.Equal(food - state.Config.Economy.MealFood - arrivals, keep.Store.Stock[(int)ResourceType.Food]);
        Assert.Equal(0, keep.Store.Reserved[(int)ResourceType.Food]);
    }

    [Fact]
    public void Without_food_workers_stay_hungry()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        keep.Store!.Stock[(int)ResourceType.Food] = 0;
        var worker = EntityOps.SpawnWorker(state, 0, new FixVec2(Fix.FromInt(20), Fix.FromInt(20)));
        worker.HungryAtTick = state.Tick;
        TestWorld.MatchFor(state).Run(20 * 20);
        Assert.True(worker.IsHungry(state.Tick));
        Assert.Equal(1, state.HungryWorkers(0));
    }
}


public class RallyPointTests
{
    [Fact]
    public void New_regiments_walk_to_the_rally_point()
    {
        var state = TestWorld.FlatState(60, 40, players: 1);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        foreach (var (r, a) in state.Config.Economy.StartingStock) keep.Store!.Stock[(int)r] = a;
        var barracks = TestWorld.AddActive(state, 0, BuildingType.Barracks, new TileCoord(18, 10));
        var rally = new FixVec2(Fix.FromInt(40), Fix.FromInt(30));
        var match = TestWorld.MatchFor(state);

        Assert.False(CommandProcessor.Apply(state, new SetRallyPointCommand(0, keep.Id, rally)).Ok);
        match.Commands.Enqueue(new SetRallyPointCommand(0, barracks.Id, rally));
        match.Commands.Enqueue(new RecruitCommand(0, barracks.Id, SoldierType.Spearmen));
        match.Run(20 * 50);

        var regiment = Assert.Single(state.Regiments.Values);
        Assert.True(FixVec2.Distance(state.RegimentCentroid(regiment), rally) < Fix.FromInt(4), $"Regiment at {state.RegimentCentroid(regiment)}");

        match.Commands.Enqueue(new SetRallyPointCommand(0, barracks.Id, null));
        match.Run(1);
        Assert.Null(barracks.RallyPoint);
    }
}

public class FireTests
{
    [Fact]
    public void Small_fires_die_down_once_the_attack_stops()
    {
        var state = TestWorld.FlatState(60, 40, players: 2);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        keep.Store!.Stock[(int)ResourceType.Food] = 0; // no Workers arrive, so nobody repairs or douses
        var house = TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(20, 10));
        FireSystem.Ignite(state, house, Fix.FromDecimal(0.3m));
        house.LastAttackedTick = state.Tick;
        var match = TestWorld.MatchFor(state);
        match.Run(20 * 30);
        Assert.Equal(Fix.Zero, house.Fire);
        Assert.True(house.Hp < house.MaxHp);
        Assert.True(state.Buildings.ContainsKey(house.Id));
    }

    [Fact]
    public void Big_fires_burn_the_building_down()
    {
        var state = TestWorld.FlatState(60, 40, players: 2);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        keep.Store!.Stock[(int)ResourceType.Food] = 0; // no Workers arrive, so nobody repairs or douses
        var house = TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(20, 10));
        FireSystem.Ignite(state, house, state.Config.Combat.FireSelfSustaining);
        TestWorld.MatchFor(state).Run(20 * 150);
        Assert.False(state.Buildings.ContainsKey(house.Id));
    }

    [Fact]
    public void Melee_attackers_set_buildings_alight()
    {
        var state = TestWorld.FlatState(60, 40, players: 2);
        TestWorld.AddKeep(state, 0, new TileCoord(4, 4));
        TestWorld.AddKeep(state, 1, new TileCoord(50, 30));
        var house = TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(20, 10));
        var attackers = EntityOps.CreateRegiment(state, 1, SoldierType.MenAtArms, new FixVec2(Fix.FromInt(24), Fix.FromInt(14)), FixVec2.North);
        var match = TestWorld.MatchFor(state);
        PresenceSystem.Recompute(state);
        VisionSystem.Recompute(state);
        match.Commands.Enqueue(new AttackBuildingCommand(1, new[] { attackers.Id }, house.Id));
        match.Run(20 * 8);
        Assert.True(house.Fire > Fix.Zero || !state.Buildings.ContainsKey(house.Id));
    }
}

public class ConstructionReliabilityTests
{
    private static (GameState state, Building keep, Match match) Town(int players = 1)
    {
        var state = TestWorld.FlatState(60, 40, players);
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        foreach (var (r, a) in state.Config.Economy.StartingStock) keep.Store!.Stock[(int)r] = Math.Max(a, 100);
        return (state, keep, TestWorld.MatchFor(state));
    }

    private static int BuildersSwingingAt(GameState state, int siteId) =>
        state.Workers.Values.Count(w => w.Task == WorkerTask.Build && w.TargetSiteId == siteId);

    [Fact]
    public void Cancelled_sites_release_their_builders()
    {
        var (state, _, match) = Town();
        match.Commands.Enqueue(new PlaceBuildingCommand(0, BuildingType.House, new TileCoord(18, 12)));
        match.Run(2);
        var site = state.Buildings.Values.Single(b => b.Type == BuildingType.House);
        for (int i = 0; i < 20 * 90 && BuildersSwingingAt(state, site.Id) == 0; i++) match.Step();
        Assert.True(BuildersSwingingAt(state, site.Id) > 0, "Nobody started building");

        match.Commands.Enqueue(new CancelConstructionCommand(0, site.Id));
        match.Run(3);
        Assert.DoesNotContain(state.Workers.Values, w => w.TargetSiteId == site.Id);
        Assert.DoesNotContain(state.Workers.Values, w => w.Task == WorkerTask.Build);
    }

    [Fact]
    public void Cancelling_a_demolition_releases_its_builders()
    {
        var (state, _, match) = Town();
        var house = TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(18, 12));
        match.Commands.Enqueue(new DemolishBuildingCommand(0, house.Id));
        for (int i = 0; i < 20 * 60 && BuildersSwingingAt(state, house.Id) == 0; i++) match.Step();
        Assert.True(BuildersSwingingAt(state, house.Id) > 0, "Nobody started demolishing");

        match.Commands.Enqueue(new DemolishBuildingCommand(0, house.Id));
        match.Run(3);
        Assert.DoesNotContain(state.Workers.Values, w => w.Task == WorkerTask.Build);
    }

    [Fact]
    public void Builders_do_not_hammer_at_a_site_paused_by_enemy_presence()
    {
        var (state, _, match) = Town(players: 2);
        TestWorld.AddKeep(state, 1, new TileCoord(50, 30));
        match.Commands.Enqueue(new PlaceBuildingCommand(0, BuildingType.House, new TileCoord(20, 12)));
        match.Commands.Enqueue(new PlaceBuildingCommand(0, BuildingType.Woodcutter, new TileCoord(14, 20)));
        match.Run(2);
        var house = state.Buildings.Values.Single(b => b.Type == BuildingType.House);
        for (int i = 0; i < 20 * 90 && BuildersSwingingAt(state, house.Id) == 0; i++) match.Step();

        EntityOps.CreateRegiment(state, 1, SoldierType.Spearmen, new FixVec2(Fix.FromInt(21), Fix.FromInt(9)), FixVec2.North);
        match.Run(20 * 10);
        Assert.Equal(0, BuildersSwingingAt(state, house.Id));
    }

    [Fact]
    public void Builders_repair_damaged_buildings_and_put_out_fires()
    {
        var (state, _, match) = Town();
        var house = TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(18, 12));
        house.Hp = house.MaxHp / 3;
        house.Fire = Fix.FromDecimal(0.2m);
        house.LastAttackedTick = state.Tick;
        match.Run(20 * 90);
        Assert.Equal(house.MaxHp, house.Hp);
        Assert.Equal(Fix.Zero, house.Fire);
        Assert.DoesNotContain(state.Workers.Values, w => w.Task is WorkerTask.Repair or WorkerTask.GoToRepair);
    }

    [Fact]
    public void Buildings_need_one_reachable_edge()
    {
        var (state, _, _) = Town();
        // Wall a pocket off with water.
        for (int y = 24; y <= 32; y++)
        for (int x = 34; x <= 42; x++)
            if (x is 34 or 42 || y is 24 or 32) state.Map.Terrain[state.Map.Index(x, y)] = Terrain.Water;
        state.Map.Touch();
        TerritorySystem.Recompute(state);
        foreach (var i in Enumerable.Range(0, state.Territory.Owner.Length)) state.Territory.Owner[i] = 0;

        Assert.False(BuildRules.CanPlaceBuilding(state, 0, BuildingType.House, new TileCoord(37, 27)).Ok);
        Assert.True(BuildRules.CanPlaceBuilding(state, 0, BuildingType.House, new TileCoord(24, 24)).Ok);
        // Hemmed in by buildings on every side except one: still fine.
        Assert.True(BuildRules.CanPlaceBuilding(state, 0, BuildingType.House, new TileCoord(18, 12)).Ok);
    }
}

public class SightTests
{
    [Fact]
    public void Archers_see_further_than_they_shoot()
    {
        var state = TestWorld.FlatState(120, 60);
        var cfg = state.Config;
        foreach (var type in new[] { SoldierType.Archers, SoldierType.Longbowmen })
        {
            var ranged = cfg.Soldier(type).Ranged!;
            var longest = ranged.Range * cfg.Combat.Stance.HoldRangeMultiplier * cfg.Combat.HillsRangeMultiplier;
            Assert.True(Fix.FromInt(VisionSystem.VisionRadius(state, type)) > longest, $"{type} sees less than it can shoot");
        }
        Assert.Equal(cfg.Territory.RegimentPresence, VisionSystem.VisionRadius(state, SoldierType.MenAtArms));
    }

    [Fact]
    public void Enemies_that_shoot_at_you_are_revealed()
    {
        var state = TestWorld.FlatState(120, 60);
        TestWorld.AddKeep(state, 0, new TileCoord(4, 4));
        TestWorld.AddKeep(state, 1, new TileCoord(110, 50));
        var victims = EntityOps.CreateRegiment(state, 0, SoldierType.Spearmen, new FixVec2(Fix.FromInt(40), Fix.FromInt(30)), FixVec2.North);
        // Longbowmen well outside the Spearmen's sight, but in range.
        var archers = EntityOps.CreateRegiment(state, 1, SoldierType.Longbowmen, new FixVec2(Fix.FromInt(57), Fix.FromInt(30)), new FixVec2(-Fix.One, Fix.Zero));
        var match = TestWorld.MatchFor(state);
        PresenceSystem.Recompute(state);
        VisionSystem.Recompute(state);
        Assert.False(state.CanSee(0, archers));

        bool revealed = false;
        for (int i = 0; i < 20 * 20 && !revealed; i++)
        {
            match.Step();
            revealed = state.IsRevealed(0, archers.Id);
        }
        Assert.True(revealed, "Being shot at never revealed the archers");
        Assert.True(state.CanSee(0, archers));
        Assert.True(victims.SoldierIds.Count > 0);
    }
}

public class WallTests
{
    private static (GameState state, Building wall) WalledState(BuildingType type = BuildingType.Wall)
    {
        var state = TestWorld.FlatState(60, 40);
        TestWorld.AddKeep(state, 0, new TileCoord(6, 16));
        TestWorld.AddKeep(state, 1, new TileCoord(50, 16));
        foreach (var i in Enumerable.Range(0, state.Territory.Owner.Length)) state.Territory.Owner[i] = 0;
        Building? piece = null;
        for (int y = 0; y < 40; y++)
            piece = TestWorld.AddActive(state, 0, y == 20 ? type : BuildingType.Wall, new TileCoord(30, y));
        return (state, state.Buildings.Values.First(b => b.Type == type && b.Origin.Y == 20));
    }

    [Fact]
    public void Walls_block_everyone_and_gates_open_only_for_their_owner()
    {
        var (state, gate) = WalledState(BuildingType.Gate);
        var map = state.Map;
        Assert.False(map.IsPassable(30, 10));                 // plain wall
        Assert.False(map.IsPassableFor(30, 10, 0));
        Assert.False(map.IsPassable(30, 20));                 // the gate tile is occupied ...
        Assert.True(map.IsPassableFor(30, 20, gate.Owner));   // ... but open to its owner
        Assert.False(map.IsPassableFor(30, 20, 1));

        // The same shows up in pathing: Player 0 can reach the far side, Player 1 cannot.
        var goal = new[] { new TileCoord(40, 20) };
        var mine = FlowFieldBuilder.Build(map, MovementClass.Military, goal, player: 0);
        var theirs = FlowFieldBuilder.Build(map, MovementClass.Military, goal, player: 1);
        Assert.True(mine.IsReachable(20, 20));
        Assert.False(theirs.IsReachable(20, 20));
    }

    [Fact]
    public void Walls_go_up_through_forest_but_not_over_water()
    {
        var state = TestWorld.FlatState(40, 30, players: 1);
        TestWorld.AddKeep(state, 0, new TileCoord(4, 4));
        foreach (var i in Enumerable.Range(0, state.Territory.Owner.Length)) state.Territory.Owner[i] = 0;
        state.Map.Terrain[state.Map.Index(20, 10)] = Terrain.Forest;
        state.Map.Terrain[state.Map.Index(21, 10)] = Terrain.Water;
        state.Map.Terrain[state.Map.Index(22, 10)] = Terrain.Mountain;

        Assert.True(BuildRules.CanPlaceBuilding(state, 0, BuildingType.Wall, new TileCoord(20, 10)).Ok);
        Assert.False(BuildRules.CanPlaceBuilding(state, 0, BuildingType.Wall, new TileCoord(21, 10)).Ok);
        Assert.False(BuildRules.CanPlaceBuilding(state, 0, BuildingType.Wall, new TileCoord(22, 10)).Ok);
        // A House still cannot go on forest.
        Assert.False(BuildRules.CanPlaceBuilding(state, 0, BuildingType.House, new TileCoord(20, 10)).Ok);

        CommandProcessor.Apply(state, new PlaceWallsCommand(0, BuildingType.Wall, new[] { new TileCoord(20, 10) }));
        EntityOps.CompleteConstruction(state, state.Buildings.Values.First(b => b.Type == BuildingType.Wall));
        Assert.Equal(Terrain.Grass, state.Map.TerrainAt(new TileCoord(20, 10)));
    }

    [Fact]
    public void Shooting_over_someone_elses_wall_costs_range_and_accuracy()
    {
        var (state, _) = WalledState();
        var mine = new FixVec2(Fix.FromInt(25), Fix.FromInt(20));
        var theirs = new FixVec2(Fix.FromInt(35), Fix.FromInt(20));
        // Player 1 shooting across Player 0's wall is hindered; Player 0 shooting the same line is not.
        Assert.True(CombatSystem.EnemyWallBetween(state, 1, theirs, mine));
        Assert.False(CombatSystem.EnemyWallBetween(state, 0, mine, theirs));
        // Nothing in the way along the wall itself.
        Assert.False(CombatSystem.EnemyWallBetween(state, 1, new FixVec2(Fix.FromInt(35), Fix.FromInt(10)), theirs));
    }

    [Fact]
    public void Arrows_cannot_damage_buildings_but_melee_can()
    {
        var state = TestWorld.FlatState(60, 40);
        TestWorld.AddKeep(state, 0, new TileCoord(4, 4));
        TestWorld.AddKeep(state, 1, new TileCoord(50, 30));
        var house = TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(20, 20));
        var archers = EntityOps.CreateRegiment(state, 1, SoldierType.Longbowmen, new FixVec2(Fix.FromInt(26), Fix.FromInt(21)), FixVec2.North);
        var match = TestWorld.MatchFor(state);
        PresenceSystem.Recompute(state);
        VisionSystem.Recompute(state);
        match.Commands.Enqueue(new AttackBuildingCommand(1, new[] { archers.Id }, house.Id));
        match.Run(20 * 20);
        Assert.Equal(house.MaxHp, house.Hp);

        var swords = EntityOps.CreateRegiment(state, 1, SoldierType.MenAtArms, new FixVec2(Fix.FromInt(23), Fix.FromInt(21)), FixVec2.North);
        match.Commands.Enqueue(new AttackBuildingCommand(1, new[] { swords.Id }, house.Id));
        match.Run(20 * 20);
        Assert.True(house.Hp < house.MaxHp, "Men-at-Arms should be breaking the house down");
    }
}
