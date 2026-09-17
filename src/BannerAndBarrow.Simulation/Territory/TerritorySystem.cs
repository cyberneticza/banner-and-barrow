using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Territory;

/// <summary>
/// Territory rules:
/// - A Tile is in a Player's reach if one of their active Storehouses (the Keep included) reaches it.
/// - A Tile under an existing building stays with the building's owner while that owner reaches it.
/// - Keep reach always wins.
/// - Otherwise a Tile reached by exactly one Player is theirs; by several it is Contested.
/// Buildings left outside their owner's Territory start Burning.
/// </summary>
public static class TerritorySystem
{
    public static void Update(GameState state)
    {
        if (state.Territory.Dirty) Recompute(state);
        UpdateBurningDeadlines(state);
    }

    public static void Recompute(GameState state)
    {
        var map = state.Map;
        var territory = state.Territory;
        int w = map.Width, h = map.Height;
        var reach = new byte[w * h];
        var keepReach = new byte[w * h];

        foreach (var b in state.Buildings.Values)
        {
            if (!b.IsStorehouseLike || b.State != BuildingState.Active || b.Store == null) continue;
            int radius = b.Store.Reach;
            var c = b.Center;
            int cx = c.X.FloorToInt(), cy = c.Y.FloorToInt();
            byte bit = (byte)(1 << b.Owner);
            for (int y = System.Math.Max(0, cy - radius); y <= System.Math.Min(h - 1, cy + radius); y++)
            for (int x = System.Math.Max(0, cx - radius); x <= System.Math.Min(w - 1, cx + radius); x++)
            {
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > radius * radius) continue;
                reach[y * w + x] |= bit;
                if (b.Store.IsKeep) keepReach[y * w + x] |= bit;
            }
        }

        for (int i = 0; i < w * h; i++)
        {
            byte mask = reach[i];
            sbyte owner = TerritoryMap.None;
            var building = state.GetBuilding(map.BuildingIds[i]);
            if (building != null && building.State != BuildingState.Ruin && (mask & (1 << building.Owner)) != 0)
            {
                owner = (sbyte)building.Owner;
            }
            else if (keepReach[i] != 0 && IsSingleBit(keepReach[i]))
            {
                owner = (sbyte)BitIndex(keepReach[i]);
            }
            else if (mask != 0)
            {
                owner = IsSingleBit(mask) ? (sbyte)BitIndex(mask) : TerritoryMap.Contested;
            }
            territory.Owner[i] = owner;
        }

        territory.Dirty = false;
        territory.Version++;
        UpdateBurningState(state);
    }

    private static void UpdateBurningState(GameState state)
    {
        var rebuildTicks = state.Config.SecondsToTicks(state.Config.Territory.RebuildWindowSeconds);
        foreach (var b in state.Buildings.Values)
        {
            if (b.State == BuildingState.Ruin || b.Type == BuildingType.Keep || b.IsFort) continue;
            bool inside = true;
            foreach (var t in b.Footprint())
            {
                if (state.Territory.OwnerAt(t) != b.Owner)
                {
                    inside = false;
                    break;
                }
            }

            if (!inside && !b.IsBurning)
            {
                b.IsBurning = true;
                b.BurningDeadlineTick = state.Tick + rebuildTicks;
                state.AddEvent(b.Owner, GameEventKind.Burning,
                    $"{b.Type} is burning! Repair the Storehouse within {state.Config.Territory.RebuildWindowSeconds}s.", b.Center);
            }
            else if (inside && b.IsBurning)
            {
                b.IsBurning = false;
                state.AddEvent(b.Owner, GameEventKind.Info, $"{b.Type} saved.", b.Center);
            }
        }
    }

    private static void UpdateBurningDeadlines(GameState state)
    {
        List<Building>? lost = null;
        foreach (var b in state.Buildings.Values)
            if (b.IsBurning && state.Tick >= b.BurningDeadlineTick)
                (lost ??= new List<Building>()).Add(b);
        if (lost == null) return;
        foreach (var b in lost)
        {
            state.AddEvent(b.Owner, GameEventKind.Warning, $"{b.Type} burned down.", b.Center);
            EntityOps.DestroyBuilding(state, b, allowRuin: false);
        }
    }

    private static bool IsSingleBit(byte v) => v != 0 && (v & (v - 1)) == 0;

    private static int BitIndex(byte v)
    {
        int i = 0;
        while ((v & 1) == 0)
        {
            v >>= 1;
            i++;
        }
        return i;
    }
}

public static class PresenceSystem
{
    public static void Update(GameState state, bool force = false)
    {
        var cfg = state.Config.Territory;
        if (!force && state.Presence.LastComputedTick >= 0 && state.Tick - state.Presence.LastComputedTick < cfg.PresenceRecomputeTicks) return;
        Recompute(state);
    }

    public static void Recompute(GameState state)
    {
        var cfg = state.Config.Territory;
        var presence = state.Presence;
        presence.Clear();
        foreach (var r in state.Regiments.Values)
        {
            if (r.IsStationed || r.SoldierIds.Count == 0) continue;
            presence.Stamp(state.RegimentCentroid(r), cfg.RegimentPresence, r.Owner);
        }
        foreach (var b in state.Buildings.Values)
        {
            if (!b.IsFort || b.State == BuildingState.UnderConstruction || b.State == BuildingState.Ruin) continue;
            presence.Stamp(b.Center, b.Type == BuildingType.Stronghold ? cfg.StrongholdPresence : cfg.OutpostPresence, b.Owner);
        }
        presence.LastComputedTick = state.Tick;
    }

    public static int PresenceRadius(GameState state, Building b) =>
        b.Type == BuildingType.Stronghold ? state.Config.Territory.StrongholdPresence : state.Config.Territory.OutpostPresence;
}
