using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Game.Rendering;

/// <summary>The only place fixed-point values become floats: at the rendering boundary.</summary>
public static class FixExtensions
{
    public const float TileSize = 32f;

    public static float ToFloat(this Fix f) => f.Raw / (float)Fix.OneRaw;

    /// <summary>World tiles to world pixels.</summary>
    public static Vector2 ToPixels(this FixVec2 v) => new(v.X.ToFloat() * TileSize, v.Y.ToFloat() * TileSize);

    public static Vector2 Lerp(FixVec2 previous, FixVec2 current, float alpha) =>
        Vector2.Lerp(previous.ToPixels(), current.ToPixels(), alpha);

    public static FixVec2 ToFixTiles(this Vector2 worldPixels) =>
        new(Fix.FromRaw((long)(worldPixels.X / TileSize * Fix.OneRaw)), Fix.FromRaw((long)(worldPixels.Y / TileSize * Fix.OneRaw)));
}

/// <summary>Generated shapes used as placeholders when no sprite is configured. These are shapes, not art.</summary>
public sealed class Primitives
{
    public Texture2D Pixel { get; }
    public Texture2D Circle { get; }
    public Texture2D Ring { get; }

    public Primitives(GraphicsDevice device)
    {
        Pixel = new Texture2D(device, 1, 1);
        Pixel.SetData(new[] { Color.White });
        Circle = MakeCircle(device, 64, filled: true);
        Ring = MakeCircle(device, 64, filled: false);
    }

    private static Texture2D MakeCircle(GraphicsDevice device, int size, bool filled)
    {
        var tex = new Texture2D(device, size, size);
        var data = new Color[size * size];
        float r = size / 2f - 1;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - size / 2f + 0.5f, dy = y - size / 2f + 0.5f;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            float a = filled ? Math.Clamp(r - d + 0.5f, 0, 1) : Math.Clamp(1.5f - MathF.Abs(r - 2 - d), 0, 1);
            data[y * size + x] = Color.White * a;
        }
        tex.SetData(data);
        return tex;
    }

    public void Rect(SpriteBatch sb, Rectangle rect, Color color) => sb.Draw(Pixel, rect, color);

    public void Rect(SpriteBatch sb, Vector2 pos, Vector2 size, Color color) =>
        sb.Draw(Pixel, pos, null, color, 0f, Vector2.Zero, size, SpriteEffects.None, 0f);

    public void RectOutline(SpriteBatch sb, Vector2 pos, Vector2 size, Color color, float thickness)
    {
        Rect(sb, pos, new Vector2(size.X, thickness), color);
        Rect(sb, pos + new Vector2(0, size.Y - thickness), new Vector2(size.X, thickness), color);
        Rect(sb, pos, new Vector2(thickness, size.Y), color);
        Rect(sb, pos + new Vector2(size.X - thickness, 0), new Vector2(thickness, size.Y), color);
    }

    public void RectOutline(SpriteBatch sb, Rectangle r, Color color, int thickness = 1) =>
        RectOutline(sb, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), color, thickness);

    public void Line(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float thickness)
    {
        var d = b - a;
        float length = d.Length();
        if (length < 0.001f) return;
        sb.Draw(Pixel, a, null, color, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(length, thickness), SpriteEffects.None, 0f);
    }

    public void Disc(SpriteBatch sb, Vector2 center, float radius, Color color) =>
        sb.Draw(Circle, center, null, color, 0f, new Vector2(Circle.Width / 2f), radius * 2 / Circle.Width, SpriteEffects.None, 0f);

    public void Ellipse(SpriteBatch sb, Vector2 center, float radiusX, float radiusY, Color color) =>
        sb.Draw(Circle, center, null, color, 0f, new Vector2(Circle.Width / 2f), new Vector2(radiusX * 2 / Circle.Width, radiusY * 2 / Circle.Width), SpriteEffects.None, 0f);

    public void Circle_(SpriteBatch sb, Vector2 center, float radius, Color color) =>
        sb.Draw(Ring, center, null, color, 0f, new Vector2(Ring.Width / 2f), radius * 2 / Ring.Width, SpriteEffects.None, 0f);
}
