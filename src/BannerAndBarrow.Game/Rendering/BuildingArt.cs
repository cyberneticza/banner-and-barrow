using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using BannerAndBarrow.Simulation.Data;

namespace BannerAndBarrow.Game.Rendering;

/// <summary>
/// Paints building images in code at startup: one texture per building type and Player (team colours on roofs,
/// banners and shields). Everything is laid out in tile units on the footprint, drawn as a 3/4 top-down view with
/// shaded roofs, lit windows, props that say what the building does, and a dark outline. Chimneys sit where
/// <see cref="WorldRenderer"/> puts the smoke (72% across, 18% down).
/// </summary>
public sealed class BuildingArt
{
    public const int PixelsPerTile = 48;
    private readonly Dictionary<(BuildingType, int), Texture2D> _textures = new();
    private readonly GraphicsDevice _device;

    public BuildingArt(GraphicsDevice device) => _device = device;

    public Texture2D Get(BuildingType type, int owner, int size)
    {
        if (_textures.TryGetValue((type, owner), out var tex)) return tex;
        var canvas = new Canvas(size, PixelsPerTile, seed: (int)type * 31 + owner);
        Paint(canvas, type, Palette.Player(owner));
        canvas.Outline(new Color(28, 20, 16));
        tex = new Texture2D(_device, canvas.Width, canvas.Height);
        tex.SetData(canvas.Pixels);
        _textures[(type, owner)] = tex;
        return tex;
    }

    // ------------------------------------------------------------------ palette

    private static readonly Color Timber = new(120, 72, 38);
    private static readonly Color TimberDark = new(84, 50, 26);
    private static readonly Color Plaster = new(247, 232, 196);
    private static readonly Color StoneLight = new(196, 196, 206);
    private static readonly Color StoneDark = new(140, 140, 156);
    private static readonly Color Thatch = new(236, 186, 72);
    private static readonly Color WindowGlow = new(255, 214, 96);
    private static readonly Color Door = new(96, 56, 28);
    private static readonly Color Dirt = new(200, 162, 110);
    private static readonly Color Cobble = new(176, 168, 150);
    private static readonly Color Gold = new(255, 204, 40);
    private static readonly Color BarnRed = new(206, 52, 42);
    private static readonly Color Leaf = new(88, 170, 70);

    private static Color Team(Color player) => Vivid(player, 1.15f);

    private static Color Vivid(Color c, float amount)
    {
        float avg = (c.R + c.G + c.B) / 3f;
        byte Push(byte v) => (byte)Math.Clamp(avg + (v - avg) * amount, 0, 255);
        return new Color(Push(c.R), Push(c.G), Push(c.B), c.A);
    }

    private static Color Shade(Color c, float f) =>
        f >= 1 ? Color.Lerp(c, Color.White, Math.Min(1f, f - 1f)) : Color.Lerp(Color.Black, c, f);

    // ------------------------------------------------------------------ building recipes

