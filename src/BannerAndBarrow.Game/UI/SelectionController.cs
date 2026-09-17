using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using BannerAndBarrow.Game.Rendering;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Commands;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Territory;

namespace BannerAndBarrow.Game.UI;

public enum PlacementMode
{
    None,
    Building,
    Road,
    /// <summary>Drag a line of Walls or Gates.</summary>
    Wall,
    /// <summary>Click a building or drag across roads to have Builders tear them down.</summary>
    Demolish,
}

/// <summary>Turns mouse and keyboard input on the map into selection changes and commands for the local Player.</summary>
public sealed class SelectionController
{
    private const float T = FixExtensions.TileSize;
    private readonly Match _match;
    private Vector2? _leftPressScreen;
    private Vector2? _rightPressWorld;
    private TileCoord? _roadStart;

    public int Player { get; }
    public SelectionView View { get; } = new();
    public PlacementMode Placement { get; private set; }
    public BuildingType PlacementType { get; private set; }
    public RoadKind PlacementRoad { get; private set; }
    public Rectangle? BoxRectangle { get; private set; }

    public SelectionController(Match match, int player)
    {
        _match = match;
        Player = player;
    }

    private GameState State => _match.State;

    public void Issue(GameCommand command) => _match.Commands.Enqueue(command);

    public void BeginPlacement(BuildingType type)
    {
        Placement = PlacementMode.Building;
        PlacementType = type;
    }

    /// <summary>Walls and Gates are dragged out in lines like roads.</summary>
    public void BeginWall(BuildingType type)
    {
        Placement = PlacementMode.Wall;
        PlacementType = type;
        _roadStart = null;
    }

    public void BeginRoad(RoadKind kind)
    {
        Placement = PlacementMode.Road;
        PlacementRoad = kind;
        _roadStart = null;
    }

    public void BeginDemolish()
    {
        Placement = PlacementMode.Demolish;
        _roadStart = null;
    }

    public void CancelPlacement()
    {
        Placement = PlacementMode.None;
        _roadStart = null;
    }

    public List<Regiment> SelectedRegiments() =>
        View.RegimentIds.Select(State.GetRegiment).Where(r => r != null && r.Owner == Player).Cast<Regiment>().ToList();

    public Building? SelectedBuilding => State.GetBuilding(View.BuildingId);

    public void Prune()
    {
        View.RegimentIds.RemoveWhere(id => State.GetRegiment(id) == null);
        if (View.BuildingId != 0 && State.GetBuilding(View.BuildingId) == null) View.BuildingId = 0;
    }

    public void Update(InputState input, Camera2D camera, bool mouseOverUi)
    {
        Prune();
        HandleHotkeys(input);

        var world = camera.ScreenToWorld(input.MousePosition);
        var tile = new TileCoord((int)MathF.Floor(world.X / T), (int)MathF.Floor(world.Y / T));

        if (Placement != PlacementMode.None)
        {
            UpdatePlacement(input, tile, mouseOverUi);
            return;
        }

        if (input.LeftPressed && !mouseOverUi) _leftPressScreen = input.MousePosition;
        if (_leftPressScreen.HasValue)
        {
            var start = _leftPressScreen.Value;
            var cur = input.MousePosition;
            BoxRectangle = Vector2.Distance(start, cur) > 6
                ? new Rectangle((int)MathF.Min(start.X, cur.X), (int)MathF.Min(start.Y, cur.Y), (int)MathF.Abs(cur.X - start.X), (int)MathF.Abs(cur.Y - start.Y))
                : null;
            if (input.LeftReleased)
            {
                if (BoxRectangle.HasValue) BoxSelect(camera, BoxRectangle.Value, input.Shift);
                else ClickSelect(world, tile, input.Shift);
                _leftPressScreen = null;
                BoxRectangle = null;
            }
        }

        if (input.RightPressed && !mouseOverUi) _rightPressWorld = world;
        if (input.RightReleased && _rightPressWorld.HasValue)
        {
            var press = _rightPressWorld.Value;
            _rightPressWorld = null;
            // Moving engages anything spotted on the way; Ctrl marches past instead (retreats, repositioning).
            IssueRightClick(press, world, march: input.Ctrl);
        }
    }

