using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BannerAndBarrow.Game.UI;

public enum GameScreen : byte
{
    MainMenu,
    NewGame,
    Settings,
    Playing,
    Paused,
}

public enum MenuAction : byte
{
    None,
    StartGame,
    Resume,
    Restart,
    OpenSettings,
    CloseSettings,
    OpenNewGame,
    BackToMainMenu,
    Quit,
    SettingsChanged,
}

/// <summary>Main menu, new-game setup, pause menu and settings, drawn as centred panels over the world.</summary>
public sealed class Menus
{
    private static readonly string[] Difficulties = { "Easy", "Normal", "Hard" };
    private static readonly string[] SpeedLabels = { "x1", "x2", "x4" };

    public MenuAction DrawMainMenu(Ui ui, Rectangle screen, bool canContinue)
    {
        DrawTitle(ui, screen);
        var panel = CenteredPanel(ui, screen, 320, canContinue ? 226 : 178, top: screen.Height / 2 - 40);
        int y = panel.Y + 24;
        var action = MenuAction.None;
        if (canContinue && BigButton(ui, panel, ref y, "Continue")) action = MenuAction.Resume;
        if (BigButton(ui, panel, ref y, "New Game")) action = MenuAction.OpenNewGame;
        if (BigButton(ui, panel, ref y, "Settings")) action = MenuAction.OpenSettings;
        if (BigButton(ui, panel, ref y, "Quit")) action = MenuAction.Quit;
        ui.Label(new Vector2(screen.Width - 250, screen.Height - 26), "Art: Kenney (CC0) and painted in code", Ui.Dim, small: true);
        return action;
    }

    public MenuAction DrawNewGame(Ui ui, Rectangle screen, UserSettings settings)
    {
        DrawTitle(ui, screen);
        var panel = CenteredPanel(ui, screen, 460, 300, top: screen.Height / 2 - 60);
        ui.Label(new Vector2(panel.X + 24, panel.Y + 16), "New Game", Ui.Text);
        int y = panel.Y + 60;
        var action = MenuAction.None;

        ui.Label(new Vector2(panel.X + 24, y + 4), "AI difficulty", Ui.Text, small: true);
        for (int i = 0; i < Difficulties.Length; i++)
            if (ui.Button(new Rectangle(panel.X + 160 + i * 94, y, 88, 26), Difficulties[i], true, DifficultyTip(Difficulties[i]), settings.Difficulty == Difficulties[i]))
                settings.Difficulty = Difficulties[i];
        y += 44;

        ui.Label(new Vector2(panel.X + 24, y + 4), "Map", Ui.Text, small: true);
        if (ui.Button(new Rectangle(panel.X + 160, y, 88, 26), "Random", true, "A new map every game.", settings.RandomMap)) settings.RandomMap = true;
        if (ui.Button(new Rectangle(panel.X + 254, y, 88, 26), "Fixed", true, "Replay the same map (seed below).", !settings.RandomMap)) settings.RandomMap = false;
        y += 36;
        if (!settings.RandomMap)
        {
            ui.Label(new Vector2(panel.X + 24, y + 4), $"Seed  {settings.MapSeed}", Ui.Dim, small: true);
            if (ui.Button(new Rectangle(panel.X + 160, y, 88, 26), "New seed", true, "Pick a different map."))
                settings.MapSeed = Random.Shared.Next(1, int.MaxValue);
        }
        y += 44;

        if (ui.Button(new Rectangle(panel.X + 24, panel.Bottom - 50, 130, 32), "Back")) action = MenuAction.BackToMainMenu;
        if (ui.Button(new Rectangle(panel.Right - 184, panel.Bottom - 50, 160, 32), "Start", true, null, true)) action = MenuAction.StartGame;
        return action;
    }

    public MenuAction DrawPause(Ui ui, Rectangle screen, bool matchOver)
    {
        ui.Prims.Rect(ui.Batch, screen, Color.Black * 0.45f);
        var panel = CenteredPanel(ui, screen, 320, 330, top: screen.Height / 2 - 165);
        ui.Label(new Vector2(panel.X + 24, panel.Y + 16), matchOver ? "Match over" : "Paused", Ui.Text);
        int y = panel.Y + 60;
        var action = MenuAction.None;
        if (!matchOver && BigButton(ui, panel, ref y, "Resume")) action = MenuAction.Resume;
        if (BigButton(ui, panel, ref y, "Settings")) action = MenuAction.OpenSettings;
        if (BigButton(ui, panel, ref y, "Restart")) action = MenuAction.Restart;
        if (BigButton(ui, panel, ref y, "Main Menu")) action = MenuAction.BackToMainMenu;
        if (BigButton(ui, panel, ref y, "Quit to Desktop")) action = MenuAction.Quit;
        return action;
    }

