using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using BannerAndBarrow.Game.Audio;
using BannerAndBarrow.Game.Rendering;
using BannerAndBarrow.Game.UI;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;

namespace BannerAndBarrow.Game;

/// <summary>
/// MonoGame host: menus (main, new game, pause, settings), and during play runs the fixed-tick simulation from a
/// real-time accumulator, renders with interpolation between ticks, and routes input to the UI and the selection
/// controller. Behind the main menu an AI-vs-AI match plays out as a live backdrop.
/// </summary>
public sealed class BannerAndBarrowGame : Microsoft.Xna.Framework.Game
{
    private const int LocalPlayer = 0;
    private const int PanelHeight = 170;
    private const int MinimapSize = 156;
    private static readonly int[] Speeds = { 1, 2, 4 };

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sb = null!;
    private Primitives _prims = null!;
    private SpriteRegistry _sprites = null!;
    private SpriteFont _font = null!;
    private SpriteFont _small = null!;

    private GameConfig _config = null!;
    private UserSettings _settings = new();
    private Match? _match;
    private Match? _backdrop;
    private readonly Camera2D _camera = new();
    private readonly Camera2D _backdropCamera = new();
    private readonly InputState _input = new();
    private Ui _ui = null!;
    private SelectionController _selection = null!;
    private WorldRenderer _world = null!;
    private readonly Minimap _minimap = new();
    private readonly ControlPanel _panel = new();
    private readonly Hud _hud = new();
    private readonly Menus _menus = new();
    private readonly RenderOptions _options = new();
    private readonly RenderOptions _backdropOptions = new() { RevealMap = true, ShowTerritory = false };
    private readonly SelectionView _noSelection = new();
    private AudioMixer _audio = null!;
    private SoundDirector _sound = null!;

    private GameScreen _screen = GameScreen.MainMenu;
    /// <summary>Where Settings returns to.</summary>
    private GameScreen _settingsReturn = GameScreen.MainMenu;
    private double _accumulator;
    private double _backdropAccumulator;
    private bool _paused;
    private int _lastSeed;
    private string? _fatalError;
    private CaptureScript? _capture;
    /// <summary>Debug: BANNER_MENU_CAPTURE=&lt;folder&gt; saves the main menu, new game, settings and pause screens, then exits.</summary>
    private readonly string? _menuCapture = Environment.GetEnvironmentVariable("BANNER_MENU_CAPTURE");
    private int _frame;
    /// <summary>Debug: BANNER_SOUND_TEST=&lt;seconds&gt; plays an AI-vs-AI match from that point, camera on the fighting, then exits after 60 s.</summary>
    private readonly string? _soundTest = Environment.GetEnvironmentVariable("BANNER_SOUND_TEST");
    private double _soundTestElapsed;

