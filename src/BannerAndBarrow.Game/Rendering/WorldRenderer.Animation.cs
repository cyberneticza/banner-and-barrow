using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;

namespace BannerAndBarrow.Game.Rendering;

/// <summary>
/// Procedural animation on top of the static Kenney sprites: walking (bob, sway, shadow, facing), tool swings for
/// working Workers, melee lunges, growing Fields, and buildings that visibly work (smoke, forge glow, sparks,
/// output piles, waving flags). Purely visual: everything is derived from simulation state each frame.
/// </summary>
public sealed partial class WorldRenderer
{
    /// <summary>Animation clock in seconds; follows the simulation, so it stops when the game is paused.</summary>
    private float _time;
    /// <summary>Seconds on the animation clock (follows the simulation); sound uses it to time tool hits with the drawing.</summary>
    public float AnimationTime => _time;
    private readonly Dictionary<int, bool> _facingLeft = new();
    private readonly HashSet<int> _workingBuildings = new();

    private void BeginAnimationFrame(GameState state, float alpha)
    {
        _time = (state.Tick + alpha) / state.Config.Simulation.TicksPerSecond;
        _workingBuildings.Clear();
        foreach (var w in state.Workers.Values)
            if (w.Job == WorkerJob.Producer && w.Task is WorkerTask.Work or WorkerTask.Harvest or WorkerTask.Sow)
                _workingBuildings.Add(w.WorkplaceId);
        if (_facingLeft.Count > 4096) _facingLeft.Clear();
    }

