using Microsoft.Xna.Framework;
using BannerAndBarrow.Game.Rendering;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Game.Audio;

/// <summary>
/// Turns what is happening on screen into sound, positioned relative to the camera:
/// - Work (axes, picks, scythes, hammers, anvils, coins) and footsteps are close-up detail: they swell as you zoom in
///   on a Worker and vanish when zoomed out. Tool hits are timed to the same swing cycle the renderer draws.
/// - Combat (clashes, spear and armour hits, bows, arrow impacts, torches, fire, collapsing buildings) carries further.
/// - Your Soldiers shout: charging, acknowledging attack orders, archers loosing, rallying each other mid-fight,
///   cheering a victory, reporting for duty. The enemy roars when it charges you, blows a horn when a wave assaults,
///   and its marching drums can be heard as it approaches, even before you can see it.
/// Sounds of enemies you can't see stay silent, except those drums and horns.
/// </summary>
public sealed class SoundDirector
{
    private readonly AudioMixer _mixer;
    private readonly Dictionary<int, long> _nextAttack = new();
    private readonly Dictionary<int, long> _chargeReady = new();
    private readonly Dictionary<int, int> _engaged = new();
    private readonly Dictionary<int, RegimentOrder> _orders = new();
    private readonly Dictionary<int, (int Owner, Vector2 Position, bool Visible)> _buildings = new();
    private readonly Dictionary<Projectile, bool> _projectiles = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<int> _regimentsLastFrame = new();
    private readonly Dictionary<int, float> _regimentShotAt = new();
    private readonly List<Worker> _workerScratch = new();
    private GameState? _state;
    private float _lastTime = -1;
    private int _eventCursor;
    private float _nextRally = 10f;
    private float _voiceBusyUntil;
    private float _lastLoose = -100f, _lastCharge = -100f, _lastVictory = -100f, _lastAck = -100f, _lastEnemyRoar = -100f;
    private readonly Random _random = new();

    public SoundDirector(AudioMixer mixer) => _mixer = mixer;

    /// <summary>Scales everything this director plays (quieter behind the menus).</summary>
    public float Volume = 1f;

    public void Update(GameState state, Camera2D camera, WorldRenderer world, int viewer, double realSeconds, bool paused)
    {
        _mixer.Update(realSeconds);
        if (!ReferenceEquals(state, _state))
        {
            Reset(state);
            return;
        }
        float time = world.AnimationTime;
        float previous = _lastTime;
        _lastTime = time;
        if (paused || time <= previous)
        {
            _mixer.QuietLoops();
            return;
        }

        var listener = new Listener(camera, viewer);
        WorkSounds(state, world, listener, previous, time);
        CombatSounds(state, listener, viewer, time);
        ProjectileSounds(state, listener);
        BuildingSounds(state, listener);
        EventSounds(state, listener, viewer);
        EnemyApproach(state, listener, viewer);
        _regimentsLastFrame.Clear();
        foreach (var id in state.Regiments.Keys) _regimentsLastFrame.Add(id);
    }

    private void Reset(GameState state)
    {
        _state = state;
        _nextAttack.Clear();
        _chargeReady.Clear();
        _engaged.Clear();
        _orders.Clear();
        _buildings.Clear();
        _projectiles.Clear();
        _regimentsLastFrame.Clear();
        _regimentShotAt.Clear();
        _eventCursor = state.Events.Count;
        _lastTime = -1;
        _mixer.QuietLoops();
        foreach (var s in state.Soldiers.Values)
        {
            _nextAttack[s.Id] = s.NextAttackTick;
            _chargeReady[s.Id] = s.ChargeReadyTick;
        }
        foreach (var r in state.Regiments.Values)
        {
            _engaged[r.Id] = r.EngagedRegimentId;
            _orders[r.Id] = r.Order;
            _regimentsLastFrame.Add(r.Id);
        }
    }

    // ------------------------------------------------------------------ positioning