    private void HandleHotkeys(InputState input)
    {
        if (input.KeyPressed(Keys.Escape))
        {
            if (Placement != PlacementMode.None) CancelPlacement();
            else
            {
                View.RegimentIds.Clear();
                View.BuildingId = 0;
            }
        }

        var regiments = SelectedRegiments();
        var formations = new[] { (Keys.D1, FormationType.Line), (Keys.D2, FormationType.Column), (Keys.D3, FormationType.Wedge), (Keys.D4, FormationType.Loose) };
        foreach (var (key, formation) in formations)
            if (input.KeyPressed(key) && regiments.Count > 0)
                Issue(new SetFormationCommand(Player, regiments.Select(r => r.Id).ToArray(), formation));

        if (input.KeyPressed(Keys.H) && regiments.Count > 0) Issue(new StopCommand(Player, regiments.Select(r => r.Id).ToArray()));
        if (input.KeyPressed(Keys.G)) FormBattleLine();
        if (input.KeyPressed(Keys.T)) ToggleHoldGround();
        if (input.KeyPressed(Keys.M) && regiments.Count > 1) Issue(new MergeRegimentsCommand(Player, regiments.Select(r => r.Id).ToArray()));
        if (input.KeyPressed(Keys.U) && regiments.Count > 0) Issue(new UpgradeRegimentsCommand(Player, regiments.Select(r => r.Id).ToArray()));
        if (input.KeyPressed(Keys.Delete) && SelectedBuilding is { } b && b.Owner == Player)
            Issue(new DemolishBuildingCommand(Player, b.Id));
    }

    public void ToggleHoldGround()
    {
        var regiments = SelectedRegiments();
        if (regiments.Count == 0) return;
        bool hold = !regiments.All(r => r.HoldGround);
        Issue(new SetStanceCommand(Player, regiments.Select(r => r.Id).ToArray(), hold));
    }

    public void FormBattleLine()
    {
        var regiments = SelectedRegiments();
        var infantry = regiments.FirstOrDefault(r => State.Config.Soldier(r.Type).Ranged == null);
        var ranged = regiments.Where(r => State.Config.Soldier(r.Type).Ranged != null).Select(r => r.Id).ToArray();
        if (infantry == null || ranged.Length == 0)
        {
            State.AddEvent(Player, GameEventKind.CommandRejected, "Select one infantry Regiment and at least one ranged Regiment.");
            return;
        }
        Issue(new FormBattleLineCommand(Player, infantry.Id, ranged));
    }

    private void UpdatePlacement(InputState input, TileCoord tile, bool mouseOverUi)
    {
        if (input.RightPressed || (input.KeyPressed(Keys.Escape)))
        {
            CancelPlacement();
            return;
        }
        if (mouseOverUi) return;

        if (Placement == PlacementMode.Building && input.LeftPressed)
        {
            var origin = BuildingOrigin(tile);
            Issue(new PlaceBuildingCommand(Player, PlacementType, origin));
            if (!input.Shift) CancelPlacement();
        }
        else if (Placement == PlacementMode.Demolish)
        {
            if (input.LeftPressed) _roadStart = tile;
            if (input.LeftReleased && _roadStart.HasValue)
            {
                var start = _roadStart.Value;
                _roadStart = null;
                int buildingId = State.Map.InBounds(start) ? State.Map.BuildingIds[State.Map.Index(start)] : 0;
                if (start == tile && buildingId != 0)
                    Issue(new DemolishBuildingCommand(Player, buildingId));
                else
                    Issue(new DemolishRoadsCommand(Player, LineTiles(start, tile).ToArray()));
                if (!input.Shift) CancelPlacement();
            }
        }
        else if (Placement == PlacementMode.Road)
        {
            if (input.LeftPressed) _roadStart = tile;
            if (input.LeftReleased && _roadStart.HasValue)
            {
                Issue(new BuildRoadCommand(Player, PlacementRoad, LineTiles(_roadStart.Value, tile).ToArray()));
                _roadStart = null;
                if (!input.Shift) CancelPlacement();
            }
        }
        else if (Placement == PlacementMode.Wall)
        {
            if (input.LeftPressed) _roadStart = tile;
            if (input.LeftReleased && _roadStart.HasValue)
            {
                Issue(new PlaceWallsCommand(Player, PlacementType, LineTiles(_roadStart.Value, tile).ToArray()));
                _roadStart = null;
                if (!input.Shift) CancelPlacement();
            }
        }
    }

