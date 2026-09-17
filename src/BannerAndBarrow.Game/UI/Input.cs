using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using BannerAndBarrow.Game.Rendering;

namespace BannerAndBarrow.Game.UI;

public sealed class InputState
{
    private KeyboardState _keys, _prevKeys;
    private MouseState _mouse, _prevMouse;

    public void Update()
    {
        _prevKeys = _keys;
        _prevMouse = _mouse;
        _keys = Keyboard.GetState();
        _mouse = Mouse.GetState();
    }

    public Vector2 MousePosition => new(_mouse.X, _mouse.Y);
    public Point MousePoint => new(_mouse.X, _mouse.Y);
    public int WheelDelta => _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

    public bool KeyDown(Keys k) => _keys.IsKeyDown(k);
    public bool KeyPressed(Keys k) => _keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);
    public bool Shift => KeyDown(Keys.LeftShift) || KeyDown(Keys.RightShift);
    public bool Ctrl => KeyDown(Keys.LeftControl) || KeyDown(Keys.RightControl);

    public bool LeftDown => _mouse.LeftButton == ButtonState.Pressed;
    public bool LeftPressed => LeftDown && _prevMouse.LeftButton == ButtonState.Released;
    public bool LeftReleased => !LeftDown && _prevMouse.LeftButton == ButtonState.Pressed;
    public bool RightDown => _mouse.RightButton == ButtonState.Pressed;
    public bool RightPressed => RightDown && _prevMouse.RightButton == ButtonState.Released;
    public bool RightReleased => !RightDown && _prevMouse.RightButton == ButtonState.Pressed;
    public bool MiddleDown => _mouse.MiddleButton == ButtonState.Pressed;
    public Vector2 MouseDelta => new(_mouse.X - _prevMouse.X, _mouse.Y - _prevMouse.Y);
}

/// <summary>Minimal immediate-mode UI: call widgets during Draw; they report clicks from this frame's input.</summary>
public sealed class Ui
{
    private readonly SpriteBatch _sb;
    private readonly Primitives _prims;
    private readonly InputState _input;
    private string? _tooltip;
    private bool _clickConsumed;

    public SpriteFont Font { get; }
    public SpriteFont Small { get; }

    public static readonly Color PanelColor = new(28, 26, 24, 235);
    public static readonly Color PanelBorder = new(90, 80, 60);
    public static readonly Color Text = new(235, 225, 200);
    public static readonly Color Dim = new(150, 140, 120);

    public Ui(SpriteBatch sb, Primitives prims, InputState input, SpriteFont font, SpriteFont small)
    {
        _sb = sb;
        _prims = prims;
        _input = input;
        Font = font;
        Small = small;
    }

    public SpriteBatch Batch => _sb;
    public Primitives Prims => _prims;
    public InputState Input => _input;

    /// <summary>False while a menu covers this part of the UI: widgets draw but can't be hovered or clicked.</summary>
    public bool InputEnabled { get; set; } = true;
    private int _draggingSlider = -1;

    public void BeginFrame()
    {
        _tooltip = null;
        _clickConsumed = false;
    }

    public void Panel(Rectangle r)
    {
        _prims.Rect(_sb, r, PanelColor);
        _prims.RectOutline(_sb, r, PanelBorder);
    }

    public bool Button(Rectangle r, string label, bool enabled = true, string? tooltip = null, bool active = false)
    {
        bool hover = InputEnabled && r.Contains(_input.MousePoint);
        var bg = !enabled ? new Color(45, 42, 38) : active ? new Color(120, 95, 50) : hover ? new Color(85, 72, 52) : new Color(60, 52, 42);
        _prims.Rect(_sb, r, bg);
        _prims.RectOutline(_sb, r, active ? new Color(230, 190, 90) : PanelBorder);
        var size = Small.MeasureString(label);
        var pos = new Vector2(r.X + (r.Width - size.X) / 2, r.Y + (r.Height - size.Y) / 2);
        _sb.DrawString(Small, label, new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y)), enabled ? Text : Dim);
        if (hover && tooltip != null) _tooltip = tooltip;
        if (hover && enabled && _input.LeftPressed && !_clickConsumed)
        {
            _clickConsumed = true;
            return true;
        }
        return false;
    }

    public void Label(Vector2 pos, string text, Color? color = null, bool small = false) =>
        _sb.DrawString(small ? Small : Font, text, new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y)), color ?? Text);

    /// <summary>A horizontal slider: click or drag along the track. Returns the (possibly changed) value.</summary>
    public float Slider(Rectangle track, float value, float min, float max)
    {
        int id = track.X * 7919 + track.Y;
        bool hover = InputEnabled && new Rectangle(track.X - 6, track.Y - 8, track.Width + 12, track.Height + 16).Contains(_input.MousePoint);
        if (hover && _input.LeftPressed && !_clickConsumed)
        {
            _draggingSlider = id;
            _clickConsumed = true;
        }
        if (!_input.LeftDown && _draggingSlider == id) _draggingSlider = -1;
        if (_draggingSlider == id)
        {
            float t = Math.Clamp((_input.MousePosition.X - track.X) / track.Width, 0f, 1f);
            value = min + t * (max - min);
        }
        float f = (value - min) / (max - min);
        _prims.Rect(_sb, track, new Color(20, 18, 16));
        _prims.Rect(_sb, new Rectangle(track.X, track.Y, (int)(track.Width * f), track.Height), new Color(150, 115, 55));
        _prims.RectOutline(_sb, track, PanelBorder);
        var knob = new Rectangle(track.X + (int)(track.Width * f) - 5, track.Y - 5, 10, track.Height + 10);
        _prims.Rect(_sb, knob, hover || _draggingSlider == id ? new Color(240, 200, 110) : new Color(210, 180, 120));
        _prims.RectOutline(_sb, knob, Color.Black * 0.6f);
        return value;
    }

    public void Bar(Rectangle r, float t, Color fill)
    {
        _prims.Rect(_sb, r, new Color(20, 20, 20, 200));
        _prims.Rect(_sb, new Rectangle(r.X, r.Y, (int)(r.Width * Math.Clamp(t, 0, 1)), r.Height), fill);
    }

    public void DrawTooltip(Rectangle screen)
    {
        if (_tooltip == null) return;
        var size = Small.MeasureString(_tooltip);
        var pos = _input.MousePosition + new Vector2(14, -size.Y - 10);
        pos.X = Math.Min(pos.X, screen.Width - size.X - 12);
        pos.Y = Math.Max(pos.Y, 4);
        var r = new Rectangle((int)pos.X - 5, (int)pos.Y - 3, (int)size.X + 10, (int)size.Y + 6);
        _prims.Rect(_sb, r, new Color(15, 14, 12, 245));
        _prims.RectOutline(_sb, r, PanelBorder);
        _sb.DrawString(Small, _tooltip, pos, Text);
    }
}