    public BannerAndBarrowGame()
    {
        // Screenshot runs can ask for a smaller window: BANNER_CAPTURE_SIZE=1280x720.
        var size = (Environment.GetEnvironmentVariable("BANNER_CAPTURE_SIZE") ?? "1600x900").Split('x');
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = size.Length == 2 && int.TryParse(size[0], out var w) ? w : 1600,
            PreferredBackBufferHeight = size.Length == 2 && int.TryParse(size[1], out var h) ? h : 900,
            SynchronizeWithVerticalRetrace = true,
            HardwareModeSwitch = false,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        Window.AllowUserResizing = true;
        Window.Title = "Banner & Barrow";
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _prims = new Primitives(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Fonts/Default");
        _small = Content.Load<SpriteFont>("Fonts/Small");
        _ui = new Ui(_sb, _prims, _input, _font, _small);
        _sprites = SpriteRegistry.Load(Content, Path.Combine(AppContext.BaseDirectory, "Assets", "sprites.json"));
        _world = new WorldRenderer(GraphicsDevice, _prims, _sprites, _font, _small);
        _audio = new AudioMixer(Content);
        _sound = new SoundDirector(_audio);

        try
        {
            var configDir = ConfigLoader.FindConfigDirectory(AppContext.BaseDirectory)
                            ?? throw new FileNotFoundException("config/balance.json not found next to the game or in a parent folder.");
            _config = ConfigLoader.LoadFromDirectory(configDir);
        }
        catch (Exception ex)
        {
            _fatalError = "Config error: " + ex.Message;
            return;
        }

        _capture = CaptureScript.FromEnvironment();
        if (_capture != null)
        {
            // Debug capture: straight into an AI-vs-AI match with the map revealed.
            StartMatch(_capture.Seed, _config.Ai.Active, aiForBothPlayers: true);
            _options.RevealMap = true;
            return;
        }

        _settings = UserSettings.Load();
        ApplySettings();
        if (_soundTest != null && int.TryParse(_soundTest, out var from))
        {
            StartMatch(3, "Normal", aiForBothPlayers: true);
            _match!.Run(from * _config.Simulation.TicksPerSecond);
            _camera.Zoom = 1.3f;
            return;
        }
        StartBackdrop();
    }

    private Rectangle ScreenRect => new(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

    // ------------------------------------------------------------------ matches

    private void StartMatch(int seed, string difficulty, bool aiForBothPlayers = false)
    {
        _lastSeed = seed;
        _match = Match.Create(_config, seed, aiForBothPlayers, difficulty);
        _selection = new SelectionController(_match, LocalPlayer);
        foreach (var warning in _sprites.Warnings) _match.State.AddEvent(LocalPlayer, GameEventKind.Warning, warning);
        var map = _match.State.Map;
        _camera.WorldSize = new Vector2(map.Width * FixExtensions.TileSize, map.Height * FixExtensions.TileSize);
        var keep = _match.State.GetKeep(LocalPlayer);
        if (keep != null) _camera.Position = keep.Center.ToPixels();
        _camera.Zoom = 0.9f;
        _accumulator = 0;
        _paused = false;
        _options.RevealMap = false;
        _screen = GameScreen.Playing;
    }

    private void StartNewGame()
    {
        int seed = _settings.RandomMap ? Random.Shared.Next(1, int.MaxValue) : _settings.MapSeed;
        if (_settings.RandomMap) _settings.MapSeed = seed;
        _settings.Save();
        StartMatch(seed, _settings.Difficulty);
    }

    /// <summary>A quiet AI-vs-AI match behind the menus, fast-forwarded to when towns and armies exist.</summary>
    private void StartBackdrop()
    {
        _backdrop = Match.Create(_config, Random.Shared.Next(1, int.MaxValue), aiForBothPlayers: true, aiProfile: "Hard");
        _backdrop.Run(_config.Simulation.TicksPerSecond * 60 * 4);
        var map = _backdrop.State.Map;
        _backdropCamera.WorldSize = new Vector2(map.Width * FixExtensions.TileSize, map.Height * FixExtensions.TileSize);
        _backdropCamera.Zoom = 1.1f;
        var keep = _backdrop.State.GetKeep(LocalPlayer);
        if (keep != null) _backdropCamera.Position = keep.Center.ToPixels();
    }

    private void ApplySettings()
    {
        _options.GeneratedBuildings = _settings.PaintedBuildings;
        _backdropOptions.GeneratedBuildings = _settings.PaintedBuildings;
        _options.ShowTerritory = _settings.ShowTerritory;
        _audio.Master = _settings.MasterVolume;
        _audio.Effects = _settings.EffectsVolume;
        _audio.Voices = _settings.VoiceVolume;
        if (_graphics.IsFullScreen != _settings.Fullscreen)
        {
            if (_settings.Fullscreen)
            {
                _graphics.PreferredBackBufferWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
                _graphics.PreferredBackBufferHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
            }
            else
            {
                _graphics.PreferredBackBufferWidth = 1600;
                _graphics.PreferredBackBufferHeight = 900;
            }
            _graphics.IsFullScreen = _settings.Fullscreen;
            _graphics.ApplyChanges();
        }
    }

    // ------------------------------------------------------------------ update

    private void Layout()
    {
        var s = ScreenRect;
        _camera.Viewport = new Rectangle(0, Hud.TopBarHeight, s.Width, Math.Max(100, s.Height - Hud.TopBarHeight - PanelHeight));
        _backdropCamera.Viewport = s;
        _panel.Rect = new Rectangle(0, s.Height - PanelHeight, s.Width, PanelHeight);
        _minimap.Rect = new Rectangle(8, s.Height - PanelHeight + 8, MinimapSize, MinimapSize);
        _hud.LogRect = new Rectangle(s.Width - 620, Hud.TopBarHeight + 8, 610, 150);
    }

    protected override void Update(GameTime gameTime)
    {
        _input.Update();
        if (_fatalError != null)
        {
            if (_input.KeyPressed(Keys.Escape)) Exit();
            base.Update(gameTime);
            return;
        }
        Layout();
        double dt = gameTime.ElapsedGameTime.TotalSeconds;
        if (_capture != null && _match != null)
        {
            if (_capture.Advance(_match, _camera, this)) Exit();
            base.Update(gameTime);
            return;
        }

        switch (_screen)
        {
            case GameScreen.Playing when _match != null:
            {
                if (_soundTest != null)
                {
                    _soundTestElapsed += dt;
                    if (_soundTestElapsed > 60) Exit();
                    var s = _match.State;
                    var focus = s.Regiments.Values.Where(r => r.Owner == LocalPlayer && (r.EngagedRegimentId != 0 || r.EngagedBuildingId != 0))
                                    .OrderByDescending(r => r.SoldierIds.Count).Select(r => (Vector2?)s.RegimentCentroid(r).ToPixels()).FirstOrDefault()
                                ?? s.Buildings.Values.Where(b => b.Owner == LocalPlayer && b.Fire.Raw > 0).Select(b => (Vector2?)b.Center.ToPixels()).FirstOrDefault();
                    if (focus.HasValue) _camera.Position = Vector2.Lerp(_camera.Position, focus.Value, (float)Math.Min(1, dt * 2));
                }
                bool overUi = !_camera.Viewport.Contains(_input.MousePoint);
                if (IsActive)
                {
                    // Esc with nothing selected or being placed opens the pause menu; otherwise it cancels as before.
                    bool nothingToCancel = _selection.Placement == PlacementMode.None && _selection.View.RegimentIds.Count == 0 && _selection.View.BuildingId == 0;
                    if (_input.KeyPressed(Keys.Escape) && nothingToCancel)
                    {
                        _screen = GameScreen.Paused;
                        break;
                    }
                    HandleGlobalKeys();
                    UpdateCamera(_camera, (float)dt, overUi, edgeScroll: _settings.EdgeScrolling);
                    _minimap.Update(_input, _camera, _match.State, _selection);
                    _selection.Update(_input, _camera, overUi);
                }
                StepSimulation(_match, ref _accumulator, dt * Speeds[_settings.GameSpeed], _paused);
                _sound.Volume = 1f;
                _sound.Update(_match.State, _camera, _world, _world.Viewer, dt, _paused);
                break;
            }
            case GameScreen.Paused:
                if (_input.KeyPressed(Keys.Escape)) _screen = GameScreen.Playing;
                if (_match != null) _sound.Update(_match.State, _camera, _world, _world.Viewer, dt, paused: true);
                break;
            case GameScreen.Settings:
                if (_input.KeyPressed(Keys.Escape)) CloseSettings();
                if (_settingsReturn != GameScreen.Paused) AdvanceBackdrop(dt);
                else if (_match != null) _sound.Update(_match.State, _camera, _world, _world.Viewer, dt, paused: true);
                break;
            case GameScreen.NewGame:
                if (_input.KeyPressed(Keys.Escape)) _screen = GameScreen.MainMenu;
                AdvanceBackdrop(dt);
                break;
            default:
                AdvanceBackdrop(dt);
                break;
        }
        base.Update(gameTime);
    }

    private void AdvanceBackdrop(double dt)
    {
        if (_backdrop == null) return;
        if (_backdrop.State.IsOver) StartBackdrop();
        StepSimulation(_backdrop, ref _backdropAccumulator, dt, paused: false);
        _sound.Volume = 0.35f;
        _sound.Update(_backdrop.State, _backdropCamera, _world, viewer: 0, dt, paused: false);
        // Drift slowly towards whatever is happening: the largest army, else the Keep.
        var state = _backdrop.State;
        var focus = state.Regiments.Values.Where(r => !r.IsStationed).OrderByDescending(r => r.SoldierIds.Count).ThenBy(r => r.Id)
            .Select(r => (Vector2?)state.RegimentCentroid(r).ToPixels()).FirstOrDefault() ?? state.GetKeep(0)?.Center.ToPixels();
        if (focus.HasValue) _backdropCamera.Position = Vector2.Lerp(_backdropCamera.Position, focus.Value, (float)Math.Min(1, dt * 0.15));
        _backdropCamera.Clamp();
    }

    private void HandleGlobalKeys()
    {
        if (_input.KeyPressed(Keys.Space)) _paused = !_paused;
        if (_input.KeyPressed(Keys.OemPlus) || _input.KeyPressed(Keys.Add)) ChangeSpeed(+1);
        if (_input.KeyPressed(Keys.OemMinus) || _input.KeyPressed(Keys.Subtract)) ChangeSpeed(-1);
        if (_input.KeyPressed(Keys.F1)) _options.ShowFlowField = !_options.ShowFlowField;
        if (_input.KeyPressed(Keys.F2)) _options.ShowPresence = !_options.ShowPresence;
        if (_input.KeyPressed(Keys.F3)) _options.ShowTerritory = _settings.ShowTerritory = !_options.ShowTerritory;
        if (_input.KeyPressed(Keys.F4)) _options.RevealMap = !_options.RevealMap;
        if (_input.KeyPressed(Keys.F5)) _options.GeneratedBuildings = _settings.PaintedBuildings = !_options.GeneratedBuildings;
    }

    private void ChangeSpeed(int delta)
    {
        _settings.GameSpeed = Math.Clamp(_settings.GameSpeed + delta, 0, Speeds.Length - 1);
        _settings.Save();
    }

    private void UpdateCamera(Camera2D camera, float dt, bool overUi, bool edgeScroll)
    {
        var pan = Vector2.Zero;
        if (_input.KeyDown(Keys.W) || _input.KeyDown(Keys.Up)) pan.Y -= 1;
        if (_input.KeyDown(Keys.S) || _input.KeyDown(Keys.Down)) pan.Y += 1;
        if (_input.KeyDown(Keys.D) || _input.KeyDown(Keys.Right)) pan.X += 1;
        if (_input.KeyDown(Keys.Left)) pan.X -= 1;
        if (_input.KeyDown(Keys.A)) pan.X -= 1;

        var m = _input.MousePosition;
        var s = ScreenRect;
        const int edge = 4;
        if (edgeScroll && IsActive && m.X >= 0 && m.Y >= 0 && m.X < s.Width && m.Y < s.Height)
        {
            if (m.X < edge) pan.X -= 1;
            if (m.X > s.Width - edge) pan.X += 1;
            if (m.Y < edge) pan.Y -= 1;
            if (m.Y > s.Height - edge) pan.Y += 1;
        }

        camera.Position += pan * 900f * _settings.ScrollSpeed * dt / camera.Zoom;
        if (_input.MiddleDown) camera.Position -= _input.MouseDelta / camera.Zoom;
        if (!overUi && _input.WheelDelta != 0) camera.ZoomAt(_input.MousePosition, _input.WheelDelta > 0 ? 1.15f : 1 / 1.15f);
        camera.Clamp();
    }

    private void StepSimulation(Match match, ref double accumulator, double scaledSeconds, bool paused)
    {
        var tickSeconds = 1.0 / _config.Simulation.TicksPerSecond;
        if (paused || match.State.IsOver)
        {
            accumulator = 0;
            return;
        }
        accumulator += Math.Min(scaledSeconds, 1.0);
        int steps = 0;
        while (accumulator >= tickSeconds && steps < 12)
        {
            match.Step();
            accumulator -= tickSeconds;
            steps++;
        }
        if (steps == 12) accumulator = 0; // falling behind: drop time rather than spiral
    }

    // ------------------------------------------------------------------ draw

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(20, 22, 18));
        if (_fatalError != null || _config == null)
        {
            _sb.Begin();
            _sb.DrawString(_font, _fatalError ?? "Loading...", new Vector2(40, 40), Color.OrangeRed);
            _sb.DrawString(_font, "Press Esc to quit.", new Vector2(40, 70), Color.White);
            _sb.End();
            return;
        }
        Layout();
        if (_capture?.PendingShot is { } shot && _match != null)
        {
            using var target = new RenderTarget2D(GraphicsDevice, ScreenRect.Width, ScreenRect.Height);
            GraphicsDevice.SetRenderTarget(target);
            GraphicsDevice.Clear(new Color(20, 22, 18));
            DrawGame(0f, interactive: false);
            GraphicsDevice.SetRenderTarget(null);
            using (var file = File.Create(shot)) target.SaveAsPng(file, target.Width, target.Height);
            _capture.PendingShot = null;
        }

        if (_menuCapture != null) MenuCaptureStep();
        DrawScreenContents();
        base.Draw(gameTime);
    }