    private static void Paint(Canvas c, BuildingType type, Color player)
    {
        var team = Team(player);
        float s = c.Size;
        switch (type)
        {
            case BuildingType.Keep:
            {
                Pad(c, Cobble, 0.03f);
                // Curtain wall and gatehouse flanked by four towers with team-coloured roofs.
                c.Rect(0.45f, 1.4f, s - 0.9f, s - 1.75f, StoneLight);
                Stones(c, 0.45f, 1.4f, s - 0.9f, s - 1.75f);
                Crenels(c, 0.45f, 1.25f, s - 0.9f, StoneLight);
                c.Rect(1.1f, 0.75f, s - 2.2f, 1.6f, StoneDark);
                Stones(c, 1.1f, 0.75f, s - 2.2f, 1.6f);
                Crenels(c, 1.1f, 0.6f, s - 2.2f, StoneDark);
                Gable(c, 1.2f, 0.15f, s - 2.4f, 0.55f, team);
                Tower(c, 0.3f, 1.0f, 0.95f, 2.6f, team);
                Tower(c, s - 1.25f, 1.0f, 0.95f, 2.6f, team);
                c.Rect(s / 2 - 0.5f, s - 1.25f, 1.0f, 0.95f, Door);
                c.Ellipse(s / 2, s - 1.25f, 0.5f, 0.35f, Door);
                c.Rect(s / 2 - 0.42f, s - 1.1f, 0.84f, 0.1f, TimberDark);
                c.Rect(s / 2 - 0.42f, s - 0.8f, 0.84f, 0.1f, TimberDark);
                Banner(c, 1.55f, 1.55f, team);
                Banner(c, s - 1.75f, 1.55f, team);
                Window(c, 1.6f, 2.35f);
                Window(c, s - 1.9f, 2.35f);
                break;
            }
            case BuildingType.Storehouse:
            {
                Pad(c, Dirt, 0.04f);
                Hall(c, 0.25f, 0.3f, s - 0.5f, s - 0.75f, Timber, new Color(150, 96, 52), planks: true);
                // Big double doors and goods outside.
                c.Rect(s / 2 - 0.45f, s - 1.05f, 0.9f, 0.62f, Door);
                c.Line(s / 2, s - 1.05f, s / 2, s - 0.43f, 0.05f, TimberDark);
                c.Line(s / 2 - 0.45f, s - 1.05f, s / 2 + 0.45f, s - 0.43f, 0.06f, TimberDark);
                Crate(c, 0.2f, s - 0.62f, 0.42f);
                Crate(c, 0.55f, s - 0.55f, 0.36f);
                Barrel(c, s - 0.45f, s - 0.5f, 0.2f);
                Barrel(c, s - 0.85f, s - 0.45f, 0.18f);
                Banner(c, 0.45f, 0.85f, team);
                break;
            }
            case BuildingType.House:
            {
                Pad(c, new Color(120, 190, 90), 0.05f);
                Cottage(c, 0.2f, 0.25f, s - 0.4f, s - 0.45f, Plaster, team, timberFrame: true);
                Chimney(c, s);
                Flowers(c, 0.18f, s - 0.28f);
                Flowers(c, s - 0.45f, s - 0.3f);
                break;
            }
            case BuildingType.Woodcutter:
            {
                Pad(c, new Color(150, 190, 90), 0.04f);
                Cottage(c, 0.15f, 0.2f, s - 0.55f, s - 0.6f, new Color(160, 104, 56), new Color(80, 140, 60), timberFrame: false, logs: true);
                LogPile(c, s - 0.95f, s - 0.55f);
                // Chopping block with an axe.
                c.Ellipse(s - 0.35f, s - 0.95f, 0.2f, 0.12f, new Color(200, 160, 110));
                c.Rect(s - 0.55f, s - 0.95f, 0.4f, 0.2f, new Color(150, 100, 55));
                c.Line(s - 0.35f, s - 1.05f, s - 0.15f, s - 1.35f, 0.05f, Timber);
                c.Poly(new Color(210, 215, 225), (s - 0.2f, s - 1.42f), (s - 0.05f, s - 1.35f), (s - 0.13f, s - 1.25f));
                break;
            }
            case BuildingType.Quarry:
            {
                Pad(c, new Color(170, 160, 140), 0.02f);
                // A cut pit with stepped stone faces, blocks and a lifting frame.
                c.Poly(new Color(120, 115, 110), (0.2f, 0.4f), (s - 0.25f, 0.3f), (s - 0.15f, 1.1f), (0.3f, 1.2f));
                c.Rect(0.35f, 0.55f, s - 0.7f, 0.2f, StoneLight);
                c.Rect(0.4f, 0.8f, s - 0.85f, 0.2f, StoneDark);
                Block(c, 0.2f, s - 0.7f, 0.45f);
                Block(c, 0.7f, s - 0.55f, 0.35f);
                Block(c, 0.45f, s - 1.0f, 0.3f);
                c.Line(s - 0.55f, s - 0.2f, s - 0.55f, 1.0f, 0.07f, Timber);
                c.Line(s - 0.55f, 1.0f, s - 0.15f, 1.3f, 0.06f, Timber);
                c.Line(s - 0.18f, 1.3f, s - 0.18f, 1.6f, 0.02f, new Color(60, 60, 60));
                Block(c, s - 0.33f, 1.6f, 0.26f);
                break;
            }
            case BuildingType.IronMine:
            {
                Pad(c, new Color(150, 130, 110), 0.02f);
                // Rocky hill with a timber-framed shaft and an ore cart on rails.
                c.Poly(new Color(128, 112, 100), (0.05f, s - 0.55f), (0.45f, 0.35f), (1.05f, 0.15f), (s - 0.35f, 0.45f), (s - 0.05f, s - 0.55f));
                c.Poly(new Color(158, 140, 124), (0.3f, s - 0.7f), (0.55f, 0.55f), (1.0f, 0.38f), (1.1f, s - 0.7f));
                c.Rect(s / 2 - 0.4f, 0.75f, 0.8f, 0.75f, new Color(30, 22, 18));
                c.Rect(s / 2 - 0.5f, 0.65f, 0.12f, 0.9f, Timber);
                c.Rect(s / 2 + 0.38f, 0.65f, 0.12f, 0.9f, Timber);
                c.Rect(s / 2 - 0.55f, 0.58f, 1.1f, 0.14f, TimberDark);
                c.Line(s / 2 - 0.2f, 1.5f, s / 2 - 0.35f, s - 0.1f, 0.04f, new Color(90, 90, 100));
                c.Line(s / 2 + 0.2f, 1.5f, s / 2 + 0.35f, s - 0.1f, 0.04f, new Color(90, 90, 100));
                c.Rect(s / 2 - 0.35f, s - 0.6f, 0.7f, 0.32f, new Color(110, 80, 50));
                c.Ellipse(s / 2 - 0.1f, s - 0.62f, 0.14f, 0.09f, new Color(170, 90, 60));
                c.Ellipse(s / 2 + 0.15f, s - 0.64f, 0.12f, 0.08f, new Color(80, 70, 80));
                Banner(c, 0.3f, s - 0.95f, team);
                break;
            }
            case BuildingType.Farm:
            {
                Pad(c, new Color(214, 184, 108), 0.03f);
                // Red barn with white trim, a hay loft and a team weathervane pennant.
                Hall(c, 0.2f, 0.35f, s - 1.1f, s - 0.85f, BarnRed, new Color(120, 60, 40), planks: true);
                c.Poly(Color.White, (s / 2 - 0.85f, 1.25f), (s / 2 - 0.55f, 0.95f), (s / 2 - 0.25f, 1.25f), (s / 2 - 0.25f, 1.33f), (s / 2 - 0.55f, 1.03f), (s / 2 - 0.85f, 1.33f));
                c.Rect(s / 2 - 0.85f, s - 1.1f, 0.6f, 0.6f, Door);
                c.Line(s / 2 - 0.85f, s - 1.1f, s / 2 - 0.25f, s - 0.5f, 0.06f, Color.White);
                c.Line(s / 2 - 0.25f, s - 1.1f, s / 2 - 0.85f, s - 0.5f, 0.06f, Color.White);
                // Silo.
                c.Rect(s - 0.85f, 0.75f, 0.65f, s - 1.25f, new Color(210, 205, 190));
                c.Ellipse(s - 0.525f, 0.75f, 0.325f, 0.28f, team);
                c.Ellipse(s - 0.6f, 0.68f, 0.12f, 0.08f, Shade(team, 1.35f));
                for (float y = 1.0f; y < s - 0.6f; y += 0.28f) c.Rect(s - 0.85f, y, 0.65f, 0.04f, new Color(170, 165, 150));
                HayBale(c, 0.35f, s - 0.4f);
                HayBale(c, 0.85f, s - 0.32f);
                break;
            }
            case BuildingType.Smithy:
            {
                Pad(c, Cobble, 0.04f);
                Cottage(c, 0.15f, 0.25f, s - 0.3f, s - 0.55f, StoneLight, new Color(70, 70, 82), timberFrame: false, stone: true);
                Chimney(c, s, tall: true);
                // Glowing forge mouth and an anvil outside.
                c.Rect(0.4f, s - 0.85f, 0.45f, 0.4f, new Color(40, 25, 20));
                c.Ellipse(0.625f, s - 0.55f, 0.18f, 0.1f, new Color(255, 120, 30));
                c.Ellipse(0.625f, s - 0.58f, 0.1f, 0.05f, new Color(255, 230, 120));
                c.Poly(new Color(70, 72, 80), (s - 0.8f, s - 0.45f), (s - 0.2f, s - 0.45f), (s - 0.3f, s - 0.32f), (s - 0.45f, s - 0.32f), (s - 0.45f, s - 0.12f), (s - 0.6f, s - 0.12f), (s - 0.6f, s - 0.32f), (s - 0.72f, s - 0.32f));
                break;
            }
            case BuildingType.Fletcher:
            {
                Pad(c, new Color(150, 190, 90), 0.04f);
                Cottage(c, 0.15f, 0.25f, s - 0.3f, s - 0.6f, new Color(236, 214, 160), new Color(60, 140, 70), timberFrame: true);
                Chimney(c, s);
                // Rack of bows and a bundle of arrows.
                c.Rect(0.15f, s - 0.55f, s - 0.3f, 0.06f, Timber);
                for (int i = 0; i < 3; i++)
                {
                    float x = 0.35f + i * 0.45f;
                    c.Arc(x, s - 0.32f, 0.22f, 0.05f, new Color(150, 90, 40));
                    c.Line(x - 0.02f, s - 0.54f, x - 0.02f, s - 0.1f, 0.012f, Plaster);
                }
                for (int i = 0; i < 4; i++)
                    c.Line(s - 0.3f + i * 0.04f, s - 0.1f, s - 0.4f + i * 0.08f, s - 0.55f, 0.02f, new Color(180, 130, 70));
                c.Poly(Color.White, (s - 0.45f, s - 0.58f), (s - 0.1f, s - 0.58f), (s - 0.2f, s - 0.48f), (s - 0.38f, s - 0.48f));
                break;
            }
            case BuildingType.ShieldMaker:
            {
                Pad(c, new Color(150, 190, 90), 0.04f);
                Cottage(c, 0.15f, 0.25f, s - 0.3f, s - 0.6f, Plaster, new Color(150, 60, 50), timberFrame: true);
                Chimney(c, s);
                // Painted shields hanging out front in team colours.
                Shield(c, 0.4f, s - 0.3f, 0.2f, team);
                Shield(c, s / 2, s - 0.28f, 0.2f, Gold);
                Shield(c, s - 0.4f, s - 0.3f, 0.2f, team);
                break;
            }
            case BuildingType.TaxOffice:
            {
                Pad(c, Cobble, 0.04f);
                Cottage(c, 0.15f, 0.25f, s - 0.3f, s - 0.55f, new Color(236, 226, 206), Gold, timberFrame: false, stone: true);
                // Columns and a coin sign.
                c.Rect(0.35f, s - 0.95f, 0.1f, 0.6f, Color.White);
                c.Rect(s - 0.45f, s - 0.95f, 0.1f, 0.6f, Color.White);
                c.Ellipse(s / 2, 0.95f, 0.22f, 0.22f, Gold);
                c.Ellipse(s / 2, 0.95f, 0.14f, 0.14f, new Color(230, 170, 20));
                c.Rect(s / 2 - 0.02f, 0.85f, 0.04f, 0.2f, new Color(255, 240, 150));
                for (int i = 0; i < 3; i++) c.Ellipse(0.25f + i * 0.12f, s - 0.18f - i * 0.04f, 0.1f, 0.05f, Gold);
                break;
            }
            case BuildingType.Barracks:
            {
                Pad(c, Dirt, 0.03f);
                Hall(c, 0.2f, 0.3f, s - 0.4f, s - 1.0f, StoneLight, team, planks: false, stone: true);
                c.Rect(s / 2 - 0.35f, s - 1.3f, 0.7f, 0.6f, Door);
                Banner(c, 0.55f, 1.2f, team);
                Banner(c, s - 0.75f, 1.2f, team);
                // Weapon rack and a straw training dummy.
                c.Rect(0.25f, s - 0.45f, 0.9f, 0.06f, Timber);
                for (int i = 0; i < 4; i++) c.Line(0.35f + i * 0.22f, s - 0.1f, 0.35f + i * 0.22f, s - 0.75f, 0.035f, i % 2 == 0 ? Timber : new Color(200, 205, 215));
                c.Line(s - 0.55f, s - 0.1f, s - 0.55f, s - 0.7f, 0.05f, Timber);
                c.Line(s - 0.8f, s - 0.55f, s - 0.3f, s - 0.55f, 0.05f, Timber);
                c.Ellipse(s - 0.55f, s - 0.45f, 0.14f, 0.2f, Thatch);
                c.Ellipse(s - 0.55f, s - 0.75f, 0.1f, 0.1f, Thatch);
                break;
            }
            case BuildingType.Archery:
            {
                Pad(c, new Color(140, 200, 100), 0.03f);
                Hall(c, 0.2f, 0.25f, s - 0.4f, s - 1.25f, new Color(170, 118, 70), new Color(60, 140, 70), planks: true);
                c.Rect(s / 2 - 0.3f, s - 1.55f, 0.6f, 0.55f, Door);
                Banner(c, s - 0.7f, 0.8f, team);
                // Two butts with painted targets.
                Target(c, 0.65f, s - 0.45f, 0.32f);
                Target(c, s - 0.65f, s - 0.45f, 0.32f);
                break;
            }
            case BuildingType.Stable:
            {
                Pad(c, new Color(214, 184, 108), 0.03f);
                Hall(c, 0.2f, 0.25f, s - 0.4f, s - 1.2f, new Color(150, 98, 55), team, planks: true);
                for (int i = 0; i < 3; i++)
                {
                    float x = 0.45f + i * 0.75f;
                    c.Rect(x, s - 1.5f, 0.5f, 0.5f, new Color(50, 32, 20));
                    c.Rect(x, s - 1.3f, 0.5f, 0.06f, TimberDark);
                    // A horse's head looking out.
                    c.Ellipse(x + 0.25f, s - 1.28f, 0.11f, 0.14f, i == 1 ? new Color(240, 235, 225) : new Color(120, 70, 35));
                }
                Fence(c, 0.1f, s - 0.3f, s - 0.2f);
                HayBale(c, s - 0.5f, s - 0.6f);
                break;
            }
            case BuildingType.Wall:
            {
                // A block of masonry with a walkway and merlons on top.
                c.Rect(0, 0.12f, s, s - 0.12f, StoneDark);
                c.Rect(0, 0.3f, s, s - 0.3f, StoneLight);
                Stones(c, 0, 0.3f, s, s - 0.3f);
                c.Rect(0, 0.12f, s, 0.2f, Shade(StoneLight, 1.15f));
                for (float x = 0; x < s - 0.05f; x += 0.34f) c.Rect(x, 0, 0.22f, 0.16f, StoneLight);
                c.Rect(0, s - 0.1f, s, 0.1f, Color.Black * 0.25f);
                break;
            }
            case BuildingType.Gate:
            {
                c.Rect(0, 0.12f, s, s - 0.12f, StoneDark);
                c.Rect(0, 0.3f, 0.22f, s - 0.3f, StoneLight);
                c.Rect(s - 0.22f, 0.3f, 0.22f, s - 0.3f, StoneLight);
                Stones(c, 0, 0.3f, s, s - 0.3f);
                // Timber doors under a stone arch, with the owner's colours above.
                c.Rect(0.22f, 0.34f, s - 0.44f, s - 0.44f, new Color(96, 60, 30));
                for (float x = 0.26f; x < s - 0.24f; x += 0.16f) c.Rect(x, 0.34f, 0.1f, s - 0.46f, new Color(120, 76, 38));
                c.Rect(0.22f, 0.34f, s - 0.44f, 0.08f, new Color(70, 70, 78));
                c.Ellipse(s / 2, 0.36f, (s - 0.44f) / 2, 0.16f, StoneLight);
                c.Rect(0.1f, 0.06f, s - 0.2f, 0.16f, team);
                for (float x = 0; x < s - 0.05f; x += 0.34f) c.Rect(x, 0, 0.22f, 0.12f, StoneLight);
                break;
            }
            case BuildingType.Outpost:
            {
                Pad(c, Dirt, 0.1f);
                // Wooden watchtower on stilts with a team flag.
                c.Line(0.45f, s - 0.15f, 0.65f, 0.8f, 0.09f, Timber);
                c.Line(s - 0.45f, s - 0.15f, s - 0.65f, 0.8f, 0.09f, Timber);
                c.Line(0.5f, s - 0.5f, s - 0.5f, 0.95f, 0.05f, TimberDark);
                c.Line(s - 0.5f, s - 0.5f, 0.5f, 0.95f, 0.05f, TimberDark);
                c.Rect(0.4f, 0.55f, s - 0.8f, 0.4f, new Color(160, 105, 55));
                for (float x = 0.45f; x < s - 0.45f; x += 0.18f) c.Rect(x, 0.45f, 0.08f, 0.14f, new Color(160, 105, 55));
                c.Poly(team, (0.3f, 0.5f), (s / 2, 0.1f), (s - 0.3f, 0.5f));
                c.Poly(Shade(team, 0.75f), (s / 2, 0.1f), (s - 0.3f, 0.5f), (s / 2, 0.5f));
                break;
            }
            case BuildingType.Stronghold:
            {
                Pad(c, Cobble, 0.03f);
                c.Rect(0.15f, 0.7f, s - 0.3f, s - 0.85f, StoneDark);
                Stones(c, 0.15f, 0.7f, s - 0.3f, s - 0.85f);
                Crenels(c, 0.15f, 0.56f, s - 0.3f, StoneDark);
                Tower(c, s / 2 - 0.45f, 0.15f, 0.9f, 1.45f, team);
                c.Rect(s / 2 - 0.25f, s - 0.6f, 0.5f, 0.45f, Door);
                c.Ellipse(s / 2, s - 0.6f, 0.25f, 0.18f, Door);
                c.Rect(0.35f, 1.3f, 0.12f, 0.25f, new Color(30, 25, 25));
                c.Rect(s - 0.47f, 1.3f, 0.12f, 0.25f, new Color(30, 25, 25));
                break;
            }
            default:
                Pad(c, Cobble, 0.05f);
                Cottage(c, 0.2f, 0.25f, s - 0.4f, s - 0.45f, Plaster, team, timberFrame: true);
                break;
        }
    }