    private readonly struct Listener
    {
        public readonly Camera2D Camera;
        public readonly int Viewer;

        public Listener(Camera2D camera, int viewer)
        {
            Camera = camera;
            Viewer = viewer;
        }

        /// <summary>Screen-space distance from the view centre (1 = edge of the view) and pan.</summary>
        public (float Distance, float Pan) Locate(Vector2 worldPixels)
        {
            var screen = Camera.WorldToScreen(worldPixels);
            var view = Camera.Viewport;
            float dx = (screen.X - view.Center.X) / Math.Max(1f, view.Width / 2f);
            float dy = (screen.Y - view.Center.Y) / Math.Max(1f, view.Height / 2f);
            return (MathF.Sqrt(dx * dx + dy * dy), Math.Clamp(dx * 0.75f, -1f, 1f));
        }

        /// <summary>Full inside the view, fading out a little beyond its edge.</summary>
        public static float DistanceGain(float d) => d <= 1f ? 1f - 0.3f * d : Math.Max(0f, 0.7f - (d - 1f) * 1.1f);

        /// <summary>Close-up detail: silent when zoomed far out, full when zoomed well in.</summary>
        public float DetailGain => MathF.Pow(Math.Clamp((Camera.Zoom - 0.4f) / 1.2f, 0f, 1f), 1.3f);

        /// <summary>Battle noise carries: audible from a distance, louder close up.</summary>
        public float CombatGain => Math.Clamp(0.35f + Camera.Zoom * 0.45f, 0.35f, 1f);

        /// <summary>Shouts carry furthest.</summary>
        public float VoiceGain => Math.Clamp(0.6f + Camera.Zoom * 0.3f, 0.6f, 1f);
    }

    private bool Audible(GameState state, int viewer, int owner, FixVec2 position, int regimentId = 0) =>
        viewer < 0 || owner == viewer || state.CanSee(viewer, position) || (regimentId != 0 && state.IsRevealed(viewer, regimentId));

    private void PlayAt(string key, Listener listener, Vector2 worldPixels, float gain, SoundBus bus = SoundBus.Effects,
        double minInterval = 0.05, float priority = 0.5f, float pitchJitter = 0.08f)
    {
        var (d, pan) = listener.Locate(worldPixels);
        float g = gain * Listener.DistanceGain(d) * Volume;
        if (g > 0.01f) _mixer.Play(key, g, pan, pitchJitter, bus, minInterval, priority);
    }

    // ------------------------------------------------------------------ work

    private void WorkSounds(GameState state, WorldRenderer world, Listener listener, float previous, float time)
    {
        float detail = listener.DetailGain;
        if (detail <= 0.01f) return;

        // Only the Workers nearest the centre of the view: a whole town hammering at once is noise.
        _workerScratch.Clear();
        foreach (var w in state.Workers.Values)
        {
            if (w.Owner != listener.Viewer && listener.Viewer >= 0 && !state.CanSee(listener.Viewer, w.Position)) continue;
            if (listener.Locate(w.Position.ToPixels()).Distance > 1.05f) continue;
            _workerScratch.Add(w);
        }
        _workerScratch.Sort((a, b) => listener.Locate(a.Position.ToPixels()).Distance.CompareTo(listener.Locate(b.Position.ToPixels()).Distance));

        int tools = 0, steps = 0;
        foreach (var w in _workerScratch)
        {
            var at = w.Position.ToPixels();
            var workplace = state.GetBuilding(w.WorkplaceId);
            string? key = w.Task switch
            {
                WorkerTask.Build or WorkerTask.Repair => "hammer",
                WorkerTask.Sow => "sow",
                WorkerTask.Harvest => workplace?.Type switch
                {
                    BuildingType.Woodcutter => "chop",
                    BuildingType.Quarry or BuildingType.IronMine => "mine",
                    BuildingType.Farm => "scythe",
                    _ => null,
                },
                WorkerTask.Work => workplace?.Type switch
                {
                    BuildingType.Smithy => "anvil",
                    BuildingType.TaxOffice => "coins",
                    BuildingType.Fletcher or BuildingType.ShieldMaker => "hammer",
                    _ => null,
                },
                _ => null,
            };
            if (key != null && tools < 6)
            {
                // Same swing cycle as WorldRenderer.DrawWorkerTool: the blow lands at 70% of each cycle.
                float offset = WorldRenderer.Hash01(w.Id);
                float before = previous * 1.1f + offset - 0.7f, after = time * 1.1f + offset - 0.7f;
                // Coins clink every third swing, not every one.
                if (MathF.Floor(before) != MathF.Floor(after) && (key != "coins" || (int)MathF.Floor(after) % 3 == 0))
                {
                    float gain = key switch { "anvil" => 0.55f, "coins" => 0.35f, "sow" => 0.3f, "hammer" => 0.5f, _ => 0.6f };
                    PlayAt(key, listener, at, gain * detail, minInterval: 0.03, priority: 0.3f);
                    tools++;
                }
            }
            else if (!w.Arrived && steps < 2 && detail > 0.6f)
            {
                float speed = w.MaxSpeed.ToFloat();
                float rate = (5.5f + speed * 1.5f) / MathF.PI;
                float offset = WorldRenderer.Hash01(w.Id) * 2f;
                if (MathF.Floor(previous * rate + offset) != MathF.Floor(time * rate + offset))
                {
                    PlayAt("step", listener, at, 0.1f * detail, minInterval: 0.18, priority: 0.1f, pitchJitter: 0.15f);
                    steps++;
                }
            }
        }
    }

