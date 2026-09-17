using Microsoft.Xna.Framework;
using BannerAndBarrow.Game.Rendering;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Commands;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Military;

namespace BannerAndBarrow.Game.UI;

/// <summary>Bottom panel: context actions for the selection plus the build menu.</summary>
public sealed class ControlPanel
{
    public Rectangle Rect;

    private static readonly BuildingType[] BuildMenu =
    {
        BuildingType.House, BuildingType.Woodcutter, BuildingType.Quarry, BuildingType.IronMine,
        BuildingType.Farm, BuildingType.TaxOffice, BuildingType.Storehouse, BuildingType.Outpost,
        BuildingType.Smithy, BuildingType.Fletcher, BuildingType.ShieldMaker, BuildingType.Barracks,
        BuildingType.Archery, BuildingType.Stable,
    };

    private static readonly BuildingType[] WallMenu = { BuildingType.Wall, BuildingType.Gate };

    public void Draw(Ui ui, GameState state, SelectionController selection, int minimapWidth)
    {
        ui.Panel(Rect);
        const int buildWidth = 430;
        var info = new Rectangle(Rect.X + minimapWidth + 12, Rect.Y + 8, Rect.Width - minimapWidth - buildWidth - 24, Rect.Height - 16);
        var build = new Rectangle(Rect.Right - buildWidth - 6, Rect.Y + 8, buildWidth, Rect.Height - 16);

        DrawBuildMenu(ui, state, selection, build);

        var regiments = selection.SelectedRegiments();
        if (regiments.Count > 0) DrawRegiments(ui, state, selection, regiments, info);
        else if (selection.SelectedBuilding is { } b) DrawBuilding(ui, state, selection, b, info);
        else
        {
            ui.Label(new Vector2(info.X, info.Y), "Nothing selected", Ui.Dim);
            ui.Label(new Vector2(info.X, info.Y + 24),
                "Left-click / drag: select Regiments or a building.   Right-click: move (drag to set facing).\n" +
                "Right-click moves and attacks what it meets; Ctrl + right-click marches past.   Right-click your Outpost: station inside.\n" +
                "1-5 formations, M merge, U upgrade, T hold ground, G archers behind infantry, H halt, Del demolish, Space pause, Esc menu, +/- speed, F1-F5 debug.\n" +
                "Move several Regiments at once to form a Battle Group: spears/men-at-arms in front, archers behind, knights on the flanks.",
                Ui.Dim, small: true);
        }
    }

    private static void DrawBuildMenu(Ui ui, GameState state, SelectionController selection, Rectangle area)
    {
        ui.Label(new Vector2(area.X, area.Y - 2), "Build", Ui.Text, small: true);
        const int cols = 4, w = 104, h = 26, gap = 3;
        for (int i = 0; i < BuildMenu.Length; i++)
        {
            var type = BuildMenu[i];
            var def = state.Config.Building(type);
            var r = new Rectangle(area.X + (i % cols) * (w + gap), area.Y + 16 + (i / cols) * (h + gap), w, h);
            var cost = StockOps.ToArray(def.Cost);
            var tooltip = $"{type}: {StockOps.Describe(cost)}" + (type == BuildingType.Storehouse ? " (rises with count and distance from the Keep)" : "") +
                          (type == BuildingType.Outpost ? " (buildable outside your Territory)" : "");
            bool active = selection.Placement == PlacementMode.Building && selection.PlacementType == type;
            if (ui.Button(r, type.ToString(), true, tooltip, active)) selection.BeginPlacement(type);
        }
        int row = (BuildMenu.Length + cols - 1) / cols;
        var dirt = new Rectangle(area.X, area.Y + 16 + row * (h + gap), w, h);
        var stone = new Rectangle(area.X + w + gap, area.Y + 16 + row * (h + gap), w, h);
        if (ui.Button(dirt, "Dirt Road", true, "Drag to lay a Dirt Road. Free, needs Builders. Civilians x1.0, Soldiers x1.25.",
                selection.Placement == PlacementMode.Road && selection.PlacementRoad == RoadKind.Dirt))
            selection.BeginRoad(RoadKind.Dirt);
        if (ui.Button(stone, "Stone Road", true, "Drag to lay a Stone Road (own Territory, 1 Stone/tile). Everyone x1.5.",
                selection.Placement == PlacementMode.Road && selection.PlacementRoad == RoadKind.Stone))
            selection.BeginRoad(RoadKind.Stone);
        for (int i = 0; i < WallMenu.Length; i++)
        {
            var type = WallMenu[i];
            var r = new Rectangle(area.X + (2 + i) * (w + gap), area.Y + 16 + row * (h + gap), w, h);
            var tooltip = type == BuildingType.Wall
                ? $"Drag a line of Walls ({StockOps.Describe(StockOps.ToArray(state.Config.Building(type).Cost))} each). They block everyone, can be raised through forest, and only melee attackers can break them. Your archers shoot over your own walls freely; enemies shooting over them lose range and accuracy."
                : $"Drag Gates into a wall line ({StockOps.Describe(StockOps.ToArray(state.Config.Building(type).Cost))} each). Your units walk through; the enemy has to break them down.";
            if (ui.Button(r, type.ToString(), true, tooltip, selection.Placement == PlacementMode.Wall && selection.PlacementType == type))
                selection.BeginWall(type);
        }
        var demolish = new Rectangle(area.X + 4 * (w + gap), area.Y + 16 + row * (h + gap), w, h);
        if (ui.Button(demolish, "Demolish", true, "Click a building or drag across roads: Builders tear them down (half the cost refunded). Hold Shift to keep demolishing.",
                selection.Placement == PlacementMode.Demolish))
            selection.BeginDemolish();
    }