    public TileCoord BuildingOrigin(TileCoord hovered)
    {
        int size = State.Config.Building(PlacementType).Size;
        return new TileCoord(hovered.X - size / 2, hovered.Y - size / 2);
    }

    public static IEnumerable<TileCoord> LineTiles(TileCoord a, TileCoord b)
    {
        // Horizontal then vertical leg: roads read better as L-shapes than as diagonals.
        int x = a.X, y = a.Y;
        yield return a;
        while (x != b.X)
        {
            x += Math.Sign(b.X - x);
            yield return new TileCoord(x, y);
        }
        while (y != b.Y)
        {
            y += Math.Sign(b.Y - y);
            yield return new TileCoord(x, y);
        }
    }

    private void BoxSelect(Camera2D camera, Rectangle screenBox, bool add)
    {
        if (!add)
        {
            View.RegimentIds.Clear();
            View.BuildingId = 0;
        }
        foreach (var r in State.Regiments.Values)
        {
            if (r.Owner != Player || r.IsStationed) continue;
            var screen = camera.WorldToScreen(State.RegimentCentroid(r).ToPixels());
            if (screenBox.Contains((int)screen.X, (int)screen.Y)) View.RegimentIds.Add(r.Id);
        }
    }

    private void ClickSelect(Vector2 world, TileCoord tile, bool add)
    {
        var point = world.ToFixTiles();
        Soldier? best = null;
        var bestD = Fix.FromRatio(1, 2);
        foreach (var s in State.Soldiers.Values)
        {
            if (s.Stationed || (s.Owner != Player && !State.CanSee(Player, s.Position) && !State.IsRevealed(Player, s.RegimentId))) continue;
            var d = FixVec2.DistanceSquared(s.Position, point);
            if (d < bestD)
            {
                bestD = d;
                best = s;
            }
        }

        if (!add)
        {
            View.RegimentIds.Clear();
            View.BuildingId = 0;
        }

        if (best != null)
        {
            if (best.Owner == Player)
            {
                if (add && View.RegimentIds.Contains(best.RegimentId)) View.RegimentIds.Remove(best.RegimentId);
                else View.RegimentIds.Add(best.RegimentId);
            }
            return;
        }

        if (State.Map.InBounds(tile))
        {
            var id = State.Map.BuildingIds[State.Map.Index(tile)];
            if (id == 0 || (State.GetBuilding(id)?.Owner != Player && State.KnownBuilding(Player, id) == null)) id = GhostAt(tile);
            if (id != 0)
            {
                View.RegimentIds.Clear();
                View.BuildingId = id;
            }
        }
    }

    /// <summary>A remembered enemy building at the tile (it may have been destroyed since).</summary>
    private int GhostAt(TileCoord tile)
    {
        foreach (var g in State.Players[Player].KnownBuildings.Values)
            if (tile.X >= g.Origin.X && tile.Y >= g.Origin.Y && tile.X < g.Origin.X + g.Size && tile.Y < g.Origin.Y + g.Size && State.GetBuilding(g.Id) != null)
                return g.Id;
        return 0;
    }

