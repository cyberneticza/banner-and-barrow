using Microsoft.Xna.Framework;
using BannerAndBarrow.Simulation.Data;

namespace BannerAndBarrow.Game.Rendering;

/// <summary>Placeholder colours. Replaced visually by sprites when sprites.json maps a key.</summary>
public static class Palette
{
    public static readonly Color[] Players = { new(64, 120, 230), new(210, 60, 55), new(60, 180, 90), new(220, 190, 60) };

    public static Color Player(int index) => index >= 0 && index < Players.Length ? Players[index] : Color.Gray;

    public static Color Terrain(Terrain t) => t switch
    {
        Simulation.Data.Terrain.Grass => new Color(112, 156, 78),
        Simulation.Data.Terrain.Forest => new Color(58, 104, 52),
        Simulation.Data.Terrain.Hills => new Color(150, 140, 100),
        Simulation.Data.Terrain.Water => new Color(54, 104, 170),
        Simulation.Data.Terrain.Mountain => new Color(104, 98, 96),
        _ => Color.Magenta,
    };

    public static Color Resource(ResourceType r) => r switch
    {
        ResourceType.Wood => new Color(150, 100, 50),
        ResourceType.Stone => new Color(170, 170, 170),
        ResourceType.Iron => new Color(90, 110, 130),
        ResourceType.Food => new Color(230, 200, 80),
        ResourceType.Gold => new Color(250, 210, 40),
        ResourceType.Bows => new Color(170, 120, 60),
        ResourceType.Shields => new Color(160, 60, 50),
        ResourceType.Swords => new Color(210, 210, 225),
        ResourceType.Pikes => new Color(140, 150, 110),
        ResourceType.Armour => new Color(120, 125, 140),
        _ => Color.White,
    };

    public static Color Building(BuildingType t) => t switch
    {
        BuildingType.Keep => new Color(120, 110, 120),
        BuildingType.Storehouse => new Color(150, 115, 75),
        BuildingType.House => new Color(185, 150, 110),
        BuildingType.Woodcutter => new Color(120, 90, 55),
        BuildingType.Quarry => new Color(140, 140, 135),
        BuildingType.IronMine => new Color(95, 100, 115),
        BuildingType.Farm => new Color(205, 180, 90),
        BuildingType.Smithy => new Color(90, 80, 80),
        BuildingType.Fletcher => new Color(130, 100, 60),
        BuildingType.ShieldMaker => new Color(120, 70, 60),
        BuildingType.TaxOffice => new Color(200, 170, 60),
        BuildingType.Barracks => new Color(130, 70, 60),
        BuildingType.Archery => new Color(90, 120, 70),
        BuildingType.Stable => new Color(140, 100, 70),
        BuildingType.Wall => new Color(150, 148, 142),
        BuildingType.Gate => new Color(120, 90, 55),
        BuildingType.Outpost => new Color(110, 100, 90),
        BuildingType.Stronghold => new Color(90, 85, 85),
        _ => Color.Gray,
    };

    public static string ShortName(BuildingType t) => t switch
    {
        BuildingType.Keep => "KEEP",
        BuildingType.Storehouse => "Store",
        BuildingType.House => "Hse",
        BuildingType.Woodcutter => "Wood",
        BuildingType.Quarry => "Qry",
        BuildingType.IronMine => "Iron",
        BuildingType.Farm => "Farm",
        BuildingType.Smithy => "Smith",
        BuildingType.Fletcher => "Flet",
        BuildingType.ShieldMaker => "Shld",
        BuildingType.TaxOffice => "Tax",
        BuildingType.Barracks => "Brk",
        BuildingType.Archery => "Arch",
        BuildingType.Stable => "Stbl",
        BuildingType.Wall => "Wall",
        BuildingType.Gate => "Gate",
        BuildingType.Outpost => "Out",
        BuildingType.Stronghold => "Strg",
        _ => "?",
    };

    public static string Letter(SoldierType t) => t switch
    {
        SoldierType.Spearmen => "S",
        SoldierType.Pikemen => "P",
        SoldierType.MenAtArms => "M",
        SoldierType.Archers => "A",
        SoldierType.Longbowmen => "L",
        SoldierType.Knights => "K",
        _ => "?",
    };

    public static Color SoldierMark(SoldierType t) => t switch
    {
        SoldierType.Spearmen => new Color(240, 230, 200),
        SoldierType.Pikemen => new Color(200, 210, 230),
        SoldierType.MenAtArms => new Color(170, 170, 185),
        SoldierType.Archers => new Color(120, 200, 100),
        SoldierType.Longbowmen => new Color(40, 120, 40),
        SoldierType.Knights => new Color(240, 200, 60),
        _ => Color.White,
    };
}
