using Microsoft.Xna.Framework;

namespace BannerAndBarrow.Game.Rendering;

public sealed class Camera2D
{
    public Vector2 Position;
    public float Zoom = 1f;
    public float MinZoom = 0.15f;
    public float MaxZoom = 2.5f;
    public Rectangle Viewport;
    public Vector2 WorldSize;

    public Matrix View =>
        Matrix.CreateTranslation(-Position.X, -Position.Y, 0) *
        Matrix.CreateScale(Zoom, Zoom, 1) *
        Matrix.CreateTranslation(Viewport.X + Viewport.Width / 2f, Viewport.Y + Viewport.Height / 2f, 0);

    public Vector2 ScreenToWorld(Vector2 screen) => Vector2.Transform(screen, Matrix.Invert(View));

    public Vector2 WorldToScreen(Vector2 world) => Vector2.Transform(world, View);

    public RectangleF VisibleWorld
    {
        get
        {
            var tl = ScreenToWorld(new Vector2(Viewport.X, Viewport.Y));
            var br = ScreenToWorld(new Vector2(Viewport.Right, Viewport.Bottom));
            return new RectangleF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        }
    }

    public void ZoomAt(Vector2 screenPoint, float factor)
    {
        var before = ScreenToWorld(screenPoint);
        Zoom = MathHelper.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var after = ScreenToWorld(screenPoint);
        Position += before - after;
        Clamp();
    }

    public void Clamp()
    {
        Position.X = MathHelper.Clamp(Position.X, 0, WorldSize.X);
        Position.Y = MathHelper.Clamp(Position.Y, 0, WorldSize.Y);
    }
}

public readonly record struct RectangleF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X <= Right && p.Y <= Bottom;
}