    private void IssueRightClick(Vector2 pressWorld, Vector2 releaseWorld, bool march)
    {
        var regiments = SelectedRegiments();
        if (regiments.Count == 0)
        {
            // A recruitment building selected: right-click places its rally point.
            if (SelectedBuilding is { Recruitment: not null } recruiter && recruiter.Owner == Player)
                Issue(new SetRallyPointCommand(Player, recruiter.Id, releaseWorld.ToFixTiles()));
            return;
        }
        var ids = regiments.Select(r => r.Id).ToArray();
        var target = pressWorld.ToFixTiles();
        var tile = target.ToTile();

        // Enemy Soldier under the cursor?
        foreach (var s in State.Soldiers.Values)
        {
            if (s.Owner == Player || s.Stationed || !State.CanSee(Player, s.Position) && !State.IsRevealed(Player, s.RegimentId)) continue;
            if (FixVec2.DistanceSquared(s.Position, target) <= Fix.FromRatio(3, 4))
            {
                Issue(new AttackRegimentCommand(Player, ids, s.RegimentId));
                return;
            }
        }

        if (State.Map.InBounds(tile) && (State.KnownBuilding(Player, State.Map.BuildingIds[State.Map.Index(tile)]) ?? State.GetBuilding(GhostAt(tile))) is { } building &&
            (building.Owner == Player || State.KnownBuilding(Player, building.Id) != null))
        {
            if (building.Owner != Player && building.State != BuildingState.Ruin)
            {
                Issue(new AttackBuildingCommand(Player, ids, building.Id));
                return;
            }
            if (building.Owner == Player && building.IsFort)
            {
                Issue(new StationCommand(Player, ids, building.Id));
                return;
            }
            if (building.Owner == Player)
            {
                // Right-clicking another of your own buildings just moves there (e.g. to defend it).
                Issue(new MoveRegimentsCommand(Player, ids, building.NearestAccessTile(State.Map, State.RegimentCentroid(regiments[0])).Center, FixVec2.North, AttackMove: !march));
                return;
            }
        }

        var centroid = FixVec2.Zero;
        foreach (var r in regiments) centroid += State.RegimentCentroid(r);
        centroid = centroid / regiments.Count;

        var drag = releaseWorld - pressWorld;
        FixVec2 facing = drag.Length() > 20
            ? new FixVec2(Fix.FromRaw((long)(drag.X / drag.Length() * Fix.OneRaw)), Fix.FromRaw((long)(drag.Y / drag.Length() * Fix.OneRaw)))
            : (target - centroid).Normalized();
        if (facing.IsZero) facing = FixVec2.North;
        Issue(new MoveRegimentsCommand(Player, ids, target, facing, AttackMove: !march));
    }

    public void MoveSelectedTo(FixVec2 target)
    {
        var regiments = SelectedRegiments();
        if (regiments.Count == 0) return;
        var centroid = FixVec2.Zero;
        foreach (var r in regiments) centroid += State.RegimentCentroid(r);
        centroid = centroid / regiments.Count;
        var facing = (target - centroid).Normalized();
        Issue(new MoveRegimentsCommand(Player, regiments.Select(r => r.Id).ToArray(), target, facing.IsZero ? FixVec2.North : facing, false));
    }