    private void DrawScreenContents()
    {
        bool inGame = _screen is GameScreen.Playing or GameScreen.Paused || (_screen == GameScreen.Settings && _settingsReturn == GameScreen.Paused);
        if (inGame && _match != null)
        {
            DrawGame((float)(_accumulator * _config.Simulation.TicksPerSecond), interactive: _screen == GameScreen.Playing);
            if (_screen != GameScreen.Playing) DrawMenuLayer();
        }
        else
        {
            DrawBackdrop();
            DrawMenuLayer();
        }
    }

    private void MenuCaptureStep()
    {
        void Save(string name)
        {
            using var target = new RenderTarget2D(GraphicsDevice, ScreenRect.Width, ScreenRect.Height);
            GraphicsDevice.SetRenderTarget(target);
            GraphicsDevice.Clear(new Color(20, 22, 18));
            DrawScreenContents();
            GraphicsDevice.SetRenderTarget(null);
            Directory.CreateDirectory(_menuCapture!);
            using var file = File.Create(Path.Combine(_menuCapture!, name + ".png"));
            target.SaveAsPng(file, target.Width, target.Height);
        }
        _frame++;
        switch (_frame)
        {
            case 90: Save("1_main_menu"); _screen = GameScreen.NewGame; break;
            case 100: Save("2_new_game"); _settingsReturn = GameScreen.MainMenu; _screen = GameScreen.Settings; break;
            case 110: Save("3_settings"); StartMatch(7, "Normal"); break;
            case 170: _screen = GameScreen.Paused; break;
            case 180: Save("4_pause"); _settings.Brightness = 0.7f; _screen = GameScreen.Playing; break;
            case 190: Save("5_dark"); Exit(); break;
        }
    }