    // ------------------------------------------------------------------ combat and shouts

    private void CombatSounds(GameState state, Listener listener, int viewer, float time)
    {
        var cfg = state.Config;
        int blows = 0;
        foreach (var s in state.Soldiers.Values)
        {
            _nextAttack.TryGetValue(s.Id, out var lastAttack);
            _nextAttack[s.Id] = s.NextAttackTick;
            _chargeReady.TryGetValue(s.Id, out var lastCharge);
            _chargeReady[s.Id] = s.ChargeReadyTick;
            if (s.Stationed) continue;
            var regiment = state.GetRegiment(s.RegimentId);
            if (regiment == null || !Audible(state, viewer, s.Owner, s.Position, s.RegimentId)) continue;
            var at = s.Position.ToPixels();

            if (s.ChargeReadyTick > lastCharge && lastCharge != 0)
            {
                // A Knight charge landed: thundering impact, and your riders roar.
                PlayAt("armour_hit", listener, at, 0.9f * listener.CombatGain, priority: 0.9f);
                if (s.Owner == viewer) Shout("voice_charge", listener, at, time, ref _lastCharge, 7f, withHorn: true);
            }

            if (s.NextAttackTick <= lastAttack || lastAttack == 0) continue;
            var def = cfg.Soldier(s.Type);
            bool shotArrow = def.Ranged != null && state.Projectiles.Any(p => p.SourceSoldierId == s.Id && p.LaunchTick >= state.Tick - 1);
            if (shotArrow || blows >= 5) continue;

            if (regiment.EngagedBuildingId != 0 && regiment.EngagedRegimentId == 0 && def.Ranged == null)
            {
                PlayAt("whoosh", listener, at, 0.45f * listener.CombatGain, minInterval: 0.08, priority: 0.4f);
            }
            else
            {
                string key = s.Type switch
                {
                    SoldierType.Spearmen or SoldierType.Pikemen => _random.Next(3) == 0 ? "clash" : "spear_hit",
                    SoldierType.Knights => "armour_hit",
                    _ => _random.Next(4) == 0 ? "armour_hit" : "clash",
                };
                PlayAt(key, listener, at, 0.55f * listener.CombatGain, minInterval: 0.035, priority: 0.6f);
            }
            blows++;
        }
        foreach (var id in _nextAttack.Keys.ToList())
            if (!state.Soldiers.ContainsKey(id))
            {
                _nextAttack.Remove(id);
                _chargeReady.Remove(id);
            }

        var previousEngaged = new Dictionary<int, int>(_engaged);
        foreach (var r in state.Regiments.Values)
        {
            _engaged.TryGetValue(r.Id, out var wasEngaged);
            _engaged[r.Id] = r.EngagedRegimentId;
            _orders.TryGetValue(r.Id, out var oldOrder);
            _orders[r.Id] = r.Order;
            if (r.IsStationed || r.SoldierIds.Count == 0) continue;
            var at = state.RegimentCentroid(r).ToPixels();

            if (r.Owner == viewer)
            {
                // Order acknowledged.
                if (r.Order != oldOrder && r.Order is RegimentOrder.AttackRegiment or RegimentOrder.AttackBuilding or RegimentOrder.AttackMove &&
                    _regimentsLastFrame.Contains(r.Id))
                    Shout("voice_attack", listener, at, time, ref _lastAck, 3f);

                // Into melee.
                if (wasEngaged == 0 && r.EngagedRegimentId != 0 && state.Config.Soldier(r.Type).Ranged == null)
                    Shout("voice_charge", listener, at, time, ref _lastCharge, 7f, withHorn: r.SoldierIds.Count >= 12);
            }
            else if (r.EngagedRegimentId != 0 && wasEngaged == 0 && state.GetRegiment(r.EngagedRegimentId)?.Owner == viewer)
            {
                // The enemy charges you: a war cry from their lines.
                if (time - _lastEnemyRoar > 6f && Audible(state, viewer, r.Owner, state.RegimentCentroid(r), r.Id))
                {
                    _lastEnemyRoar = time;
                    PlayAt("roar_enemy", listener, at, 0.8f * listener.VoiceGain, priority: 0.95f, pitchJitter: 0.05f);
                }
            }
        }

        // Victory: an enemy Regiment that was fighting yours is gone.
        foreach (var id in _regimentsLastFrame)
        {
            if (state.Regiments.ContainsKey(id)) continue;
            var ours = state.Regiments.Values.FirstOrDefault(o => o.Owner == viewer && previousEngaged.TryGetValue(o.Id, out var e) && e == id);
            if (ours != null)
            {
                var at = state.RegimentCentroid(ours).ToPixels();
                if (time - _lastVictory > 10f)
                {
                    PlayAt("cheer", listener, at, 0.7f * listener.VoiceGain, priority: 0.9f);
                    Shout("voice_victory", listener, at, time, ref _lastVictory, 10f);
                }
            }
            _engaged.Remove(id);
            _orders.Remove(id);
        }

        // Rallying cries during a long fight.
        if (time >= _nextRally)
        {
            var fighting = state.Regiments.Values
                .Where(r => r.Owner == viewer && r.EngagedRegimentId != 0 && !r.IsStationed)
                .OrderBy(r => listener.Locate(state.RegimentCentroid(r).ToPixels()).Distance)
                .FirstOrDefault();
            if (fighting != null)
            {
                float dummy = -100f;
                Shout("voice_rally", listener, state.RegimentCentroid(fighting).ToPixels(), time, ref dummy, 0f);
                _nextRally = time + 9f + (float)_random.NextDouble() * 8f;
            }
            else
            {
                _nextRally = time + 2f;
            }
        }
    }

