using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;

namespace BannerAndBarrow.Simulation.Config;

// ============================================================================================
// Every balance value lives here and in config/*.json. The C# defaults are only a fallback;
// the JSON files are authoritative. Values marked TUNING are the ones expected to need iteration.
// ============================================================================================

public sealed class GameConfig
{
    public SimulationConfig Simulation { get; set; } = new();
    public MapConfig Map { get; set; } = new();
    public MovementConfig Movement { get; set; } = new();
    public TerritoryConfig Territory { get; set; } = new();
    public EconomyConfig Economy { get; set; } = new();
    public CombatConfig Combat { get; set; } = new();
    public FortConfig Forts { get; set; } = new();
    public Dictionary<BuildingType, BuildingDef> Buildings { get; set; } = new();
    public Dictionary<SoldierType, SoldierDef> Soldiers { get; set; } = new();
    public Dictionary<FormationType, FormationDef> Formations { get; set; } = new();
    public AiProfilesConfig Ai { get; set; } = new();

    public BuildingDef Building(BuildingType type) =>
        Buildings.TryGetValue(type, out var d) ? d : throw new InvalidOperationException($"No building config for {type}");

    public SoldierDef Soldier(SoldierType type) =>
        Soldiers.TryGetValue(type, out var d) ? d : throw new InvalidOperationException($"No soldier config for {type}");

    public FormationDef Formation(FormationType type) =>
        Formations.TryGetValue(type, out var d) ? d : throw new InvalidOperationException($"No formation config for {type}");

    public int SecondsToTicks(Fix seconds) => System.Math.Max(1, (seconds * Simulation.TicksPerSecond).RoundToInt());

    public Fix TickSeconds => Fix.One / Simulation.TicksPerSecond;
}

public sealed class SimulationConfig
{
    public int TicksPerSecond { get; set; } = 20;
    /// <summary>TUNING: Soldier cap per Player.</summary>
    public int MaxSoldiersPerPlayer { get; set; } = 200;
}

public sealed class MapConfig
{
    /// <summary>0 = pick a random seed at startup.</summary>
    public int Seed { get; set; } = 20260916;
    public int Width { get; set; } = 192;
    public int Height { get; set; } = 192;
    public int KeepInset { get; set; } = 28;
    public int KeepClearRadius { get; set; } = 8;
    public int NoiseCellSize { get; set; } = 24;
    public int NoiseOctaves { get; set; } = 3;
    public Fix WaterLevel { get; set; } = Fix.FromDecimal(0.24m);
    public Fix HillLevel { get; set; } = Fix.FromDecimal(0.66m);
    public Fix MountainLevel { get; set; } = Fix.FromDecimal(0.78m);
    public Fix ForestLevel { get; set; } = Fix.FromDecimal(0.6m);
    public int RidgeHalfThickness { get; set; } = 3;
    public int ChokepointCount { get; set; } = 3;
    public int ChokepointHalfWidth { get; set; } = 3;
    public int StoneClustersPerSide { get; set; } = 3;
    public int IronClustersPerSide { get; set; } = 2;
    public int ClusterRadius { get; set; } = 2;
    public int MaxGenerationAttempts { get; set; } = 8;
}

public sealed class MovementConfig
{
    public Fix WorkerSpeed { get; set; } = Fix.FromInt(4);
    public Fix WorkerRadius { get; set; } = Fix.FromDecimal(0.25m);
    /// <summary>TUNING: speed multipliers for Workers/Carriers/Builders. Off-road is deliberately punishing.</summary>
    public RoadSpeeds Civilian { get; set; } = new() { OffRoad = Fix.Half, Dirt = Fix.One, Stone = Fix.FromDecimal(1.5m) };
    /// <summary>TUNING: speed multipliers for Soldiers. Roads are a bonus, not a requirement.</summary>
    public RoadSpeeds Military { get; set; } = new() { OffRoad = Fix.One, Dirt = Fix.FromDecimal(1.25m), Stone = Fix.FromDecimal(1.5m) };
    /// <summary>TUNING: Workers plan routes as if off-road tiles cost this many times more, so they stick to roads.</summary>
    public Fix CivilianOffRoadRouteCost { get; set; } = Fix.FromDecimal(2.5m);
    public Fix ForestMultiplier { get; set; } = Fix.FromDecimal(0.6m);
    public Fix HillsMultiplier { get; set; } = Fix.FromDecimal(0.8m);
    public int FlowFieldCacheCapacity { get; set; } = 128;
    public int FlowFieldRebuildsPerTick { get; set; } = 3;
    public int FlowFieldMinorStaleTicks { get; set; } = 400;
    /// <summary>A Soldier within this distance of its formation slot walks straight to it instead of following the flow field.</summary>
    public Fix SlotDirectDistance { get; set; } = Fix.FromInt(5);
    /// <summary>The Regiment's banner waits when Soldiers lag further than this behind their slots on average.</summary>
    public Fix RegimentMaxLag { get; set; } = Fix.FromInt(4);
    /// <summary>Banner speed multiplier while Soldiers lag behind.</summary>
    public Fix LagSlowdown { get; set; } = Fix.Half;
    /// <summary>Soldiers further than this from their slot are ignored when deciding whether the banner waits.</summary>
    public Fix StragglerDistance { get; set; } = Fix.FromInt(10);
    public SteeringSettings Steering { get; set; } = new();
}