    // ------------------------------------------------------------------ parts

    private static void Pad(Canvas c, Color color, float inset)
    {
        float s = c.Size;
        c.RoundRect(inset, inset + 0.12f, s - inset * 2, s - inset * 2 - 0.12f, 0.25f, Shade(color, 0.8f));
        c.RoundRect(inset, inset, s - inset * 2, s - inset * 2 - 0.1f, 0.25f, color);
        c.Speckle(inset, inset, s - inset * 2, s - inset * 2, Shade(color, 0.88f), 0.08f);
    }

    /// <summary>A small house: walls with door and windows under a two-tone gable roof.</summary>
    private static void Cottage(Canvas c, float x, float y, float w, float h, Color wall, Color roof, bool timberFrame, bool logs = false, bool stone = false)
    {
        float roofH = h * 0.52f;
        float wallTop = y + roofH * 0.8f;
        c.Rect(x + 0.08f, wallTop, w - 0.16f, y + h - wallTop, wall);
        if (logs)
            for (float ly = wallTop + 0.08f; ly < y + h; ly += 0.14f)
                c.Rect(x + 0.08f, ly, w - 0.16f, 0.035f, Shade(wall, 0.7f));
        if (stone) Stones(c, x + 0.08f, wallTop, w - 0.16f, y + h - wallTop);
        if (timberFrame)
        {
            c.Rect(x + 0.08f, wallTop, 0.06f, y + h - wallTop, Timber);
            c.Rect(x + w - 0.14f, wallTop, 0.06f, y + h - wallTop, Timber);
            c.Rect(x + 0.08f, wallTop + (y + h - wallTop) * 0.45f, w - 0.16f, 0.05f, Timber);
            c.Line(x + 0.1f, wallTop, x + w * 0.3f, wallTop + (y + h - wallTop) * 0.45f, 0.04f, Timber);
            c.Line(x + w - 0.1f, wallTop, x + w * 0.7f, wallTop + (y + h - wallTop) * 0.45f, 0.04f, Timber);
        }
        // Shadow under the eaves.
        c.Rect(x + 0.08f, wallTop, w - 0.16f, 0.1f, Color.Black * 0.25f);
        Gable(c, x, y, w, roofH, roof);
        float doorW = Math.Min(0.36f, w * 0.22f);
        c.Rect(x + w / 2 - doorW / 2, y + h - 0.5f, doorW, 0.5f, Door);
        c.Ellipse(x + w / 2, y + h - 0.5f, doorW / 2, 0.1f, Door);
        c.Ellipse(x + w / 2 + doorW * 0.25f, y + h - 0.25f, 0.025f, 0.025f, Gold);
        Window(c, x + w * 0.2f, y + h - 0.62f);
        Window(c, x + w * 0.8f - 0.24f, y + h - 0.62f);
    }