    /// <summary>One voice at a time, each category with its own cooldown.</summary>
    private void Shout(string key, Listener listener, Vector2 at, float time, ref float last, float cooldown, bool withHorn = false)
    {
        if (time - last < cooldown || time < _voiceBusyUntil) return;
        last = time;
        _voiceBusyUntil = time + 1.2f;
        PlayAt(key, listener, at, 0.9f * listener.VoiceGain, SoundBus.Voices, minInterval: 0.5, priority: 1f, pitchJitter: 0.04f);
        if (withHorn) PlayAt("horn_own", listener, at, 0.45f * listener.VoiceGain, priority: 0.9f, pitchJitter: 0.02f);
    }

    // ------------------------------------------------------------------ arrows

    private void ProjectileSounds(GameState state, Listener listener)
    {
        int viewer = listener.Viewer;
        float time = _lastTime;
        foreach (var p in state.Projectiles)
        {
            if (_projectiles.ContainsKey(p)) continue;
            _projectiles[p] = true;
            if (!Audible(state, viewer, p.Owner, p.Origin)) continue;
            var shooter = state.GetSoldier(p.SourceSoldierId);
            int regimentId = shooter?.RegimentId ?? -p.SourceBuildingId;
            // One twang per volley per Regiment, not one per archer.
            if (_regimentShotAt.TryGetValue(regimentId, out var lastShot) && time - lastShot < 0.25f) continue;
            _regimentShotAt[regimentId] = time;
            PlayAt("bow", listener, p.Origin.ToPixels(), (p.IsLongbow ? 0.55f : 0.4f) * listener.CombatGain, minInterval: 0.04, priority: 0.5f, pitchJitter: 0.12f);
            if (p.Owner == viewer && shooter != null && time - _lastLoose > 12f)
                Shout("voice_loose", listener, p.Origin.ToPixels(), time, ref _lastLoose, 12f);
        }
        foreach (var p in _projectiles.Keys.ToList())
        {
            if (state.Projectiles.Contains(p)) continue;
            _projectiles.Remove(p);
            if (!Audible(state, viewer, p.Owner, p.Target)) continue;
            PlayAt("arrow_hit", listener, p.Target.ToPixels(), 0.3f * listener.CombatGain, minInterval: 0.05, priority: 0.2f, pitchJitter: 0.15f);
        }
    }

    // ------------------------------------------------------------------ buildings

