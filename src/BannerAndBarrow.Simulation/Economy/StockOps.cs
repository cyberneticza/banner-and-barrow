using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Economy;

public static class StockOps
{
    public static int[] ToArray(Dictionary<ResourceType, int>? amounts)
    {
        var result = new int[Resources.Count];
        if (amounts == null) return result;
        foreach (var (r, a) in amounts) result[(int)r] = a;
        return result;
    }

    public static Building? Nearest(GameState state, int player, FixVec2 position, Func<Building, bool> filter)
    {
        Building? best = null;
        Fix bestD = Fix.MaxValue;
        foreach (var b in state.Buildings.Values)
        {
            if (b.Owner != player || !b.IsStorehouseLike || !b.IsActive || b.Store == null || !filter(b)) continue;
            var d = FixVec2.DistanceSquared(b.Center, position);
            if (d < bestD)
            {
                bestD = d;
                best = b;
            }
        }
        return best;
    }

    public static Building? NearestStorehouse(GameState state, int player, FixVec2 position) =>
        Nearest(state, player, position, _ => true);

    public static Building? NearestWithAvailable(GameState state, int player, FixVec2 position, ResourceType r, int min = 1) =>
        Nearest(state, player, position, b => b.Store!.Available(r) >= min);

    public static Building? NearestWithCapacity(GameState state, int player, FixVec2 position, ResourceType r, int amount) =>
        Nearest(state, player, position, b => b.Store!.FreeCapacity(r) >= amount);

    public static int TotalAvailable(GameState state, int player, ResourceType r)
    {
        int total = 0;
        foreach (var s in state.StorehousesOf(player)) total += System.Math.Max(0, s.Store!.Available(r));
        return total;
    }

    public static bool CanAfford(GameState state, int player, int[] cost)
    {
        for (int i = 0; i < Resources.Count; i++)
            if (cost[i] > 0 && TotalAvailable(state, player, (ResourceType)i) < cost[i]) return false;
        return true;
    }

    public static int[] Missing(GameState state, int player, int[] cost)
    {
        var missing = new int[Resources.Count];
        for (int i = 0; i < Resources.Count; i++)
            missing[i] = System.Math.Max(0, cost[i] - TotalAvailable(state, player, (ResourceType)i));
        return missing;
    }

    /// <summary>
    /// Takes <paramref name="cost"/> from the Player's Storehouses, nearest first. All-or-nothing.
    /// SIMPLIFICATION: recruitment and upgrades draw instantly rather than being carried.
    /// </summary>
    public static bool TryWithdraw(GameState state, int player, FixVec2 near, int[] cost)
    {
        if (!CanAfford(state, player, cost)) return false;
        var stores = state.StorehousesOf(player).OrderBy(b => FixVec2.DistanceSquared(b.Center, near)).ThenBy(b => b.Id).ToList();
        for (int i = 0; i < Resources.Count; i++)
        {
            int remaining = cost[i];
            foreach (var s in stores)
            {
                if (remaining <= 0) break;
                int take = System.Math.Min(remaining, System.Math.Max(0, s.Store!.Available((ResourceType)i)));
                s.Store.Stock[i] -= take;
                remaining -= take;
            }
        }
        return true;
    }

    public static string Describe(int[] cost)
    {
        var parts = new List<string>();
        for (int i = 0; i < Resources.Count; i++)
            if (cost[i] > 0) parts.Add($"{cost[i]} {(ResourceType)i}");
        return parts.Count == 0 ? "free" : string.Join(", ", parts);
    }
}
