using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Military;
using BannerAndBarrow.Simulation.Territory;

namespace BannerAndBarrow.Game.Rendering;

public sealed class RenderOptions
{
    public bool ShowPresence;
    public bool ShowFlowField;
    public bool ShowTerritory = true;
    /// <summary>Whose eyes the world is drawn through (fog of war).</summary>
    public int ViewPlayer;
    /// <summary>Debug: draw everything as if there were no fog.</summary>
    public bool RevealMap;
    /// <summary>Buildings use the art painted by <see cref="BuildingArt"/> instead of the Kenney sprites (F5 toggles).</summary>
    public bool GeneratedBuildings = true;
}

/// <summary>Draws the map, buildings, units and projectiles in world space (1 tile = 32 px).</summary>
public sealed partial class WorldRenderer
{
    private const float T = FixExtensions.TileSize;
    private readonly GraphicsDevice _device;
    private readonly Primitives _p;
    private readonly SpriteRegistry _sprites;
    private readonly SpriteFont _font;
    private readonly SpriteFont _small;

    private Texture2D? _terrainTexture;
    private int _terrainMajor = -1, _terrainMinor = -1;
    private Texture2D? _territoryTexture;
    private int _territoryVersion = -1;
    private Texture2D? _presenceTexture;
    private long _presenceTick = -2;

    public Texture2D? TerrainTexture => _terrainTexture;
    public Texture2D? TerritoryTexture => _territoryTexture;

    public WorldRenderer(GraphicsDevice device, Primitives primitives, SpriteRegistry sprites, SpriteFont font, SpriteFont small)
    {
        _device = device;
        _p = primitives;
        _sprites = sprites;
        _font = font;
        _small = small;
        _buildingArt = new BuildingArt(device);
    }

    private readonly BuildingArt _buildingArt;
    private bool _generatedBuildings = true;

    /// <summary>Draws the building's image into its footprint: painted art, a Kenney sprite, or a flat placeholder. Returns false for the placeholder.</summary>
    private bool DrawBuildingImage(SpriteBatch sb, BuildingType type, int owner, int buildingSize, Rectangle dest, Color tint)
    {
        if (_generatedBuildings)
        {
            var tex = _buildingArt.Get(type, owner, buildingSize);
            sb.Draw(tex, new Rectangle(dest.X - 2, dest.Y - 2, dest.Width + 4, dest.Height + 4), tint);
            return true;
        }
        var sprite = _sprites.Get("building." + type);
        if (!sprite.HasValue) return false;
        DrawFitted(sb, sprite.Value, dest, tint);
        return true;
    }

    private Texture2D? _fogTexture;
    private int _fogVersion = -1;
    private int _fogPlayer = -1;
    private bool _fogReveal;
    private int _territoryVisionVersion = -1;
    public Texture2D? FogTexture => _fogTexture;

    /// <summary>Set by <see cref="Draw"/>: whose vision applies, or -1 when the map is revealed.</summary>
    public int Viewer { get; private set; }

    public bool Sees(GameState state, TileCoord t) => Viewer < 0 || state.CanSee(Viewer, t);
    public bool Sees(GameState state, FixVec2 p) => Viewer < 0 || state.CanSee(Viewer, p);
    public bool Sees(GameState state, Building b) => Viewer < 0 || state.CanSee(Viewer, b);

    public void SetViewer(GameState state, RenderOptions options) =>
        Viewer = options.RevealMap || !state.Config.Territory.FogOfWar ? -1 : options.ViewPlayer;

