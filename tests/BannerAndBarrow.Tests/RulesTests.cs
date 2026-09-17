using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Military;
using BannerAndBarrow.Simulation.Territory;
using BannerAndBarrow.Simulation.World;

namespace BannerAndBarrow.Tests;

public class MapGeneratorTests
{
    [Fact]
    public void Same_seed_same_map()
    {
        var a = MapGenerator.Generate(TestWorld.Config(), 1234);
        var b = MapGenerator.Generate(TestWorld.Config(), 1234);
        Assert.Equal(a.Terrain, b.Terrain);
        Assert.Equal(a.Deposits, b.Deposits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(777)]
    [InlineData(20260916)]
    public void Map_is_mirrored_connected_and_has_chokepoints(int seed)
    {
        var map = MapGenerator.Generate(TestWorld.Config(), seed);
        int n = map.Width * map.Height;
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(map.Terrain[i], map.Terrain[n - 1 - i]);
            Assert.Equal(map.Deposits[i], map.Deposits[n - 1 - i]);
        }
        Assert.True(MapGenerator.KeepsConnected(map));
        Assert.Equal(TestWorld.Config().Map.ChokepointCount, map.Chokepoints.Count);
    }
}

public class TerritoryTests
{
    [Fact]
    public void Keep_claims_its_reach()
    {
        var state = TestWorld.FlatState();
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        Assert.Equal(0, state.Territory.OwnerAt(keep.CenterTile));
        Assert.Equal(TerritoryMap.None, state.Territory.OwnerAt(new TileCoord(60, 10)));
    }

    [Fact]
    public void Overlapping_storehouse_reach_is_contested_but_keep_reach_wins()
    {
        var state = TestWorld.FlatState(120, 40);
        TestWorld.AddKeep(state, 0, new TileCoord(10, 16));
        TestWorld.AddKeep(state, 1, new TileCoord(80, 16));
        // Storehouses whose 10-tile reach overlaps around x = 45.
        TestWorld.AddActive(state, 0, BuildingType.Storehouse, new TileCoord(36, 17));
        TestWorld.AddActive(state, 1, BuildingType.Storehouse, new TileCoord(52, 17));

        Assert.Equal(TerritoryMap.Contested, state.Territory.OwnerAt(new TileCoord(46, 18)));
        Assert.Equal(0, state.Territory.OwnerAt(new TileCoord(12, 18)));
    }

    [Fact]
    public void Existing_building_keeps_its_tiles_when_enemy_reach_overlaps()
    {
        var state = TestWorld.FlatState(120, 40);
        TestWorld.AddKeep(state, 0, new TileCoord(10, 16));
        TestWorld.AddKeep(state, 1, new TileCoord(80, 16));
        TestWorld.AddActive(state, 0, BuildingType.Storehouse, new TileCoord(36, 17));
        var house = TestWorld.AddActive(state, 0, BuildingType.House, new TileCoord(44, 18));
        TestWorld.AddActive(state, 1, BuildingType.Storehouse, new TileCoord(52, 17));

        foreach (var t in house.Footprint()) Assert.Equal(0, state.Territory.OwnerAt(t));
        Assert.False(house.IsBurning);
    }

    [Fact]
    public void Destroying_a_storehouse_leaves_a_ruin_and_burns_buildings_until_repaired()
    {
        var state = TestWorld.FlatState(120, 40);
        TestWorld.AddKeep(state, 0, new TileCoord(10, 16));
        var store = TestWorld.AddActive(state, 0, BuildingType.Storehouse, new TileCoord(36, 17));
        var farm = TestWorld.AddActive(state, 0, BuildingType.Farm, new TileCoord(42, 17));

        EntityOps.DestroyBuilding(state, store, allowRuin: true);
        TerritorySystem.Recompute(state);
        Assert.Equal(BuildingState.Ruin, store.State);
        Assert.True(farm.IsBurning);

        Assert.True(BuildRules.CanRepairRuin(state, 0, store).Ok);
        EntityOps.StartRepair(state, store);
        EntityOps.CompleteConstruction(state, store);
        TerritorySystem.Recompute(state);
        Assert.False(farm.IsBurning);
    }

    [Fact]
    public void Burning_building_is_destroyed_when_the_rebuild_window_runs_out()
    {
        var state = TestWorld.FlatState(120, 40);
        TestWorld.AddKeep(state, 0, new TileCoord(10, 16));
        var store = TestWorld.AddActive(state, 0, BuildingType.Storehouse, new TileCoord(36, 17));
        var farm = TestWorld.AddActive(state, 0, BuildingType.Farm, new TileCoord(42, 17));
        EntityOps.DestroyBuilding(state, store, allowRuin: true);
        TerritorySystem.Recompute(state);

        state.Tick = farm.BurningDeadlineTick;
        TerritorySystem.Update(state);
        Assert.False(state.Buildings.ContainsKey(farm.Id));
    }
}