public sealed class RoadSpeeds
{
    public Fix OffRoad { get; set; } = Fix.One;
    public Fix Dirt { get; set; } = Fix.One;
    public Fix Stone { get; set; } = Fix.One;
}

public sealed class TerritoryConfig
{
    /// <summary>TUNING: Territory reach (tiles) of the Keep and of a Storehouse.</summary>
    public int KeepReach { get; set; } = 16;
    public int StorehouseReach { get; set; } = 10;
    /// <summary>TUNING: Presence radii (tiles).</summary>
    public int RegimentPresence { get; set; } = 6;
    public int OutpostPresence { get; set; } = 10;
    public int StrongholdPresence { get; set; } = 14;
    public int PresenceRecomputeTicks { get; set; } = 5;
    /// <summary>Players only see their own Territory and Presence; enemy buildings elsewhere are remembered as last seen.</summary>
    public bool FogOfWar { get; set; } = true;
    /// <summary>TUNING: Regiments and forts see at least this many tiles beyond their longest shot, so archers never shoot at nothing.</summary>
    public int VisionBeyondRange { get; set; } = 2;
    /// <summary>TUNING: an enemy that attacks your Soldiers, Workers or buildings is visible to you for this long.</summary>
    public Fix RevealAttackerSeconds { get; set; } = Fix.FromInt(6);
    /// <summary>TUNING: seconds to Repair a Ruin before Burning buildings are destroyed.</summary>
    public Fix RebuildWindowSeconds { get; set; } = Fix.FromInt(120);
    public int OutpostMinSpacing { get; set; } = 12;
    /// <summary>TUNING: each additional Storehouse costs base × multiplier^owned × (1 + distanceWeight × tilesFromKeep).</summary>
    public Fix StorehouseCostMultiplier { get; set; } = Fix.FromDecimal(1.5m);
    public Fix StorehouseCostDistanceWeight { get; set; } = Fix.FromDecimal(0.02m);
    public Fix RuinRepairCostFraction { get; set; } = Fix.Half;
    /// <summary>Storehouses within this distance are neighbours for Demand relaying.</summary>
    public int StorehouseNeighbourRadius { get; set; } = 24;
    public int DemandMaxHops { get; set; } = 4;
}

