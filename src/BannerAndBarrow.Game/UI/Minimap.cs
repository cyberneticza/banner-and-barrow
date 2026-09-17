using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using BannerAndBarrow.Game.Rendering;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Game.UI;

/// <summary>Whole-map overview: terrain, territory, buildings, every unit and the camera frame. Click to look, right-click to move.</summary>
public sealed class Minimap
{
    public Rectangle Rect;

    public void Update(InputState input, Camera2D camera, GameState state, SelectionController selection)
    {
        if (!Rect.Contains(input.MousePoint)) return;
        var tiles = ScreenToTiles(input.MousePosition, state);
        if (input.LeftDown)
        {
            camera.Position = new Vector2(tiles.X * FixExtensions.TileSize, tiles.Y * FixExtensions.TileSize);
            camera.Clamp();
        }
        if (input.RightPressed)
        {
            selection.MoveSelectedTo(new FixVec2(
                Fix.FromRaw((long)(tiles.X * Fix.OneRaw)),
                Fix.FromRaw((long)(tiles.Y * Fix.OneRaw))));
        }
    }

    private Vector2 ScreenToTiles(Vector2 screen, GameState state) => new(
        (screen.X - Rect.X) / Rect.Width * state.Map.Width,
        (screen.Y - Rect.Y) / Rect.Height * state.Map.Height);

    public void Draw(Ui ui, GameState state, WorldRenderer world, Camera2D camera)
    {
        var sb = ui.Batch;
        var p = ui.Prims;
        ui.Panel(new Rectangle(Rect.X - 3, Rect.Y - 3, Rect.Width + 6, Rect.Height + 6));
        world.RefreshCaches(state);
        if (world.TerrainTexture != null) sb.Draw(world.TerrainTexture, Rect, Color.White);
        if (world.TerritoryTexture != null) sb.Draw(world.TerritoryTexture, Rect, Color.White * 2f);

        float sx = Rect.Width / (float)state.Map.Width, sy = Rect.Height / (float)state.Map.Height;
        Vector2 ToMini(FixVec2 v) => new(Rect.X + v.X.ToFloat() * sx, Rect.Y + v.Y.ToFloat() * sy);

        foreach (var b in state.Buildings.Values)
        {
            if (!world.Sees(state, b)) continue;
            var color = b.State == BuildingState.Ruin ? Color.DarkGray : Palette.Player(b.Owner);
            p.Rect(sb, new Vector2(Rect.X + b.Origin.X * sx, Rect.Y + b.Origin.Y * sy), new Vector2(Math.Max(2, b.Size * sx), Math.Max(2, b.Size * sy)), color);
        }
        if (world.Viewer >= 0)
            foreach (var ghost in state.Players[world.Viewer].KnownBuildings.Values)
            {
                if (state.GetBuilding(ghost.Id) is { } live && world.Sees(state, live)) continue;
                p.Rect(sb, new Vector2(Rect.X + ghost.Origin.X * sx, Rect.Y + ghost.Origin.Y * sy),
                    new Vector2(Math.Max(2, ghost.Size * sx), Math.Max(2, ghost.Size * sy)), Palette.Player(ghost.Owner) * 0.5f);
            }
        foreach (var w in state.Workers.Values)
            if (w.Owner == world.Viewer || world.Sees(state, w.Position))
                p.Rect(sb, ToMini(w.Position), new Vector2(1), Color.Lerp(Palette.Player(w.Owner), Color.White, 0.5f));
        foreach (var s in state.Soldiers.Values)
        {
            if (s.Stationed || (s.Owner != world.Viewer && !world.Sees(state, s.Position) && !state.IsRevealed(world.Viewer, s.RegimentId))) continue;
            p.Rect(sb, ToMini(s.Position) - new Vector2(1), new Vector2(2), Palette.Player(s.Owner));
        }

        if (world.Viewer >= 0 && world.FogTexture != null) sb.Draw(world.FogTexture, Rect, Color.White * 1.4f);

        var vis = camera.VisibleWorld;
        float t = FixExtensions.TileSize;
        var tl = new Vector2(Rect.X + vis.X / t * sx, Rect.Y + vis.Y / t * sy);
        var size = new Vector2(vis.Width / t * sx, vis.Height / t * sy);
        var clampedTl = Vector2.Max(tl, new Vector2(Rect.X, Rect.Y));
        var clampedBr = Vector2.Min(tl + size, new Vector2(Rect.Right, Rect.Bottom));
        p.RectOutline(sb, clampedTl, clampedBr - clampedTl, Color.White, 1f);
    }
}
