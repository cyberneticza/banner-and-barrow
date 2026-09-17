using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.World;

namespace BannerAndBarrow.Simulation.Entities;

public enum BuildingState : byte
{
    UnderConstruction,
    Active,
    /// <summary>Outpost turning into a Stronghold.</summary>
    Upgrading,
    /// <summary>A destroyed Storehouse. Can be Repaired when no enemy Presence covers it.</summary>
    Ruin,
}

/// <summary>Anything Builders deliver materials to and work on: buildings and road tiles.</summary>
public abstract class ConstructionTarget
{
    public int Id;
    public int Owner;
    public readonly int[] Required = new int[Resources.Count];
    public readonly int[] Delivered = new int[Resources.Count];
    /// <summary>Materials a Builder is currently carrying here.</summary>
    public readonly int[] Incoming = new int[Resources.Count];
    public Fix WorkDone;
    public Fix WorkRequired = Fix.One;

    public abstract bool NeedsConstruction { get; }

    /// <summary>True while Builders should come and work here: construction, repair or demolition.</summary>
    public virtual bool IsWorkSite => NeedsConstruction;
    public abstract FixVec2 WorkPoint { get; }

    public bool MaterialsComplete
    {
        get
        {
            for (int i = 0; i < Resources.Count; i++)
                if (Delivered[i] < Required[i]) return false;
            return true;
        }
    }

    /// <summary>Materials still to bring that nobody is carrying yet.</summary>
    public int Outstanding(ResourceType r) => Required[(int)r] - Delivered[(int)r] - Incoming[(int)r];

    public Fix Progress => WorkRequired.Raw == 0 ? Fix.One : Fix.Min(Fix.One, WorkDone / WorkRequired);
}

public sealed class StorehouseData
{
    public readonly int[] Stock = new int[Resources.Count];
    /// <summary>Stock promised to a Demand, a Builder or a producer. Not available to anyone else.</summary>
    public readonly int[] Reserved = new int[Resources.Count];
    /// <summary>Stock on its way here (Workers delivering). Counts against capacity.</summary>
    public readonly int[] Incoming = new int[Resources.Count];
    public readonly int[] MinimumStock = new int[Resources.Count];
    public int Capacity;
    public int Upgrades;
    public int Reach;
    public bool IsKeep;
    public int CarriersPurchased;
    public readonly List<int> CarrierIds = new();
    public long StarvedSinceTick = -1;
    public bool Starved;

    public int Available(ResourceType r) => Stock[(int)r] - Reserved[(int)r];
    public int Surplus(ResourceType r) => System.Math.Max(0, Available(r) - MinimumStock[(int)r]);
    public int FreeCapacity(ResourceType r) => Capacity - Stock[(int)r] - Incoming[(int)r];
}

public sealed class FortData
{
    public readonly List<int> StationedRegimentIds = new();
    public int GarrisonAlive;
    public long[] GarrisonNextShotTick = Array.Empty<long>();
    public long GarrisonNextRespawnTick;
    public long NextAlertTick;
    public long UpgradeCompleteTick;
}

public sealed class RecruitmentData
{
    public readonly List<SoldierType> Queue = new();
    public bool Paid;
    public long CompleteTick;
    public long NextDemandTick;
}

public sealed class Building : ConstructionTarget
{
    public BuildingType Type;
    public TileCoord Origin;
    public int Size;
    public Fix Hp;
    public Fix MaxHp;
    public BuildingState State;
    public bool IsRepair;
    /// <summary>Marked for demolition: stops working and Builders tear it down.</summary>
    public bool Demolishing;
    public bool IsBurning;
    public long BurningDeadlineTick;
    public long LastAttackedTick = long.MinValue / 2;
    /// <summary>How fiercely the building is on fire, 0 (not at all) to 1. Torches and fire arrows set it alight.</summary>
    public Fix Fire;
    /// <summary>Recruitment buildings: where newly trained Regiments walk to, or null for just outside the door.</summary>
    public FixVec2? RallyPoint;
    public readonly List<int> WorkerIds = new();
    /// <summary>Produced goods waiting at a production building until a full batch is carried to a Storehouse.</summary>
    public readonly int[] OutputStock = new int[Resources.Count];
    public StorehouseData? Store;
    public FortData? Fort;
    public RecruitmentData? Recruitment;
    /// <summary>Tiles around the footprint Workers and Soldiers walk to. Recomputed when the map changes.</summary>
    public TileCoord[] AccessTiles = Array.Empty<TileCoord>();
    public int AccessTilesVersion = -1;