    private void DrawBackdrop()
    {
        if (_backdrop == null) return;
        var previousScissor = GraphicsDevice.ScissorRectangle;
        GraphicsDevice.ScissorRectangle = _backdropCamera.Viewport;
        _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true }, null, _backdropCamera.View);
        _world.Draw(_sb, _backdrop.State, _backdropCamera, (float)(_backdropAccumulator * _config.Simulation.TicksPerSecond), _noSelection, _backdropOptions);
        _sb.End();
        GraphicsDevice.ScissorRectangle = previousScissor;
        DrawBrightness(_backdropCamera.Viewport, extraDim: 0.35f);
    }

    /// <summary>Darkens or lightens the scene (not the UI) according to the Brightness setting.</summary>
    private void DrawBrightness(Rectangle area, float extraDim = 0f)
    {
        float b = _settings.Brightness;
        if (b < 1f || extraDim > 0f)
        {
            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            _prims.Rect(_sb, area, Color.Black * Math.Clamp(1f - (1f - Math.Max(0f, 1f - b)) * (1f - extraDim), 0f, 0.9f));
            _sb.End();
        }
        if (b > 1f)
        {
            _sb.Begin(SpriteSortMode.Deferred, BlendState.Additive);
            _prims.Rect(_sb, area, Color.White * ((b - 1f) * 0.45f));
            _sb.End();
        }
    }

    private void DrawGame(float alpha, bool interactive)
    {
        var match = _match!;
        var state = match.State;

        var previousScissor = GraphicsDevice.ScissorRectangle;
        GraphicsDevice.ScissorRectangle = _camera.Viewport;
        _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true }, null, _camera.View);
        _world.Draw(_sb, state, _camera, alpha, _selection.View, _options);
        if (interactive) _selection.DrawWorld(_sb, _prims, _small, _camera, _input);
        _sb.End();
        GraphicsDevice.ScissorRectangle = previousScissor;
        DrawBrightness(_camera.Viewport);

        _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _ui.BeginFrame();
        _ui.InputEnabled = interactive;
        if (interactive) _selection.DrawScreen(_sb, _prims);
        _hud.DrawTopBar(_ui, state, LocalPlayer, ScreenRect.Width - 76, _paused ? "PAUSED" : $"x{Speeds[_settings.GameSpeed]}", _match == null ? "" : AiLabel(state));
        if (_ui.Button(new Rectangle(ScreenRect.Width - 72, 2, 66, 22), "Menu", true, "Pause menu (Esc)")) _screen = GameScreen.Paused;
        var jump = _hud.DrawLog(_ui, state, LocalPlayer);
        if (jump.HasValue) _camera.Position = jump.Value.ToPixels();
        _panel.Draw(_ui, state, _selection, MinimapSize);
        _minimap.Draw(_ui, state, _world, _camera);
        if (_paused && interactive) _hud.DrawPaused(_ui, ScreenRect);
        _hud.DrawMatchOver(_ui, state, LocalPlayer, ScreenRect);
        if (state.IsOver && interactive)
        {
            var r = new Rectangle(ScreenRect.Width / 2 - 170, ScreenRect.Height / 3 + 70, 160, 34);
            if (_ui.Button(r, "Play again")) StartNewGame();
            if (_ui.Button(new Rectangle(r.Right + 20, r.Y, 160, 34), "Main Menu")) BackToMainMenu();
        }
        _ui.DrawTooltip(ScreenRect);
        _ui.InputEnabled = true;
        _sb.End();
    }

    private string AiLabel(GameState state) => _capture != null ? _config.Ai.Active : _settings.Difficulty;

    private void DrawMenuLayer()
    {
        _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _ui.BeginFrame();
        _ui.InputEnabled = true;
        var screen = ScreenRect;
        var action = _screen switch
        {
            GameScreen.MainMenu => _menus.DrawMainMenu(_ui, screen, canContinue: _match != null && !_match.State.IsOver),
            GameScreen.NewGame => _menus.DrawNewGame(_ui, screen, _settings),
            GameScreen.Paused => _menus.DrawPause(_ui, screen, matchOver: _match?.State.IsOver == true),
            GameScreen.Settings => _menus.DrawSettings(_ui, screen, _settings, overGame: _settingsReturn == GameScreen.Paused),
            _ => MenuAction.None,
        };
        _ui.DrawTooltip(screen);
        _sb.End();
        Handle(action);
    }

    private void Handle(MenuAction action)
    {
        switch (action)
        {
            case MenuAction.OpenNewGame:
                _screen = GameScreen.NewGame;
                break;
            case MenuAction.StartGame:
                StartNewGame();
                break;
            case MenuAction.Resume:
                _screen = GameScreen.Playing;
                break;
            case MenuAction.Restart:
                StartMatch(_lastSeed, _settings.Difficulty);
                break;
            case MenuAction.OpenSettings:
                _settingsReturn = _screen == GameScreen.Paused ? GameScreen.Paused : GameScreen.MainMenu;
                _screen = GameScreen.Settings;
                break;
            case MenuAction.CloseSettings:
                CloseSettings();
                break;
            case MenuAction.SettingsChanged:
                ApplySettings();
                break;
            case MenuAction.BackToMainMenu:
                BackToMainMenu();
                break;
            case MenuAction.Quit:
                _settings.Save();
                Exit();
                break;
        }
    }

    private void CloseSettings()
    {
        _settings.Save();
        ApplySettings();
        _screen = _settingsReturn;
    }

    private void BackToMainMenu()
    {
        if (_match?.State.IsOver == true) _match = null;
        _screen = GameScreen.MainMenu;
        _backdropAccumulator = 0;
    }
}