    public static float Hash01(int id, int salt = 0)
    {
        uint h = (uint)(id * 73856093) ^ (uint)(salt * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65535f;
    }

    private static float Frac(float v) => v - MathF.Floor(v);

    // ------------------------------------------------------------------ units

    /// <summary>Screen pose of a unit this frame: bob offset, sway angle, facing and whether it is walking.</summary>
    private (Vector2 Pos, float Rotation, bool FlipX, bool Walking, float Phase) UnitPose(NavAgent agent, int id, Vector2 pos, int cadenceId)
    {
        var dx = (agent.Position.X - agent.PreviousPosition.X).ToFloat();
        var dy = (agent.Position.Y - agent.PreviousPosition.Y).ToFloat();
        bool walking = dx * dx + dy * dy > 0.0004f;
        if (MathF.Abs(dx) > 0.01f) _facingLeft[id] = dx < 0;
        bool left = _facingLeft.TryGetValue(id, out var l) && l;

        // Soldiers of one Regiment share a cadence so they march in step; Workers each have their own.
        float speed = agent.MaxSpeed.ToFloat();
        float phase = _time * (5.5f + speed * 1.5f) + Hash01(cadenceId) * MathF.Tau;
        if (!walking) return (pos + new Vector2(0, MathF.Sin(_time * 1.7f + Hash01(id) * 6f) * 0.5f), 0f, left, false, phase);
        float bob = -MathF.Abs(MathF.Sin(phase)) * 3f;
        float sway = MathF.Sin(phase) * 0.12f;
        return (pos + new Vector2(0, bob), sway, left, true, phase);
    }

    private void DrawShadow(SpriteBatch sb, Vector2 pos, float r, bool walking, float phase)
    {
        float squash = walking ? 1f - MathF.Abs(MathF.Sin(phase)) * 0.15f : 1f;
        _p.Ellipse(sb, pos + new Vector2(0, r * 1.05f), r * 1.1f * squash, r * 0.42f * squash, Color.Black * 0.28f);
    }

    /// <summary>Walking feet: two small dark dots stepping under the unit.</summary>
    private void DrawFeet(SpriteBatch sb, Vector2 groundPos, float r, float phase)
    {
        float step = MathF.Sin(phase) * r * 0.45f;
        _p.Disc(sb, groundPos + new Vector2(-r * 0.35f + step, r * 0.95f), r * 0.2f, new Color(40, 30, 25));
        _p.Disc(sb, groundPos + new Vector2(r * 0.35f - step, r * 0.95f), r * 0.2f, new Color(40, 30, 25));
    }

    private void DrawWorker(SpriteBatch sb, GameState state, Worker w, Vector2 lerped, float zoom)
    {
        float r = w.Radius.ToFloat() * T;
        var col = Color.Lerp(Palette.Player(w.Owner), Color.White, 0.45f);
        var pose = UnitPose(w, w.Id, lerped, w.Id);
        var workplace = state.GetBuilding(w.WorkplaceId);

        DrawShadow(sb, lerped, r, pose.Walking, pose.Phase);
        if (pose.Walking && zoom >= 0.4f) DrawFeet(sb, lerped, r, pose.Phase);

        // Working: lean into the swing.
        float swing = 0f;
        bool working = w.Task is WorkerTask.Harvest or WorkerTask.Sow or WorkerTask.Build or WorkerTask.Repair ||
                       (w.Task == WorkerTask.Work && workplace != null);
        var body = pose.Pos;
        float rotation = pose.Rotation;
        if (working)
        {
            swing = MathF.Sin(_time * 7f + Hash01(w.Id) * 6f);
            rotation = swing * 0.08f;
            body += new Vector2(0, -MathF.Max(0, swing) * 1.5f);
        }

        if (!DrawUnitSprite(sb, "unit.Worker", w.Owner, body, r * 3.2f, col, rotation, pose.FlipX)) _p.Disc(sb, body, r, col);

        if (zoom >= 0.35f)
        {
            if (working) DrawWorkerTool(sb, state, w, workplace, body, r, pose.FlipX);
            if (w.CarryAmount > 0)
            {
                // Load on the back, bouncing with each step.
                float bounce = pose.Walking ? MathF.Abs(MathF.Cos(pose.Phase)) * 1.5f : 0f;
                var back = body + new Vector2(pose.FlipX ? r * 0.8f : -r * 0.8f, -r * 1.2f - bounce);
                DrawCarriedLoad(sb, w.CarryType, back, r);
            }
            if (w.IsHungry(state.Tick) && w.Owner == Viewer)
            {
                float pulse = 0.75f + 0.25f * MathF.Sin(_time * 5f + w.Id);
                var icon = body + new Vector2(0, -r * 2.6f);
                _p.Disc(sb, icon, 4.2f * pulse, new Color(120, 30, 20));
                _p.Disc(sb, icon, 3.2f * pulse, Palette.Resource(ResourceType.Food));
            }
        }
    }

    private void DrawCarriedLoad(SpriteBatch sb, ResourceType type, Vector2 at, float r)
    {
        var c = Palette.Resource(type);
        switch (type)
        {
            case ResourceType.Wood:
                for (int i = 0; i < 3; i++)
                {
                    var y = at.Y + i * 2.2f - 2f;
                    _p.Line(sb, new Vector2(at.X - r * 0.8f, y), new Vector2(at.X + r * 0.8f, y), c, 2.2f);
                    _p.Disc(sb, new Vector2(at.X + r * 0.8f, y), 1.2f, new Color(200, 160, 110));
                }
                break;
            case ResourceType.Food:
                _p.Disc(sb, at, r * 0.75f, new Color(190, 160, 90));
                _p.Line(sb, at + new Vector2(-1, -r * 0.7f), at + new Vector2(-2, -r * 1.3f), new Color(210, 180, 70), 1.5f);
                _p.Line(sb, at + new Vector2(1, -r * 0.7f), at + new Vector2(2, -r * 1.3f), new Color(210, 180, 70), 1.5f);
                break;
            default:
                _p.Rect(sb, at - new Vector2(r * 0.6f), new Vector2(r * 1.2f), c);
                _p.RectOutline(sb, at - new Vector2(r * 0.6f), new Vector2(r * 1.2f), Color.Black * 0.4f, 1f);
                break;
        }
    }

    /// <summary>Axe, pick, scythe, seed bag, hammer or tongs, swinging in time with the body.</summary>
    private void DrawWorkerTool(SpriteBatch sb, GameState state, Worker w, Building? workplace, Vector2 body, float r, bool left)
    {
        float side = left ? -1f : 1f;
        float cycle = Frac(_time * 1.1f + Hash01(w.Id));
        var shaft = new Color(120, 85, 45);
        var steel = new Color(200, 205, 215);
        var hand = body + new Vector2(side * r * 0.7f, -r * 0.2f);

        if (w.Task == WorkerTask.Sow)
        {
            // Scatter seed in an arc in front of the Farmer.
            _p.Disc(sb, body + new Vector2(-side * r * 0.6f, 0), r * 0.5f, new Color(150, 120, 70));
            for (int i = 0; i < 4; i++)
            {
                float f = Frac(cycle + i * 0.25f);
                var p = hand + new Vector2(side * (f * r * 3f), -r * 0.8f + f * f * r * 3.5f);
                _p.Rect(sb, p, new Vector2(1.6f), new Color(230, 210, 140) * (1f - f));
            }
            return;
        }

        if (w.Task is WorkerTask.Build or WorkerTask.Repair)
        {
            DrawSwing(sb, hand, side, cycle, r * 1.4f, shaft, steel, headWidth: 5f, headLength: 3f, out var hit);
            if (hit) DrawBurst(sb, hand + new Vector2(side * r * 1.6f, r * 0.6f), new Color(170, 150, 120), w.Id, cycle);
            return;
        }

        switch (workplace?.Type)
        {
            case BuildingType.Woodcutter:
            {
                DrawSwing(sb, hand, side, cycle, r * 1.7f, shaft, steel, headWidth: 4f, headLength: 5f, out var hit);
                if (hit) DrawBurst(sb, hand + new Vector2(side * r * 1.9f, r * 0.2f), new Color(200, 160, 100), w.Id, cycle);
                break;
            }
            case BuildingType.Quarry or BuildingType.IronMine:
            {
                DrawSwing(sb, hand, side, cycle, r * 1.7f, shaft, steel, headWidth: 7f, headLength: 2f, out var hit);
                if (hit)
                {
                    DrawBurst(sb, hand + new Vector2(side * r * 1.9f, r * 0.4f), new Color(160, 160, 160), w.Id, cycle);
                    _p.Disc(sb, hand + new Vector2(side * r * 1.9f, r * 0.4f), 2f, new Color(255, 230, 150) * 0.8f);
                }
                break;
            }
            case BuildingType.Farm:
            {
                // Scythe sweeping low and wide.
                float a = MathF.Sin(cycle * MathF.Tau) * 1.1f;
                var dir = new Vector2(MathF.Cos(a) * side, MathF.Sin(a) * 0.4f + 0.5f);
                var tip = hand + dir * r * 2f;
                _p.Line(sb, hand, tip, shaft, 1.8f);
                _p.Line(sb, tip, tip + new Vector2(-side * r * 0.9f, r * 0.3f), steel, 2f);
                if (MathF.Abs(a) < 0.3f)
                    for (int i = 0; i < 3; i++)
                        _p.Line(sb, tip + new Vector2(i * 2 - 2, 0), tip + new Vector2(i * 2 - 1, -3 - i), new Color(220, 190, 90), 1.2f);
                break;
            }
            default:
            {
                // Workshops: hammering at a bench.
                DrawSwing(sb, hand, side, cycle, r * 1.2f, shaft, steel, headWidth: 4f, headLength: 3f, out var hit);
                if (hit && workplace?.Type is BuildingType.Smithy)
                    DrawBurst(sb, hand + new Vector2(side * r * 1.3f, r * 0.5f), new Color(255, 190, 60), w.Id, cycle);
                break;
            }
        }
    }

    /// <summary>A tool raised back then brought down in front; <paramref name="hit"/> is true around the moment of impact.</summary>
    private void DrawSwing(SpriteBatch sb, Vector2 hand, float side, float cycle, float length, Color shaft, Color head,
        float headWidth, float headLength, out bool hit)
    {
        // Slow wind-up, fast strike.
        float t = cycle < 0.7f ? cycle / 0.7f : 1f - (cycle - 0.7f) / 0.3f;
        float angle = MathHelper.Lerp(-2.2f, 0.5f, 1f - t * t);
        var dir = new Vector2(MathF.Sin(angle) * side, -MathF.Cos(angle));
        var tip = hand + dir * length;
        _p.Line(sb, hand, tip, shaft, 2f);
        var perp = new Vector2(-dir.Y, dir.X);
        _p.Line(sb, tip - perp * headWidth / 2, tip + perp * headWidth / 2, head, headLength);
        hit = cycle >= 0.68f && cycle < 0.9f;
    }

    /// <summary>A few chips flying out from an impact point.</summary>
    private void DrawBurst(SpriteBatch sb, Vector2 at, Color color, int seed, float cycle)
    {
        float f = Math.Clamp((cycle - 0.68f) / 0.22f, 0f, 1f);
        for (int i = 0; i < 5; i++)
        {
            float a = -MathF.PI / 2 + (Hash01(seed, i) - 0.5f) * 2.4f;
            var p = at + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (2f + f * 9f) + new Vector2(0, f * f * 6f);
            _p.Rect(sb, p, new Vector2(1.8f), color * (1f - f));
        }
    }

    private void DrawSoldier(SpriteBatch sb, GameState state, Soldier s, Vector2 lerped, bool selected, float zoom)
    {
        float r = s.Radius.ToFloat() * T;
        var regiment = state.GetRegiment(s.RegimentId);
        var pose = UnitPose(s, s.Id, lerped, s.RegimentId);
        var def = state.Config.Soldier(s.Type);

        // Mounted Soldiers canter: a bigger, slower bob.
        var body = pose.Pos;
        if (def.IsMounted && pose.Walking) body += new Vector2(0, -MathF.Abs(MathF.Sin(pose.Phase * 0.7f)) * 2f);

        // Fighting: lunge into each blow.
        var combat = zoom >= 0.3f ? CombatStateOf(state, s, regiment, def) : (CombatAction.None, Vector2.Zero, 0f, 1f);
        body += StrikeLunge(combat.Item1, body, combat.Item2, combat.Item3, r);

        DrawShadow(sb, lerped, def.IsMounted ? r * 1.2f : r, pose.Walking, pose.Phase);
        if (pose.Walking && zoom >= 0.4f && !def.IsMounted) DrawFeet(sb, lerped, r, pose.Phase);
        if (!DrawUnitSprite(sb, "unit." + s.Type, s.Owner, body, r * 2.6f, Palette.Player(s.Owner), pose.Rotation, pose.FlipX))
        {
            _p.Disc(sb, body, r, Palette.Player(s.Owner));
            _p.Disc(sb, body, r * 0.45f, Palette.SoldierMark(s.Type));
        }
        bool melee = combat.Item1 == CombatAction.Melee;
        // Spears and swords in a blow are drawn by the combat layer instead of the resting gear.
        if (zoom >= 0.45f && regiment != null && !(melee && s.Type is not SoldierType.MenAtArms)) DrawSoldierGear(sb, state, s, regiment, body, r);
        if (zoom >= 0.35f && combat.Item1 != CombatAction.None) DrawCombatAction(sb, state, s, combat, body, r);
        if (selected) _p.Circle_(sb, lerped, r + 3, Color.White);
    }

    // ------------------------------------------------------------------ fields

    /// <summary>A sown Field: tilled soil with rows of crop that grow, sway in the wind and turn golden when ripe.</summary>
    private void DrawField(SpriteBatch sb, GameState state, int x, int y, int index)
    {
        var map = state.Map;
        var origin = new Vector2(x * T, y * T);
        long sown = map.CropSownTick[index], ripe = map.CropRipeTick[index];
        float growth = ripe <= sown ? 1f : Math.Clamp((state.Tick - sown) / (float)(ripe - sown), 0f, 1f);

        _p.Rect(sb, origin + new Vector2(1), new Vector2(T - 2), new Color(110, 80, 50));
        for (int row = 0; row < 4; row++)
            _p.Rect(sb, origin + new Vector2(3, 5 + row * 7), new Vector2(T - 6, 2), new Color(85, 60, 38));

        if (growth < 0.08f)
        {
            // Freshly sown: seed specks.
            for (int i = 0; i < 8; i++)
                _p.Rect(sb, origin + new Vector2(5 + (i % 4) * 7, 4 + (i / 4) * 14 + (i % 2) * 7), new Vector2(1.5f), new Color(220, 200, 150));
            return;
        }

        bool isRipe = growth >= 1f;
        var green = new Color(70, 150, 50);
        var gold = new Color(225, 190, 80);
        var stalk = isRipe ? gold : Color.Lerp(new Color(90, 170, 60), green, growth);
        float height = 3f + growth * 9f;
        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 5; col++)
        {
            var baseP = origin + new Vector2(4 + col * 6, 7 + row * 7);
            float wind = MathF.Sin(_time * 2.2f + x * 0.6f + y * 0.35f + col * 0.4f) * growth * 1.8f;
            var tip = baseP + new Vector2(wind, -height);
            _p.Line(sb, baseP, tip, stalk, 1.3f);
            if (growth > 0.6f) _p.Disc(sb, tip, isRipe ? 1.8f : 1.2f, isRipe ? new Color(240, 210, 110) : new Color(150, 190, 80));
        }
    }