public class BuildRulesTests
{
    [Fact]
    public void Enemy_presence_blocks_building_even_with_own_soldiers_nearby()
    {
        var state = TestWorld.FlatState();
        TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var spot = new TileCoord(18, 12);
        Assert.True(BuildRules.CanPlaceBuilding(state, 0, BuildingType.House, spot).Ok);

        EntityOps.CreateRegiment(state, 1, SoldierType.Spearmen, new FixVec2(Fix.FromInt(19), Fix.FromInt(13)), FixVec2.North);
        EntityOps.CreateRegiment(state, 0, SoldierType.Spearmen, new FixVec2(Fix.FromInt(17), Fix.FromInt(13)), FixVec2.North);
        PresenceSystem.Recompute(state);

        var result = BuildRules.CanPlaceBuilding(state, 0, BuildingType.House, spot);
        Assert.False(result.Ok);
        Assert.Contains("Presence", result.Reason);
    }

    [Fact]
    public void Normal_buildings_need_own_territory_but_outposts_do_not()
    {
        var state = TestWorld.FlatState();
        TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var outside = new TileCoord(60, 30);
        Assert.False(BuildRules.CanPlaceBuilding(state, 0, BuildingType.House, outside).Ok);
        Assert.True(BuildRules.CanPlaceBuilding(state, 0, BuildingType.Outpost, outside).Ok);
    }

    [Fact]
    public void Outposts_cannot_be_built_in_enemy_territory_or_too_close_together()
    {
        var state = TestWorld.FlatState();
        TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        TestWorld.AddKeep(state, 1, new TileCoord(80, 50));
        Assert.False(BuildRules.CanPlaceBuilding(state, 0, BuildingType.Outpost, new TileCoord(78, 48)).Ok);

        TestWorld.AddActive(state, 1, BuildingType.Outpost, new TileCoord(50, 30));
        Assert.False(BuildRules.CanPlaceBuilding(state, 0, BuildingType.Outpost, new TileCoord(55, 30)).Ok);
        Assert.True(BuildRules.CanPlaceBuilding(state, 0, BuildingType.Outpost, new TileCoord(35, 30)).Ok);
    }

    [Fact]
    public void Stone_roads_need_own_territory_dirt_roads_do_not()
    {
        var state = TestWorld.FlatState();
        TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var outside = new TileCoord(60, 30);
        Assert.True(BuildRules.CanPlaceRoad(state, 0, outside, RoadKind.Dirt).Ok);
        Assert.False(BuildRules.CanPlaceRoad(state, 0, outside, RoadKind.Stone).Ok);
        Assert.True(BuildRules.CanPlaceRoad(state, 0, new TileCoord(16, 12), RoadKind.Stone).Ok);
    }

    [Fact]
    public void Storehouse_cost_rises_with_count_and_distance()
    {
        var state = TestWorld.FlatState(120, 60);
        TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        var nearCost = BuildRules.GetCost(state, 0, BuildingType.Storehouse, new TileCoord(18, 12));
        var farCost = BuildRules.GetCost(state, 0, BuildingType.Storehouse, new TileCoord(22, 20));
        Assert.True(farCost[(int)ResourceType.Wood] > nearCost[(int)ResourceType.Wood]);

        TestWorld.AddActive(state, 0, BuildingType.Storehouse, new TileCoord(18, 18));
        var secondCost = BuildRules.GetCost(state, 0, BuildingType.Storehouse, new TileCoord(18, 12));
        Assert.True(secondCost[(int)ResourceType.Wood] > nearCost[(int)ResourceType.Wood]);
    }
}

public class CombatMathTests
{
    private static CombatMath.AttackContext Ranged(SoldierType type, FixVec2 from)
    {
        var def = TestWorld.Config().Soldier(type).Ranged!;
        return new CombatMath.AttackContext(def.Damage, def.Penetration, true, false, from, null, null, false, false);
    }

    private static CombatMath.DefenderContext Defender(SoldierType type, FormationType formation, FixVec2 facing) =>
        new(TestWorld.Config().Soldier(type), FixVec2.Zero, facing, TestWorld.Config().Formation(formation), true);

    private static readonly FixVec2 InFront = new(Fix.Zero, Fix.FromInt(-10));
    private static readonly FixVec2 ToTheSide = new(Fix.FromInt(10), Fix.Zero);
    private static readonly FixVec2 Behind = new(Fix.Zero, Fix.FromInt(10));