    /// <summary>A long hall: walls, a wide gable roof with a ridge and eaves.</summary>
    private static void Hall(Canvas c, float x, float y, float w, float h, Color wall, Color roof, bool planks, bool stone = false)
    {
        float roofH = h * 0.5f;
        float wallTop = y + roofH * 0.85f;
        c.Rect(x + 0.06f, wallTop, w - 0.12f, y + h - wallTop, wall);
        if (planks)
            for (float px = x + 0.18f; px < x + w - 0.1f; px += 0.2f)
                c.Rect(px, wallTop, 0.025f, y + h - wallTop, Shade(wall, 0.72f));
        if (stone) Stones(c, x + 0.06f, wallTop, w - 0.12f, y + h - wallTop);
        c.Rect(x + 0.06f, wallTop, w - 0.12f, 0.1f, Color.Black * 0.25f);
        Gable(c, x, y, w, roofH, roof);
        for (float wx = x + 0.35f; wx < x + w - 0.5f; wx += 0.8f)
            if (Math.Abs(wx + 0.12f - (x + w / 2)) > 0.5f) Window(c, wx, y + h - 0.6f);
    }

    /// <summary>A gable roof seen from the front and above: lit left slope, shaded right slope, tiles and a ridge.</summary>
    private static void Gable(Canvas c, float x, float y, float w, float h, Color roof)
    {
        var lit = Shade(roof, 1.12f);
        var dark = Shade(roof, 0.72f);
        c.Poly(Shade(roof, 0.5f), (x - 0.04f, y + h + 0.06f), (x + w + 0.04f, y + h + 0.06f), (x + w - 0.1f, y + 0.02f), (x + 0.1f, y + 0.02f));
        c.Poly(lit, (x - 0.04f, y + h), (x + w / 2, y + h), (x + w / 2, y), (x + 0.1f, y));
        c.Poly(dark, (x + w / 2, y + h), (x + w + 0.04f, y + h), (x + w - 0.1f, y), (x + w / 2, y));
        for (float ty = y + 0.14f; ty < y + h - 0.02f; ty += 0.14f)
        {
            c.Rect(x + 0.02f, ty, w / 2 - 0.02f, 0.025f, Shade(lit, 0.82f));
            c.Rect(x + w / 2, ty, w / 2 - 0.02f, 0.025f, Shade(dark, 0.8f));
        }
        c.Rect(x + w / 2 - 0.04f, y, 0.08f, h, Shade(roof, 0.55f));
        c.Rect(x - 0.04f, y + h - 0.04f, w + 0.08f, 0.06f, Shade(roof, 0.45f));
    }