    // ------------------------------------------------------------------ buildings

    /// <summary>Signs of life on top of a building: smoke, glow, sparks, piles, flags and scaffolding.</summary>
    private void DrawBuildingLife(SpriteBatch sb, GameState state, Building b, Vector2 pos, Vector2 size, float zoom)
    {
        if (zoom < 0.3f) return;
        if (b.NeedsConstruction && !b.IsRepair)
        {
            DrawScaffolding(sb, pos, size, b.Progress.ToFloat());
            return;
        }
        if (b.State != BuildingState.Active || b.Demolishing) return;

        bool working = _workingBuildings.Contains(b.Id);
        var chimney = pos + new Vector2(size.X * 0.72f, size.Y * 0.18f);
        switch (b.Type)
        {
            case BuildingType.House:
            case BuildingType.Keep:
                DrawSmoke(sb, chimney, b.Id, new Color(210, 210, 210), 0.35f, 3);
                break;
            case BuildingType.Smithy:
                if (!working) break;
                DrawSmoke(sb, chimney, b.Id, new Color(90, 90, 95), 0.55f, 4);
                DrawForge(sb, pos + new Vector2(size.X * 0.3f, size.Y * 0.78f), b.Id);
                break;
            case BuildingType.Fletcher:
            case BuildingType.ShieldMaker:
                if (working) DrawSmoke(sb, chimney, b.Id, new Color(180, 175, 170), 0.4f, 3);
                break;
            case BuildingType.TaxOffice:
                if (working) DrawGlints(sb, pos, size, b.Id);
                break;
            case BuildingType.Barracks:
            case BuildingType.Archery:
            case BuildingType.Stable:
                DrawFlag(sb, pos + new Vector2(size.X * 0.15f, size.Y * 0.1f), Palette.Player(b.Owner), b.Recruitment?.Paid == true ? 1.4f : 0.7f, b.Id);
                if (b.Recruitment?.Paid == true) DrawDrill(sb, b, pos, size);
                break;
            case BuildingType.Outpost:
            case BuildingType.Stronghold:
                DrawFlag(sb, pos + new Vector2(size.X * 0.5f, 2), Palette.Player(b.Owner), 1f, b.Id);
                break;
        }
        DrawOutputPile(sb, state, b, pos, size);
    }