    private static void DrawRegiments(Ui ui, GameState state, SelectionController selection, List<Regiment> regiments, Rectangle area)
    {
        var ids = regiments.Select(r => r.Id).ToArray();
        int y = area.Y;
        foreach (var r in regiments.Take(5))
        {
            var def = state.Config.Soldier(r.Type);
            float cohesion = r.Cohesion.ToFloat() / state.Config.Combat.CohesionMax.ToFloat();
            var status = r.IsStationed ? "stationed" : RegimentSystem.IsBroken(state, r) ? "BROKEN" : r.Order.ToString();
            ui.Label(new Vector2(area.X, y), $"{r.Type}  {r.SoldierIds.Count}/{r.StartingSize}  {r.Formation}  {status}", Ui.Text, small: true);
            ui.Bar(new Rectangle(area.X + 300, y + 4, 80, 7), cohesion, RegimentSystem.IsBroken(state, r) ? Color.Red : Color.Gold);
            y += 15;
        }
        if (regiments.Count > 5) ui.Label(new Vector2(area.X, y), $"+{regiments.Count - 5} more", Ui.Dim, small: true);

        int by = area.Bottom - 58;
        int bx = area.X;
        foreach (var formation in Enum.GetValues<FormationType>())
        {
            bool allowed = regiments.All(r => state.Config.Soldier(r.Type).Formations.Contains(formation));
            bool active = regiments.All(r => r.Formation == formation);
            var tooltip = FormationTooltip(state, formation);
            if (ui.Button(new Rectangle(bx, by, 82, 24), formation.ToString(), allowed, tooltip, active))
                selection.Issue(new SetFormationCommand(selection.Player, ids, formation));
            bx += 86;
        }
        by += 28;
        if (ui.Button(new Rectangle(area.X, by, 170, 24), "Archers behind infantry (G)", true,
                "Select one infantry and one or more ranged Regiments: the ranged ones form up and follow behind."))
            selection.FormBattleLine();
        if (ui.Button(new Rectangle(area.X + 174, by, 70, 24), "Halt (H)"))
            selection.Issue(new StopCommand(selection.Player, ids));
        var upgradeable = regiments.Where(r => Manufacturing.UpgradeTarget(state, r.Type) != null).ToList();
        if (upgradeable.Count > 0)
        {
            var target = Manufacturing.UpgradeTarget(state, upgradeable[0].Type)!.Value;
            var targetDef = state.Config.Soldier(target);
            var perSoldier = StockOps.Describe(StockOps.ToArray(targetDef.UpgradeCostPerSoldier));
            bool pending = upgradeable.All(r => state.Players[selection.Player].PendingUpgrades.Contains(r.Id));
            if (ui.Button(new Rectangle(area.X + 476, by, 150, 24), pending ? "Upgrade ordered" : $"Upgrade to {target} (U)", !pending,
                    $"Next to a {targetDef.RecruitedAt}: each Soldier needs {perSoldier}. If goods are short the order waits and workshops make them."))
                selection.Issue(new UpgradeRegimentsCommand(selection.Player, upgradeable.Select(r => r.Id).ToArray()));
        }
        if (ui.Button(new Rectangle(area.X + 382, by, 90, 24), "Merge (M)", regiments.Count > 1,
                "Merge selected Regiments of the same type into bigger ones (up to 24 Soldiers, 12 Knights)."))
            selection.Issue(new MergeRegimentsCommand(selection.Player, ids));
        bool holding = regiments.All(r => r.HoldGround);
        if (ui.Button(new Rectangle(area.X + 248, by, 130, 24), "Hold ground (T)", true,
                "Stance: the Regiment won't chase. Standing archers shoot faster, further and more accurately; archers on the move are worse.",
                holding))
            selection.ToggleHoldGround();
    }