    public FixVec2 Center => new(Fix.FromInt(Origin.X) + Fix.FromRatio(Size, 2), Fix.FromInt(Origin.Y) + Fix.FromRatio(Size, 2));
    public TileCoord CenterTile => new(Origin.X + Size / 2, Origin.Y + Size / 2);

    public override bool NeedsConstruction => State == BuildingState.UnderConstruction;
    public override bool IsWorkSite => NeedsConstruction || Demolishing;
    public override FixVec2 WorkPoint => Center;

    public bool IsActive => State == BuildingState.Active && !Demolishing;
    public bool IsStorehouseLike => Type is BuildingType.Keep or BuildingType.Storehouse;
    public bool IsFort => Type is BuildingType.Outpost or BuildingType.Stronghold;

    public bool Covers(TileCoord t) => t.X >= Origin.X && t.Y >= Origin.Y && t.X < Origin.X + Size && t.Y < Origin.Y + Size;

    public IEnumerable<TileCoord> Footprint()
    {
        for (int y = Origin.Y; y < Origin.Y + Size; y++)
        for (int x = Origin.X; x < Origin.X + Size; x++)
            yield return new TileCoord(x, y);
    }

    /// <summary>Distance from a point to the footprint rectangle (0 inside).</summary>
    public Fix DistanceToFootprint(FixVec2 p)
    {
        var minX = Fix.FromInt(Origin.X);
        var minY = Fix.FromInt(Origin.Y);
        var maxX = minX + Size;
        var maxY = minY + Size;
        var cx = Fix.Clamp(p.X, minX, maxX);
        var cy = Fix.Clamp(p.Y, minY, maxY);
        return (p - new FixVec2(cx, cy)).Length;
    }

    public TileCoord[] GetAccessTiles(TileMap map)
    {
        if (AccessTilesVersion == map.MajorVersion && AccessTiles.Length > 0) return AccessTiles;
        var list = new List<TileCoord>();
        for (int y = Origin.Y - 1; y <= Origin.Y + Size; y++)
        for (int x = Origin.X - 1; x <= Origin.X + Size; x++)
        {
            bool border = x == Origin.X - 1 || y == Origin.Y - 1 || x == Origin.X + Size || y == Origin.Y + Size;
            if (border && map.InBounds(x, y) && map.IsPassable(x, y)) list.Add(new TileCoord(x, y));
        }
        AccessTiles = list.ToArray();
        AccessTilesVersion = map.MajorVersion;
        return AccessTiles;
    }

    public TileCoord NearestAccessTile(TileMap map, FixVec2 from)
    {
        var tiles = GetAccessTiles(map);
        if (tiles.Length == 0) return new TileCoord(Origin.X + Size / 2, Origin.Y + Size);
        var best = tiles[0];
        var bestD = FixVec2.DistanceSquared(best.Center, from);
        for (int i = 1; i < tiles.Length; i++)
        {
            var d = FixVec2.DistanceSquared(tiles[i].Center, from);
            if (d < bestD)
            {
                bestD = d;
                best = tiles[i];
            }
        }
        return best;
    }
}

public sealed class RoadSite : ConstructionTarget
{
    public TileCoord Tile;
    /// <summary>The road to lay. <see cref="RoadKind.None"/> means the existing road is being torn up.</summary>
    public RoadKind Kind;
    public bool IsRemoval => Kind == RoadKind.None;

    public override bool NeedsConstruction => true;
    public override FixVec2 WorkPoint => Tile.Center;
}