    private void DrawSmoke(SpriteBatch sb, Vector2 chimney, int seed, Color color, float strength, int puffs)
    {
        for (int i = 0; i < puffs; i++)
        {
            float f = Frac(_time * 0.35f + i / (float)puffs + Hash01(seed));
            var drift = new Vector2(MathF.Sin(_time * 0.8f + i * 1.7f + seed) * 4f + f * 8f, -f * 34f);
            _p.Disc(sb, chimney + drift, 3f + f * 7f, color * (strength * (1f - f)));
        }
    }

    private void DrawForge(SpriteBatch sb, Vector2 at, int seed)
    {
        float flicker = 0.6f + 0.4f * MathF.Sin(_time * 13f + seed) * MathF.Sin(_time * 7.3f);
        _p.Disc(sb, at, 9f, new Color(255, 120, 30) * (0.25f * flicker));
        _p.Disc(sb, at, 4f, new Color(255, 200, 90) * (0.7f * flicker));
        for (int i = 0; i < 4; i++)
        {
            float f = Frac(_time * 1.3f + i * 0.27f + Hash01(seed, i));
            var p = at + new Vector2((Hash01(seed, i + 7) - 0.5f) * 14f * f, -f * 18f);
            _p.Rect(sb, p, new Vector2(1.5f), new Color(255, 210, 90) * (1f - f));
        }
    }