    private static string FormationTooltip(GameState state, FormationType formation)
    {
        var f = state.Config.Formation(formation);
        return $"{formation}: speed x{f.SpeedMultiplier}, front arrows x{f.FrontRangedMultiplier * f.AllRangedMultiplier}, " +
               $"front melee x{f.FrontMeleeMultiplier * f.AllMeleeMultiplier}, flank x{f.FlankMultiplier}, rear x{f.RearMultiplier}, " +
               $"melee dealt x{f.OutgoingMeleeMultiplier}, charge x{f.ChargeBonusMultiplier}";
    }

    private static void DrawBuilding(Ui ui, GameState state, SelectionController selection, Building b, Rectangle area)
    {
        int player = selection.Player;
        bool own = b.Owner == player;
        if (!own && !state.CanSee(player, b))
        {
            var seen = state.Players[player].KnownBuildings.GetValueOrDefault(b.Id);
            var ago = seen == null ? 0 : (state.Tick - seen.SeenTick) / state.Config.Simulation.TicksPerSecond;
            ui.Label(new Vector2(area.X, area.Y), $"{seen?.Type ?? b.Type}  ({state.Players[b.Owner].Name})  last seen {ago / 60}:{ago % 60:00} ago", Ui.Dim);
            ui.Label(new Vector2(area.X, area.Y + 22), "Out of sight. Send Regiments to scout it: you see your Territory and around your Regiments and forts.", Ui.Dim, small: true);
            return;
        }
        var title = $"{b.Type}  ({state.Players[b.Owner].Name})  HP {b.Hp.FloorToInt()}/{b.MaxHp.FloorToInt()}  {b.State}{(b.IsBurning ? "  BURNING" : "")}";
        ui.Label(new Vector2(area.X, area.Y), title, b.IsBurning ? Color.OrangeRed : Ui.Text);
        int y = area.Y + 22;

        if (own && b.Type != BuildingType.Keep && !b.NeedsConstruction && b.State != BuildingState.Ruin)
        {
            var label = b.Demolishing ? "Cancel demolition" : "Demolish (Del)";
            if (ui.Button(new Rectangle(area.Right - 150, area.Y, 150, 22), label, true,
                    b.Demolishing ? $"Builders are tearing it down ({(int)(b.Progress.ToFloat() * 100)}%)" : "Builders tear this building down and return half its cost."))
                selection.Issue(new DemolishBuildingCommand(selection.Player, b.Id));
            if (b.Demolishing) return;
        }

        if (b.NeedsConstruction)
        {
            var parts = Resources.All.Where(r => b.Required[(int)r] > 0).Select(r => $"{r} {b.Delivered[(int)r]}/{b.Required[(int)r]}");
            ui.Label(new Vector2(area.X, y), $"Materials: {string.Join("  ", parts)}   Work {(int)(b.Progress.ToFloat() * 100)}%", Ui.Text, small: true);
            if (own && ui.Button(new Rectangle(area.X, area.Bottom - 26, 150, 24), "Cancel (Del)"))
                selection.Issue(new CancelConstructionCommand(player, b.Id));
            return;
        }

        if (b.State == BuildingState.Ruin)
        {
            ui.Label(new Vector2(area.X, y), "A destroyed Storehouse. Repair it before nearby buildings burn down. Enemy Presence blocks repairs.", Ui.Text, small: true);
            if (own && ui.Button(new Rectangle(area.X, area.Bottom - 26, 150, 24), "Repair Storehouse"))
                selection.Issue(new RepairRuinCommand(player, b.Id));
            return;
        }
        if (!own) return;

        if (b.Store != null) DrawStorehouse(ui, state, selection, b, area, y);
        else if (b.Recruitment != null) DrawRecruitment(ui, state, selection, b, area, y);
        else if (b.Fort != null) DrawFort(ui, state, selection, b, area, y);
        else
        {
            var def = state.Config.Building(b.Type);
            if (def.WorkerSlots > 0)
            {
                var worker = b.WorkerIds.Select(state.GetWorker).FirstOrDefault();
                bool housingFull = state.WorkerCount(player) >= state.WorkerRoom(player);
                ui.Label(new Vector2(area.X, y), worker == null
                        ? (housingFull ? "No Worker: all Houses are full. Build more Houses." : "Waiting for a Worker to walk here from the Keep.")
                        : $"Worker: {worker.Task}", worker == null ? Color.Gold : Ui.Text, small: true);
                if (def.Recipes.Count > 0) DrawWorkshop(ui, state, player, b, def, new Vector2(area.X, y + 18));
                else if (def.Outputs.Count > 0)
                {
                    var (resource, _) = def.Outputs.First();
                    ui.Label(new Vector2(area.X + 330, y), $"{resource} here: {b.OutputStock[(int)resource]}/{def.BatchSize} (carried to a Storehouse when full)", Ui.Text, small: true);
                }
                if (b.Type == BuildingType.Farm)
                    ui.Label(new Vector2(area.X, y + 16), $"Room for Fields: {WorkerSystem.CountNodesInRange(state, b.Type, b.CenterTile)} tiles (keeps {state.Config.Economy.FarmFields} sown; crops ripen in {state.Config.Economy.CropGrowSeconds}s)", Ui.Dim, small: true);
                if (b.Type is BuildingType.Woodcutter or BuildingType.Quarry or BuildingType.IronMine)
                    ui.Label(new Vector2(area.X, y + 16), $"Resource tiles in reach: {WorkerSystem.CountNodesInRange(state, b.Type, b.CenterTile)}", Ui.Dim, small: true);
            }
            if (def.WorkerRoom > 0) ui.Label(new Vector2(area.X, y), $"Provides room for {def.WorkerRoom} Workers.", Ui.Text, small: true);
        }
    }