    private static void Window(Canvas c, float x, float y)
    {
        c.Rect(x - 0.03f, y - 0.03f, 0.3f, 0.3f, TimberDark);
        c.Rect(x, y, 0.24f, 0.24f, WindowGlow);
        c.Rect(x, y, 0.24f, 0.08f, Shade(WindowGlow, 1.25f));
        c.Rect(x + 0.105f, y, 0.03f, 0.24f, TimberDark);
        c.Rect(x, y + 0.105f, 0.24f, 0.03f, TimberDark);
    }

    private static void Chimney(Canvas c, float s, bool tall = false)
    {
        float x = s * 0.72f - 0.1f, top = s * 0.18f - (tall ? 0.12f : 0f);
        c.Rect(x, top, 0.22f, tall ? 0.6f : 0.42f, new Color(160, 90, 70));
        c.Rect(x - 0.03f, top - 0.04f, 0.28f, 0.08f, new Color(110, 70, 60));
        for (float y = top + 0.12f; y < top + (tall ? 0.6f : 0.42f); y += 0.12f) c.Rect(x, y, 0.22f, 0.02f, new Color(120, 70, 55));
    }

    private static void Tower(Canvas c, float x, float y, float w, float h, Color roof)
    {
        float roofH = w * 0.9f;
        c.Rect(x, y + roofH * 0.6f, w, h - roofH * 0.6f, StoneLight);
        Stones(c, x, y + roofH * 0.6f, w, h - roofH * 0.6f);
        c.Rect(x + w / 2 - 0.08f, y + roofH + 0.25f, 0.16f, 0.3f, new Color(35, 30, 30));
        c.Poly(Shade(roof, 1.1f), (x - 0.08f, y + roofH), (x + w / 2, y - 0.1f), (x + w / 2, y + roofH));
        c.Poly(Shade(roof, 0.72f), (x + w / 2, y - 0.1f), (x + w + 0.08f, y + roofH), (x + w / 2, y + roofH));
        c.Line(x + w / 2, y - 0.1f, x + w / 2, y - 0.4f, 0.03f, TimberDark);
        c.Poly(roof, (x + w / 2, y - 0.4f), (x + w / 2 + 0.3f, y - 0.32f), (x + w / 2, y - 0.24f));
    }