    private void DrawGlints(SpriteBatch sb, Vector2 pos, Vector2 size, int seed)
    {
        for (int i = 0; i < 3; i++)
        {
            float f = Frac(_time * 0.8f + i / 3f + Hash01(seed, i));
            var p = pos + new Vector2(size.X * (0.25f + 0.25f * i), size.Y * 0.35f - f * 10f);
            float a = MathF.Sin(f * MathF.PI);
            _p.Line(sb, p - new Vector2(3, 0), p + new Vector2(3, 0), Color.Gold * a, 1.2f);
            _p.Line(sb, p - new Vector2(0, 3), p + new Vector2(0, 3), Color.Gold * a, 1.2f);
        }
    }

    private void DrawFlag(SpriteBatch sb, Vector2 poleTop, Color color, float windiness, int seed)
    {
        var poleBottom = poleTop + new Vector2(0, 22);
        _p.Line(sb, poleBottom, poleTop, new Color(90, 70, 50), 1.6f);
        const int segments = 6;
        const float length = 14f, height = 7f;
        for (int i = 0; i < segments; i++)
        {
            float u = i / (float)segments;
            float wave = MathF.Sin(_time * 5f * windiness - u * 5f + seed) * 1.8f * u * windiness;
            var top = poleTop + new Vector2(u * length, wave);
            _p.Rect(sb, top, new Vector2(length / segments + 0.5f, height * (1f - u * 0.3f)), Color.Lerp(color, Color.Black, u * 0.25f));
        }
    }