    public void RefreshCaches(GameState state)
    {
        var map = state.Map;
        if (_terrainTexture == null || _terrainMajor != map.MajorVersion || (_terrainMinor != map.MinorVersion && state.Tick % 20 == 0))
        {
            _terrainTexture ??= new Texture2D(_device, map.Width, map.Height);
            var data = new Color[map.Width * map.Height];
            for (int i = 0; i < data.Length; i++) data[i] = Palette.Terrain(map.Terrain[i]);
            _terrainTexture.SetData(data);
            _terrainMajor = map.MajorVersion;
            _terrainMinor = map.MinorVersion;
        }

        bool fogChanged = _fogVersion != state.Vision.Version || _fogPlayer != Viewer || _fogReveal != (Viewer < 0);
        if (_territoryTexture == null || _territoryVersion != state.Territory.Version || (fogChanged && _territoryVisionVersion != state.Vision.Version))
        {
            _territoryTexture ??= new Texture2D(_device, map.Width, map.Height);
            var data = new Color[map.Width * map.Height];
            for (int i = 0; i < data.Length; i++)
            {
                var owner = state.Territory.Owner[i];
                // Enemy Territory is only shown where we can see it.
                if (owner >= 0 && owner != Viewer && Viewer >= 0 && (state.Vision.Mask[i] & (1 << Viewer)) == 0) owner = TerritoryMap.None;
                data[i] = owner == TerritoryMap.Contested ? new Color(40, 40, 40) * 0.35f
                        : owner >= 0 ? Palette.Player(owner) * 0.22f
                        : Color.Transparent;
            }
            _territoryTexture.SetData(data);
            _territoryVersion = state.Territory.Version;
            _territoryVisionVersion = state.Vision.Version;
        }

        if (_fogTexture == null || fogChanged)
        {
            _fogTexture ??= new Texture2D(_device, map.Width, map.Height);
            var data = new Color[map.Width * map.Height];
            if (Viewer >= 0)
            {
                byte bit = (byte)(1 << Viewer);
                var fog = new Color(8, 10, 20) * 0.5f;
                for (int i = 0; i < data.Length; i++) data[i] = (state.Vision.Mask[i] & bit) != 0 ? Color.Transparent : fog;
            }
            _fogTexture.SetData(data);
            _fogVersion = state.Vision.Version;
            _fogPlayer = Viewer;
            _fogReveal = Viewer < 0;
        }
    }

    public void Draw(SpriteBatch sb, GameState state, Camera2D camera, float alpha, SelectionView selection, RenderOptions options)
    {
        SetViewer(state, options);
        _generatedBuildings = options.GeneratedBuildings;
        RefreshCaches(state);
        BeginAnimationFrame(state, alpha);
        BeginCombatFrame(state);
        var map = state.Map;
        var vis = camera.VisibleWorld;
        int x0 = Math.Max(0, (int)(vis.X / T) - 1), y0 = Math.Max(0, (int)(vis.Y / T) - 1);
        int x1 = Math.Min(map.Width - 1, (int)(vis.Right / T) + 1), y1 = Math.Min(map.Height - 1, (int)(vis.Bottom / T) + 1);
        bool detail = camera.Zoom >= 0.28f;

        // Terrain
        if (_sprites.Has("terrain.grass") && camera.Zoom >= 0.4f)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                DrawTileSprite(sb, "terrain." + map.Terrain[map.Index(x, y)].ToString().ToLowerInvariant(), x, y, Palette.Terrain(map.Terrain[map.Index(x, y)]));
        }
        else if (_terrainTexture != null)
        {
            sb.Draw(_terrainTexture, Vector2.Zero, null, Color.White, 0f, Vector2.Zero, T, SpriteEffects.None, 0f);
        }

        if (detail) DrawTileDetails(sb, state, x0, y0, x1, y1);

        if (options.ShowTerritory && _territoryTexture != null)
        {
            // Textured terrain needs a lighter tint than the flat placeholder colours to stay readable.
            var tint = _sprites.Has("terrain.grass") ? Color.White * 0.45f : Color.White;
            sb.Draw(_territoryTexture, Vector2.Zero, null, tint, 0f, Vector2.Zero, T, SpriteEffects.None, 0f);
            DrawBorders(sb, state, x0, y0, x1, y1, camera.Zoom);
        }

        if (options.ShowPresence) DrawPresence(sb, state);

        if (options.ShowFlowField) DrawFlowField(sb, state, selection, x0, y0, x1, y1, camera.Zoom);

        foreach (var site in state.RoadSites.Values)
        {
            if (site.Owner != Viewer && !Sees(state, site.Tile)) continue;
            var origin = new Vector2(site.Tile.X * T, site.Tile.Y * T);
            if (site.IsRemoval)
            {
                _p.Line(sb, origin + new Vector2(8), origin + new Vector2(T - 8), Color.OrangeRed, 3f);
                _p.Line(sb, origin + new Vector2(T - 8, 8), origin + new Vector2(8, T - 8), Color.OrangeRed, 3f);
            }
            else
            {
                _p.Rect(sb, origin + new Vector2(10), new Vector2(T - 20), Palette.Player(site.Owner) * 0.5f);
            }
        }

        foreach (var b in state.Buildings.Values)
        {
            if (b.Origin.X > x1 + 1 || b.Origin.Y > y1 + 1 || b.Origin.X + b.Size < x0 - 1 || b.Origin.Y + b.Size < y0 - 1) continue;
            if (Sees(state, b)) DrawBuilding(sb, state, b, selection.BuildingId == b.Id, camera.Zoom);
        }
        if (Viewer >= 0)
        {
            // Enemy buildings out of sight, as last seen.
            foreach (var ghost in state.Players[Viewer].KnownBuildings.Values)
            {
                if (ghost.Origin.X > x1 + 1 || ghost.Origin.Y > y1 + 1 || ghost.Origin.X + ghost.Size < x0 - 1 || ghost.Origin.Y + ghost.Size < y0 - 1) continue;
                if (state.GetBuilding(ghost.Id) is { } live && Sees(state, live)) continue;
                DrawGhostBuilding(sb, ghost, selection.BuildingId == ghost.Id, camera.Zoom);
            }
        }

