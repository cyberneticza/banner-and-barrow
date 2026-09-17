using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace BannerAndBarrow.Game.Rendering;

public sealed class SpriteEntry
{
    public string Texture { get; set; } = "";
    public int[]? Source { get; set; }
}

public readonly record struct Sprite(Texture2D Texture, Rectangle Source);

/// <summary>
/// Maps sprite keys (e.g. "building.Keep") to content-pipeline textures listed in Assets/sprites.json.
/// Unmapped or unbuilt sprites return null, and renderers fall back to placeholder shapes.
/// </summary>
public sealed class SpriteRegistry
{
    private readonly Dictionary<string, Sprite> _sprites = new();

    public List<string> Warnings { get; } = new();

    public static SpriteRegistry Load(ContentManager content, string manifestPath)
    {
        var registry = new SpriteRegistry();
        if (!File.Exists(manifestPath))
        {
            registry.Warnings.Add($"No sprite manifest at {manifestPath}; using placeholder shapes.");
            return registry;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        var entries = JsonSerializer.Deserialize<Dictionary<string, SpriteEntry>>(File.ReadAllText(manifestPath), options) ?? new();
        foreach (var (key, entry) in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Texture)) continue;
            try
            {
                var tex = content.Load<Texture2D>(entry.Texture);
                var src = entry.Source is { Length: 4 }
                    ? new Rectangle(entry.Source[0], entry.Source[1], entry.Source[2], entry.Source[3])
                    : tex.Bounds;
                registry._sprites[key] = new Sprite(tex, src);
            }
            catch (ContentLoadException)
            {
                registry.Warnings.Add($"Sprite '{key}' -> '{entry.Texture}' is not built; add it to Content.mgcb.");
            }
        }
        return registry;
    }

    public Sprite? Get(string key) => _sprites.TryGetValue(key, out var s) ? s : null;

    public bool Has(string key) => _sprites.ContainsKey(key);

    public int Count => _sprites.Count;
}
