using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.World;

/// <summary>
/// Which walkable region each tile belongs to, so rules can ask "can Workers get there at all?" without a
/// pathfinding query. Recomputed lazily (a flood fill) whenever the map's <see cref="TileMap.MajorVersion"/> changes.
/// </summary>
public sealed class Connectivity
{
    private readonly TileMap _map;
    private readonly int[] _component;
    private readonly int[] _queue;
    private int _version = -1;

    public Connectivity(TileMap map)
    {
        _map = map;
        _component = new int[map.Width * map.Height];
        _queue = new int[map.Width * map.Height];
    }

    /// <summary>Region id of a tile, 0 for impassable or out of bounds.</summary>
    public int ComponentAt(TileCoord t)
    {
        if (!_map.InBounds(t)) return 0;
        Refresh();
        return _component[_map.Index(t)];
    }

    private void Refresh()
    {
        if (_version == _map.MajorVersion) return;
        _version = _map.MajorVersion;
        Array.Clear(_component);
        int w = _map.Width, h = _map.Height, next = 0;
        for (int start = 0; start < _component.Length; start++)
        {
            if (_component[start] != 0 || !_map.IsPassable(start % w, start / w)) continue;
            next++;
            int head = 0, tail = 0;
            _queue[tail++] = start;
            _component[start] = next;
            while (head < tail)
            {
                int i = _queue[head++];
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (_component[n] != 0 || !_map.IsPassable(nx, ny)) continue;
                    // Diagonal steps need one of the two side tiles open, as the movement solver does.
                    if (dx != 0 && dy != 0 && !_map.IsPassable(x + dx, y) && !_map.IsPassable(x, y + dy)) continue;
                    _component[n] = next;
                    _queue[tail++] = n;
                }
            }
        }
    }
}