    public void DrawWorld(SpriteBatch sb, Primitives p, SpriteFont small, Camera2D camera, InputState input)
    {
        var world = camera.ScreenToWorld(input.MousePosition);
        var tile = new TileCoord((int)MathF.Floor(world.X / T), (int)MathF.Floor(world.Y / T));
        DrawFormationPlan(sb, p, small, camera, input, world, tile);

        if (Placement == PlacementMode.Building)
        {
            var def = State.Config.Building(PlacementType);
            var origin = BuildingOrigin(tile);
            var check = BuildRules.CanPlaceBuilding(State, Player, PlacementType, origin);
            var color = check.Ok ? Color.LimeGreen : Color.Red;
            var pos = new Vector2(origin.X * T, origin.Y * T);
            p.Rect(sb, pos, new Vector2(def.Size * T), color * 0.35f);
            p.RectOutline(sb, pos, new Vector2(def.Size * T), color, 2f / camera.Zoom);

            if (PlacementType is BuildingType.Woodcutter or BuildingType.Quarry or BuildingType.IronMine)
            {
                var radius = State.Config.Economy.ProductionRadius * T;
                var center = pos + new Vector2(def.Size * T / 2);
                p.Circle_(sb, center, radius, Color.White * 0.5f);
            }
            if (PlacementType == BuildingType.Storehouse)
                p.Circle_(sb, pos + new Vector2(def.Size * T / 2), State.Config.Territory.StorehouseReach * T, Palette.Player(Player) * 0.8f);

            var cost = BuildRules.GetCost(State, Player, PlacementType, origin);
            var label = check.Ok ? Simulation.Economy.StockOps.Describe(cost) + "  (hold Shift to place more)" : check.Reason;
            if (PlacementType is BuildingType.Woodcutter or BuildingType.Quarry or BuildingType.IronMine)
                label += $"  ({Simulation.Economy.WorkerSystem.CountNodesInRange(State, PlacementType, new TileCoord(origin.X + def.Size / 2, origin.Y + def.Size / 2))} resource tiles in reach)";
            if (PlacementType == BuildingType.Farm)
                label += $"  ({Simulation.Economy.WorkerSystem.CountNodesInRange(State, PlacementType, new TileCoord(origin.X + def.Size / 2, origin.Y + def.Size / 2))} tiles of room for Fields)";
            float scale = 1f / camera.Zoom;
            sb.DrawString(small, label, pos + new Vector2(0, def.Size * T + 4), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
        else if (Placement == PlacementMode.Demolish)
        {
            var tiles = _roadStart.HasValue ? LineTiles(_roadStart.Value, tile).ToArray() : new[] { tile };
            foreach (var t in tiles)
            {
                if (!State.Map.InBounds(t)) continue;
                var b = State.GetBuilding(State.Map.BuildingIds[State.Map.Index(t)]);
                bool target = State.Map.Roads[State.Map.Index(t)] != RoadKind.None || (b != null && b.Owner == Player && tiles.Length == 1);
                p.Rect(sb, new Vector2(t.X * T + 2, t.Y * T + 2), new Vector2(T - 4), (target ? Color.OrangeRed : Color.Gray) * 0.45f);
            }
            float scale = 1f / camera.Zoom;
            sb.DrawString(small, "Demolish: click a building or drag across roads (Shift: keep demolishing, Esc: stop)",
                new Vector2(tile.X * T, (tile.Y + 1) * T + 4), Color.OrangeRed, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
        else if (Placement == PlacementMode.Road)
        {
            var tiles = _roadStart.HasValue ? LineTiles(_roadStart.Value, tile) : new[] { tile };
            foreach (var t in tiles)
            {
                var ok = BuildRules.CanPlaceRoad(State, Player, t, PlacementRoad).Ok;
                p.Rect(sb, new Vector2(t.X * T + 4, t.Y * T + 4), new Vector2(T - 8), (ok ? Color.LimeGreen : Color.Red) * 0.45f);
            }
        }
        else if (Placement == PlacementMode.Wall)
        {
            var tiles = (_roadStart.HasValue ? LineTiles(_roadStart.Value, tile) : new[] { tile }).ToArray();
            int ok = 0;
            foreach (var t in tiles)
            {
                bool can = BuildRules.CanPlaceBuilding(State, Player, PlacementType, t).Ok;
                if (can) ok++;
                p.Rect(sb, new Vector2(t.X * T + 2, t.Y * T + 2), new Vector2(T - 4), (can ? Color.LimeGreen : Color.Red) * 0.45f);
            }
            var cost = BuildRules.GetCost(State, Player, PlacementType, tile);
            for (int i = 0; i < cost.Length; i++) cost[i] *= ok;
            float scale = 1f / camera.Zoom;
            var label = $"{PlacementType} x{ok}: {Simulation.Economy.StockOps.Describe(cost)}" +
                        (PlacementType == BuildingType.Gate ? "  (your units walk through; the enemy must break it)" : "  (blocks everyone; only melee can break it)");
            sb.DrawString(small, label, new Vector2(tile.X * T, (tile.Y + 1) * T + 4), Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// Shows where a move will put the men: while the right button is held, the Regiments are drawn where they
    /// would stand, facing the way you drag. Once ordered, the same marks stay on the ground until they arrive,
    /// so you can see the formation forming up.
    /// </summary>
    private void DrawFormationPlan(SpriteBatch sb, Primitives p, SpriteFont small, Camera2D camera, InputState input, Vector2 world, TileCoord tile)
    {
        var regiments = SelectedRegiments();
        if (regiments.Count == 0 || Placement != PlacementMode.None) return;
        var colour = Palette.Player(Player);
        float scale = Math.Clamp(1f / camera.Zoom, 0.6f, 2.5f);

        if (input.RightDown && _rightPressWorld is { } press)
        {
            // Hovering your own fort: this drop will send them inside instead.
            var fort = State.Map.InBounds(tile) ? State.GetBuilding(State.Map.BuildingIds[State.Map.Index(tile)]) : null;
            if (fort is { IsFort: true } && fort.Owner == Player)
            {
                var pos = new Vector2(fort.Origin.X * T, fort.Origin.Y * T);
                var size = new Vector2(fort.Size * T);
                p.Rect(sb, pos, size, colour * 0.3f);
                p.RectOutline(sb, pos, size, Color.White, 2f * scale);
                int inside = fort.Fort?.StationedRegimentIds.Count ?? 0;
                sb.DrawString(small, $"Station inside ({inside}/{Simulation.Military.FortSystem.CapacityRegiments(State, fort)})",
                    pos + new Vector2(0, size.Y + 4), Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                return;
            }

            var drag = world - press;
            var facing = drag.Length() > 20 ? Vector2.Normalize(drag) : Vector2.Normalize(press - Centroid(regiments));
            if (!float.IsFinite(facing.X) || facing.LengthSquared() < 0.01f) facing = new Vector2(0, -1);
            DrawSlots(sb, p, regiments, press, facing, colour, scale, ghost: true);
            p.Line(sb, press, press + facing * 40f * scale, Color.White * 0.8f, 2f * scale);
            return;
        }

        // Ordered and still marching: show where each man is headed.
        foreach (var r in regiments)
        {
            if (r.AnchorArrived || r.IsStationed) continue;
            var facing = new Vector2(r.DestinationFacing.X.ToFloat(), r.DestinationFacing.Y.ToFloat());
            if (facing.LengthSquared() < 0.01f) facing = new Vector2(0, -1);
            DrawSlots(sb, p, new List<Regiment> { r }, r.Destination.ToPixels(), facing, colour, scale, ghost: false);
        }
    }

    private Vector2 Centroid(List<Regiment> regiments)
    {
        var sum = Vector2.Zero;
        foreach (var r in regiments) sum += State.RegimentCentroid(r).ToPixels();
        return sum / Math.Max(1, regiments.Count);
    }

    /// <summary>Draws one marker per Soldier at its formation slot. Several Regiments line up beside or behind each other.</summary>
    private void DrawSlots(SpriteBatch sb, Primitives p, List<Regiment> regiments, Vector2 centre, Vector2 facing, Color colour, float scale, bool ghost)
    {
        var right = new Vector2(-facing.Y, facing.X);
        float offset = 0f;
        var ordered = regiments.OrderBy(r => r.Id).ToList();
        foreach (var r in ordered)
        {
            var def = State.Config.Soldier(r.Type);
            var formation = State.Config.Formation(r.Formation);
            int count = r.SoldierIds.Count;
            float spacing = formation.Spacing.ToFloat() * T;
            // Column: Regiments queue up one behind another. Everything else: side by side.
            bool column = r.Formation == FormationType.Column;
            float width = Math.Min(count, formation.Width > 0 ? formation.Width : 4) * spacing;
            float depth = Simulation.Military.FormationLayout.Depth(formation, count).ToFloat() * T;
            var anchor = centre + (column ? -facing * (offset + depth / 2) : right * (offset + width / 2 - (Span(ordered) / 2)));
            for (int i = 0; i < count; i++)
            {
                var slot = Simulation.Military.FormationLayout.SlotOffset(formation, i, count);
                var at = anchor + right * (slot.X.ToFloat() * T) + facing * (slot.Y.ToFloat() * T);
                float radius = def.Radius.ToFloat() * T;
                p.Circle_(sb, at, radius + 1.5f * scale, colour * (ghost ? 0.85f : 0.5f));
                if (ghost) p.Disc(sb, at, radius * 0.5f, colour * 0.5f);
            }
            offset += (column ? depth : width) + spacing;
        }
    }

    /// <summary>Total width of Regiments standing side by side, so the block stays centred on the cursor.</summary>
    private float Span(List<Regiment> regiments)
    {
        float span = 0f;
        foreach (var r in regiments)
        {
            if (r.Formation == FormationType.Column) continue;
            var formation = State.Config.Formation(r.Formation);
            float spacing = formation.Spacing.ToFloat() * T;
            span += Math.Min(r.SoldierIds.Count, formation.Width > 0 ? formation.Width : 4) * spacing + spacing;
        }
        return span;
    }

    public void DrawScreen(SpriteBatch sb, Primitives p)
    {
        if (BoxRectangle is { } box)
        {
            p.Rect(sb, box, Color.White * 0.08f);
            p.RectOutline(sb, box, Color.White * 0.8f);
        }
    }
}
