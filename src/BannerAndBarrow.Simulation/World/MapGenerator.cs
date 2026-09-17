using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.World;

/// <summary>
/// Seeded procedural map: fractal value noise for elevation and moisture, mirrored through the map centre
/// so both Players get an identical (rotated) start, a central mountain ridge broken by guaranteed passes,
/// resource clusters, and a connectivity check between the two Keeps.
/// </summary>
public static class MapGenerator
{
    public const int KeepSize = 4;

    public static TileMap Generate(GameConfig config, int seed)
    {
        var mapCfg = config.Map;
        TileMap? map = null;
        for (int attempt = 0; attempt < System.Math.Max(1, mapCfg.MaxGenerationAttempts); attempt++)
        {
            map = GenerateOnce(config, seed + attempt * 7919);
            if (KeepsConnected(map)) return map;
        }

        // Fallback: carve a straight grass corridor between the Keeps (symmetric under the mirror).
        CarveCorridor(map!);
        return map!;
    }

    private static TileMap GenerateOnce(GameConfig config, int seed)
    {
        var cfg = config.Map;
        int w = cfg.Width, h = cfg.Height;
        var map = new TileMap(w, h, config.Movement) { Seed = seed };
        var rng = new DeterministicRandom((ulong)(uint)seed);

        // 1. Base terrain from noise, mirrored through the centre (tile i and tile n-1-i are identical).
        int n = w * h;
        for (int i = 0; i < n; i++)
        {
            int mirror = n - 1 - i;
            if (mirror < i)
            {
                map.Terrain[i] = map.Terrain[mirror];
                continue;
            }
            int x = i % w, y = i / w;
            var elevation = FractalNoise(x, y, seed, cfg.NoiseCellSize, cfg.NoiseOctaves);
            var moisture = FractalNoise(x, y, seed ^ 0x5bd1e995, cfg.NoiseCellSize, cfg.NoiseOctaves);
            map.Terrain[i] =
                elevation < cfg.WaterLevel ? Terrain.Water :
                elevation > cfg.MountainLevel ? Terrain.Mountain :
                elevation > cfg.HillLevel ? Terrain.Hills :
                moisture > cfg.ForestLevel ? Terrain.Forest :
                Terrain.Grass;
        }

        // 2. Central ridge along the anti-diagonal with evenly spaced passes (chokepoints).
        int diag = w - 1;
        var gapCenters = new List<int>();
        int count = System.Math.Max(1, cfg.ChokepointCount);
        int spacing = 2 * diag / (count + 1);
        for (int k = 0; k < count; k++) gapCenters.Add((2 * k - (count - 1)) * spacing / 2);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int along = x - y;
            int across = System.Math.Abs(x + y - diag);
            bool inGap = gapCenters.Any(c => System.Math.Abs(along - c) <= cfg.ChokepointHalfWidth * 2);
            if (inGap && across <= cfg.RidgeHalfThickness + 4)
            {
                map.Terrain[map.Index(x, y)] = Terrain.Grass;
            }
            else if (across <= cfg.RidgeHalfThickness)
            {
                map.Terrain[map.Index(x, y)] = Terrain.Mountain;
            }
        }
        foreach (var c in gapCenters)
        {
            int cx = (diag + c) / 2, cy = (diag - c) / 2;
            if (map.InBounds(cx, cy)) map.Chokepoints.Add(new TileCoord(cx, cy));
        }

        // 3. Keep sites in opposite corners, with cleared ground around them.
        var keep0 = new TileCoord(cfg.KeepInset, cfg.KeepInset);
        var keep1 = new TileCoord(w - KeepSize - cfg.KeepInset, h - KeepSize - cfg.KeepInset);
        map.KeepSites = new[] { keep0, keep1 };
        var keepCenter0 = new TileCoord(keep0.X + KeepSize / 2, keep0.Y + KeepSize / 2);
        FillCircleMirrored(map, keepCenter0, cfg.KeepClearRadius, Terrain.Grass, onlyImpassableOrForest: false);

        // 4. Guaranteed starting resources near each Keep (placed for Player 0, mirrored for Player 1).
        FillCircleMirrored(map, Offset(keepCenter0, 10, -2), 3, Terrain.Forest, onlyImpassableOrForest: false);
        FillCircleMirrored(map, Offset(keepCenter0, -3, 10), 2, Terrain.Forest, onlyImpassableOrForest: false);
        PlaceDepositMirrored(map, config, Offset(keepCenter0, 7, 9), ResourceType.Stone, cfg.ClusterRadius);
        PlaceDepositMirrored(map, config, Offset(keepCenter0, 12, 8), ResourceType.Iron, 1);

        // 5. Extra clusters scattered on Player 0's half.
        for (int i = 0; i < cfg.StoneClustersPerSide; i++)
            PlaceDepositMirrored(map, config, RandomHalfTile(map, rng, keepCenter0), ResourceType.Stone, cfg.ClusterRadius);
        for (int i = 0; i < cfg.IronClustersPerSide; i++)
            PlaceDepositMirrored(map, config, RandomHalfTile(map, rng, keepCenter0), ResourceType.Iron, cfg.ClusterRadius - 1);

        // 6. Wood on every forest tile.
        for (int i = 0; i < n; i++)
            if (map.Terrain[i] == Terrain.Forest) map.ResourceAmount[i] = config.Economy.WoodPerForestTile;

        // Keep footprints must be clear of resources.
        foreach (var site in map.KeepSites)
            for (int y = site.Y - 1; y <= site.Y + KeepSize; y++)
            for (int x = site.X - 1; x <= site.X + KeepSize; x++)
                if (map.InBounds(x, y))
                {
                    int i = map.Index(x, y);
                    map.Terrain[i] = Terrain.Grass;
                    map.Deposits[i] = null;
                    map.ResourceAmount[i] = 0;
                }