    private static void Crenels(Canvas c, float x, float y, float w, Color color)
    {
        for (float cx = x; cx < x + w - 0.05f; cx += 0.3f) c.Rect(cx, y, 0.18f, 0.18f, color);
    }

    private static void Stones(Canvas c, float x, float y, float w, float h)
    {
        int row = 0;
        for (float sy = y + 0.2f; sy < y + h; sy += 0.2f, row++)
        {
            c.Rect(x, sy, w, 0.02f, Color.Black * 0.18f);
            for (float sx = x + (row % 2 == 0 ? 0.15f : 0.3f); sx < x + w; sx += 0.3f)
                c.Rect(sx, sy - 0.2f, 0.02f, 0.2f, Color.Black * 0.15f);
        }
    }

    private static void Banner(Canvas c, float x, float y, Color team)
    {
        c.Rect(x, y, 0.22f, 0.45f, team);
        c.Poly(team, (x, y + 0.45f), (x + 0.22f, y + 0.45f), (x + 0.11f, y + 0.58f));
        c.Rect(x + 0.14f, y, 0.08f, 0.45f, Shade(team, 0.75f));
        c.Ellipse(x + 0.11f, y + 0.22f, 0.06f, 0.06f, Gold);
        c.Rect(x - 0.04f, y - 0.03f, 0.3f, 0.05f, Gold);
    }

