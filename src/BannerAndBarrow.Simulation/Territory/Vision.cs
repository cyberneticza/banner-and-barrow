using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Territory;

/// <summary>Bitmask per Tile of which Players can currently see it (fog of war).</summary>
public sealed class VisionMap
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Mask { get; }
    /// <summary>Bumped on every recompute so renderers can cache.</summary>
    public int Version { get; internal set; }

    public VisionMap(int width, int height)
    {
        Width = width;
        Height = height;
        Mask = new byte[width * height];
    }

    public bool IsVisible(int x, int y, int player) =>
        x >= 0 && y >= 0 && x < Width && y < Height && (Mask[y * Width + x] & (1 << player)) != 0;
}

/// <summary>What a Player last saw of an enemy building. Shown faded where the Player can't see now.</summary>
public sealed class BuildingSighting
{
    public int Id;
    public int Owner;
    public BuildingType Type;
    public TileCoord Origin;
    public int Size;
    public BuildingState State;
    public long SeenTick;

    public FixVec2 Center => new(Fix.FromInt(Origin.X) + Fix.FromRatio(Size, 2), Fix.FromInt(Origin.Y) + Fix.FromRatio(Size, 2));
}

/// <summary>
/// Fog of war. A Player sees their own Territory, around their Regiments and forts (their Presence, or past their
/// longest shot when that is further, so archers never shoot at what nobody can see), and the tiles around their own
/// buildings. Enemies that attack a Player are revealed to them for a few seconds wherever they stand. Enemy
/// buildings seen are remembered as they were last seen until the Player sees the spot again. AI and human Players
/// use the same rules.
/// </summary>
public static class VisionSystem
{
    public static void Update(GameState state)
    {
        var v = state.Vision;
        if (state.Territory.Version == state.VisionTerritoryVersion && state.Presence.LastComputedTick == state.VisionPresenceTick) return;
        Recompute(state);
    }

    /// <summary>How far a Regiment of this type sees: its Presence, or past its longest possible shot if that is further.</summary>
    public static int VisionRadius(GameState state, SoldierType type)
    {
        var cfg = state.Config;
        int radius = cfg.Territory.RegimentPresence;
        var ranged = cfg.Soldier(type).Ranged;
        if (ranged == null) return radius;
        var longest = ranged.Range * Fix.Max(Fix.One, cfg.Combat.Stance.HoldRangeMultiplier) * Fix.Max(Fix.One, cfg.Combat.HillsRangeMultiplier);
        return System.Math.Max(radius, longest.CeilToInt() + cfg.Territory.VisionBeyondRange);
    }

    /// <summary>Forts see their Presence, or past the longest shot any Soldier stationed inside could make.</summary>
    public static int FortVisionRadius(GameState state, Building fort)
    {
        var cfg = state.Config;
        var longest = cfg.Forts.MeleeMissile.Range;
        foreach (var (_, def) in cfg.Soldiers)
            if (def.Ranged != null && def.Ranged.Range > longest) longest = def.Ranged.Range;
        return System.Math.Max(PresenceSystem.PresenceRadius(state, fort), longest.CeilToInt() + cfg.Territory.VisionBeyondRange);
    }

    private static void Stamp(VisionMap v, FixVec2 center, int radius, int player)
    {
        int cx = center.X.FloorToInt(), cy = center.Y.FloorToInt();
        int r2 = radius * radius;
        byte bit = (byte)(1 << player);
        for (int y = System.Math.Max(0, cy - radius); y <= System.Math.Min(v.Height - 1, cy + radius); y++)
        for (int x = System.Math.Max(0, cx - radius); x <= System.Math.Min(v.Width - 1, cx + radius); x++)
        {
            int dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= r2) v.Mask[y * v.Width + x] |= bit;
        }
    }

    public static void Recompute(GameState state)
    {
        var v = state.Vision;
        var territory = state.Territory.Owner;
        var mask = v.Mask;
        for (int i = 0; i < mask.Length; i++)
        {
            sbyte owner = territory[i];
            mask[i] = (byte)(owner >= 0 ? 1 << owner : 0);
        }
        foreach (var r in state.Regiments.Values)
        {
            if (r.IsStationed || r.SoldierIds.Count == 0) continue;
            Stamp(v, state.RegimentCentroid(r), VisionRadius(state, r.Type), r.Owner);
        }
        foreach (var b in state.Buildings.Values)
            if (b.IsFort && b.State is not (BuildingState.UnderConstruction or BuildingState.Ruin))
                Stamp(v, b.Center, FortVisionRadius(state, b), b.Owner);
        foreach (var p in state.Players)
        {
            foreach (var (id, until) in p.RevealedRegiments.ToList())
                if (state.Tick >= until || !state.Regiments.ContainsKey(id)) p.RevealedRegiments.Remove(id);
        }
        foreach (var b in state.Buildings.Values)
        {
            // Own buildings (e.g. an Outpost site beyond the border) always see their surroundings a little.
            byte bit = (byte)(1 << b.Owner);
            for (int y = System.Math.Max(0, b.Origin.Y - 2); y < System.Math.Min(v.Height, b.Origin.Y + b.Size + 2); y++)
            for (int x = System.Math.Max(0, b.Origin.X - 2); x < System.Math.Min(v.Width, b.Origin.X + b.Size + 2); x++)
                mask[y * v.Width + x] |= bit;
        }
        v.Version++;
        state.VisionTerritoryVersion = state.Territory.Version;
        state.VisionPresenceTick = state.Presence.LastComputedTick;
        UpdateSightings(state);
    }

    private static void UpdateSightings(GameState state)
    {
        foreach (var player in state.Players)
        {
            var known = player.KnownBuildings;
            // Forget buildings whose spot is in view but which are gone.
            foreach (var sighting in known.Values.ToList())
            {
                if (!AnyVisible(state, player.Index, sighting.Origin, sighting.Size)) continue;
                if (state.GetBuilding(sighting.Id) == null) known.Remove(sighting.Id);
            }
            foreach (var b in state.Buildings.Values)
            {
                if (b.Owner == player.Index || !AnyVisible(state, player.Index, b.Origin, b.Size)) continue;
                Remember(state, player.Index, b);
            }
        }
    }

    /// <summary>Records what the Player can currently make out of an enemy building (e.g. a fort that just shot at them).</summary>
    public static void Remember(GameState state, int player, Building b)
    {
        var known = state.Players[player].KnownBuildings;
        if (!known.TryGetValue(b.Id, out var s))
        {
            s = new BuildingSighting { Id = b.Id };
            known[b.Id] = s;
        }
        s.Owner = b.Owner;
        s.Type = b.Type;
        s.Origin = b.Origin;
        s.Size = b.Size;
        s.State = b.State;
        s.SeenTick = state.Tick;
    }

    public static bool AnyVisible(GameState state, int player, TileCoord origin, int size)
    {
        if (!state.Config.Territory.FogOfWar) return true;
        for (int y = origin.Y; y < origin.Y + size; y++)
        for (int x = origin.X; x < origin.X + size; x++)
            if (state.Vision.IsVisible(x, y, player)) return true;
        return false;
    }
}
