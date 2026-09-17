using Microsoft.Xna.Framework;
using BannerAndBarrow.Game.Rendering;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;

namespace BannerAndBarrow.Game;

/// <summary>
/// Debug: set BANNER_CAPTURE=&lt;folder&gt; to run an AI-vs-AI match and save screenshots instead of playing.
/// BANNER_CAPTURE_AT lists match seconds (default "240,600"); at each, the camera visits the Keep, a Farm,
/// a Smithy and the largest army of Player 0, a quarter of a second apart so animations can be compared.
/// BANNER_CAPTURE_SEED picks the map (default 3). BANNER_CAPTURE_UNTIL=fire keeps playing past each time until a
/// building is well alight (up to 10 minutes), for checking siege visuals. The game exits when done.
/// </summary>
public sealed class CaptureScript
{
    private readonly string _folder;
    private readonly Queue<long> _times = new();
    private readonly Queue<(string Name, Func<GameState, Vector2?> Focus, float Zoom)> _shots = new();
    private long _nextShotTick;
    private string _label = "";
    public int Seed { get; private init; } = 3;
    private bool _untilFire;
    public string? PendingShot { get; set; }

    public static CaptureScript? FromEnvironment()
    {
        var folder = Environment.GetEnvironmentVariable("BANNER_CAPTURE");
        if (string.IsNullOrEmpty(folder)) return null;
        Directory.CreateDirectory(folder);
        var script = new CaptureScript(folder)
        {
            Seed = int.TryParse(Environment.GetEnvironmentVariable("BANNER_CAPTURE_SEED"), out var seed) ? seed : 3,
            _untilFire = Environment.GetEnvironmentVariable("BANNER_CAPTURE_UNTIL") == "fire",
        };
        var at = Environment.GetEnvironmentVariable("BANNER_CAPTURE_AT") ?? "240,600";
        foreach (var part in at.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (long.TryParse(part.Trim(), out var seconds)) script._times.Enqueue(seconds);
        return script;
    }

    private CaptureScript(string folder) => _folder = folder;

    /// <summary>Moves the match and camera on to the next screenshot. Returns true when everything is captured.</summary>
    public bool Advance(Match match, Camera2D camera, Microsoft.Xna.Framework.Game game)
    {
        if (PendingShot != null) return false;
        var state = match.State;
        int tps = state.Config.Simulation.TicksPerSecond;

        if (_shots.Count == 0)
        {
            if (_times.Count == 0) return true;
            long seconds = _times.Dequeue();
            while (state.Tick < seconds * tps && !state.IsOver) match.Step();
            if (_untilFire)
            {
                long limit = state.Tick + 600L * tps;
                while (state.Tick < limit && !state.IsOver && !state.Buildings.Values.Any(b => b.Fire.ToFloat() > 0.35f)) match.Step();
                seconds = state.Tick / tps;
            }
            _label = $"{seconds / 60:00}m{seconds % 60:00}s";
            foreach (var shot in Plan()) _shots.Enqueue(shot);
            _nextShotTick = state.Tick;
        }

        while (state.Tick < _nextShotTick && !state.IsOver) match.Step();
        var (name, focus, zoom) = _shots.Dequeue();
        var at = focus(state);
        if (at == null) return false;
        camera.Position = at.Value;
        camera.Zoom = zoom;
        camera.Clamp();
        PendingShot = Path.Combine(_folder, $"{_label}_{name}.png");
        _nextShotTick = state.Tick + tps / 4;
        return false;
    }

    private static IEnumerable<(string, Func<GameState, Vector2?>, float)> Plan()
    {
        Vector2? Building(GameState s, BuildingType type) =>
            s.Buildings.Values.FirstOrDefault(b => b.Owner == 0 && b.Type == type && b.IsActive)?.Center.ToPixels();
        Vector2? Army(GameState s) =>
            s.Regiments.Values.Where(r => r.Owner == 0 && !r.IsStationed).OrderByDescending(r => r.SoldierIds.Count)
                .Select(r => (Vector2?)s.RegimentCentroid(r).ToPixels()).FirstOrDefault();
        Vector2? Battle(GameState s) =>
            s.Regiments.Values.Where(r => r.EngagedRegimentId != 0 || r.EngagedBuildingId != 0).OrderByDescending(r => r.SoldierIds.Count)
                .Select(r => (Vector2?)s.RegimentCentroid(r).ToPixels()).FirstOrDefault();
        Vector2? Fire(GameState s) =>
            s.Buildings.Values.Where(b => b.Fire.Raw > 0).OrderByDescending(b => b.Fire).Select(b => (Vector2?)b.Center.ToPixels()).FirstOrDefault();
        for (int frame = 0; frame < 2; frame++)
        {
            yield return ($"battle{frame}", Battle, 1.9f);
            yield return ($"fire{frame}", Fire, 1.6f);
            yield return ($"keep{frame}", s => s.GetKeep(0)?.Center.ToPixels(), 1.0f);
            yield return ($"farm{frame}", s => Building(s, BuildingType.Farm), 1.8f);
            yield return ($"smithy{frame}", s => Building(s, BuildingType.Smithy), 1.8f);
            yield return ($"woodcutter{frame}", s => Building(s, BuildingType.Woodcutter), 1.8f);
            yield return ($"army{frame}", Army, 1.6f);
        }
    }
}
