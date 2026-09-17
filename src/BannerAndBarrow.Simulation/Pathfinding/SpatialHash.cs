using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Pathfinding;

/// <summary>Uniform grid bucketing for neighbour queries. Stores caller-defined integer handles.</summary>
public sealed class SpatialHash
{
    private readonly List<int>[] _buckets;
    private readonly List<int> _usedBuckets = new();
    private readonly int _cellSize;
    private readonly int _cols;
    private readonly int _rows;

    public SpatialHash(int worldWidth, int worldHeight, int cellSize)
    {
        _cellSize = System.Math.Max(1, cellSize);
        _cols = (worldWidth + _cellSize - 1) / _cellSize;
        _rows = (worldHeight + _cellSize - 1) / _cellSize;
        _buckets = new List<int>[_cols * _rows];
        for (int i = 0; i < _buckets.Length; i++) _buckets[i] = new List<int>(4);
    }

    public void Clear()
    {
        foreach (var b in _usedBuckets) _buckets[b].Clear();
        _usedBuckets.Clear();
    }

    public void Insert(int handle, FixVec2 position)
    {
        int b = BucketIndex(position.X.FloorToInt() / _cellSize, position.Y.FloorToInt() / _cellSize);
        if (_buckets[b].Count == 0) _usedBuckets.Add(b);
        _buckets[b].Add(handle);
    }

    /// <summary>Adds every handle whose bucket overlaps the query square. Caller filters by exact distance.</summary>
    public void Query(FixVec2 center, Fix radius, List<int> results)
    {
        int minX = (center.X - radius).FloorToInt() / _cellSize;
        int maxX = (center.X + radius).FloorToInt() / _cellSize;
        int minY = (center.Y - radius).FloorToInt() / _cellSize;
        int maxY = (center.Y + radius).FloorToInt() / _cellSize;
        minX = IntMath.Clamp(minX, 0, _cols - 1);
        maxX = IntMath.Clamp(maxX, 0, _cols - 1);
        minY = IntMath.Clamp(minY, 0, _rows - 1);
        maxY = IntMath.Clamp(maxY, 0, _rows - 1);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
            results.AddRange(_buckets[y * _cols + x]);
    }

    private int BucketIndex(int cx, int cy) =>
        IntMath.Clamp(cy, 0, _rows - 1) * _cols + IntMath.Clamp(cx, 0, _cols - 1);
}