        foreach (var b in state.Buildings.Values)
            if (b.RallyPoint != null && (Viewer < 0 || b.Owner == Viewer))
                DrawRallyPoint(sb, b, selection.BuildingId == b.Id, camera.Zoom);

        // Fog: dims everything we can't see right now; units there aren't drawn at all.
        if (Viewer >= 0 && _fogTexture != null)
            sb.Draw(_fogTexture, Vector2.Zero, null, Color.White, 0f, Vector2.Zero, T, SpriteEffects.None, 0f);

        var visPad = new RectangleF(vis.X - T, vis.Y - T, vis.Width + 2 * T, vis.Height + 2 * T);
        foreach (var w in state.Workers.Values)
        {
            var pos = FixExtensions.Lerp(w.PreviousPosition, w.Position, alpha);
            if (!visPad.Contains(pos) || (w.Owner != Viewer && !Sees(state, w.Position))) continue;
            DrawWorker(sb, state, w, pos, camera.Zoom);
        }

        foreach (var s in state.Soldiers.Values)
        {
            if (s.Stationed) continue;
            var pos = FixExtensions.Lerp(s.PreviousPosition, s.Position, alpha);
            if (!visPad.Contains(pos) || (s.Owner != Viewer && !Sees(state, s.Position) && !state.IsRevealed(Viewer, s.RegimentId))) continue;
            DrawSoldier(sb, state, s, pos, selection.RegimentIds.Contains(s.RegimentId), camera.Zoom);
        }

        foreach (var r in state.Regiments.Values)
        {
            if (r.IsStationed || r.SoldierIds.Count == 0) continue;
            if (Viewer >= 0 && !state.CanSee(Viewer, r)) continue;
            DrawBanner(sb, state, r, selection.RegimentIds.Contains(r.Id), camera.Zoom);
        }

