namespace BannerAndBarrow.Simulation.Data;

public enum ResourceType : byte
{
    Wood,
    Stone,
    Iron,
    Food,
    Gold,
    // Goods: made by manufacturing buildings (make to order, then a small stock), consumed when recruiting.
    Bows,
    Shields,
    Swords,
    Pikes,
    Armour,
}

public static class Resources
{
    public const int Count = 10;
    public static readonly ResourceType[] All = (ResourceType[])Enum.GetValues(typeof(ResourceType));
    public static readonly ResourceType[] Goods = { ResourceType.Bows, ResourceType.Shields, ResourceType.Swords, ResourceType.Pikes, ResourceType.Armour };
    public static bool IsGoods(ResourceType r) => r >= ResourceType.Bows;
}

public enum BuildingType : byte
{
    Keep,
    Storehouse,
    House,
    Woodcutter,
    Quarry,
    IronMine,
    Farm,
    Smithy,
    Fletcher,
    ShieldMaker,
    TaxOffice,
    Barracks,
    Archery,
    Stable,
    Outpost,
    /// <summary>Not placeable: an Outpost becomes a Stronghold through an upgrade.</summary>
    Stronghold,
    /// <summary>One tile of wall: blocks everyone, and only melee attackers can break it.</summary>
    Wall,
    /// <summary>A gate in a wall: your own side walks through, the enemy has to break it.</summary>
    Gate,
}

public enum SoldierType : byte
{
    Spearmen,
    /// <summary>Upgraded Spearmen: pikes and armour.</summary>
    Pikemen,
    MenAtArms,
    Archers,
    Longbowmen,
    Knights,
}

public enum FormationType : byte
{
    Line,
    Column,
    Wedge,
    Loose,
}

public enum FormationShape : byte
{
    Grid,
    Wedge,
}

public enum Terrain : byte
{
    Grass,
    Forest,
    Hills,
    Water,
    Mountain,
}

public enum RoadKind : byte
{
    None,
    Dirt,
    Stone,
}