public sealed class EconomyConfig
{
    public Dictionary<ResourceType, int> StartingStock { get; set; } = new();
    public int KeepWorkerRoom { get; set; } = 10;
    /// <summary>TUNING: Workers can be killed by Soldiers; their job is re-staffed by a newly spawned Worker.</summary>
    public Fix WorkerHp { get; set; } = Fix.FromInt(30);
    public Fix WorkerSpawnIntervalSeconds { get; set; } = Fix.One;
    public int WorkerCarryCapacity { get; set; } = 5;
    public int CarrierCapacity { get; set; } = 10;
    public int BuildersBase { get; set; } = 3;
    public int BuildersPerSite { get; set; } = 1;
    public int BuildersMax { get; set; } = 8;
    public Fix BuildWorkPerSecond { get; set; } = Fix.One;
    /// <summary>Demolition takes this fraction of the build time; this fraction of the cost is refunded.</summary>
    public Fix DemolishTimeFraction { get; set; } = Fix.FromDecimal(0.4m);
    /// <summary>TUNING: Builders start repairing a damaged building this long after it was last attacked.</summary>
    public Fix RepairDelaySeconds { get; set; } = Fix.FromInt(10);
    /// <summary>TUNING: HP each repairing Builder restores per second, as a fraction of max HP.</summary>
    public Fix RepairFractionPerSecond { get; set; } = Fix.FromDecimal(0.02m);
    /// <summary>TUNING: Fire each repairing Builder puts out per second.</summary>
    public Fix FireDousePerSecond { get; set; } = Fix.FromDecimal(0.03m);
    public int MaxRepairersPerBuilding { get; set; } = 2;
    public Fix DemolishRefundFraction { get; set; } = Fix.Half;
    public int StorehouseStartCarriers { get; set; } = 2;
    public int CarriersPerPurchase { get; set; } = 2;
    public int StorehouseMaxCarriers { get; set; } = 8;
    public Dictionary<ResourceType, int> CarrierPurchaseCost { get; set; } = new();
    public int StorehouseMaxUpgrades { get; set; } = 1;
    public Fix StorehouseUpgradeCapacityMultiplier { get; set; } = Fix.FromInt(2);
    public Dictionary<ResourceType, int> StorehouseUpgradeCost { get; set; } = new();
    public int ProductionRadius { get; set; } = 8;
    /// <summary>TUNING: once orders are filled, manufacturers keep making each item until the Player holds this many.</summary>
    public int GoodsStockTarget { get; set; } = 10;
    /// <summary>Regiments must be this close to the recruiting building to be upgraded.</summary>
    public int UpgradeRadius { get; set; } = 16;
    public int WoodPerForestTile { get; set; } = 12;
    public int StonePerDeposit { get; set; } = 400;
    public int IronPerDeposit { get; set; } = 300;
    public Fix TreeRegrowSeconds { get; set; } = Fix.FromInt(180);
    /// <summary>TUNING: Farmers sow and harvest Field tiles within this radius of the Farm.</summary>
    public int FarmFieldRadius { get; set; } = 4;
    /// <summary>TUNING: how many Fields one Farm keeps planted.</summary>
    public int FarmFields { get; set; } = 8;
    public Fix SowSeconds { get; set; } = Fix.FromInt(3);
    /// <summary>TUNING: time from sowing until a Field can be harvested.</summary>
    public Fix CropGrowSeconds { get; set; } = Fix.FromInt(50);
    /// <summary>TUNING: Food it costs to bring a new Worker into the world.</summary>
    public int WorkerSpawnFood { get; set; } = 1;
    /// <summary>TUNING: every Worker walks to a Storehouse to eat this much Food once per meal interval.</summary>
    public int MealFood { get; set; } = 1;
    public Fix MealIntervalSeconds { get; set; } = Fix.FromInt(120);
    /// <summary>TUNING: work speed of a Hungry Worker (no Food to eat).</summary>
    public Fix HungryWorkSpeed { get; set; } = Fix.Half;
    public int TaxGoldPerHouse { get; set; } = 1;
    public int TaxGoldMaxPerCycle { get; set; } = 12;
    public Fix LogisticsIntervalSeconds { get; set; } = Fix.One;
    public Fix StarvedAfterSeconds { get; set; } = Fix.FromInt(10);
}

/// <summary>One product a manufacturing building can make.</summary>
public sealed class RecipeDef
{
    public ResourceType Output { get; set; }
    public int Amount { get; set; } = 1;
    public Dictionary<ResourceType, int> Inputs { get; set; } = new();
    public Fix WorkSeconds { get; set; } = Fix.FromInt(6);
}

public sealed class BuildingDef
{
    public int Size { get; set; } = 2;
    public int Hp { get; set; } = 300;
    public Dictionary<ResourceType, int> Cost { get; set; } = new();
    public Fix BuildSeconds { get; set; } = Fix.FromInt(20);
    public bool Placeable { get; set; } = true;
    public int WorkerSlots { get; set; }
    public Fix WorkSeconds { get; set; } = Fix.FromInt(8);
    public Dictionary<ResourceType, int> Inputs { get; set; } = new();
    public Dictionary<ResourceType, int> Outputs { get; set; } = new();
    /// <summary>Manufacturing buildings: the products they can make. Chosen each cycle: orders first, then stock.</summary>
    public List<RecipeDef> Recipes { get; set; } = new();
    /// <summary>TUNING: output collects at the building until this much is ready, then the Worker carries the batch to a Storehouse.</summary>
    public int BatchSize { get; set; } = 8;
    /// <summary>For Storehouse/Keep: capacity per resource.</summary>
    public int StorageCapacity { get; set; }
    /// <summary>For House/Keep: Worker room.</summary>
    public int WorkerRoom { get; set; }
}

