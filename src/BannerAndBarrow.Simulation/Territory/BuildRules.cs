using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Territory;

public readonly record struct PlacementResult(bool Ok, string Reason)
{
    public static readonly PlacementResult Success = new(true, "");
    public static PlacementResult Fail(string reason) => new(false, reason);
}

/// <summary>Where Players may build. Shared by the player UI, the AI and command validation.</summary>
public static class BuildRules
{
    /// <summary>Walls and Gates: one tile, dragged in lines, and raised over forest as well as open ground.</summary>
    public static bool IsWallPiece(BuildingType type) => type is BuildingType.Wall or BuildingType.Gate;

    public static PlacementResult CanPlaceBuilding(GameState state, int player, BuildingType type, TileCoord origin)
    {
        var def = state.Config.Building(type);
        if (!def.Placeable) return PlacementResult.Fail($"{type} cannot be placed directly.");
        var map = state.Map;
        if (IsWallPiece(type)) return CanPlaceWallPiece(state, player, origin);

        for (int y = origin.Y; y < origin.Y + def.Size; y++)
        for (int x = origin.X; x < origin.X + def.Size; x++)
        {
            if (!map.InBounds(x, y)) return PlacementResult.Fail("Outside the map.");
            int i = map.Index(x, y);
            var terrain = map.Terrain[i];
            if (terrain is Terrain.Water or Terrain.Mountain) return PlacementResult.Fail("Blocked terrain.");
            if (terrain == Terrain.Forest) return PlacementResult.Fail("Clear the forest first.");
            if (map.BuildingIds[i] != 0) return PlacementResult.Fail("Occupied.");
            if (map.Deposits[i] != null) return PlacementResult.Fail("On a resource deposit.");
            if (map.Roads[i] != RoadKind.None) return PlacementResult.Fail("On a road.");
            var tile = new TileCoord(x, y);
            if (state.Presence.HasEnemyPresence(tile, player)) return PlacementResult.Fail("Enemy Presence blocks building here.");

            if (type == BuildingType.Outpost)
            {
                if (state.Territory.IsEnemyOwned(tile, player)) return PlacementResult.Fail("Cannot build in enemy Territory.");
            }
            else if (state.Territory.OwnerAt(tile) != player)
            {
                return PlacementResult.Fail("Outside your Territory.");
            }
        }

        // Workers only need to reach one edge, but they must be able to reach one.
        bool reachable = false, anyEdge = false;
        var keep = state.GetKeep(player);
        for (int y = origin.Y - 1; y <= origin.Y + def.Size && !reachable; y++)
        for (int x = origin.X - 1; x <= origin.X + def.Size && !reachable; x++)
        {
            bool border = x == origin.X - 1 || y == origin.Y - 1 || x == origin.X + def.Size || y == origin.Y + def.Size;
            if (!border || !map.InBounds(x, y) || !map.IsPassable(x, y)) continue;
            anyEdge = true;
            reachable = keep == null || state.CanWorkersReach(player, new TileCoord(x, y));
        }
        if (!anyEdge) return PlacementResult.Fail("No free edge: Workers need to reach at least one side.");
        if (!reachable) return PlacementResult.Fail("Workers can't walk there from your Keep.");

        if (type == BuildingType.Outpost)
        {
            var center = new FixVec2(Fix.FromInt(origin.X) + Fix.FromRatio(def.Size, 2), Fix.FromInt(origin.Y) + Fix.FromRatio(def.Size, 2));
            var minSpacing = Fix.FromInt(state.Config.Territory.OutpostMinSpacing);
            foreach (var b in state.Buildings.Values)
            {
                if (!b.IsFort) continue;
                if (FixVec2.DistanceSquared(b.Center, center) < minSpacing * minSpacing)
                    return PlacementResult.Fail("Too close to another Outpost.");
            }
        }

        return PlacementResult.Success;
    }

