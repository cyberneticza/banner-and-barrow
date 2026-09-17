using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;

namespace BannerAndBarrow.Simulation.World;

/// <summary>The terrain grid, roads, resource amounts and building occupancy. Implements the pathfinding view.</summary>
public sealed class TileMap : INavGrid
{
    private readonly MovementConfig _movement;

    public int Width { get; }
    public int Height { get; }
    public Terrain[] Terrain { get; }
    public RoadKind[] Roads { get; }
    /// <summary>Entity id of the building covering the tile, 0 if none.</summary>
    public int[] BuildingIds { get; }
    /// <summary>Stone/Iron deposit type on the tile (only those two resources), or null.</summary>
    public ResourceType?[] Deposits { get; }
    /// <summary>Remaining wood on a forest tile, or remaining ore in a deposit.</summary>
    public int[] ResourceAmount { get; }
    /// <summary>Tick at which a felled/planted tile becomes forest again (0 = not regrowing).</summary>
    public long[] RegrowAtTick { get; }
    /// <summary>Owner of a Gate on the tile (it is passable only for them), or -1.</summary>
    public sbyte[] GateOwner { get; }
    /// <summary>Tick at which a sown Field is ripe, 0 when the tile has no crop.</summary>
    public long[] CropRipeTick { get; }
    /// <summary>Tick the crop was sown (for drawing its growth).</summary>
    public long[] CropSownTick { get; }

    public int MajorVersion { get; private set; }
    public int MinorVersion { get; private set; }

    /// <summary>Top-left tiles for each Player's Keep, chosen by the generator.</summary>
    public TileCoord[] KeepSites { get; internal set; } = Array.Empty<TileCoord>();
    /// <summary>Centre tiles of the guaranteed passes through the central ridge.</summary>
    public List<TileCoord> Chokepoints { get; } = new();
    public int Seed { get; internal set; }

    public TileMap(int width, int height, MovementConfig movement)
    {
        Width = width;
        Height = height;
        _movement = movement;
        int n = width * height;
        Terrain = new Terrain[n];
        Roads = new RoadKind[n];
        BuildingIds = new int[n];
        Deposits = new ResourceType?[n];
        ResourceAmount = new int[n];
        RegrowAtTick = new long[n];
        CropRipeTick = new long[n];
        CropSownTick = new long[n];
        GateOwner = new sbyte[n];
        Array.Fill(GateOwner, (sbyte)-1);
    }

    public bool HasCrop(int index) => CropRipeTick[index] != 0;
    public bool IsRipe(int index, long tick) => CropRipeTick[index] != 0 && tick >= CropRipeTick[index];

    public void Sow(int index, long tick, long ripeTick)
    {
        CropSownTick[index] = tick;
        CropRipeTick[index] = ripeTick;
    }

    public void ClearCrop(int index)
    {
        CropSownTick[index] = 0;
        CropRipeTick[index] = 0;
    }

    public int Index(int x, int y) => y * Width + x;
    public int Index(TileCoord t) => t.Y * Width + t.X;
    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public bool InBounds(TileCoord t) => InBounds(t.X, t.Y);

    public Terrain TerrainAt(TileCoord t) => Terrain[Index(t)];

    public static bool IsTerrainPassable(Terrain t) => t != Data.Terrain.Water && t != Data.Terrain.Mountain;

    public bool IsPassable(int x, int y)
    {
        int i = y * Width + x;
        return IsTerrainPassable(Terrain[i]) && BuildingIds[i] == 0;
    }

    /// <summary>As <see cref="IsPassable"/>, but a Gate is open to its owner.</summary>
    public bool IsPassableFor(int x, int y, int player)
    {
        int i = y * Width + x;
        if (!IsTerrainPassable(Terrain[i])) return false;
        return BuildingIds[i] == 0 || GateOwner[i] == player;
    }

    public Fix GetSpeedMultiplier(int x, int y, MovementClass movementClass)
    {
        int i = y * Width + x;
        var speeds = movementClass == MovementClass.Civilian ? _movement.Civilian : _movement.Military;
        switch (Roads[i])
        {
            case RoadKind.Dirt: return speeds.Dirt;
            case RoadKind.Stone: return speeds.Stone;
        }
        return Terrain[i] switch
        {
            Data.Terrain.Forest => speeds.OffRoad * _movement.ForestMultiplier,
            Data.Terrain.Hills => speeds.OffRoad * _movement.HillsMultiplier,
            _ => speeds.OffRoad,
        };
    }

    public Fix GetRouteCostMultiplier(int x, int y, MovementClass movementClass) =>
        movementClass == MovementClass.Civilian && Roads[y * Width + x] == RoadKind.None ? _movement.CivilianOffRoadRouteCost : Fix.One;

    public void SetBuilding(TileCoord origin, int size, int buildingId, sbyte gateOwner = -1)
    {
        for (int y = origin.Y; y < origin.Y + size; y++)
        for (int x = origin.X; x < origin.X + size; x++)
            if (InBounds(x, y))
            {
                BuildingIds[Index(x, y)] = buildingId;
                GateOwner[Index(x, y)] = buildingId == 0 ? (sbyte)-1 : gateOwner;
                ClearCrop(Index(x, y));
            }
        MajorVersion++;
    }

    public void SetRoad(TileCoord tile, RoadKind kind)
    {
        Roads[Index(tile)] = kind;
        if (kind != RoadKind.None) ClearCrop(Index(tile));
        MajorVersion++;
    }

    public void FellTree(TileCoord tile, long regrowAt)
    {
        int i = Index(tile);
        Terrain[i] = Data.Terrain.Grass;
        ResourceAmount[i] = 0;
        RegrowAtTick[i] = regrowAt;
        MinorVersion++;
    }

    public void PlantSapling(TileCoord tile, long regrowAt)
    {
        RegrowAtTick[Index(tile)] = regrowAt;
    }

    public void RegrowTree(int index, int wood)
    {
        Terrain[index] = Data.Terrain.Forest;
        ResourceAmount[index] = wood;
        RegrowAtTick[index] = 0;
        MinorVersion++;
    }

    /// <summary>For map generation and tests: marks the grid as changed.</summary>
    public void Touch() => MajorVersion++;
}