    private static void DrawWorkshop(Ui ui, GameState state, int player, Building b, Simulation.Config.BuildingDef def, Vector2 at)
    {
        var orders = Manufacturing.Orders(state, player);
        var stock = state.TotalStock(player);
        int target = state.Config.Economy.GoodsStockTarget;
        ui.Label(at, $"Makes to order first, then keeps up to {target} of each in stock.", Ui.Dim, small: true);
        float y = at.Y + 16;
        foreach (var recipe in def.Recipes)
        {
            var inputs = StockOps.Describe(StockOps.ToArray(recipe.Inputs));
            int o = orders[(int)recipe.Output];
            ui.Label(new Vector2(at.X, y),
                $"{recipe.Output}: {recipe.Amount} from {inputs}   ordered {o}   in stock {stock[(int)recipe.Output]}   here {b.OutputStock[(int)recipe.Output]}",
                o > stock[(int)recipe.Output] ? Color.Gold : Ui.Text, small: true);
            y += 16;
        }
    }

    private static void DrawStorehouse(Ui ui, GameState state, SelectionController selection, Building b, Rectangle area, int y)
    {
        var store = b.Store!;
        int player = selection.Player;
        ui.Label(new Vector2(area.X, y), $"Capacity {store.Capacity}/resource   Carriers {store.CarrierIds.Count}/{store.CarriersPurchased}{(store.Starved ? "   STARVED - add Carriers" : "")}",
            store.Starved ? Color.Yellow : Ui.Text, small: true);
        y += 18;
        int col = 0;
        foreach (var r in Resources.All)
        {
            int x = area.X + col * 190;
            int ry = y + (int)r / 3 * 0;
            ui.Label(new Vector2(x, y + ((int)r % 3) * 20), $"{r} {store.Stock[(int)r]}  min {store.MinimumStock[(int)r]}", Ui.Text, small: true);
            if (ui.Button(new Rectangle(x + 118, y + ((int)r % 3) * 20, 20, 17), "-", true, "Lower Minimum Stock by 10"))
                selection.Issue(new SetMinimumStockCommand(player, b.Id, r, store.MinimumStock[(int)r] - 10));
            if (ui.Button(new Rectangle(x + 140, y + ((int)r % 3) * 20, 20, 17), "+", true, "Raise Minimum Stock by 10: Carriers keep this much here"))
                selection.Issue(new SetMinimumStockCommand(player, b.Id, r, store.MinimumStock[(int)r] + 10));
            if ((int)r % 3 == 2) col++;
            _ = ry;
        }

        var eco = state.Config.Economy;
        int by = area.Bottom - 26;
        if (ui.Button(new Rectangle(area.X, by, 170, 24), $"Upgrade storage ({store.Upgrades}/{eco.StorehouseMaxUpgrades})",
                store.Upgrades < eco.StorehouseMaxUpgrades, $"Cost {StockOps.Describe(StockOps.ToArray(eco.StorehouseUpgradeCost))}"))
            selection.Issue(new UpgradeStorehouseCommand(player, b.Id));
        if (ui.Button(new Rectangle(area.X + 174, by, 170, 24), $"+{eco.CarriersPerPurchase} Carriers", store.CarriersPurchased < eco.StorehouseMaxCarriers,
                $"Cost {StockOps.Describe(StockOps.ToArray(eco.CarrierPurchaseCost))}. Carriers count toward the Worker cap."))
            selection.Issue(new BuyCarriersCommand(player, b.Id));
    }

