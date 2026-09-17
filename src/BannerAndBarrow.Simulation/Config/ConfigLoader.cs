using System.Text.Json;
using System.Text.Json.Serialization;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Config;

/// <summary>Reads Fix values from JSON numbers via decimal, so parsing never touches floating point.</summary>
public sealed class FixJsonConverter : JsonConverter<Fix>
{
    public override Fix Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => Fix.FromDecimal(reader.GetDecimal()),
            JsonTokenType.String => Fix.FromDecimal(decimal.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture)),
            _ => throw new JsonException($"Expected number for Fix, got {reader.TokenType}"),
        };

    public override void Write(Utf8JsonWriter writer, Fix value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.ToDecimal());
}

public static class ConfigLoader
{
    public const string BalanceFile = "balance.json";
    public const string MapFile = "map.json";
    public const string AiFile = "ai_profiles.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new FixJsonConverter(), new JsonStringEnumConverter() },
    };

    /// <summary>Loads balance.json, map.json and ai_profiles.json from <paramref name="directory"/>.</summary>
    public static GameConfig LoadFromDirectory(string directory)
    {
        var balancePath = Path.Combine(directory, BalanceFile);
        if (!File.Exists(balancePath))
            throw new FileNotFoundException($"Balance config not found at '{balancePath}'.");

        var config = Deserialize<GameConfig>(File.ReadAllText(balancePath));

        var mapPath = Path.Combine(directory, MapFile);
        if (File.Exists(mapPath)) config.Map = Deserialize<MapConfig>(File.ReadAllText(mapPath));

        var aiPath = Path.Combine(directory, AiFile);
        if (File.Exists(aiPath)) config.Ai = Deserialize<AiProfilesConfig>(File.ReadAllText(aiPath));

        Validate(config);
        return config;
    }

    /// <summary>Walks up from <paramref name="start"/> looking for a "config" directory containing balance.json.</summary>
    public static string? FindConfigDirectory(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, BalanceFile))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new JsonException($"Could not parse {typeof(T).Name}");

    public static void Validate(GameConfig config)
    {
        foreach (var type in Enum.GetValues<Data.BuildingType>())
            if (!config.Buildings.ContainsKey(type)) throw new InvalidDataException($"balance.json is missing building '{type}'.");
        foreach (var type in Enum.GetValues<Data.SoldierType>())
            if (!config.Soldiers.ContainsKey(type)) throw new InvalidDataException($"balance.json is missing soldier '{type}'.");
        foreach (var type in Enum.GetValues<Data.FormationType>())
            if (!config.Formations.ContainsKey(type)) throw new InvalidDataException($"balance.json is missing formation '{type}'.");
        if (config.Simulation.TicksPerSecond <= 0) throw new InvalidDataException("ticksPerSecond must be positive.");
    }
}