    private void BuildingSounds(GameState state, Listener listener)
    {
        int viewer = listener.Viewer;
        // Fire: a crackling loop for each of the three nearest burning buildings.
        var burning = state.Buildings.Values
            .Where(b => (b.Fire.Raw > 0 || b.IsBurning) && (viewer < 0 || state.CanSee(viewer, b)))
            .Select(b => (Building: b, Loc: listener.Locate(b.Center.ToPixels())))
            .OrderBy(x => x.Loc.Distance)
            .Take(3)
            .ToList();
        for (int i = 0; i < 3; i++)
        {
            if (i < burning.Count)
            {
                var (b, loc) = burning[i];
                float fire = Math.Max(b.Fire.ToFloat(), b.IsBurning ? 0.5f : 0f);
                _mixer.SetLoop($"fire{i}", "fire", (0.15f + fire * 0.6f) * Listener.DistanceGain(loc.Distance) * listener.CombatGain * Volume, loc.Pan);
            }
            else
            {
                _mixer.SetLoop($"fire{i}", "fire", 0f);
            }
        }

        // Collapses: a building that was visible last frame is gone.
        foreach (var (id, info) in _buildings.ToList())
        {
            if (state.Buildings.ContainsKey(id)) continue;
            _buildings.Remove(id);
            if (info.Visible) PlayAt("collapse", listener, info.Position, 0.8f * listener.CombatGain, priority: 0.85f);
        }
        foreach (var b in state.Buildings.Values)
            _buildings[b.Id] = (b.Owner, b.Center.ToPixels(), viewer < 0 || b.Owner == viewer || state.CanSee(viewer, b));
    }

    private void EventSounds(GameState state, Listener listener, int viewer)
    {
        if (_eventCursor > state.Events.Count) _eventCursor = 0;
        float time = _lastTime;
        for (; _eventCursor < state.Events.Count; _eventCursor++)
        {
            var e = state.Events[_eventCursor];
            var at = e.Position?.ToPixels();
            if (e.Player == viewer && at.HasValue)
            {
                if (e.Message.EndsWith("completed.")) PlayAt("complete", listener, at.Value, 0.6f * Math.Max(0.4f, listener.DetailGain), priority: 0.7f);
                else if (e.Message.EndsWith("Regiment ready."))
                {
                    PlayAt("recruit", listener, at.Value, 0.6f, priority: 0.7f);
                    float lastReady = -100f;
                    Shout("voice_ready", listener, at.Value, time, ref lastReady, 0f);
                }
            }
            else if (e.Player != viewer && e.Message.StartsWith("AI wave assaults"))
            {
                // The enemy sounds the attack: a distant horn from the direction of its army.
                var army = state.Regiments.Values.Where(r => r.Owner == e.Player && !r.IsStationed && r.Order is RegimentOrder.AttackMove or RegimentOrder.Move)
                    .Select(r => state.RegimentCentroid(r).ToPixels()).ToList();
                var from = army.Count > 0 ? army.Aggregate(Vector2.Zero, (a, b) => a + b) / army.Count : listener.Camera.Position;
                var (_, pan) = listener.Locate(from);
                _mixer.Play("horn_enemy", 0.55f * Volume, pan, 0.02f, SoundBus.Effects, 5, 1f);
            }
        }
    }

    /// <summary>Marching drums get louder as enemy Regiments on the move close in on your buildings, seen or not.</summary>
    private void EnemyApproach(GameState state, Listener listener, int viewer)
    {
        if (viewer < 0)
        {
            _mixer.SetLoop("drums", "drums", 0f);
            return;
        }
        const float hearing = 55f;
        float nearest = float.MaxValue;
        Vector2 source = Vector2.Zero;
        int marching = 0;
        foreach (var r in state.Regiments.Values)
        {
            if (r.Owner == viewer || r.IsStationed || r.SoldierIds.Count == 0) continue;
            if (r.Order is not (RegimentOrder.Move or RegimentOrder.AttackMove or RegimentOrder.AttackBuilding or RegimentOrder.AttackRegiment)) continue;
            var c = state.RegimentCentroid(r);
            foreach (var b in state.Buildings.Values)
            {
                if (b.Owner != viewer) continue;
                float d = FixVec2.Distance(b.Center, c).ToFloat();
                if (d >= nearest) continue;
                nearest = d;
                source = c.ToPixels();
            }
            if (nearest < hearing) marching += r.SoldierIds.Count;
        }
        if (nearest >= hearing || marching == 0)
        {
            _mixer.SetLoop("drums", "drums", 0f);
            return;
        }
        float closeness = MathF.Pow(1f - nearest / hearing, 1.5f);
        float size = Math.Clamp(marching / 30f, 0.4f, 1f);
        var (_, pan) = listener.Locate(source);
        _mixer.SetLoop("drums", "drums", 0.55f * closeness * size * Volume, pan);
    }
}