    private static void Crate(Canvas c, float x, float y, float size)
    {
        c.Rect(x, y, size, size * 0.8f, new Color(190, 140, 80));
        c.Rect(x, y, size, size * 0.2f, new Color(215, 170, 110));
        c.Line(x, y + size * 0.2f, x + size, y + size * 0.8f, 0.035f, new Color(130, 90, 50));
        c.Line(x + size, y + size * 0.2f, x, y + size * 0.8f, 0.035f, new Color(130, 90, 50));
    }

    private static void Barrel(Canvas c, float cx, float cy, float r)
    {
        c.Ellipse(cx, cy, r, r * 1.25f, new Color(150, 95, 50));
        c.Rect(cx - r, cy - r * 0.6f, r * 2, 0.035f, new Color(90, 90, 100));
        c.Rect(cx - r, cy + r * 0.5f, r * 2, 0.035f, new Color(90, 90, 100));
        c.Ellipse(cx, cy - r * 1.05f, r * 0.8f, r * 0.3f, new Color(185, 130, 80));
    }

    private static void LogPile(Canvas c, float x, float y)
    {
        for (int row = 0; row < 3; row++)
        for (int i = 0; i < 3 - row; i++)
        {
            float cx = x + 0.12f + i * 0.24f + row * 0.12f, cy = y + 0.3f - row * 0.2f;
            c.Ellipse(cx, cy, 0.12f, 0.11f, new Color(140, 90, 45));
            c.Ellipse(cx, cy, 0.08f, 0.075f, new Color(222, 180, 120));
            c.Ellipse(cx, cy, 0.03f, 0.03f, new Color(170, 120, 70));
        }
    }

    private static void Block(Canvas c, float x, float y, float size)
    {
        c.Rect(x, y, size, size * 0.7f, StoneDark);
        c.Rect(x, y, size, size * 0.25f, StoneLight);
    }

    private static void HayBale(Canvas c, float cx, float cy)
    {
        c.Ellipse(cx, cy, 0.24f, 0.17f, Thatch);
        c.Ellipse(cx - 0.05f, cy - 0.04f, 0.14f, 0.08f, Shade(Thatch, 1.2f));
        c.Line(cx - 0.18f, cy, cx + 0.18f, cy, 0.02f, Shade(Thatch, 0.7f));
    }

    private static void Shield(Canvas c, float cx, float cy, float r, Color color)
    {
        c.Poly(new Color(90, 60, 30), (cx - r - 0.03f, cy - r - 0.03f), (cx + r + 0.03f, cy - r - 0.03f), (cx + r + 0.03f, cy), (cx, cy + r * 1.3f), (cx - r - 0.03f, cy));
        c.Poly(color, (cx - r, cy - r), (cx + r, cy - r), (cx + r, cy), (cx, cy + r * 1.2f), (cx - r, cy));
        c.Rect(cx - 0.025f, cy - r, 0.05f, r * 2.1f, Shade(color, 1.35f));
        c.Ellipse(cx, cy - r * 0.2f, 0.05f, 0.05f, Gold);
    }

    private static void Target(Canvas c, float cx, float cy, float r)
    {
        c.Line(cx - r * 0.6f, cy + r * 1.2f, cx, cy, 0.05f, Timber);
        c.Line(cx + r * 0.6f, cy + r * 1.2f, cx, cy, 0.05f, Timber);
        c.Ellipse(cx, cy - r * 0.2f, r, r, Thatch);
        c.Ellipse(cx, cy - r * 0.2f, r * 0.8f, r * 0.8f, Color.White);
        c.Ellipse(cx, cy - r * 0.2f, r * 0.58f, r * 0.58f, new Color(220, 40, 40));
        c.Ellipse(cx, cy - r * 0.2f, r * 0.38f, r * 0.38f, Color.White);
        c.Ellipse(cx, cy - r * 0.2f, r * 0.18f, r * 0.18f, new Color(220, 40, 40));
        c.Line(cx + r * 0.1f, cy - r * 0.3f, cx + r * 0.7f, cy - r * 0.9f, 0.02f, new Color(120, 80, 40));
    }

    private static void Fence(Canvas c, float x0, float y, float x1)
    {
        c.Rect(x0, y - 0.18f, x1 - x0, 0.04f, Timber);
        c.Rect(x0, y - 0.06f, x1 - x0, 0.04f, Timber);
        for (float x = x0; x <= x1; x += 0.35f) c.Rect(x, y - 0.26f, 0.06f, 0.3f, TimberDark);
    }

    private static void Flowers(Canvas c, float x, float y)
    {
        c.Ellipse(x + 0.12f, y + 0.05f, 0.16f, 0.08f, Leaf);
        c.Ellipse(x + 0.04f, y, 0.04f, 0.04f, new Color(240, 80, 120));
        c.Ellipse(x + 0.14f, y - 0.03f, 0.04f, 0.04f, new Color(255, 220, 60));
        c.Ellipse(x + 0.22f, y + 0.01f, 0.04f, 0.04f, new Color(160, 100, 240));
    }
}

/// <summary>A small software canvas with anti-aliased shapes in tile units.</summary>
internal sealed class Canvas
{
    public readonly int Width, Height;
    public readonly Color[] Pixels;
    public readonly float Size;
    private readonly float _ppt;
    private readonly int _seed;