public sealed class AttackDef
{
    public Fix Damage { get; set; } = Fix.FromInt(10);
    public Fix Penetration { get; set; } = Fix.Zero;
    public Fix Range { get; set; } = Fix.One;
    public Fix MinRange { get; set; } = Fix.Zero;
    public Fix CooldownSeconds { get; set; } = Fix.One;
    public Fix ProjectileSpeed { get; set; } = Fix.FromInt(20);
    /// <summary>Radius of aim error (tiles) per tile of distance.</summary>
    public Fix SpreadPerTile { get; set; } = Fix.FromDecimal(0.03m);
}

public sealed class SoldierDef
{
    public Fix Hp { get; set; } = Fix.FromInt(100);
    public Fix Armour { get; set; } = Fix.Zero;
    public Fix Speed { get; set; } = Fix.FromInt(2);
    public Fix Radius { get; set; } = Fix.FromDecimal(0.3m);
    public AttackDef Melee { get; set; } = new();
    /// <summary>Null for melee-only Soldiers.</summary>
    public AttackDef? Ranged { get; set; }
    /// <summary>Multiplier on the first melee hit after a charge at speed.</summary>
    public Fix ChargeMultiplier { get; set; } = Fix.One;
    /// <summary>Damage multiplier against Knights (spears).</summary>
    public Fix BonusVsMounted { get; set; } = Fix.One;
    public bool IsMounted { get; set; }
    public int RegimentSize { get; set; } = 6;
    /// <summary>Largest Regiment that merging squads of this type can build.</summary>
    public int MaxRegimentSize { get; set; } = 24;
    public Dictionary<ResourceType, int> RecruitCost { get; set; } = new();
    public Fix RecruitSeconds { get; set; } = Fix.FromInt(20);
    public BuildingType RecruitedAt { get; set; } = BuildingType.Barracks;
    /// <summary>If set, Regiments of this type can be upgraded into this type (e.g. Spearmen into Pikemen).</summary>
    public SoldierType? UpgradeFrom { get; set; }
    public Dictionary<ResourceType, int> UpgradeCostPerSoldier { get; set; } = new();
    /// <summary>These Soldiers carry a shield: they raise it against arrows and take less damage from them.</summary>
    public bool Shield { get; set; }
    public List<FormationType> Formations { get; set; } = new();
}

public sealed class FormationDef
{
    public FormationShape Shape { get; set; } = FormationShape.Grid;
    /// <summary>Soldiers per rank for Grid shapes.</summary>
    public int Width { get; set; } = 8;
    public Fix Spacing { get; set; } = Fix.One;
    public Fix SpeedMultiplier { get; set; } = Fix.One;
    public Fix FrontRangedMultiplier { get; set; } = Fix.One;
    public Fix FrontMeleeMultiplier { get; set; } = Fix.One;
    public Fix AllRangedMultiplier { get; set; } = Fix.One;
    public Fix AllMeleeMultiplier { get; set; } = Fix.One;
    public Fix FlankMultiplier { get; set; } = Fix.FromDecimal(1.5m);
    public Fix RearMultiplier { get; set; } = Fix.FromInt(2);
    public Fix OutgoingMeleeMultiplier { get; set; } = Fix.One;
    /// <summary>Multiplier on an attacker's charge bonus when charging in this formation (0 = cannot charge).</summary>
    public Fix ChargeBonusMultiplier { get; set; } = Fix.One;
    /// <summary>Multiplier on charge damage received from the front.</summary>
    public Fix AntiChargeMultiplier { get; set; } = Fix.One;
    public Fix CohesionLossMultiplier { get; set; } = Fix.One;
}

