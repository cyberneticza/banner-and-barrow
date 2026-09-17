using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Pathfinding;

/// <summary>
/// Identifies a shared flow field. Positive ids are entity goals (e.g. a building), negative ids are tile goals.
/// </summary>
public readonly record struct FlowGoalKey(long Id, MovementClass Class, int Player = -1)
{
    public static FlowGoalKey ForEntity(int entityId, MovementClass cls, int player = -1) => new(entityId, cls, player);
    public static FlowGoalKey ForTile(TileCoord tile, int mapWidth, MovementClass cls, int player = -1) =>
        new(-(tile.Y * (long)mapWidth + tile.X) - 1, cls, player);
}

/// <summary>
/// LRU cache of flow fields with a per-tick rebuild budget. When the map changes, stale fields keep
/// serving agents (they are almost always still correct) until the budget allows a rebuild. This avoids
/// frame spikes when a building is placed while dozens of fields are live.
/// </summary>
public sealed class FlowFieldCache
{
    private sealed class Entry
    {
        public required FlowField Field;
        public required TileCoord[] Goals;
        public long LastUsedTick;
        public bool Queued;
    }

    private readonly INavGrid _grid;
    private readonly Dictionary<FlowGoalKey, Entry> _entries = new();
    private readonly Queue<FlowGoalKey> _rebuildQueue = new();
    private long _tick;

    public int Capacity { get; }
    public int RebuildBudgetPerTick { get; }
    public int MinorStaleTicks { get; }
    public int Count => _entries.Count;
    public int TotalBuilds { get; private set; }
    public int BuildsThisTick { get; private set; }

    public FlowFieldCache(INavGrid grid, int capacity, int rebuildBudgetPerTick, int minorStaleTicks)
    {
        _grid = grid;
        Capacity = System.Math.Max(4, capacity);
        RebuildBudgetPerTick = System.Math.Max(1, rebuildBudgetPerTick);
        MinorStaleTicks = minorStaleTicks;
    }

    public bool TryPeek(FlowGoalKey key, out FlowField? field)
    {
        if (_entries.TryGetValue(key, out var e))
        {
            field = e.Field;
            return true;
        }
        field = null;
        return false;
    }

    public IEnumerable<(FlowGoalKey Key, FlowField Field)> Entries => _entries.Select(kv => (kv.Key, kv.Value.Field));

    /// <summary>Returns the field for <paramref name="key"/>, building it immediately if it has never existed.</summary>
    public FlowField Get(FlowGoalKey key, TileCoord[] goals)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.LastUsedTick = _tick;
            if (!SameGoals(entry.Goals, goals))
            {
                entry.Goals = goals;
                Build(entry.Field, goals);
            }
            else if (IsStale(entry.Field) && !entry.Queued)
            {
                entry.Queued = true;
                _rebuildQueue.Enqueue(key);
            }
            return entry.Field;
        }

        if (_entries.Count >= Capacity) EvictLeastRecentlyUsed();
        var field = new FlowField(_grid.Width, _grid.Height, key.Class) { Player = key.Player };
        Build(field, goals);
        _entries[key] = new Entry { Field = field, Goals = goals, LastUsedTick = _tick };
        return field;
    }

    /// <summary>Call once per simulation tick before movement. Spends the rebuild budget on stale fields.</summary>
    public void BeginTick(long tick)
    {
        _tick = tick;
        BuildsThisTick = 0;
        int budget = RebuildBudgetPerTick;
        while (budget > 0 && _rebuildQueue.Count > 0)
        {
            var key = _rebuildQueue.Dequeue();
            if (!_entries.TryGetValue(key, out var entry)) continue;
            entry.Queued = false;
            if (!IsStale(entry.Field)) continue;
            Build(entry.Field, entry.Goals);
            budget--;
        }
    }

    public void Clear()
    {
        _entries.Clear();
        _rebuildQueue.Clear();
    }

    private bool IsStale(FlowField field) =>
        field.MajorVersion != _grid.MajorVersion ||
        (field.MinorVersion != _grid.MinorVersion && _tick - field.BuiltAtTick > MinorStaleTicks);

    private void Build(FlowField field, TileCoord[] goals)
    {
        FlowFieldBuilder.Rebuild(field, _grid, goals);
        field.BuiltAtTick = _tick;
        TotalBuilds++;
        BuildsThisTick++;
    }

    private void EvictLeastRecentlyUsed()
    {
        FlowGoalKey? oldest = null;
        long oldestTick = long.MaxValue;
        foreach (var (key, entry) in _entries)
        {
            if (entry.LastUsedTick < oldestTick)
            {
                oldestTick = entry.LastUsedTick;
                oldest = key;
            }
        }
        if (oldest.HasValue) _entries.Remove(oldest.Value);
    }

    private static bool SameGoals(TileCoord[] a, TileCoord[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