        map.Touch();
        return map;
    }

    private static TileCoord Offset(TileCoord t, int dx, int dy) => new(t.X + dx, t.Y + dy);

    private static TileCoord RandomHalfTile(TileMap map, DeterministicRandom rng, TileCoord avoid)
    {
        for (int tries = 0; tries < 200; tries++)
        {
            int x = rng.Next(4, map.Width - 4), y = rng.Next(4, map.Height - 4);
            if (x + y >= map.Width - 12) continue; // stay on Player 0's side of the ridge
            if (TileCoord.DistanceSquared(new TileCoord(x, y), avoid) < 12 * 12) continue;
            if (!TileMap.IsTerrainPassable(map.Terrain[map.Index(x, y)])) continue;
            return new TileCoord(x, y);
        }
        return new TileCoord(map.Width / 4, map.Height / 4);
    }

    private static void FillCircleMirrored(TileMap map, TileCoord center, int radius, Terrain terrain, bool onlyImpassableOrForest)
    {
        for (int y = center.Y - radius; y <= center.Y + radius; y++)
        for (int x = center.X - radius; x <= center.X + radius; x++)
        {
            if (!map.InBounds(x, y)) continue;
            if ((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) > radius * radius) continue;
            int i = map.Index(x, y);
            if (onlyImpassableOrForest && TileMap.IsTerrainPassable(map.Terrain[i]) && map.Terrain[i] != Terrain.Forest) continue;
            SetMirrored(map, i, terrain, null, 0);
        }
    }

    private static void PlaceDepositMirrored(TileMap map, GameConfig config, TileCoord center, ResourceType type, int radius)
    {
        int amount = type == ResourceType.Stone ? config.Economy.StonePerDeposit : config.Economy.IronPerDeposit;
        radius = System.Math.Max(0, radius);
        for (int y = center.Y - radius; y <= center.Y + radius; y++)
        for (int x = center.X - radius; x <= center.X + radius; x++)
        {
            if (!map.InBounds(x, y)) continue;
            if ((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y) > radius * radius) continue;
            int i = map.Index(x, y);
            SetMirrored(map, i, Terrain.Hills, type, amount);
        }
    }

    private static void SetMirrored(TileMap map, int index, Terrain terrain, ResourceType? deposit, int amount)
    {
        int mirror = map.Width * map.Height - 1 - index;
        foreach (var i in new[] { index, mirror })
        {
            map.Terrain[i] = terrain;
            map.Deposits[i] = deposit;
            map.ResourceAmount[i] = amount;
        }
    }

    private static Fix FractalNoise(int x, int y, int seed, int cellSize, int octaves)
    {
        Fix sum = Fix.Zero, amplitudeSum = Fix.Zero, amplitude = Fix.One;
        int size = System.Math.Max(2, cellSize);
        for (int o = 0; o < System.Math.Max(1, octaves); o++)
        {
            sum += ValueNoise(x, y, seed + o * 1013, size) * amplitude;
            amplitudeSum += amplitude;
            amplitude = amplitude * Fix.Half;
            size = System.Math.Max(2, size / 2);
        }
        return sum / amplitudeSum;
    }

    private static Fix ValueNoise(int x, int y, int seed, int cellSize)
    {
        int cx = x / cellSize, cy = y / cellSize;
        Fix fx = Fix.FromRatio(x % cellSize, cellSize), fy = Fix.FromRatio(y % cellSize, cellSize);
        fx = SmoothStep(fx);
        fy = SmoothStep(fy);
        var a = IntHash.HashToFix(cx, cy, seed);
        var b = IntHash.HashToFix(cx + 1, cy, seed);
        var c = IntHash.HashToFix(cx, cy + 1, seed);
        var d = IntHash.HashToFix(cx + 1, cy + 1, seed);
        return Fix.Lerp(Fix.Lerp(a, b, fx), Fix.Lerp(c, d, fx), fy);
    }

    private static Fix SmoothStep(Fix t) => t * t * (Fix.FromInt(3) - t * 2);

    public static bool KeepsConnected(TileMap map)
    {
        if (map.KeepSites.Length < 2) return true;
        var start = new TileCoord(map.KeepSites[0].X + KeepSize / 2, map.KeepSites[0].Y + KeepSize + 1);
        var goal = new TileCoord(map.KeepSites[1].X + KeepSize / 2, map.KeepSites[1].Y - 2);
        var visited = new bool[map.Width * map.Height];
        var queue = new Queue<TileCoord>();
        queue.Enqueue(start);
        visited[map.Index(start)] = true;
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (TileCoord.ChebyshevDistance(t, goal) <= 2) return true;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = t.X + dx, ny = t.Y + dy;
                if (!map.InBounds(nx, ny)) continue;
                int i = map.Index(nx, ny);
                if (visited[i] || !map.IsPassable(nx, ny)) continue;
                visited[i] = true;
                queue.Enqueue(new TileCoord(nx, ny));
            }
        }
        return false;
    }

    private static void CarveCorridor(TileMap map)
    {
        var a = map.KeepSites[0];
        var b = map.KeepSites[1];
        int steps = System.Math.Max(System.Math.Abs(b.X - a.X), System.Math.Abs(b.Y - a.Y));
        for (int s = 0; s <= steps; s++)
        {
            int x = a.X + (b.X - a.X) * s / steps, y = a.Y + (b.Y - a.Y) * s / steps;
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
                if (map.InBounds(x + ox, y + oy))
                {
                    int i = map.Index(x + ox, y + oy);
                    if (!TileMap.IsTerrainPassable(map.Terrain[i])) map.Terrain[i] = Terrain.Grass;
                }
        }
        map.Touch();
    }
}