public sealed class CombatConfig
{
    /// <summary>TUNING: damage factor = clamp(1 + (penetration − armour) × scale, min, max).</summary>
    public Fix PenetrationScale { get; set; } = Fix.FromDecimal(0.12m);
    public Fix MinDamageFactor { get; set; } = Fix.FromDecimal(0.15m);
    public Fix MaxDamageFactor { get; set; } = Fix.FromDecimal(1.6m);
    /// <summary>Idle/attack-moving Regiments engage enemies within this distance of their banner.</summary>
    public Fix EngageRadius { get; set; } = Fix.FromInt(8);
    /// <summary>Melee Regiments stop chasing beyond this distance from where they were ordered.</summary>
    public Fix LeashRadius { get; set; } = Fix.FromInt(14);
    /// <summary>Soldiers break formation to fight enemies this close.</summary>
    public Fix SoldierAggroRadius { get; set; } = Fix.FromInt(3);
    public Fix CohesionMax { get; set; } = Fix.FromInt(100);
    public Fix CohesionLossPerCasualty { get; set; } = Fix.FromInt(4);
    public Fix CohesionLossPerFlankHit { get; set; } = Fix.FromDecimal(0.5m);
    public Fix CohesionRecoveryPerSecond { get; set; } = Fix.FromInt(2);
    public Fix CohesionRecoveryDelaySeconds { get; set; } = Fix.FromInt(5);
    public Fix BrokenThreshold { get; set; } = Fix.FromInt(25);
    public Fix BrokenDamageDealtMultiplier { get; set; } = Fix.Half;
    /// <summary>Ranged Soldiers switch to melee when an enemy is this close.</summary>
    public Fix RangedMeleeFallbackDistance { get; set; } = Fix.FromInt(2);
    public Fix RangedMeleePenalty { get; set; } = Fix.Half;
    public Fix HillsRangeMultiplier { get; set; } = Fix.FromDecimal(1.2m);
    public Fix ForestArrowBlockChance { get; set; } = Fix.FromDecimal(0.4m);
    public Fix ProjectileHitRadius { get; set; } = Fix.FromDecimal(0.45m);
    public bool FriendlyFire { get; set; } = true;
    /// <summary>Soldiers attack enemy Workers they come across.</summary>
    public bool SoldiersAttackWorkers { get; set; } = true;
    public Fix BuildingMeleeDamageMultiplier { get; set; } = Fix.One;
    public Fix BuildingRangedDamageMultiplier { get; set; } = Fix.FromDecimal(0.15m);
    /// <summary>TUNING: shooting over a wall that isn't yours: how much range is left, and how much wider the spread.</summary>
    public Fix WallShotRangeMultiplier { get; set; } = Fix.FromDecimal(0.6m);
    public Fix WallShotSpreadMultiplier { get; set; } = Fix.FromInt(2);
    /// <summary>TUNING: arrow damage taken by a Soldier with a shield.</summary>
    public Fix ShieldRangedDamageMultiplier { get; set; } = Fix.FromDecimal(0.55m);
    /// <summary>
    /// TUNING: torches (melee hits on a building) add Fire = this × damage ÷ max HP, so big stone buildings take a
    /// long siege to catch; arrows add the smaller amount.
    /// </summary>
    public Fix FirePerTorchDamage { get; set; } = Fix.FromInt(2);
    public Fix FirePerArrowDamage { get; set; } = Fix.FromDecimal(0.5m);
    /// <summary>TUNING: at full Fire a building loses this fraction of its max HP per second.</summary>
    public Fix FireDamageFractionPerSecond { get; set; } = Fix.FromDecimal(0.02m);
    /// <summary>TUNING: a fire at least this big spreads on its own (+FireGrowthPerSecond) until the building burns down.</summary>
    public Fix FireSelfSustaining { get; set; } = Fix.FromDecimal(0.6m);
    public Fix FireGrowthPerSecond { get; set; } = Fix.FromDecimal(0.01m);
    /// <summary>TUNING: smaller fires die down at this rate once nobody has attacked the building for FireDecayDelaySeconds.</summary>
    public Fix FireDecayPerSecond { get; set; } = Fix.FromDecimal(0.03m);
    public Fix FireDecayDelaySeconds { get; set; } = Fix.FromInt(8);
    public Fix ChargeMinSpeedFraction { get; set; } = Fix.FromDecimal(0.6m);
    public Fix ChargeCooldownSeconds { get; set; } = Fix.FromInt(10);
    /// <summary>TUNING: ranged Regiments (not holding ground) fall back when enemy melee comes this close.</summary>
    public Fix KiteTriggerDistance { get; set; } = Fix.FromInt(6);
    public Fix KiteStepDistance { get; set; } = Fix.FromInt(7);
    public Fix KiteCooldownSeconds { get; set; } = Fix.FromInt(3);
    /// <summary>Front/flank/rear boundaries as dot(facing, direction to attacker).</summary>
    public Fix FrontDotThreshold { get; set; } = Fix.FromDecimal(0.5m);
    public Fix RearDotThreshold { get; set; } = Fix.FromDecimal(-0.5m);
    public StanceConfig Stance { get; set; } = new();
    public BattleGroupConfig BattleGroups { get; set; } = new();
}

