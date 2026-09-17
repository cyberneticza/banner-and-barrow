using Microsoft.Xna.Framework;
using BannerAndBarrow.Game.Rendering;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Game.UI;

/// <summary>Top resource bar, event log and end-of-match banner.</summary>
public sealed class Hud
{
    public const int TopBarHeight = 26;
    public Rectangle LogRect;

    public void DrawTopBar(Ui ui, GameState state, int player, int screenWidth, string speedLabel, string aiProfile)
    {
        var bar = new Rectangle(0, 0, screenWidth, TopBarHeight);
        ui.Panel(bar);
        var stock = state.TotalStock(player);
        float x = 8;
        foreach (var r in Resources.All)
        {
            ui.Prims.Rect(ui.Batch, new Rectangle((int)x, 8, 10, 10), Palette.Resource(r));
            var text = $"{r} {stock[(int)r]}";
            ui.Label(new Vector2(x + 14, 5), text, Ui.Text, small: true);
            x += ui.Small.MeasureString(text).X + 20;
        }
        long seconds = state.Tick / state.Config.Simulation.TicksPerSecond;
        int unfilled = state.UnfilledJobs(player);
        bool housingFull = state.WorkerCount(player) >= state.WorkerRoom(player);
        int hungry = state.StarvingWorkers(player);
        if (hungry > 0 && unfilled == 0)
        {
            var warning = $"! {hungry} Workers hungry - they work at half speed. Build Farms";
            var warnSize = ui.Small.MeasureString(warning);
            var warnPos = new Vector2(screenWidth / 2f - warnSize.X / 2, TopBarHeight + 6);
            ui.Prims.Rect(ui.Batch, new Rectangle((int)warnPos.X - 6, TopBarHeight + 4, (int)warnSize.X + 12, 20), Color.DarkOrange * 0.85f);
            ui.Label(warnPos, warning, Color.White, small: true);
        }
        if (unfilled > 0)
        {
            var warning = housingFull ? $"! {unfilled} jobs unfilled - build Houses" : $"! {unfilled} jobs waiting for Workers";
            var warnSize = ui.Small.MeasureString(warning);
            var warnPos = new Vector2(screenWidth / 2f - warnSize.X / 2, TopBarHeight + 6);
            ui.Prims.Rect(ui.Batch, new Rectangle((int)warnPos.X - 6, TopBarHeight + 4, (int)warnSize.X + 12, 20), (housingFull ? Color.DarkRed : Color.DarkGoldenrod) * 0.85f);
            ui.Label(warnPos, warning, Color.White, small: true);
        }
        var right = $"Workers {state.WorkerCount(player)}/{state.WorkerRoom(player)}   Soldiers {state.SoldierCount(player)}/{state.Config.Simulation.MaxSoldiersPerPlayer}   " +
                    $"{seconds / 60:00}:{seconds % 60:00}  {speedLabel}   Seed {state.Map.Seed}   AI: {aiProfile}";
        var size = ui.Small.MeasureString(right);
        ui.Label(new Vector2(screenWidth - size.X - 10, 5), right, Ui.Text, small: true);
    }

    /// <summary>Returns a world position to jump the camera to when an event line is clicked.</summary>
    public FixVec2? DrawLog(Ui ui, GameState state, int player)
    {
        var tps = state.Config.Simulation.TicksPerSecond;
        var recent = state.Events
            .Where(e => (e.Player == player || e.Kind == GameEventKind.MatchOver) && state.Tick - e.Tick < 25 * tps)
            .TakeLast(7).ToList();
        FixVec2? jump = null;
        int y = LogRect.Y;
        foreach (var e in recent)
        {
            var color = e.Kind switch
            {
                GameEventKind.Alert => Color.OrangeRed,
                GameEventKind.Burning => Color.Orange,
                GameEventKind.Starved => Color.Yellow,
                GameEventKind.CommandRejected => new Color(255, 150, 150),
                GameEventKind.MatchOver => Color.Gold,
                GameEventKind.Warning => Color.Orange,
                _ => Ui.Text,
            };
            float age = (state.Tick - e.Tick) / (float)(25 * tps);
            var text = e.Message + (e.Position.HasValue ? "  [click]" : "");
            var size = ui.Small.MeasureString(text);
            var r = new Rectangle(LogRect.Right - (int)size.X - 12, y, (int)size.X + 10, 18);
            ui.Prims.Rect(ui.Batch, r, new Color(0, 0, 0) * (0.55f * (1 - age * 0.6f)));
            ui.Label(new Vector2(r.X + 5, r.Y + 2), text, color * (1 - age * 0.5f), small: true);
            if (e.Position.HasValue && r.Contains(ui.Input.MousePoint) && ui.Input.LeftPressed) jump = e.Position;
            y += 20;
        }
        return jump;
    }

    public void DrawMatchOver(Ui ui, GameState state, int player, Rectangle screen)
    {
        if (!state.IsOver) return;
        bool won = state.Winner == player;
        var text = won ? "VICTORY - the enemy Keep has fallen" : "DEFEAT - your Keep has fallen";
        var size = ui.Font.MeasureString(text) * 2;
        var r = new Rectangle((int)(screen.Width / 2 - size.X / 2 - 20), screen.Height / 3 - 20, (int)size.X + 40, (int)size.Y + 40);
        ui.Panel(r);
        ui.Batch.DrawString(ui.Font, text, new Vector2(r.X + 20, r.Y + 20), won ? Color.Gold : Color.OrangeRed, 0f, Vector2.Zero, 2f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
    }

    public void DrawPaused(Ui ui, Rectangle screen)
    {
        const string text = "PAUSED (Space)";
        var size = ui.Font.MeasureString(text);
        ui.Label(new Vector2(screen.Width / 2 - size.X / 2, TopBarHeight + 10), text, Color.Yellow);
    }
}
