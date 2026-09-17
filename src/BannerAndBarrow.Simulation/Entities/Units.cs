using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;

namespace BannerAndBarrow.Simulation.Entities;

public sealed class Soldier : NavAgent
{
    public int Owner;
    public SoldierType Type;
    public int RegimentId;
    public Fix Hp;
    public long NextAttackTick;
    public long ChargeReadyTick;
    public int TargetSoldierId;
    public bool Stationed;
}

public enum RegimentOrder : byte
{
    Idle,
    /// <summary>Forced march: ignore enemies until arrival.</summary>
    Move,
    /// <summary>Move, but engage enemies met on the way.</summary>
    AttackMove,
    AttackRegiment,
    AttackBuilding,
    /// <summary>Walk to an Outpost or Stronghold and go inside.</summary>
    Station,
    /// <summary>Keep position behind another Regiment ("archers behind infantry").</summary>
    Follow,
}

public sealed class Regiment
{
    public int Id;
    public int Owner;
    public SoldierType Type;
    public readonly List<int> SoldierIds = new();
    public FormationType Formation;
    /// <summary>Hold Ground stance: no chasing; ranged Soldiers that have stopped shoot faster, further and straighter.</summary>
    public bool HoldGround;
    /// <summary>Battle Group this Regiment belongs to, or 0.</summary>
    public int GroupId;
    /// <summary>Until this tick the Regiment is falling back from melee (fire and retreat).</summary>
    public long KiteUntilTick;
    /// <summary>Banner position: the virtual leader Soldiers keep their formation slots around.</summary>
    public FixVec2 Anchor;
    public FixVec2 Facing = FixVec2.North;
    public FixVec2 Destination;
    public FixVec2 DestinationFacing = FixVec2.North;
    public TileCoord[] DestinationGoals = Array.Empty<TileCoord>();
    public FlowGoalKey DestinationKey;
    public bool AnchorArrived = true;
    public RegimentOrder Order;
    public int TargetRegimentId;
    public int TargetBuildingId;
    public int FollowRegimentId;
    public Fix FollowGap = Fix.FromInt(3);
    /// <summary>Enemy Regiment this one is currently fighting (auto-engagement or ordered).</summary>
    public int EngagedRegimentId;
    public int EngagedBuildingId;
    /// <summary>Where the Regiment was told to be; melee chases are leashed to this point.</summary>
    public FixVec2 HoldPoint;
    public int StationedInBuildingId;
    public Fix Cohesion;
    public long LastDamagedTick = long.MinValue / 2;
    public int StartingSize;
    /// <summary>Average distance of Soldiers from their slots last tick; the banner slows when this grows.</summary>
    public Fix AverageLag;
    /// <summary>Last tick this Regiment was moving. Ranged Soldiers need to stand still a while to shoot at full range.</summary>
    public long LastMovedTick = long.MinValue / 2;

    public bool IsStationed => StationedInBuildingId != 0;
}

public enum WorkerJob : byte
{
    None,
    Builder,
    Producer,
    Carrier,
}

public enum WorkerTask : byte
{
    Idle,
    GoToWorkplace,
    GoToNode,
    Harvest,
    Work,
    GoToPickup,
    GoToDeliver,
    GoToSite,
    Build,
    Wait,
    /// <summary>Walking to a Storehouse to eat.</summary>
    GoToEat,
    /// <summary>Farmer sowing a Field.</summary>
    Sow,
    /// <summary>Builder walking to a damaged building.</summary>
    GoToRepair,
    /// <summary>Builder repairing a damaged building (and putting out its Fire).</summary>
    Repair,
}

public sealed class Worker : NavAgent
{
    public int Owner;
    public Fix Hp;
    public WorkerJob Job;
    /// <summary>Production building for Producers, home Storehouse for Carriers.</summary>
    public int WorkplaceId;
    public WorkerTask Task;
    public long TaskEndTick;

    public ResourceType CarryType;
    public int CarryAmount;
    /// <summary>Inputs carried to a processing building (Smithy).</summary>
    public readonly int[] CarriedInputs = new int[Resources.Count];
    /// <summary>Inputs reserved at <see cref="PickupStorehouseId"/> but not yet collected.</summary>
    public readonly int[] ReservedInputs = new int[Resources.Count];

    public int TargetBuildingId;
    public int TargetSiteId;
    public TileCoord TargetTile;
    public int ReservedNodeIndex = -1;
    /// <summary>Index into the workplace's recipes for the current manufacturing cycle, -1 when none.</summary>
    public int ActiveRecipe = -1;
    /// <summary>Farmer heading to a Field to sow it rather than harvest it.</summary>
    public bool Sowing;

    // Needs: the Worker gets Hungry at this tick unless it has eaten.
    public long HungryAtTick;
    public long NextMealTryTick;
    public int MealStorehouseId;
    public bool IsHungry(long tick) => tick >= HungryAtTick;

    // Carrier trip bookkeeping.
    public int ShipmentId;
    public int CarryReserved;
    public int CarryExtra;

    // Builder trip bookkeeping.
    public int PickupStorehouseId;
    public int PickupAmount;
}

public sealed class Projectile
{
    public int Owner;
    public FixVec2 Origin;
    public FixVec2 Target;
    public long LaunchTick;
    public long ImpactTick;
    public Fix Damage;
    public Fix Penetration;
    public int SourceSoldierId;
    public int SourceBuildingId;
    public bool IsLongbow;
}