/// <summary>TUNING: rank layout of Battle Groups and when their front rank swaps.</summary>
public sealed class BattleGroupConfig
{
    /// <summary>Enemy melee Regiments within this distance decide which melee type takes the front rank.</summary>
    public Fix ThreatRadius { get; set; } = Fix.FromInt(22);
    /// <summary>Ranks don't reshuffle once the enemy is closer than this.</summary>
    public Fix MinSwapDistance { get; set; } = Fix.FromInt(6);
    public Fix ThreatCheckSeconds { get; set; } = Fix.One;
    /// <summary>Side-by-side gap between Regiments in a rank, and front-to-back gap between ranks (tiles).</summary>
    public Fix RegimentGap { get; set; } = Fix.FromDecimal(1.5m);
    public Fix RankGap { get; set; } = Fix.FromInt(2);
}

/// <summary>TUNING: how the Hold Ground stance and moving change ranged fire.</summary>
public sealed class StanceConfig
{
    public Fix HoldRangeMultiplier { get; set; } = Fix.FromDecimal(1.2m);
    /// <summary>TUNING: tiles of range a ranged Regiment loses right after moving.</summary>
    public Fix MovedRangePenaltyTiles { get; set; } = Fix.FromInt(3);
    /// <summary>TUNING: seconds of standing still needed to win that range back.</summary>
    public Fix SettleSeconds { get; set; } = Fix.FromInt(5);
    public Fix HoldCooldownMultiplier { get; set; } = Fix.FromDecimal(0.67m);
    public Fix HoldSpreadMultiplier { get; set; } = Fix.FromDecimal(0.7m);
    public Fix AdvanceRangeMultiplier { get; set; } = Fix.FromDecimal(0.85m);
    public Fix AdvanceCooldownMultiplier { get; set; } = Fix.FromDecimal(1.25m);
    public Fix AdvanceSpreadMultiplier { get; set; } = Fix.FromDecimal(1.4m);
}

public sealed class FortConfig
{
    public int OutpostCapacityRegiments { get; set; } = 1;
    public int StrongholdCapacityRegiments { get; set; } = 5;
    public int StrongholdActiveShooterSlots { get; set; } = 16;
    public int GarrisonSize { get; set; } = 4;
    public SoldierType GarrisonType { get; set; } = SoldierType.Archers;
    public Fix GarrisonRespawnSeconds { get; set; } = Fix.FromInt(30);
    /// <summary>Garrison only returns if the Stronghold has not been attacked for this long.</summary>
    public Fix GarrisonQuietSeconds { get; set; } = Fix.FromInt(15);
    /// <summary>TUNING: seconds between shots for fort archers (Garrison). Independent of field archers' volley rate.</summary>
    public Fix ShooterCooldownSeconds { get; set; } = Fix.FromInt(3);
    /// <summary>TUNING: fort archers loose heavy, aimed shots: arrow damage is multiplied by this.</summary>
    public Fix ShooterDamageMultiplier { get; set; } = Fix.FromInt(4);
    /// <summary>Stationed ranged Soldiers shoot this much faster than the Garrison.</summary>
    public Fix StationedFireRateMultiplier { get; set; } = Fix.FromDecimal(1.5m);
    /// <summary>Stationed melee Soldiers fill empty shooter slots with a weak, short-ranged missile.</summary>
    public AttackDef MeleeMissile { get; set; } = new() { Damage = Fix.FromInt(4), Penetration = Fix.One, Range = Fix.FromInt(6), CooldownSeconds = Fix.FromInt(4), ProjectileSpeed = Fix.FromInt(14), SpreadPerTile = Fix.FromDecimal(0.06m) };
    /// <summary>Chance that a projectile hitting a Stronghold kills an active shooter instead of damaging walls.</summary>
    public Fix ShooterHitChance { get; set; } = Fix.FromDecimal(0.25m);
    public Fix StationedDamageTakenMultiplier { get; set; } = Fix.Half;
    public Dictionary<ResourceType, int> StrongholdUpgradeCost { get; set; } = new();
    public Fix StrongholdUpgradeSeconds { get; set; } = Fix.FromInt(30);
    public int StrongholdHp { get; set; } = 2000;
    public Fix AlertCooldownSeconds { get; set; } = Fix.FromInt(20);
}
