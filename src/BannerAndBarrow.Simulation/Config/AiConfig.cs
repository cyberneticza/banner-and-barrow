using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Config;

public sealed class AiProfilesConfig
{
    /// <summary>Profile the AI opponent uses. Presets: Easy, Normal, Hard.</summary>
    public string Active { get; set; } = "Normal";
    public Dictionary<string, AiProfile> Profiles { get; set; } = new();

    public AiProfile Get(string? name = null)
    {
        name ??= Active;
        if (Profiles.TryGetValue(name, out var p)) return p;
        if (Profiles.Count > 0) return Profiles.Values.First();
        return new AiProfile();
    }
}

public sealed class BuildStep
{
    public BuildingType Building { get; set; }
    /// <summary>Target total count of this building type once the step is done.</summary>
    public int Count { get; set; } = 1;
}

/// <summary>TUNING: an opening plan the AI picks at the start of a Match (weighted random, seeded).</summary>
public sealed class AiStrategy
{
    public int Weight { get; set; } = 1;
    /// <summary>Replaces the profile build order when not empty.</summary>
    public List<BuildStep> BuildOrder { get; set; } = new();
    /// <summary>Replaces the profile army composition when not empty.</summary>
    public Dictionary<SoldierType, int> ArmyComposition { get; set; } = new();
    public Fix FirstWaveMultiplier { get; set; } = Fix.One;
    public Fix EscalationMultiplier { get; set; } = Fix.One;
    public Fix IntervalMultiplier { get; set; } = Fix.One;
    public Fix ExtraPeaceSeconds { get; set; } = Fix.Zero;
}

/// <summary>TUNING: all AI behaviour knobs. Loaded from config/ai_profiles.json.</summary>
public sealed class AiProfile
{
    public Fix DecisionIntervalSeconds { get; set; } = Fix.One;
    public List<BuildStep> BuildOrder { get; set; } = new();
    public Dictionary<string, AiStrategy> Strategies { get; set; } = new();
    public int MaxConcurrentSites { get; set; } = 3;
    /// <summary>Build a House when free Worker room drops to this.</summary>
    public int HouseWhenFreeRoomBelow { get; set; } = 3;
    public Dictionary<SoldierType, int> ArmyComposition { get; set; } = new();
    /// <summary>Recruitment may substitute an affordable type only while it is at most this far over its share.</summary>
    public Fix CompositionSlack { get; set; } = Fix.FromDecimal(0.5m);

    // --- Escalation: attack size grows each minute after the peace window, and attacks come more often.
    /// <summary>Scales how fast attacks grow. The main difficulty knob.</summary>
    public Fix Aggression { get; set; } = Fix.One;
    public Fix PeaceSeconds { get; set; } = Fix.FromInt(120);
    public Fix FirstWaveSoldiers { get; set; } = Fix.FromInt(6);
    public Fix WaveSoldiersPerMinute { get; set; } = Fix.FromInt(4);
    public int MaxWaveSoldiers { get; set; } = 180;
    public Fix FirstWaveIntervalSeconds { get; set; } = Fix.FromInt(150);
    public Fix MinWaveIntervalSeconds { get; set; } = Fix.FromInt(70);
    public Fix WaveIntervalShrinkPerMinute { get; set; } = Fix.FromInt(3);
    /// <summary>Attacks up to this size are raids straight at the economy; bigger ones stage and assault the town.</summary>
    public int RaidMaxSoldiers { get; set; } = 18;
    /// <summary>Soldiers kept at home when a (non-raid) wave goes out.</summary>
    public int HomeGuardSoldiers { get; set; } = 12;
    /// <summary>If the army can't reach the target size in this long, attack with 60% of it.</summary>
    public Fix MaxWaveWaitSeconds { get; set; } = Fix.FromInt(90);
    public Fix WaveTimingJitter { get; set; } = Fix.FromDecimal(0.2m);

    // --- Provocation: enemy Soldiers in the town make escalation faster (x(1+provocation)).
    public Fix ProvocationPerSoldierSecond { get; set; } = Fix.FromDecimal(0.002m);
    /// <summary>Provocation gained before this time counts double (punishes early aggression).</summary>
    public Fix EarlyProvocationSeconds { get; set; } = Fix.FromInt(600);
    public Fix MaxProvocation { get; set; } = Fix.One;
    public Fix ProvocationDecayPerMinute { get; set; } = Fix.FromDecimal(0.05m);

    /// <summary>Waves gather this far short of the enemy Keep before assaulting together.</summary>
    public Fix StagingDistance { get; set; } = Fix.FromInt(34);
    /// <summary>Extra seconds (beyond the march) to wait for stragglers at the staging point.</summary>
    public Fix StagingMaxWaitSeconds { get; set; } = Fix.FromInt(45);
    /// <summary>Waves march to the staging point without stopping to fight (they still defend themselves in melee).</summary>
    public bool MarchWithoutEngaging { get; set; } = true;

    /// <summary>Build Smithies (up to this many) while Smithy goods are backlogged and Iron is plentiful.</summary>
    public int MaxSmithies { get; set; } = 4;
    /// <summary>TUNING: keep at least one Farm per this many Workers (Workers eat), plus one.</summary>
    public int WorkersPerFarm { get; set; } = 8;
    /// <summary>Build extra Barracks while Food exceeds this (so a winning economy turns into a bigger army).</summary>
    public int ExtraBarracksFood { get; set; } = 150;
    public int MaxBarracks { get; set; } = 2;
    public Fix DefenseRadius { get; set; } = Fix.FromInt(22);
    public Fix ExpansionIntervalSeconds { get; set; } = Fix.FromInt(150);
    public int MaxStorehouses { get; set; } = 3;
    /// <summary>Production buildings further than this from any Storehouse justify a new Storehouse beside them.</summary>
    public Fix LogisticsDistance { get; set; } = Fix.FromInt(11);
    /// <summary>Never place a Storehouse closer than this to another one: overlapping reach buys nothing.</summary>
    public Fix StorehouseSpacing { get; set; } = Fix.FromInt(13);
    public Fix OutpostAfterSeconds { get; set; } = Fix.FromInt(360);
    public Fix StrongholdAfterSeconds { get; set; } = Fix.FromInt(720);
    public int RegimentsToStation { get; set; } = 1;
    public bool BuildRoads { get; set; } = true;
    public int RoadTilesPerDecision { get; set; } = 12;
    public Dictionary<ResourceType, int> StorehouseMinimumStock { get; set; } = new();
    /// <summary>STUB: not used yet. Intended for pulling back broken Regiments.</summary>
    public bool RetreatWhenBroken { get; set; }
}
