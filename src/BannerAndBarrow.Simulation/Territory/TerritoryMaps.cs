using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Territory;

/// <summary>Owner of every Tile's Territory: a Player index, <see cref="None"/> or <see cref="Contested"/>.</summary>
public sealed class TerritoryMap
{
    public const sbyte None = -1;
    public const sbyte Contested = -2;

    public int Width { get; }
    public int Height { get; }
    public sbyte[] Owner { get; }
    public bool Dirty { get; set; } = true;
    public int Version { get; internal set; }

    public TerritoryMap(int width, int height)
    {
        Width = width;
        Height = height;
        Owner = new sbyte[width * height];
        Array.Fill(Owner, None);
    }

    public sbyte OwnerAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? Owner[y * Width + x] : None;
    public sbyte OwnerAt(TileCoord t) => OwnerAt(t.X, t.Y);
    public bool IsOwnedBy(TileCoord t, int player) => OwnerAt(t) == player;
    public bool IsEnemyOwned(TileCoord t, int player)
    {
        var o = OwnerAt(t);
        return o >= 0 && o != player;
    }
}

/// <summary>Bitmask per Tile of which Players have Presence there.</summary>
public sealed class PresenceMap
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Mask { get; }
    public long LastComputedTick { get; internal set; } = -1;

    public PresenceMap(int width, int height)
    {
        Width = width;
        Height = height;
        Mask = new byte[width * height];
    }

    public byte MaskAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? Mask[y * Width + x] : (byte)0;

    public bool HasPresence(TileCoord t, int player) => (MaskAt(t.X, t.Y) & (1 << player)) != 0;

    /// <summary>True if any Player other than <paramref name="player"/> has Presence on the tile.</summary>
    public bool HasEnemyPresence(TileCoord t, int player) => (MaskAt(t.X, t.Y) & ~(1 << player) & 0xFF) != 0;

    public void Clear() => Array.Clear(Mask);

    public void Stamp(FixVec2 center, int radius, int player)
    {
        int cx = center.X.FloorToInt(), cy = center.Y.FloorToInt();
        int r2 = radius * radius;
        byte bit = (byte)(1 << player);
        for (int y = System.Math.Max(0, cy - radius); y <= System.Math.Min(Height - 1, cy + radius); y++)
        for (int x = System.Math.Max(0, cx - radius); x <= System.Math.Min(Width - 1, cx + radius); x++)
        {
            int dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= r2) Mask[y * Width + x] |= bit;
        }
    }
}
