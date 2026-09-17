using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Territory;

namespace BannerAndBarrow.Simulation.AI;

/// <summary>Searches outward from a point for a legal building spot that keeps a walkable ring around it.</summary>
public static class PlacementFinder
{
    public static TileCoord? Find(GameState state, int player, BuildingType type, TileCoord near, int maxRadius, int minNodes = 0)
    {
        var def = state.Config.Building(type);
        for (int r = 0; r <= maxRadius; r++)
        for (int oy = -r; oy <= r; oy++)
        for (int ox = -r; ox <= r; ox++)
        {
            if (System.Math.Max(System.Math.Abs(ox), System.Math.Abs(oy)) != r) continue;
            var origin = new TileCoord(near.X + ox - def.Size / 2, near.Y + oy - def.Size / 2);
            if (!BuildRules.CanPlaceBuilding(state, player, type, origin).Ok) continue;
            if (!HasClearRing(state, origin, def.Size)) continue;
            if (minNodes > 0)
            {
                var center = new TileCoord(origin.X + def.Size / 2, origin.Y + def.Size / 2);
                if (WorkerSystem.CountNodesInRange(state, type, center) < minNodes) continue;
            }
            return origin;
        }
        return null;
    }

    /// <summary>Keeps one free tile around buildings so AI towns don't wall themselves in.</summary>
    private static bool HasClearRing(GameState state, TileCoord origin, int size)
    {
        var map = state.Map;
        for (int y = origin.Y - 1; y <= origin.Y + size; y++)
        for (int x = origin.X - 1; x <= origin.X + size; x++)
        {
            bool border = x == origin.X - 1 || y == origin.Y - 1 || x == origin.X + size || y == origin.Y + size;
            if (!border) continue;
            if (!map.InBounds(x, y)) return false;
            if (map.BuildingIds[map.Index(x, y)] != 0) return false;
        }
        return true;
    }

    /// <summary>Nearest tile holding the resource a gatherer needs, searched around <paramref name="from"/>.</summary>
    public static TileCoord? NearestResourceTile(GameState state, BuildingType gatherer, TileCoord from, int radius)
    {
        var map = state.Map;
        TileCoord? best = null;
        int bestD = int.MaxValue;
        for (int y = from.Y - radius; y <= from.Y + radius; y++)
        for (int x = from.X - radius; x <= from.X + radius; x++)
        {
            if (!map.InBounds(x, y)) continue;
            int i = map.Index(x, y);
            bool match = gatherer switch
            {
                BuildingType.Woodcutter => map.Terrain[i] == Terrain.Forest && map.ResourceAmount[i] > 0,
                BuildingType.Quarry => map.Deposits[i] == ResourceType.Stone && map.ResourceAmount[i] > 0,
                BuildingType.IronMine => map.Deposits[i] == ResourceType.Iron && map.ResourceAmount[i] > 0,
                _ => false,
            };
            if (!match) continue;
            int d = TileCoord.DistanceSquared(new TileCoord(x, y), from);
            if (d < bestD)
            {
                bestD = d;
                best = new TileCoord(x, y);
            }
        }
        return best;
    }
}