    public MenuAction DrawSettings(Ui ui, Rectangle screen, UserSettings settings, bool overGame)
    {
        if (overGame) ui.Prims.Rect(ui.Batch, screen, Color.Black * 0.45f);
        else DrawTitle(ui, screen);
        var panel = CenteredPanel(ui, screen, 560, 530, top: screen.Height / 2 - 250);
        ui.Label(new Vector2(panel.X + 24, panel.Y + 16), "Settings", Ui.Text);
        int x = panel.X + 24, cx = panel.X + 220, y = panel.Y + 64;
        const int row = 42;
        bool changed = false;

        ui.Label(new Vector2(x, y + 4), "Game speed", Ui.Text, small: true);
        for (int i = 0; i < SpeedLabels.Length; i++)
            if (ui.Button(new Rectangle(cx + i * 70, y, 64, 26), SpeedLabels[i], true, "Also + / - during play.", settings.GameSpeed == i))
            {
                settings.GameSpeed = i;
                changed = true;
            }
        y += row;

        ui.Label(new Vector2(x, y + 4), "Brightness", Ui.Text, small: true);
        var brightness = ui.Slider(new Rectangle(cx, y + 6, 220, 14), settings.Brightness, 0.5f, 1.5f);
        ui.Label(new Vector2(cx + 232, y + 4), $"{(int)MathF.Round(settings.Brightness * 100)}%", Ui.Dim, small: true);
        if (MathF.Abs(brightness - settings.Brightness) > 0.001f)
        {
            settings.Brightness = brightness;
            changed = true;
        }
        y += row;

        ui.Label(new Vector2(x, y + 4), "Camera scroll speed", Ui.Text, small: true);
        var scroll = ui.Slider(new Rectangle(cx, y + 6, 220, 14), settings.ScrollSpeed, 0.25f, 3f);
        ui.Label(new Vector2(cx + 232, y + 4), $"x{settings.ScrollSpeed:0.00}", Ui.Dim, small: true);
        if (MathF.Abs(scroll - settings.ScrollSpeed) > 0.001f)
        {
            settings.ScrollSpeed = scroll;
            changed = true;
        }
        y += row;

        changed |= VolumeSlider(ui, x, cx, ref y, row, "Master volume", settings.MasterVolume, v => settings.MasterVolume = v);
        changed |= VolumeSlider(ui, x, cx, ref y, row, "Sound effects", settings.EffectsVolume, v => settings.EffectsVolume = v);
        changed |= VolumeSlider(ui, x, cx, ref y, row, "Soldier shouts", settings.VoiceVolume, v => settings.VoiceVolume = v);

        changed |= Toggle(ui, x, cx, ref y, row, "Scroll at screen edges", settings.EdgeScrolling, v => settings.EdgeScrolling = v);
        changed |= Toggle(ui, x, cx, ref y, row, "Fullscreen", settings.Fullscreen, v => settings.Fullscreen = v);
        changed |= Toggle(ui, x, cx, ref y, row, "Show Territory overlay", settings.ShowTerritory, v => settings.ShowTerritory = v, "F3 during play.");

        ui.Label(new Vector2(x, y + 4), "Building art", Ui.Text, small: true);
        if (ui.Button(new Rectangle(cx, y, 100, 26), "Painted", true, "Colourful art painted by the game (F5 toggles).", settings.PaintedBuildings) && !settings.PaintedBuildings)
        {
            settings.PaintedBuildings = true;
            changed = true;
        }
        if (ui.Button(new Rectangle(cx + 106, y, 100, 26), "Original", true, "Kenney Medieval RTS sprites.", !settings.PaintedBuildings) && settings.PaintedBuildings)
        {
            settings.PaintedBuildings = false;
            changed = true;
        }

        if (ui.Button(new Rectangle(panel.Right - 154, panel.Bottom - 50, 130, 32), "Back", true, null, true)) return MenuAction.CloseSettings;
        return changed ? MenuAction.SettingsChanged : MenuAction.None;
    }

    private static bool VolumeSlider(Ui ui, int x, int cx, ref int y, int row, string label, float value, Action<float> set)
    {
        ui.Label(new Vector2(x, y + 4), label, Ui.Text, small: true);
        var next = ui.Slider(new Rectangle(cx, y + 6, 220, 14), value, 0f, 1f);
        ui.Label(new Vector2(cx + 232, y + 4), $"{(int)MathF.Round(next * 100)}%", Ui.Dim, small: true);
        y += row;
        if (MathF.Abs(next - value) <= 0.001f) return false;
        set(next);
        return true;
    }

    private static bool Toggle(Ui ui, int x, int cx, ref int y, int row, string label, bool value, Action<bool> set, string? tip = null)
    {
        ui.Label(new Vector2(x, y + 4), label, Ui.Text, small: true);
        bool changed = false;
        if (ui.Button(new Rectangle(cx, y, 64, 26), "On", true, tip, value) && !value)
        {
            set(true);
            changed = true;
        }
        if (ui.Button(new Rectangle(cx + 70, y, 64, 26), "Off", true, tip, !value) && value)
        {
            set(false);
            changed = true;
        }
        y += row;
        return changed;
    }

    private static string DifficultyTip(string difficulty) => difficulty switch
    {
        "Easy" => "A gentle opponent: 5-minute peace, small attacks.",
        "Hard" => "Aggressive: prefers rushing, escalates fast.",
        _ => "2-minute peace, then escalating attacks.",
    };

    private static void DrawTitle(Ui ui, Rectangle screen)
    {
        const string title = "BANNER & BARROW";
        const float scale = 3f;
        var size = ui.Font.MeasureString(title) * scale;
        var pos = new Vector2(screen.Width / 2f - size.X / 2, Math.Max(24f, screen.Height / 2f - 340));
        ui.Batch.DrawString(ui.Font, title, pos + new Vector2(4, 4), Color.Black * 0.6f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        ui.Batch.DrawString(ui.Font, title, pos, new Color(240, 200, 90), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        const string subtitle = "a medieval town and war game";
        var sub = ui.Font.MeasureString(subtitle);
        ui.Label(new Vector2(screen.Width / 2f - sub.X / 2, pos.Y + size.Y + 6), subtitle, Ui.Text);
    }

    private static Rectangle CenteredPanel(Ui ui, Rectangle screen, int width, int height, int top)
    {
        var r = new Rectangle(screen.Width / 2 - width / 2, Math.Max(10, top), width, height);
        ui.Panel(r);
        return r;
    }

    private static bool BigButton(Ui ui, Rectangle panel, ref int y, string label)
    {
        var r = new Rectangle(panel.X + 30, y, panel.Width - 60, 38);
        y += 48;
        return ui.Button(r, label);
    }
}
