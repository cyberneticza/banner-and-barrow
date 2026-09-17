using System.Text.Json;

namespace BannerAndBarrow.Game;

/// <summary>
/// Player preferences, saved as JSON in the user's profile (%APPDATA%\BannerAndBarrow\settings.json on Windows) so
/// they survive reinstalls and never need write access to the install folder. Balance lives in config/, not here.
/// </summary>
public sealed class UserSettings
{
    public string Difficulty { get; set; } = "Normal";
    /// <summary>New games use a random map unless this is off, in which case <see cref="MapSeed"/> is replayed.</summary>
    public bool RandomMap { get; set; } = true;
    public int MapSeed { get; set; } = 20260916;
    /// <summary>Index into the game speeds (x1, x2, x4).</summary>
    public int GameSpeed { get; set; }
    /// <summary>Scene brightness: 1 is normal, lower darkens the map, higher lightens it. The UI is unaffected.</summary>
    public float Brightness { get; set; } = 1f;
    public float ScrollSpeed { get; set; } = 1f;
    public bool EdgeScrolling { get; set; } = true;
    public bool Fullscreen { get; set; }
    public bool PaintedBuildings { get; set; } = true;
    public bool ShowTerritory { get; set; } = true;
    public float MasterVolume { get; set; } = 0.8f;
    public float EffectsVolume { get; set; } = 1f;
    public float VoiceVolume { get; set; } = 1f;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BannerAndBarrow", "settings.json");

    public static UserSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), Json)?.Clamped() ?? new UserSettings();
        }
        catch (Exception)
        {
            // A corrupt settings file just means defaults; it is rewritten on the next save.
        }
        return new UserSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception)
        {
            // Read-only profile or similar: settings still apply for this session.
        }
    }

    private UserSettings Clamped()
    {
        GameSpeed = Math.Clamp(GameSpeed, 0, 2);
        Brightness = Math.Clamp(Brightness, 0.5f, 1.5f);
        ScrollSpeed = Math.Clamp(ScrollSpeed, 0.25f, 3f);
        MasterVolume = Math.Clamp(MasterVolume, 0f, 1f);
        EffectsVolume = Math.Clamp(EffectsVolume, 0f, 1f);
        VoiceVolume = Math.Clamp(VoiceVolume, 0f, 1f);
        return this;
    }
}