    /// <summary>
    /// A wall may go anywhere you could walk and that isn't enemy ground: over grass, hills and forest (the trees
    /// come down), but not water, mountains, roads, resource deposits or other buildings.
    /// </summary>
    private static PlacementResult CanPlaceWallPiece(GameState state, int player, TileCoord tile)
    {
        var map = state.Map;
        if (!map.InBounds(tile)) return PlacementResult.Fail("Outside the map.");
        int i = map.Index(tile);
        if (!World.TileMap.IsTerrainPassable(map.Terrain[i])) return PlacementResult.Fail("Walls need ground you could walk on.");
        if (map.BuildingIds[i] != 0) return PlacementResult.Fail("Occupied.");
        if (map.Deposits[i] != null) return PlacementResult.Fail("On a resource deposit.");
        if (map.Roads[i] != RoadKind.None) return PlacementResult.Fail("On a road.");
        if (state.Presence.HasEnemyPresence(tile, player)) return PlacementResult.Fail("Enemy Presence blocks building here.");
        if (state.Territory.IsEnemyOwned(tile, player)) return PlacementResult.Fail("Cannot build in enemy Territory.");
        return PlacementResult.Success;
    }

    public static PlacementResult CanPlaceRoad(GameState state, int player, TileCoord tile, RoadKind kind)
    {
        var map = state.Map;
        if (!map.InBounds(tile)) return PlacementResult.Fail("Outside the map.");
        int i = map.Index(tile);
        if (!World.TileMap.IsTerrainPassable(map.Terrain[i])) return PlacementResult.Fail("Blocked terrain.");
        if (map.BuildingIds[i] != 0) return PlacementResult.Fail("Occupied.");
        if (map.Roads[i] >= kind) return PlacementResult.Fail("Road already there.");
        if (state.Presence.HasEnemyPresence(tile, player)) return PlacementResult.Fail("Enemy Presence blocks building here.");
        if (state.Territory.IsEnemyOwned(tile, player)) return PlacementResult.Fail("Cannot build in enemy Territory.");
        if (kind == RoadKind.Stone && state.Territory.OwnerAt(tile) != player)
            return PlacementResult.Fail("Stone Roads need your own Territory.");
        foreach (var site in state.RoadSites.Values)
            if (site.Tile == tile) return PlacementResult.Fail("Road already planned.");
        return PlacementResult.Success;
    }

    /// <summary>Building cost for this Player at this spot. Storehouses get more expensive with count and distance.</summary>
    public static int[] GetCost(GameState state, int player, BuildingType type, TileCoord origin)
    {
        var def = state.Config.Building(type);
        var cost = new int[Resources.Count];
        foreach (var (r, amount) in def.Cost) cost[(int)r] = amount;
        if (type != BuildingType.Storehouse) return cost;

        var cfg = state.Config.Territory;
        int owned = state.Buildings.Values.Count(b => b.Owner == player && b.Type == BuildingType.Storehouse && b.State != BuildingState.Ruin);
        Fix factor = Fix.One;
        for (int i = 0; i < owned; i++) factor *= cfg.StorehouseCostMultiplier;
        var keep = state.GetKeep(player);
        if (keep != null)
        {
            var center = new FixVec2(Fix.FromInt(origin.X) + Fix.FromRatio(def.Size, 2), Fix.FromInt(origin.Y) + Fix.FromRatio(def.Size, 2));
            factor *= Fix.One + cfg.StorehouseCostDistanceWeight * FixVec2.Distance(center, keep.Center);
        }
        for (int i = 0; i < cost.Length; i++) cost[i] = (Fix.FromInt(cost[i]) * factor).CeilToInt();
        return cost;
    }

    public static int[] GetRoadCost(RoadKind kind)
    {
        var cost = new int[Resources.Count];
        if (kind == RoadKind.Stone) cost[(int)ResourceType.Stone] = 1;
        return cost;
    }

    public static PlacementResult CanRepairRuin(GameState state, int player, Building ruin)
    {
        if (ruin.Owner != player) return PlacementResult.Fail("Not your Ruin.");
        if (ruin.State != BuildingState.Ruin) return PlacementResult.Fail("Not a Ruin.");
        foreach (var t in ruin.Footprint())
            if (state.Presence.HasEnemyPresence(t, player))
                return PlacementResult.Fail("Drive the enemy away before repairing.");
        return PlacementResult.Success;
    }
}