    [Fact]
    public void Longbows_hurt_armour_far_more_than_shortbows()
    {
        var cfg = TestWorld.Config();
        var target = Defender(SoldierType.MenAtArms, FormationType.Line, FixVec2.North);
        var (bow, _) = CombatMath.ComputeDamage(cfg, Ranged(SoldierType.Archers, InFront), target);
        var (longbow, _) = CombatMath.ComputeDamage(cfg, Ranged(SoldierType.Longbowmen, InFront), target);
        Assert.True(longbow > bow * 2, $"longbow {longbow} vs bow {bow}");
        Assert.True(cfg.Soldier(SoldierType.Longbowmen).Ranged!.Range > cfg.Soldier(SoldierType.Archers).Ranged!.Range);
    }

    [Fact]
    public void Shields_blunt_arrows_and_flanks_are_still_softer()
    {
        var cfg = TestWorld.Config();
        // Men-at-Arms carry shields and raise them against arrows; Spearmen don't.
        var (shielded, zone) = CombatMath.ComputeDamage(cfg, Ranged(SoldierType.Archers, InFront), Defender(SoldierType.MenAtArms, FormationType.Line, FixVec2.North));
        var (bare, _) = CombatMath.ComputeDamage(cfg, Ranged(SoldierType.Archers, InFront), Defender(SoldierType.Spearmen, FormationType.Line, FixVec2.North));
        var (flank, flankZone) = CombatMath.ComputeDamage(cfg, Ranged(SoldierType.Archers, ToTheSide), Defender(SoldierType.Spearmen, FormationType.Line, FixVec2.North));
        Assert.Equal(HitZone.Front, zone);
        Assert.Equal(HitZone.Flank, flankZone);
        Assert.True(cfg.Soldier(SoldierType.MenAtArms).Shield && !cfg.Soldier(SoldierType.Spearmen).Shield);
        Assert.True(shielded < bare, $"shielded {shielded} should take less than bare {bare}");
        Assert.True(flank > bare);
    }

    [Fact]
    public void Rear_attacks_hit_harder_than_frontal_ones()
    {
        var cfg = TestWorld.Config();
        var target = Defender(SoldierType.Spearmen, FormationType.Line, FixVec2.North);
        var (front, _) = CombatMath.ComputeDamage(cfg, Ranged(SoldierType.Archers, InFront), target);
        var (rear, zone) = CombatMath.ComputeDamage(cfg, Ranged(SoldierType.Archers, Behind), target);
        Assert.Equal(HitZone.Rear, zone);
        Assert.True(rear > front);
    }

    [Fact]
    public void Knight_charge_hits_loose_order_harder_than_a_formed_line_and_spears_punish_knights()
    {
        var cfg = TestWorld.Config();
        var knight = cfg.Soldier(SoldierType.Knights);
        var charge = new CombatMath.AttackContext(knight.Melee.Damage, knight.Melee.Penetration, false, true, InFront, knight,
            cfg.Formation(FormationType.Wedge), false, false);
        var (intoLine, _) = CombatMath.ComputeDamage(cfg, charge, Defender(SoldierType.Spearmen, FormationType.Line, FixVec2.North));
        var (intoLoose, _) = CombatMath.ComputeDamage(cfg, charge, Defender(SoldierType.Spearmen, FormationType.Loose, FixVec2.North));
        // A formed line takes a charge better than men spread out in loose order.
        Assert.True(intoLine < intoLoose, $"line {intoLine} should resist a charge better than loose {intoLoose}");

        var spear = cfg.Soldier(SoldierType.Spearmen);
        var sword = cfg.Soldier(SoldierType.MenAtArms);
        var spearHit = new CombatMath.AttackContext(spear.Melee.Damage, spear.Melee.Penetration, false, false, InFront, spear, cfg.Formation(FormationType.Line), false, false);
        var (spearVsKnight, _) = CombatMath.ComputeDamage(cfg, spearHit, Defender(SoldierType.Knights, FormationType.Line, FixVec2.North));
        var (spearVsSword, _) = CombatMath.ComputeDamage(cfg, spearHit, Defender(SoldierType.MenAtArms, FormationType.Line, FixVec2.North));
        Assert.True(spearVsKnight > spearVsSword);
        _ = sword;
    }

    [Fact]
    public void Formation_slots_are_distinct_and_front_rank_is_forward()
    {
        foreach (var formation in Enum.GetValues<FormationType>())
        {
            var def = TestWorld.Config().Formation(formation);
            var slots = Enumerable.Range(0, 16).Select(i => FormationLayout.SlotOffset(def, i, 16)).ToList();
            Assert.Equal(16, slots.Distinct().Count());
            Assert.True(slots[0].Y >= slots[15].Y, $"{formation}: first slot should be in the front rank");
        }
    }
}