    public Canvas(int tiles, int pixelsPerTile, int seed)
    {
        Size = tiles;
        _ppt = pixelsPerTile;
        _seed = seed;
        Width = Height = tiles * pixelsPerTile;
        Pixels = new Color[Width * Height];
    }

    private void Blend(int x, int y, Color color, float coverage)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height || coverage <= 0) return;
        float a = color.A / 255f * coverage;
        ref var dst = ref Pixels[y * Width + x];
        float da = dst.A / 255f;
        float outA = a + da * (1 - a);
        if (outA <= 0) return;
        byte Mix(byte s, byte d) => (byte)Math.Clamp((s * a + d * da * (1 - a)) / outA, 0, 255);
        dst = new Color(Mix(color.R, dst.R), Mix(color.G, dst.G), Mix(color.B, dst.B), (byte)(outA * 255));
    }

    /// <summary>Fills pixels whose sub-samples pass <paramref name="inside"/> (4×4 supersampling), within a tile-unit bounding box.</summary>
    private void Fill(float minX, float minY, float maxX, float maxY, Color color, Func<float, float, bool> inside)
    {
        int x0 = Math.Max(0, (int)MathF.Floor(minX * _ppt)), y0 = Math.Max(0, (int)MathF.Floor(minY * _ppt));
        int x1 = Math.Min(Width - 1, (int)MathF.Ceiling(maxX * _ppt)), y1 = Math.Min(Height - 1, (int)MathF.Ceiling(maxY * _ppt));
        for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            int hits = 0;
            for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 4; sx++)
                if (inside((px + (sx + 0.5f) / 4f) / _ppt, (py + (sy + 0.5f) / 4f) / _ppt)) hits++;
            if (hits > 0) Blend(px, py, color, hits / 16f);
        }
    }

    public void Rect(float x, float y, float w, float h, Color color) =>
        Fill(x, y, x + w, y + h, color, (px, py) => px >= x && px < x + w && py >= y && py < y + h);

    public void RoundRect(float x, float y, float w, float h, float radius, Color color) =>
        Fill(x, y, x + w, y + h, color, (px, py) =>
        {
            float cx = Math.Clamp(px, x + radius, x + w - radius), cy = Math.Clamp(py, y + radius, y + h - radius);
            float dx = px - cx, dy = py - cy;
            return px >= x && px <= x + w && py >= y && py <= y + h && dx * dx + dy * dy <= radius * radius;
        });

    public void Ellipse(float cx, float cy, float rx, float ry, Color color) =>
        Fill(cx - rx, cy - ry, cx + rx, cy + ry, color, (px, py) =>
        {
            float dx = (px - cx) / rx, dy = (py - cy) / ry;
            return dx * dx + dy * dy <= 1;
        });

    /// <summary>An arc (bow shape): the right half of an ellipse outline.</summary>
    public void Arc(float cx, float cy, float r, float thickness, Color color) =>
        Fill(cx - r, cy - r, cx + r, cy + r, color, (px, py) =>
        {
            float dx = (px - cx) / (r * 0.45f), dy = (py - cy) / r;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            return px >= cx && MathF.Abs(d - 1) * r * 0.6f <= thickness;
        });

    public void Line(float x0, float y0, float x1, float y1, float thickness, Color color)
    {
        float dx = x1 - x0, dy = y1 - y0, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) return;
        float nx = -dy / len * thickness / 2, ny = dx / len * thickness / 2;
        Poly(color, (x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny));
    }

    public void Poly(Color color, params (float X, float Y)[] points)
    {
        float minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        Fill(minX, minY, maxX, maxY, color, (px, py) =>
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                var (xi, yi) = points[i];
                var (xj, yj) = points[j];
                if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        });
    }

    /// <summary>Scattered darker specks over already-painted pixels, for texture.</summary>
    public void Speckle(float x, float y, float w, float h, Color color, float density)
    {
        int x0 = (int)(x * _ppt), y0 = (int)(y * _ppt), x1 = (int)((x + w) * _ppt), y1 = (int)((y + h) * _ppt);
        for (int py = Math.Max(0, y0); py < Math.Min(Height, y1); py++)
        for (int px = Math.Max(0, x0); px < Math.Min(Width, x1); px++)
        {
            uint hash = (uint)(px * 73856093) ^ (uint)(py * 19349663) ^ (uint)(_seed * 83492791);
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            if ((hash & 0xFFFF) / 65535f < density && Pixels[py * Width + px].A > 200) Blend(px, py, color, 0.6f);
        }
    }

    /// <summary>A dark rim around every painted shape (cartoon outline), so buildings read clearly on grass.</summary>
    public void Outline(Color color)
    {
        var copy = (Color[])Pixels.Clone();
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            if (copy[y * Width + x].A >= 128) continue;
            bool edge = false;
            for (int oy = -2; oy <= 2 && !edge; oy++)
            for (int ox = -2; ox <= 2 && !edge; ox++)
            {
                int nx = x + ox, ny = y + oy;
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height || ox * ox + oy * oy > 5) continue;
                if (copy[ny * Width + nx].A >= 128) edge = true;
            }
            if (edge) Blend(x, y, color, 0.85f);
        }
    }
}