    /// <summary>Recruits drilling outside a training building: two small figures stepping back and forth.</summary>
    private void DrawDrill(SpriteBatch sb, Building b, Vector2 pos, Vector2 size)
    {
        var ground = pos + new Vector2(size.X * 0.5f, size.Y + 6f);
        for (int i = 0; i < 2; i++)
        {
            float step = MathF.Sin(_time * 3f + i * MathF.PI) * 5f;
            var p = ground + new Vector2((i - 0.5f) * 14f + step, 0);
            _p.Ellipse(sb, p + new Vector2(0, 3), 3.5f, 1.4f, Color.Black * 0.25f);
            _p.Disc(sb, p - new Vector2(0, MathF.Abs(MathF.Sin(_time * 6f + i)) * 1.5f), 3f, Color.Lerp(Palette.Player(b.Owner), Color.White, 0.3f));
            if (b.Type == BuildingType.Archery) _p.Line(sb, p + new Vector2(3, -4), p + new Vector2(3, 3), new Color(120, 85, 45), 1.2f);
            else _p.Line(sb, p + new Vector2(3, 2), p + new Vector2(3, -9), new Color(120, 85, 45), 1.2f);
        }
    }

    private void DrawScaffolding(SpriteBatch sb, Vector2 pos, Vector2 size, float progress)
    {
        var wood = new Color(150, 110, 60) * 0.9f;
        float top = pos.Y + size.Y * (1f - 0.35f - progress * 0.6f);
        for (int i = 0; i <= 2; i++)
        {
            float x = pos.X + 4 + i * (size.X - 8) / 2;
            _p.Line(sb, new Vector2(x, pos.Y + size.Y - 2), new Vector2(x, top), wood, 2f);
        }
        for (float y = pos.Y + size.Y - 8; y > top; y -= 12)
            _p.Line(sb, new Vector2(pos.X + 3, y), new Vector2(pos.X + size.X - 3, y), wood, 1.6f);
        _p.Line(sb, new Vector2(pos.X + 4, pos.Y + size.Y - 2), new Vector2(pos.X + size.X - 4, top), wood * 0.7f, 1.2f);
    }

    /// <summary>What has piled up at a production building, drawn as a little heap beside the door.</summary>
    private void DrawOutputPile(SpriteBatch sb, GameState state, Building b, Vector2 pos, Vector2 size)
    {
        int total = 0;
        ResourceType kind = ResourceType.Wood;
        for (int i = 0; i < Resources.Count; i++)
        {
            if (b.OutputStock[i] <= total) continue;
            total = b.OutputStock[i];
            kind = (ResourceType)i;
        }
        if (total <= 0) return;
        var c = Palette.Resource(kind);
        var anchor = pos + new Vector2(size.X - 10, size.Y - 5);
        int shown = Math.Min(total, 9);
        for (int i = 0; i < shown; i++)
        {
            int layer = i < 4 ? 0 : i < 7 ? 1 : 2;
            int slot = i < 4 ? i : i < 7 ? i - 4 : i - 7;
            var p = anchor + new Vector2(-slot * 5f - layer * 2.5f, -layer * 4f);
            if (kind == ResourceType.Wood)
            {
                _p.Line(sb, p - new Vector2(0, 0), p + new Vector2(-4, 0), c, 3f);
                _p.Disc(sb, p, 1.6f, new Color(210, 170, 120));
            }
            else if (kind == ResourceType.Food)
            {
                _p.Disc(sb, p, 2.6f, new Color(215, 185, 90));
            }
            else
            {
                _p.Rect(sb, p - new Vector2(2.2f), new Vector2(4.4f), c);
            }
        }
    }
}