    private static void DrawRecruitment(Ui ui, GameState state, SelectionController selection, Building b, Rectangle area, int y)
    {
        var rec = b.Recruitment!;
        ui.Label(new Vector2(area.X, y), rec.Queue.Count == 0 ? "Queue: empty" : "Queue (click to cancel):", Ui.Text, small: true);
        int qx = area.X + 150;
        for (int i = 0; i < rec.Queue.Count; i++)
        {
            var label = rec.Queue[i].ToString();
            if (i == 0) label += rec.Paid ? $" {Math.Max(0, (rec.CompleteTick - state.Tick) / state.Config.Simulation.TicksPerSecond)}s" : " (waiting)";
            var tip = i == 0 && rec.Paid ? "Cancel training: the full cost is refunded." : "Remove from the queue.";
            int width = (int)ui.Small.MeasureString(label).X + 24;
            if (ui.Button(new Rectangle(qx, y - 2, width, 20), label + " x", true, tip))
                selection.Issue(new CancelRecruitCommand(selection.Player, b.Id, i));
            qx += width + 4;
        }
        ui.Label(new Vector2(area.X, y + 20), $"Soldiers {state.SoldierCount(selection.Player)}/{state.Config.Simulation.MaxSoldiersPerPlayer}", Ui.Dim, small: true);
        ui.Label(new Vector2(area.X + 160, y + 20), b.RallyPoint == null
            ? "Right-click the map to set a rally point for new Regiments."
            : "New Regiments gather at the flag. Right-click to move it.", Ui.Dim, small: true);
        if (b.RallyPoint != null && ui.Button(new Rectangle(area.Right - 130, y + 18, 130, 20), "Clear rally point", true,
                "New Regiments will wait just outside the building again."))
            selection.Issue(new SetRallyPointCommand(selection.Player, b.Id, null));
        int bx = area.X;
        foreach (var type in Enum.GetValues<SoldierType>())
        {
            var def = state.Config.Soldier(type);
            if (def.RecruitedAt != b.Type) continue;
            var cost = StockOps.ToArray(def.RecruitCost);
            var ranged = def.Ranged != null ? $", range {def.Ranged.Range}, pen {def.Ranged.Penetration}" : "";
            var tip = $"{type} x{def.RegimentSize}: {StockOps.Describe(cost)}. HP {def.Hp}, armour {def.Armour}, speed {def.Speed}{ranged}";
            tip += StockOps.CanAfford(state, selection.Player, cost) ? "" : ". Missing goods are ordered from your workshops; the recruit waits until they arrive.";
            if (ui.Button(new Rectangle(bx, area.Bottom - 26, 110, 24), $"{type}", true, tip))
                selection.Issue(new RecruitCommand(selection.Player, b.Id, type));
            bx += 114;
        }
    }

    private static void DrawFort(Ui ui, GameState state, SelectionController selection, Building b, Rectangle area, int y)
    {
        var fort = b.Fort!;
        int cap = FortSystem.CapacityRegiments(state, b);
        var line = $"Stationed Regiments {fort.StationedRegimentIds.Count}/{cap}";
        if (b.Type == BuildingType.Stronghold)
            line += $"   Garrison {fort.GarrisonAlive}/{state.Config.Forts.GarrisonSize}   Active shooters {FortSystem.ActiveShooters(state, b).Count}/{state.Config.Forts.StrongholdActiveShooterSlots}";
        ui.Label(new Vector2(area.X, y), line, Ui.Text, small: true);
        ui.Label(new Vector2(area.X, y + 16), "Right-click this building with Regiments selected to station them.", Ui.Dim, small: true);

        int by = area.Bottom - 26;
        if (b.Type == BuildingType.Outpost &&
            ui.Button(new Rectangle(area.X, by, 170, 24), "Upgrade to Stronghold", b.State == BuildingState.Active,
                $"Cost {StockOps.Describe(StockOps.ToArray(state.Config.Forts.StrongholdUpgradeCost))}. Adds a Garrison and lets Stationed Soldiers shoot."))
            selection.Issue(new UpgradeOutpostCommand(selection.Player, b.Id));
        if (ui.Button(new Rectangle(area.X + 174, by, 130, 24), "Release all", fort.StationedRegimentIds.Count > 0))
            selection.Issue(new UnstationCommand(selection.Player, b.Id));
    }
}
