using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;

namespace BannerAndBarrow.Game.Audio;

public enum SoundBus : byte
{
    Effects,
    Voices,
}

/// <summary>
/// Loads every clip under Content/Audio (grouped by name without the trailing _N into variants), plays one-shots with
/// a cap on how many overlap, and keeps named loops whose volume glides towards a target. Missing clips are silent,
/// so the game runs fine without any audio content.
/// </summary>
public sealed class AudioMixer : IDisposable
{
    private const int MaxOneShots = 20;
    private readonly Dictionary<string, List<SoundEffect>> _clips = new();
    private readonly Dictionary<string, double> _lastPlayed = new();
    private readonly List<(SoundEffectInstance Instance, double EndTime)> _active = new();
    private readonly Dictionary<string, (SoundEffectInstance Instance, float Target)> _loops = new();
    private readonly Random _random = new();
    private double _now;
    private bool _available = true;
    /// <summary>Debug: BANNER_SOUND_LOG=&lt;file&gt; appends a count of every sound played (and loops) every 10 seconds.</summary>
    private readonly string? _logPath = Environment.GetEnvironmentVariable("BANNER_SOUND_LOG");
    private readonly Dictionary<string, int> _playCounts = new();
    private double _nextLog = 10;

    public float Master = 0.8f;
    public float Effects = 1f;
    public float Voices = 1f;

    public AudioMixer(ContentManager content)
    {
        var root = Path.Combine(AppContext.BaseDirectory, content.RootDirectory, "Audio");
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.xnb", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, content.RootDirectory), file);
                var asset = Path.ChangeExtension(rel, null).Replace('\\', '/');
                var name = Path.GetFileNameWithoutExtension(file);
                int underscore = name.LastIndexOf('_');
                var key = underscore > 0 && int.TryParse(name[(underscore + 1)..], out _) ? name[..underscore] : name;
                var clip = content.Load<SoundEffect>(asset);
                if (!_clips.TryGetValue(key, out var list)) _clips[key] = list = new List<SoundEffect>();
                list.Add(clip);
            }
        }
        catch (Exception)
        {
            // No audio device (or broken content): play nothing rather than crash.
            _available = false;
        }
    }

    public bool Has(string key) => _clips.ContainsKey(key);

    public void Update(double seconds)
    {
        _now += seconds;
        if (_logPath != null && _now >= _nextLog)
        {
            _nextLog = _now + 10;
            var loops = string.Join(" ", _loops.Where(l => l.Value.Target > 0.001f).Select(l => $"{l.Key}={l.Value.Target:0.00}"));
            File.AppendAllText(_logPath, $"[{_now:0}s] clips={_clips.Count} active={_active.Count} played: " +
                string.Join(" ", _playCounts.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")) + $" loops: {loops}{Environment.NewLine}");
        }
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_now < _active[i].EndTime && _active[i].Instance.State == SoundState.Playing) continue;
            _active[i].Instance.Dispose();
            _active.RemoveAt(i);
        }
        foreach (var (key, loop) in _loops)
        {
            float current = loop.Instance.Volume;
            float target = Math.Clamp(loop.Target * Master * Effects, 0f, 1f);
            float step = (float)seconds * 1.5f;
            float next = current + Math.Clamp(target - current, -step, step);
            loop.Instance.Volume = next;
            if (next <= 0.001f && loop.Instance.State == SoundState.Playing) loop.Instance.Pause();
            else if (next > 0.001f && loop.Instance.State != SoundState.Playing) loop.Instance.Play();
        }
    }

    /// <summary>
    /// Plays a random variant. <paramref name="minInterval"/> stops the same sound machine-gunning; lower
    /// <paramref name="priority"/> sounds are dropped first when many overlap.
    /// </summary>
    public bool Play(string key, float volume, float pan = 0f, float pitchJitter = 0.08f, SoundBus bus = SoundBus.Effects,
        double minInterval = 0.05, float priority = 0.5f)
    {
        if (!_available || volume <= 0.01f || !_clips.TryGetValue(key, out var variants)) return false;
        if (_lastPlayed.TryGetValue(key, out var last) && _now - last < minInterval) return false;
        if (_active.Count >= MaxOneShots && priority < 0.8f) return false;
        if (_active.Count >= MaxOneShots + 6) return false;
        var clip = variants[_random.Next(variants.Count)];
        float gain = volume * Master * (bus == SoundBus.Voices ? Voices : Effects);
        if (gain <= 0.005f) return false;
        try
        {
            var instance = clip.CreateInstance();
            instance.Volume = Math.Clamp(gain, 0f, 1f);
            instance.Pan = Math.Clamp(pan, -1f, 1f);
            instance.Pitch = Math.Clamp(((float)_random.NextDouble() * 2 - 1) * pitchJitter, -1f, 1f);
            instance.Play();
            _active.Add((instance, _now + clip.Duration.TotalSeconds + 0.1));
            _lastPlayed[key] = _now;
            if (_logPath != null) _playCounts[key] = _playCounts.GetValueOrDefault(key) + 1;
            return true;
        }
        catch (Exception)
        {
            // Out of voices on this device: skip the sound.
            return false;
        }
    }

    /// <summary>Sets how loud a named loop should be (0 fades it out). The loop starts on first use.</summary>
    public void SetLoop(string name, string key, float volume, float pan = 0f)
    {
        if (!_available || !_clips.TryGetValue(key, out var variants)) return;
        if (!_loops.TryGetValue(name, out var loop))
        {
            if (volume <= 0.001f) return;
            try
            {
                var instance = variants[0].CreateInstance();
                instance.IsLooped = true;
                instance.Volume = 0f;
                loop = (instance, volume);
            }
            catch (Exception)
            {
                return;
            }
        }
        loop.Instance.Pan = Math.Clamp(pan, -1f, 1f);
        _loops[name] = (loop.Instance, volume);
    }

    /// <summary>Fades every loop out (pause, menus).</summary>
    public void QuietLoops()
    {
        foreach (var name in _loops.Keys.ToList()) _loops[name] = (_loops[name].Instance, 0f);
    }

    public void Dispose()
    {
        foreach (var (instance, _) in _active) instance.Dispose();
        foreach (var loop in _loops.Values) loop.Instance.Dispose();
        _active.Clear();
        _loops.Clear();
    }
}