        foreach (var proj in state.Projectiles)
            if (Sees(state, proj.Origin) || Sees(state, proj.Target)) DrawProjectile(sb, state, proj, alpha);
    }

    private void DrawGhostBuilding(SpriteBatch sb, BuildingSighting ghost, bool selected, float zoom)
    {
        var pos = new Vector2(ghost.Origin.X * T + 2, ghost.Origin.Y * T + 2);
        var size = new Vector2(ghost.Size * T - 4);
        var dest = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
        var player = Palette.Player(ghost.Owner);
        if (ghost.State == BuildingState.Ruin)
        {
            _p.Rect(sb, dest, new Color(50, 45, 45) * 0.6f);
            DrawCenteredText(sb, "RUIN?", pos + size / 2, Color.White * 0.6f, zoom);
        }
        else
        {
            float a = ghost.State == BuildingState.UnderConstruction ? 0.3f : 0.55f;
            if (!DrawBuildingImage(sb, ghost.Type, ghost.Owner, ghost.Size, dest, Color.LightGray * a))
            {
                _p.Rect(sb, dest, Palette.Building(ghost.Type) * a);
                DrawCenteredText(sb, Palette.ShortName(ghost.Type), pos + size / 2, Color.White * 0.6f, zoom);
            }
            _p.RectOutline(sb, pos, size, player * 0.5f, 2f);
        }
        if (selected) _p.RectOutline(sb, pos - new Vector2(4), size + new Vector2(8), Color.White * 0.7f, 2f);
    }

    private void DrawTileSprite(SpriteBatch sb, string key, int x, int y, Color fallback)
    {
        var sprite = _sprites.Get(key);
        var dest = new Rectangle(x * (int)T, y * (int)T, (int)T, (int)T);
        if (sprite.HasValue) sb.Draw(sprite.Value.Texture, dest, sprite.Value.Source, Color.White);
        else _p.Rect(sb, dest, fallback);
    }

    /// <summary>Draws "key.p{owner}" (team-coloured art) if present, otherwise "key" tinted with the Player colour.</summary>
    private bool DrawUnitSprite(SpriteBatch sb, string key, int owner, Vector2 pos, float size, Color tint, float rotation = 0f, bool flipX = false)
    {
        var sprite = _sprites.Get($"{key}.p{owner}");
        var color = Color.White;
        if (!sprite.HasValue)
        {
            sprite = _sprites.Get(key);
            color = Color.Lerp(Color.White, tint, 0.45f);
        }
        if (!sprite.HasValue) return false;
        var src = sprite.Value.Source;
        float scale = size / Math.Max(src.Width, src.Height);
        sb.Draw(sprite.Value.Texture, pos, src, color, rotation, new Vector2(src.Width / 2f, src.Height / 2f), scale,
            flipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        return true;
    }

    /// <summary>Scales a sprite to fit inside <paramref name="dest"/> keeping its aspect ratio, anchored to the bottom centre.</summary>
    private static void DrawFitted(SpriteBatch sb, Sprite sprite, Rectangle dest, Color color)
    {
        var src = sprite.Source;
        float scale = Math.Min(dest.Width / (float)src.Width, dest.Height / (float)src.Height);
        var size = new Vector2(src.Width, src.Height) * scale;
        var pos = new Vector2(dest.X + (dest.Width - size.X) / 2, dest.Bottom - size.Y);
        sb.Draw(sprite.Texture, pos, src, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawTileDetails(SpriteBatch sb, GameState state, int x0, int y0, int x1, int y1)
    {
        var map = state.Map;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            int i = map.Index(x, y);
            var origin = new Vector2(x * T, y * T);
            var road = map.Roads[i];
            if (road != RoadKind.None)
            {
                var key = road == RoadKind.Stone ? "road.stone" : "road.dirt";
                var sprite = _sprites.Get(key);
                if (sprite.HasValue) sb.Draw(sprite.Value.Texture, new Rectangle((int)origin.X, (int)origin.Y, (int)T, (int)T), sprite.Value.Source, Color.White);
                else _p.Rect(sb, origin + new Vector2(3), new Vector2(T - 6), road == RoadKind.Stone ? new Color(165, 160, 150) : new Color(150, 120, 80));
            }

            if (map.HasCrop(i) && Sees(state, new TileCoord(x, y))) DrawField(sb, state, x, y, i);

            if (map.Terrain[i] == Terrain.Forest && map.ResourceAmount[i] > 0)
            {
                var sprite = _sprites.Get("resource.tree");
                if (sprite.HasValue) DrawFitted(sb, sprite.Value, new Rectangle((int)origin.X + 1, (int)origin.Y - 4, (int)T - 2, (int)T + 2), Color.White);
                else
                {
                    uint h = (uint)(x * 73856093 ^ y * 19349663);
                    var off = new Vector2(8 + h % 16, 8 + (h >> 8) % 16);
                    _p.Disc(sb, origin + off, 7f + (h >> 16) % 4, new Color(34, 78, 38));
                }
            }
            else if (map.RegrowAtTick[i] != 0)
            {
                var sapling = _sprites.Get("resource.sapling");
                if (sapling.HasValue) DrawFitted(sb, sapling.Value, new Rectangle((int)origin.X + 8, (int)origin.Y + 10, (int)T - 16, (int)T - 12), Color.White);
                else _p.Disc(sb, origin + new Vector2(T / 2), 3f, new Color(80, 140, 60));
            }

            if (map.Deposits[i] is ResourceType deposit && map.ResourceAmount[i] > 0)
            {
                var key = deposit == ResourceType.Iron ? "resource.iron" : "resource.stone";
                var sprite = _sprites.Get(key);
                if (sprite.HasValue) DrawFitted(sb, sprite.Value, new Rectangle((int)origin.X + 2, (int)origin.Y + 2, (int)T - 4, (int)T - 4), Color.White);
                else
                {
                    var col = deposit == ResourceType.Iron ? new Color(70, 80, 100) : new Color(185, 185, 180);
                    _p.Disc(sb, origin + new Vector2(11, 13), 6f, col);
                    _p.Disc(sb, origin + new Vector2(21, 20), 5f, col * 0.9f);
                }
            }
        }
    }

    private void DrawBorders(SpriteBatch sb, GameState state, int x0, int y0, int x1, int y1, float zoom)
    {
        var owner = state.Territory.Owner;
        int w = state.Map.Width;
        float thick = Math.Max(2f, 2f / zoom);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            if (Viewer >= 0 && !state.Vision.IsVisible(x, y, Viewer)) continue;
            var o = owner[y * w + x];
            if (x + 1 < w && owner[y * w + x + 1] != o)
            {
                var c = BorderColor(o, owner[y * w + x + 1]);
                _p.Rect(sb, new Vector2((x + 1) * T - thick / 2, y * T), new Vector2(thick, T), c);
            }
            if (y + 1 < state.Map.Height && owner[(y + 1) * w + x] != o)
            {
                var c = BorderColor(o, owner[(y + 1) * w + x]);
                _p.Rect(sb, new Vector2(x * T, (y + 1) * T - thick / 2), new Vector2(T, thick), c);
            }
        }
    }

    private static Color BorderColor(sbyte a, sbyte b)
    {
        var o = a >= 0 ? a : b;
        return o >= 0 ? Palette.Player(o) * 0.9f : new Color(30, 30, 30) * 0.6f;
    }

    private int _presenceViewer = -2;

    private void DrawPresence(SpriteBatch sb, GameState state)
    {
        var map = state.Map;
        if (_presenceTexture == null || _presenceTick != state.Presence.LastComputedTick || _presenceViewer != Viewer)
        {
            _presenceTexture ??= new Texture2D(_device, map.Width, map.Height);
            var data = new Color[map.Width * map.Height];
            for (int i = 0; i < data.Length; i++)
            {
                byte m = state.Presence.Mask[i];
                if (Viewer >= 0 && (state.Vision.Mask[i] & (1 << Viewer)) == 0) m &= (byte)(1 << Viewer);
                data[i] = m == 0 ? Color.Transparent : (m & (m - 1)) != 0 ? new Color(255, 255, 255) * 0.25f : Palette.Player(m == 1 ? 0 : m == 2 ? 1 : 2) * 0.28f;
            }
            _presenceTexture.SetData(data);
            _presenceTick = state.Presence.LastComputedTick;
            _presenceViewer = Viewer;
        }
        sb.Draw(_presenceTexture, Vector2.Zero, null, Color.White, 0f, Vector2.Zero, T, SpriteEffects.None, 0f);
    }

    private void DrawFlowField(SpriteBatch sb, GameState state, SelectionView selection, int x0, int y0, int x1, int y1, float zoom)
    {
        if (zoom < 0.35f) return;
        var regiment = selection.RegimentIds.Select(state.GetRegiment).FirstOrDefault(r => r != null);
        if (regiment == null || !state.FlowFields.TryPeek(regiment.DestinationKey, out var field) || field == null) return;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            var dir = field.GetDirection(x, y);
            var center = new Vector2(x * T + T / 2, y * T + T / 2);
            if (field.IsGoal(x, y))
            {
                _p.Rect(sb, center - new Vector2(6), new Vector2(12), Color.Yellow * 0.8f);
                continue;
            }
            if (dir.IsZero) continue;
            var d = new Vector2(dir.X.ToFloat(), dir.Y.ToFloat());
            _p.Line(sb, center - d * 9, center + d * 9, Color.White * 0.55f, 2f);
            _p.Disc(sb, center + d * 9, 2.5f, Color.White * 0.8f);
        }
    }

    private void DrawBuilding(SpriteBatch sb, GameState state, Building b, bool selected, float zoom)
    {
        var pos = new Vector2(b.Origin.X * T + 2, b.Origin.Y * T + 2);
        var size = new Vector2(b.Size * T - 4);
        var player = Palette.Player(b.Owner);
        var dest = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);

        if (b.State == BuildingState.Ruin)
        {
            _p.Rect(sb, dest, new Color(50, 45, 45));
            _p.Line(sb, pos, pos + size, new Color(120, 40, 30), 4f);
            _p.Line(sb, pos + new Vector2(size.X, 0), pos + new Vector2(0, size.Y), new Color(120, 40, 30), 4f);
            _p.RectOutline(sb, pos, size, player * 0.6f, 3f);
            DrawCenteredText(sb, "RUIN", pos + size / 2, Color.White, zoom);
        }
        else
        {
            float a = b.NeedsConstruction ? 0.45f : 1f;
            if (!_generatedBuildings) _p.Rect(sb, dest, player * 0.18f);
            bool drawn = DrawBuildingImage(sb, b.Type, b.Owner, b.Size, dest, Color.White * a);
            if (drawn)
            {
                if (!_generatedBuildings) _p.RectOutline(sb, pos, size, player * 0.85f, 2f);
            }
            else
            {
                _p.Rect(sb, dest, Palette.Building(b.Type) * a);
                _p.RectOutline(sb, pos, size, player, 3f);
            }
            if (!drawn) DrawCenteredText(sb, Palette.ShortName(b.Type), pos + size / 2, Color.White * (0.5f + a / 2), zoom);
            DrawBuildingLife(sb, state, b, pos, size, zoom);

            if (b.NeedsConstruction)
            {
                var progress = b.Progress.ToFloat();
                int req = 0, del = 0;
                for (int i = 0; i < Resources.Count; i++)
                {
                    req += b.Required[i];
                    del += b.Delivered[i];
                }
                float materials = req == 0 ? 1f : del / (float)req;
                _p.Rect(sb, pos + new Vector2(4, size.Y - 18), new Vector2(size.X - 8, 5), Color.Black * 0.6f);
                _p.Rect(sb, pos + new Vector2(4, size.Y - 18), new Vector2((size.X - 8) * materials, 5), new Color(200, 160, 80));
                _p.Rect(sb, pos + new Vector2(4, size.Y - 10), new Vector2(size.X - 8, 5), Color.Black * 0.6f);
                _p.Rect(sb, pos + new Vector2(4, size.Y - 10), new Vector2((size.X - 8) * progress, 5), new Color(90, 200, 90));
                if (b.IsRepair) DrawCenteredText(sb, "REPAIR", pos + new Vector2(size.X / 2, 12), Color.Orange, zoom);
            }
            if (b.Demolishing)
            {
                _p.Rect(sb, dest, Color.Black * 0.35f);
                _p.Rect(sb, pos + new Vector2(4, size.Y - 10), new Vector2(size.X - 8, 5), Color.Black * 0.6f);
                _p.Rect(sb, pos + new Vector2(4, size.Y - 10), new Vector2((size.X - 8) * b.Progress.ToFloat(), 5), Color.OrangeRed);
                DrawCenteredText(sb, "DEMOLISHING", pos + new Vector2(size.X / 2, 12), Color.OrangeRed, zoom);
            }
            if (b.State == BuildingState.Upgrading)
                DrawCenteredText(sb, "UPGRADING", pos + new Vector2(size.X / 2, 12), Color.Yellow, zoom);

            if (b.Hp < b.MaxHp && !b.NeedsConstruction)
            {
                float hp = b.Hp.ToFloat() / Math.Max(1f, b.MaxHp.ToFloat());
                _p.Rect(sb, pos + new Vector2(0, -8), new Vector2(size.X, 5), Color.Black * 0.7f);
                _p.Rect(sb, pos + new Vector2(0, -8), new Vector2(size.X * hp, 5), hp > 0.5f ? Color.LimeGreen : hp > 0.25f ? Color.Orange : Color.Red);
            }

            DrawBuildingFire(sb, state, b, pos, size);
            if (b.IsBurning)
            {
                var secondsLeft = Math.Max(0, (b.BurningDeadlineTick - state.Tick) / state.Config.Simulation.TicksPerSecond);
                DrawCenteredText(sb, $"BURNING {secondsLeft}s", pos + new Vector2(size.X / 2, size.Y + 10), Color.OrangeRed, zoom);
            }

            if (b.Fort != null && b.State != BuildingState.UnderConstruction)
            {
                int cap = FortSystem.CapacityRegiments(state, b);
                var label = b.Type == BuildingType.Stronghold
                    ? $"G{b.Fort.GarrisonAlive} R{b.Fort.StationedRegimentIds.Count}/{cap}"
                    : $"R{b.Fort.StationedRegimentIds.Count}/{cap}";
                DrawCenteredText(sb, label, pos + new Vector2(size.X / 2, size.Y - 10), Color.White, zoom);
            }

            if (b.Store?.Starved == true)
                DrawCenteredText(sb, "NEEDS CARRIERS", pos + new Vector2(size.X / 2, -16), Color.Yellow, zoom);

            var def = state.Config.Building(b.Type);
            if (b.IsActive && def.Outputs.Count > 0)
            {
                // Pile waiting at the building: "6/9" until a full batch is carried off.
                var (resource, _) = def.Outputs.First();
                int pile = b.OutputStock[(int)resource];
                var label = $"{pile}/{def.BatchSize}";
                var badgePos = pos + new Vector2(4, 4);
                _p.Rect(sb, badgePos, new Vector2(10, 10), Palette.Resource(resource));
                if (zoom >= 0.3f)
                {
                    var scale = Math.Clamp(1f / zoom, 1f, 2.2f);
                    sb.DrawString(_small, label, badgePos + new Vector2(13, -2), Color.Black * 0.7f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                    sb.DrawString(_small, label, badgePos + new Vector2(12, -3), Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                }
            }

            if (state.MissingWorkers(b) > 0)
            {
                // "!" badge: this building is waiting for a Worker or Carrier.
                float scale = Math.Clamp(1f / zoom, 1f, 2.5f);
                var center = pos + new Vector2(size.X - 2, 2);
                _p.Disc(sb, center, 9f * scale, Color.Black * 0.8f);
                _p.Disc(sb, center, 7.5f * scale, Color.Gold);
                var mark = _font.MeasureString("!") * scale;
                sb.DrawString(_font, "!", center - mark / 2 + new Vector2(0, scale), Color.Black, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }

        if (selected) _p.RectOutline(sb, pos - new Vector2(4), size + new Vector2(8), Color.White, 2f);
    }

    private void DrawCenteredText(SpriteBatch sb, string text, Vector2 center, Color color, float zoom)
    {
        if (zoom < 0.3f) return;
        var scale = Math.Clamp(1f / zoom, 1f, 2.2f);
        var size = _small.MeasureString(text) * scale;
        var pos = center - size / 2;
        sb.DrawString(_small, text, pos + new Vector2(1, 1), Color.Black * 0.7f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        sb.DrawString(_small, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    /// <summary>Spears, lances and shields that show what a Formation is doing.</summary>
    private void DrawSoldierGear(SpriteBatch sb, GameState state, Soldier s, Regiment regiment, Vector2 pos, float r)
    {
        var facing = new Vector2(regiment.Facing.X.ToFloat(), regiment.Facing.Y.ToFloat());
        if (facing.LengthSquared() < 0.01f) facing = new Vector2(0, -1);
        facing.Normalize();
        var right = new Vector2(-facing.Y, facing.X);
        bool braced = regiment.AnchorArrived || regiment.EngagedRegimentId != 0;
        var shaft = new Color(120, 85, 45);
        var steel = new Color(200, 205, 215);
        var shield = Color.Lerp(Palette.Player(s.Owner), Color.White, 0.25f);

        switch (s.Type)
        {
            case SoldierType.Spearmen or SoldierType.Pikemen:
            {
                float reach = s.Type == SoldierType.Pikemen ? 20f : 13f;
                if (braced && regiment.Formation == FormationType.Line)
                {
                    // Spears levelled at the enemy.
                    var tip = pos + facing * (r + reach);
                    _p.Line(sb, pos - facing * (r * 0.6f), tip, shaft, 2f);
                    _p.Line(sb, tip - facing * 4f, tip, steel, 2.5f);
                }
                else
                {
                    // Carried upright on the march.
                    var top = pos + right * (r * 0.7f) + new Vector2(0, -r - reach + 2f);
                    _p.Line(sb, pos + right * (r * 0.7f) + new Vector2(0, r * 0.5f), top, shaft, 2f);
                    _p.Line(sb, top, top + new Vector2(0, 4f), steel, 2.5f);
                }
                break;
            }
            case SoldierType.Knights when regiment.Formation == FormationType.Wedge || !regiment.AnchorArrived:
            {
                var tip = pos + facing * (r + 12f);
                _p.Line(sb, pos - facing * (r * 0.4f) + right * (r * 0.4f), tip + right * (r * 0.4f), shaft, 2f);
                _p.Line(sb, tip + right * (r * 0.4f) - facing * 3f, tip + right * (r * 0.4f), steel, 2.5f);
                break;
            }
        }

        if (state.Config.Soldier(s.Type).Shield && ShieldedFrom(state, s) is { } incoming)
        {
            // Arrows on the way: the shield comes up between the Soldier and the archers.
            var toward = incoming - pos;
            if (toward.LengthSquared() > 0.01f) toward.Normalize();
            else toward = facing;
            var across = new Vector2(-toward.Y, toward.X);
            var center = pos + toward * (r * 1.05f);
            var half = across * (r * 1.1f);
            _p.Line(sb, center - half, center + half, Color.Black * 0.6f, 7f);
            _p.Line(sb, center - half * 0.92f, center + half * 0.92f, shield, 5f);
            _p.Disc(sb, center, 1.8f, steel);
        }
        else if (s.Type == SoldierType.MenAtArms)
        {
            // Shield slung at the side.
            var center = pos - right * (r * 0.85f);
            _p.Line(sb, center - facing * (r * 0.45f), center + facing * (r * 0.45f), shield, 4f);
        }
    }

    /// <summary>Where the arrows are coming from, if any are about to land near this Soldier.</summary>
    private Vector2? ShieldedFrom(GameState state, Soldier s)
    {
        int tps = state.Config.Simulation.TicksPerSecond;
        foreach (var p in state.Projectiles)
        {
            if (p.Owner == s.Owner || p.ImpactTick - state.Tick > tps) continue;
            if (FixVec2.DistanceSquared(p.Target, s.Position) > Fix.FromInt(6)) continue;
            return p.Origin.ToPixels();
        }
        return null;
    }

    private void DrawBanner(SpriteBatch sb, GameState state, Regiment r, bool selected, float zoom)
    {
        var centroid = state.RegimentCentroid(r).ToPixels();
        var anchor = r.Anchor.ToPixels();
        var player = Palette.Player(r.Owner);
        float scale = Math.Clamp(1f / zoom, 1f, 3f);

        var poleBase = centroid + new Vector2(0, -14 * scale);
        _p.Line(sb, poleBase, poleBase + new Vector2(0, -16 * scale), Color.SaddleBrown, 2f * scale);
        var flag = poleBase + new Vector2(0, -16 * scale);
        _p.Rect(sb, flag, new Vector2(18, 11) * scale, player);
        sb.DrawString(_small, Palette.Letter(r.Type), flag + new Vector2(5, -1) * scale, Color.White, 0f, Vector2.Zero, scale * 0.9f, SpriteEffects.None, 0f);

        float cohesion = r.Cohesion.ToFloat() / Math.Max(1f, state.Config.Combat.CohesionMax.ToFloat());
        bool broken = RegimentSystem.IsBroken(state, r);
        _p.Rect(sb, flag + new Vector2(0, 12 * scale), new Vector2(18 * scale, 3 * scale), Color.Black * 0.7f);
        _p.Rect(sb, flag + new Vector2(0, 12 * scale), new Vector2(18 * scale * cohesion, 3 * scale), broken ? Color.Red : Color.Gold);
        if (r.HoldGround)
        {
            // Small shield badge beside the flag: this Regiment holds its ground.
            var badge = flag + new Vector2(20, 1) * scale;
            _p.Rect(sb, badge, new Vector2(8, 9) * scale, Color.Black * 0.7f);
            _p.Rect(sb, badge + new Vector2(1, 1) * scale, new Vector2(6, 7) * scale, Color.Gold);
        }

        if (!selected) return;
        var facing = new Vector2(r.Facing.X.ToFloat(), r.Facing.Y.ToFloat());
        _p.Line(sb, anchor, anchor + facing * 40, Color.White * 0.8f, 2f * scale);
        if (!r.AnchorArrived)
        {
            var dest = r.Destination.ToPixels();
            _p.Line(sb, centroid, dest, Color.White * 0.25f, 1.5f * scale);
            _p.Circle_(sb, dest, 10 * scale, Color.White * 0.7f);
        }
        if (r.EngagedRegimentId != 0 && state.GetRegiment(r.EngagedRegimentId) is { } enemy)
            _p.Line(sb, centroid, state.RegimentCentroid(enemy).ToPixels(), Color.Red * 0.5f, 1.5f * scale);
    }

    private void DrawProjectile(SpriteBatch sb, GameState state, Projectile proj, float alpha)
    {
        if (!_sprites.Has("projectile.arrow"))
        {
            DrawArrow(sb, state, proj, alpha);
            return;
        }
        float duration = Math.Max(1, proj.ImpactTick - proj.LaunchTick);
        float t = Math.Clamp((state.Tick + alpha - proj.LaunchTick) / duration, 0f, 1f);
        var a = proj.Origin.ToPixels();
        var b = proj.Target.ToPixels();
        var ground = Vector2.Lerp(a, b, t);
        float dist = Vector2.Distance(a, b);
        float height = 4 * t * (1 - t) * dist * 0.18f;
        var pos = ground - new Vector2(0, height);
        var sprite = _sprites.Get("projectile.arrow");
        float tNext = Math.Min(1f, t + 0.05f);
        var next = Vector2.Lerp(a, b, tNext) - new Vector2(0, 4 * tNext * (1 - tNext) * dist * 0.18f);
        var dir = next - pos;
        if (dir.LengthSquared() < 0.01f) dir = b - a;
        dir.Normalize();
        if (sprite.HasValue)
        {
            var src = sprite.Value.Source;
            sb.Draw(sprite.Value.Texture, pos, src, Color.White, MathF.Atan2(dir.Y, dir.X), new Vector2(src.Width / 2f, src.Height / 2f), 14f / src.Width, SpriteEffects.None, 0f);
            return;
        }
        float len = proj.IsLongbow ? 14f : 10f;
        _p.Line(sb, pos - dir * len / 2, pos + dir * len / 2, proj.IsLongbow ? new Color(60, 40, 20) : new Color(110, 80, 40), 2f);
        _p.Rect(sb, pos + dir * len / 2 - new Vector2(1), new Vector2(3), Color.LightGray);
    }
}

/// <summary>What the renderer needs to know about the player's selection.</summary>
public sealed class SelectionView
{
    public HashSet<int> RegimentIds { get; } = new();
    public int BuildingId { get; set; }
}
