using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Commands;

/// <summary>
/// Everything a Player can do goes through a command. The human UI and the AI issue the same commands,
/// so the AI cannot cheat and a future lockstep layer only needs to serialise these.
/// </summary>
public abstract record GameCommand(int Player);

public sealed record PlaceBuildingCommand(int Player, BuildingType Type, TileCoord Origin) : GameCommand(Player);

public sealed record BuildRoadCommand(int Player, RoadKind Kind, TileCoord[] Tiles) : GameCommand(Player);

public sealed record CancelConstructionCommand(int Player, int BuildingId) : GameCommand(Player);

/// <summary>Builders tear the building down (partial refund). Issuing it again on a building being demolished cancels.</summary>
public sealed record DemolishBuildingCommand(int Player, int BuildingId) : GameCommand(Player);

/// <summary>Builders tear up the roads on these tiles; planned roads there are simply cancelled.</summary>
/// <summary>Raises a line of Walls or Gates, one building per tile, skipping tiles that can't take one.</summary>
public sealed record PlaceWallsCommand(int Player, BuildingType Type, TileCoord[] Tiles) : GameCommand(Player);

public sealed record DemolishRoadsCommand(int Player, TileCoord[] Tiles) : GameCommand(Player);

/// <summary>Removes an entry from a Barracks/Stable queue, refunding it if training had already been paid for.</summary>
/// <summary>Where a recruitment building sends its new Regiments; a null point clears it.</summary>
public sealed record SetRallyPointCommand(int Player, int BuildingId, FixVec2? Point) : GameCommand(Player);

public sealed record CancelRecruitCommand(int Player, int BuildingId, int QueueIndex) : GameCommand(Player);

public sealed record MoveRegimentsCommand(int Player, int[] RegimentIds, FixVec2 Target, FixVec2 Facing, bool AttackMove) : GameCommand(Player);

public sealed record SetFormationCommand(int Player, int[] RegimentIds, FormationType Formation) : GameCommand(Player);

/// <summary>Merges the selected Regiments of each Soldier type into as few Regiments as their size cap allows.</summary>
public sealed record MergeRegimentsCommand(int Player, int[] RegimentIds) : GameCommand(Player);

/// <summary>Upgrades Regiments (e.g. Spearmen into Pikemen) next to the right building; waits for goods if short.</summary>
public sealed record UpgradeRegimentsCommand(int Player, int[] RegimentIds) : GameCommand(Player);

public sealed record SetStanceCommand(int Player, int[] RegimentIds, bool HoldGround) : GameCommand(Player);

public sealed record AttackRegimentCommand(int Player, int[] RegimentIds, int TargetRegimentId) : GameCommand(Player);

public sealed record AttackBuildingCommand(int Player, int[] RegimentIds, int TargetBuildingId) : GameCommand(Player);

/// <summary>Ranged Regiments form up behind the infantry Regiment and follow it.</summary>
public sealed record FormBattleLineCommand(int Player, int InfantryRegimentId, int[] RangedRegimentIds) : GameCommand(Player);

public sealed record StopCommand(int Player, int[] RegimentIds) : GameCommand(Player);

public sealed record RecruitCommand(int Player, int BuildingId, SoldierType Type) : GameCommand(Player);

public sealed record StationCommand(int Player, int[] RegimentIds, int FortId) : GameCommand(Player);

public sealed record UnstationCommand(int Player, int FortId) : GameCommand(Player);

public sealed record UpgradeOutpostCommand(int Player, int FortId) : GameCommand(Player);

public sealed record UpgradeStorehouseCommand(int Player, int StorehouseId) : GameCommand(Player);

public sealed record BuyCarriersCommand(int Player, int StorehouseId) : GameCommand(Player);

public sealed record SetMinimumStockCommand(int Player, int StorehouseId, ResourceType Resource, int Amount) : GameCommand(Player);

public sealed record RepairRuinCommand(int Player, int BuildingId) : GameCommand(Player);
