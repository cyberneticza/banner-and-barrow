using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Game.Rendering;

/// <summary>
/// Combat visuals derived from simulation timing: weapon swings and thrusts on each melee blow, bows drawn and
/// loosed, arrows in flight, torches thrown at buildings, and buildings that catch fire as <see cref="Building.Fire"/> grows.
/// </summary>
public sealed partial class WorldRenderer
{
    /// <summary>Latest arrow launch tick per Soldier, gathered from live projectiles each frame.</summary>
    private readonly Dictionary<int, long> _lastShot = new();

    private void BeginCombatFrame(GameState state)
    {
        foreach (var p in state.Projectiles)
        {
            if (p.SourceSoldierId == 0) continue;
            if (!_lastShot.TryGetValue(p.SourceSoldierId, out var t) || p.LaunchTick > t) _lastShot[p.SourceSoldierId] = p.LaunchTick;
        }
        if (_lastShot.Count > 2048)
        {
            long cutoff = state.Tick - state.Config.Simulation.TicksPerSecond * 10;
            foreach (var id in _lastShot.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList()) _lastShot.Remove(id);
        }
    }

    private enum CombatAction : byte
    {
        None,
        Melee,
        Shoot,
        Torch,
    }

    /// <summary>What a Soldier is visibly doing, toward which point, and how far through the attack it is (seconds since the blow).</summary>
    private (CombatAction Action, Vector2 Toward, float SinceStrike, float Cooldown) CombatStateOf(GameState state, Soldier s, Regiment? regiment, Simulation.Config.SoldierDef def)
    {
        int tps = state.Config.Simulation.TicksPerSecond;
        float now = _time;
        var here = s.Position;

        // Closest enemy in melee reach: the movement target, else the nearest Soldier of the Regiment we're fighting.
        Soldier? foe = state.GetSoldier(s.TargetSoldierId);
        if ((foe == null || foe.Owner == s.Owner) && regiment != null && state.GetRegiment(regiment.EngagedRegimentId) is { } enemy)
        {
            Fix best = Fix.FromInt(3);
            foreach (var id in enemy.SoldierIds)
            {
                if (state.GetSoldier(id) is not { } e) continue;
                var d = FixVec2.Distance(e.Position, here);
                if (d < best)
                {
                    best = d;
                    foe = e;
                }
            }
        }
        var melee = def.Melee;
        if (foe != null && foe.Owner != s.Owner)
        {
            var d = FixVec2.Distance(foe.Position, here).ToFloat();
            if (d <= melee.Range.ToFloat() + 1.2f)
            {
                float cd = melee.CooldownSeconds.ToFloat();
                float since = now - (s.NextAttackTick / (float)tps - cd);
                return (CombatAction.Melee, foe.Position.ToPixels(), since, cd);
            }
        }

        if (def.Ranged != null && _lastShot.TryGetValue(s.Id, out var shot))
        {
            float since = now - shot / (float)tps;
            float cd = Math.Max(0.3f, (s.NextAttackTick - shot) / (float)tps);
            if (since < cd + 1.5f)
            {
                var target = regiment == null ? here : regiment.Anchor + regiment.Facing * Fix.FromInt(8);
                if (state.GetRegiment(regiment?.EngagedRegimentId ?? 0) is { } shotAt) target = state.RegimentCentroid(shotAt);
                else if (state.GetBuilding(regiment?.EngagedBuildingId ?? 0) is { } siegeTarget) target = siegeTarget.Center;
                return (CombatAction.Shoot, target.ToPixels(), since, cd);
            }
        }

        if (def.Ranged == null && regiment != null && state.GetBuilding(regiment.EngagedBuildingId) is { } building &&
            building.DistanceToFootprint(here).ToFloat() <= melee.Range.ToFloat() + 1.2f)
        {
            float cd = melee.CooldownSeconds.ToFloat();
            float since = now - (s.NextAttackTick / (float)tps - cd);
            return (CombatAction.Torch, NearestFootprintPoint(building, s.Position.ToPixels()), since, cd);
        }
        return (CombatAction.None, Vector2.Zero, 0f, 1f);
    }

    private static Vector2 NearestFootprintPoint(Building b, Vector2 from)
    {
        float x0 = b.Origin.X * T + 6, y0 = b.Origin.Y * T + 6, x1 = (b.Origin.X + b.Size) * T - 6, y1 = (b.Origin.Y + b.Size) * T - 6;
        return new Vector2(Math.Clamp(from.X, x0, x1), Math.Clamp(from.Y, y0, y1));
    }

    /// <summary>Body offset for the blow itself: a lunge in on the strike, eased back out.</summary>
    private static Vector2 StrikeLunge(CombatAction action, Vector2 body, Vector2 toward, float since, float r)
    {
        if (action is not (CombatAction.Melee or CombatAction.Torch) || since < 0 || since > 0.35f) return Vector2.Zero;
        var dir = toward - body;
        if (dir.LengthSquared() < 0.01f) return Vector2.Zero;
        dir.Normalize();
        return dir * MathF.Sin(since / 0.35f * MathF.PI) * r * (action == CombatAction.Torch ? 0.5f : 0.9f);
    }

    private void DrawCombatAction(SpriteBatch sb, GameState state, Soldier s, (CombatAction Action, Vector2 Toward, float SinceStrike, float Cooldown) combat,
        Vector2 body, float r)
    {
        var dir = combat.Toward - body;
        if (dir.LengthSquared() < 0.01f) dir = new Vector2(1, 0);
        dir.Normalize();
        var perp = new Vector2(-dir.Y, dir.X);
        switch (combat.Action)
        {
            case CombatAction.Melee:
                DrawMeleeBlow(sb, s, body, dir, perp, combat.SinceStrike, combat.Cooldown, r);
                break;
            case CombatAction.Shoot:
                DrawBow(sb, s, body, dir, perp, combat.SinceStrike, combat.Cooldown, r);
                break;
            case CombatAction.Torch:
                DrawTorchThrow(sb, s, body, combat.Toward, dir, combat.SinceStrike, combat.Cooldown, r);
                break;
        }
    }

    // ------------------------------------------------------------------ melee

    private void DrawMeleeBlow(SpriteBatch sb, Soldier s, Vector2 body, Vector2 dir, Vector2 perp, float since, float cooldown, float r)
    {
        var steel = new Color(225, 230, 240);
        var shaft = new Color(120, 85, 45);
        float p = cooldown <= 0 ? 0 : Frac(Math.Max(0f, since) / cooldown);

        if (s.Type is SoldierType.Spearmen or SoldierType.Pikemen)
        {
            // Thrust: drawn back, then driven forward fast on the blow.
            float reach = s.Type == SoldierType.Pikemen ? 22f : 15f;
            float extend = p < 0.18f ? MathF.Sin(p / 0.18f * MathF.PI / 2) : p < 0.45f ? 1f - (p - 0.18f) / 0.27f : -0.25f * MathF.Sin((p - 0.45f) / 0.55f * MathF.PI);
            var grip = body + perp * r * 0.5f;
            var tip = grip + dir * (r + reach * (0.55f + 0.45f * extend));
            _p.Line(sb, grip - dir * r * 0.8f, tip, shaft, 2.2f);
            _p.Line(sb, tip - dir * 5f, tip, steel, 3f);
            if (p < 0.12f) DrawHitSpark(sb, tip, s.Id, p / 0.12f);
            return;
        }

        // Sword (or knight's blade): raised over the shoulder, cut down across the enemy, recover.
        float length = s.Type == SoldierType.Knights ? r * 2.6f : s.Type is SoldierType.Archers or SoldierType.Longbowmen ? r * 1.7f : r * 2.1f;
        float raised = -2.1f, cut = 0.9f;
        float angle = p < 0.16f ? MathHelper.Lerp(raised, cut, EaseOut(p / 0.16f))
                    : p < 0.55f ? MathHelper.Lerp(cut, cut - 0.3f, (p - 0.16f) / 0.39f)
                    : MathHelper.Lerp(cut - 0.3f, raised, EaseInOut((p - 0.55f) / 0.45f));
        var hand = body + perp * r * 0.55f;
        var blade = Rotate(dir, angle);
        var tipPoint = hand + blade * length;
        if (p < 0.16f)
        {
            // Motion smear behind the cut.
            for (int i = 1; i <= 3; i++)
            {
                var ghost = Rotate(dir, angle - i * 0.35f);
                _p.Line(sb, hand + ghost * length * 0.4f, hand + ghost * length, Color.White * (0.25f / i), 2.5f);
            }
        }
        _p.Line(sb, hand, hand + blade * 3f, new Color(90, 60, 30), 3f);
        _p.Line(sb, hand + blade * 3f, tipPoint, steel, 2.2f);
        _p.Line(sb, hand + blade * 3f - Rotate(blade, MathF.PI / 2) * 2.5f, hand + blade * 3f + Rotate(blade, MathF.PI / 2) * 2.5f, new Color(180, 150, 60), 1.6f);
        if (p is > 0.1f and < 0.24f) DrawHitSpark(sb, tipPoint, s.Id, (p - 0.1f) / 0.14f);
    }

    private void DrawHitSpark(SpriteBatch sb, Vector2 at, int seed, float f)
    {
        for (int i = 0; i < 5; i++)
        {
            float a = Hash01(seed, i + 31) * MathF.Tau;
            var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
            _p.Line(sb, at + d * (1f + f * 3f), at + d * (3f + f * 7f), Color.Lerp(Color.White, new Color(255, 200, 80), f) * (1f - f), 1.3f);
        }
    }

    // ------------------------------------------------------------------ bows

    private void DrawBow(SpriteBatch sb, Soldier s, Vector2 body, Vector2 dir, Vector2 perp, float since, float cooldown, float r)
    {
        bool longbow = s.Type == SoldierType.Longbowmen;
        float half = longbow ? r * 1.15f : r * 0.85f;
        var wood = longbow ? new Color(95, 60, 30) : new Color(140, 95, 50);

        // Released at since=0: string snaps forward and quivers; then nock and draw steadily until the next shot.
        float draw;
        float quiver = 0f;
        if (since < 0.18f)
        {
            draw = 0f;
            quiver = MathF.Sin(since * 90f) * (1f - since / 0.18f) * 2.5f;
        }
        else
        {
            float recover = Math.Max(0.2f, cooldown - 0.18f);
            draw = Math.Clamp((since - 0.18f) / recover, 0f, 1f);
            draw = EaseOut(draw);
        }

        // Aim a little upward for the arc; longbows loft higher.
        var aim = Vector2.Normalize(dir + new Vector2(0, longbow ? -0.35f : -0.2f));
        var aimPerp = new Vector2(-aim.Y, aim.X);
        var grip = body + aim * r * 0.9f;
        float bend = half * (0.35f + draw * 0.25f);
        var tipA = grip + aimPerp * half - aim * bend;
        var tipB = grip - aimPerp * half - aim * bend;
        // Bow limbs as a curve through the grip.
        var midA = grip + aimPerp * half * 0.55f - aim * bend * 0.3f;
        var midB = grip - aimPerp * half * 0.55f - aim * bend * 0.3f;
        _p.Line(sb, tipA, midA, wood, 1.6f);
        _p.Line(sb, midA, grip, wood, 2f);
        _p.Line(sb, grip, midB, wood, 2f);
        _p.Line(sb, midB, tipB, wood, 1.6f);

        var nock = grip - aim * (bend + draw * half * 0.9f) + aimPerp * quiver;
        var stringColor = new Color(235, 230, 210) * 0.75f;
        _p.Line(sb, tipA, nock, stringColor, 1f);
        _p.Line(sb, nock, tipB, stringColor, 1f);

        if (since >= 0.18f)
        {
            // Arrow on the string, head past the grip.
            var head = nock + aim * (longbow ? r * 2.2f : r * 1.7f);
            _p.Line(sb, nock, head, new Color(150, 110, 60), 1.4f);
            _p.Line(sb, head - aim * 3f, head, new Color(210, 215, 225), 2f);
            _p.Line(sb, nock, nock + aim * 2.5f + aimPerp * 1.2f, new Color(200, 60, 50), 1f);
            _p.Line(sb, nock, nock + aim * 2.5f - aimPerp * 1.2f, new Color(200, 60, 50), 1f);
        }
        else
        {
            // The release: a puff of air and a flash along the shot line.
            float f = since / 0.18f;
            _p.Line(sb, grip + aim * 2f, grip + aim * (6f + f * 14f), Color.White * (0.5f * (1f - f)), longbow ? 2.2f : 1.5f);
            _p.Disc(sb, grip, 2f + f * 4f, Color.White * (0.2f * (1f - f)));
        }
    }

    // ------------------------------------------------------------------ torches

    private void DrawTorchThrow(SpriteBatch sb, Soldier s, Vector2 body, Vector2 target, Vector2 dir, float since, float cooldown, float r)
    {
        float p = cooldown <= 0 ? 0 : Frac(Math.Max(0f, since) / cooldown);
        const float flight = 0.4f;
        float flightFraction = flight / Math.Max(0.5f, cooldown);

        if (p < flightFraction)
        {
            // Torch tumbling through the air in an arc.
            float t = p / flightFraction;
            var hand = body + new Vector2(0, -r);
            var ground = Vector2.Lerp(hand, target, t);
            var pos = ground - new Vector2(0, 4 * t * (1 - t) * 18f);
            float spin = t * 9f + Hash01(s.Id) * 6f;
            var stick = new Vector2(MathF.Cos(spin), MathF.Sin(spin));
            _p.Line(sb, pos - stick * 4f, pos + stick * 4f, new Color(90, 55, 25), 2f);
            DrawFlame(sb, pos + stick * 4f, 4f, s.Id, 1f);
            _p.Ellipse(sb, ground + new Vector2(0, 2), 2.5f, 1f, Color.Black * 0.25f);
        }
        else
        {
            // Landed: a burst of flame at the wall; the next torch is lit in hand.
            float since2 = (p - flightFraction) * cooldown;
            if (since2 < 0.35f)
            {
                float f = since2 / 0.35f;
                for (int i = 0; i < 6; i++)
                {
                    float a = -MathF.PI / 2 + (Hash01(s.Id, i) - 0.5f) * 2.6f;
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    _p.Rect(sb, target + d * (2f + f * 10f), new Vector2(1.8f), Color.Lerp(new Color(255, 230, 120), new Color(255, 90, 20), f) * (1f - f));
                }
                DrawFlame(sb, target, 6f * (1f - f * 0.5f), s.Id, 1f - f * 0.6f);
            }
            var side = new Vector2(-dir.Y, dir.X);
            var held = body + side * r * 0.6f + new Vector2(0, -r * 0.4f);
            float lift = p > 0.8f ? (p - 0.8f) / 0.2f : 0f;
            var top = held + new Vector2(0, -r * 1.1f) - dir * lift * r;
            _p.Line(sb, held, top, new Color(90, 55, 25), 2f);
            DrawFlame(sb, top, 3.5f, s.Id + 7, 1f);
        }
    }

    /// <summary>A flickering flame: red base, orange body, yellow tip.</summary>
    private void DrawFlame(SpriteBatch sb, Vector2 at, float size, int seed, float strength)
    {
        float flick = 0.8f + 0.2f * MathF.Sin(_time * 17f + seed * 1.3f) * MathF.Sin(_time * 11.3f + seed);
        float s = size * flick;
        _p.Disc(sb, at + new Vector2(0, -s * 0.2f), s * 1.6f, new Color(255, 120, 20) * (0.18f * strength));
        _p.Disc(sb, at, s, new Color(220, 50, 20) * (0.9f * strength));
        _p.Disc(sb, at + new Vector2(MathF.Sin(_time * 9f + seed) * s * 0.15f, -s * 0.55f), s * 0.75f, new Color(255, 140, 30) * strength);
        _p.Disc(sb, at + new Vector2(MathF.Sin(_time * 13f + seed) * s * 0.2f, -s * 1.05f), s * 0.45f, new Color(255, 225, 90) * strength);
    }

    // ------------------------------------------------------------------ burning buildings

    /// <summary>
    /// Fire grows with <see cref="Building.Fire"/> (and territory Burning): scorch marks, then flames that spread across
    /// the roof, thick smoke, embers and a glow, with the building charring as it burns.
    /// </summary>
    private void DrawBuildingFire(SpriteBatch sb, GameState state, Building b, Vector2 pos, Vector2 size)
    {
        float fire = b.Fire.ToFloat();
        if (b.IsBurning)
        {
            var total = state.Config.SecondsToTicks(state.Config.Territory.RebuildWindowSeconds);
            float elapsed = 1f - Math.Clamp((b.BurningDeadlineTick - state.Tick) / (float)Math.Max(1, total), 0f, 1f);
            fire = Math.Max(fire, 0.25f + elapsed * 0.75f);
        }
        float damage = 1f - Math.Clamp(b.Hp.ToFloat() / Math.Max(1f, b.MaxHp.ToFloat()), 0f, 1f);
        if (fire <= 0.001f && damage < 0.05f) return;

        var dest = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
        // Charring and damage darken the building.
        float char_ = Math.Clamp(damage * 0.35f + fire * 0.4f, 0f, 0.65f);
        if (char_ > 0.01f) _p.Rect(sb, dest, new Color(25, 15, 10) * char_);
        // Scorch marks.
        int scorches = (int)(damage * 6);
        for (int i = 0; i < scorches; i++)
        {
            var at = pos + new Vector2(Hash01(b.Id, 100 + i) * size.X, (0.3f + Hash01(b.Id, 200 + i) * 0.65f) * size.Y);
            _p.Ellipse(sb, at, 4f + Hash01(b.Id, 300 + i) * 5f, 2.5f + Hash01(b.Id, 400 + i) * 2f, Color.Black * 0.35f);
        }
        if (fire <= 0.001f) return;

        // Glow on the ground around the building.
        float pulse = 0.85f + 0.15f * MathF.Sin(_time * 6f + b.Id);
        _p.Disc(sb, pos + size / 2, size.X * (0.45f + fire * 0.35f), new Color(255, 110, 20) * (0.12f * fire * pulse));

        int flames = 1 + (int)(fire * 9f);
        for (int i = 0; i < flames; i++)
        {
            // Fires start at one spot and spread outward as they grow.
            float spread = Math.Min(1f, 0.25f + fire);
            var local = new Vector2(0.5f + (Hash01(b.Id, i) - 0.5f) * spread, 0.35f + Hash01(b.Id, i + 50) * 0.55f * spread);
            var at = pos + local * size;
            float flameSize = (3f + fire * 9f) * (0.6f + Hash01(b.Id, i + 90) * 0.6f);
            DrawFlame(sb, at, flameSize, b.Id * 7 + i, Math.Min(1f, 0.5f + fire));

            // Smoke rising from each flame.
            int puffs = fire > 0.3f ? 3 : 2;
            for (int k = 0; k < puffs; k++)
            {
                float f = Frac(_time * (0.35f + fire * 0.2f) + k / (float)puffs + Hash01(b.Id, i + k * 13));
                var drift = new Vector2(MathF.Sin(_time * 0.9f + i + k) * 5f + f * 10f, -flameSize - f * (30f + fire * 40f));
                var gray = Color.Lerp(new Color(60, 55, 50), new Color(120, 115, 110), f);
                _p.Disc(sb, at + drift, (3f + f * (8f + fire * 10f)), gray * (0.45f * (1f - f) * Math.Min(1f, fire * 2f)));
            }
        }

        // Embers drifting up.
        int embers = (int)(fire * 12);
        for (int i = 0; i < embers; i++)
        {
            float f = Frac(_time * 0.7f + Hash01(b.Id, i + 500));
            var at = pos + new Vector2(Hash01(b.Id, i + 600) * size.X + MathF.Sin(_time * 3f + i) * 4f, size.Y * (0.6f - f * 0.9f));
            _p.Rect(sb, at, new Vector2(1.6f), new Color(255, 180, 60) * (1f - f));
        }
    }

    // ------------------------------------------------------------------ arrows

    private void DrawArrow(SpriteBatch sb, GameState state, Projectile proj, float alpha)
    {
        float duration = Math.Max(1, proj.ImpactTick - proj.LaunchTick);
        float t = Math.Clamp((state.Tick + alpha - proj.LaunchTick) / duration, 0f, 1f);
        var a = proj.Origin.ToPixels();
        var b = proj.Target.ToPixels();
        float dist = Vector2.Distance(a, b);
        float arc = proj.IsLongbow ? 0.26f : 0.17f;
        Vector2 At(float u) => Vector2.Lerp(a, b, u) - new Vector2(0, 4 * u * (1 - u) * dist * arc);
        var ground = Vector2.Lerp(a, b, t);
        var pos = At(t);
        var next = At(Math.Min(1f, t + 0.03f));
        var dir = next - pos;
        if (dir.LengthSquared() < 0.0001f) dir = b - a;
        if (dir.LengthSquared() < 0.0001f) dir = new Vector2(1, 0);
        dir.Normalize();
        var perp = new Vector2(-dir.Y, dir.X);

        // Shadow on the ground, shrinking with height.
        float height = Vector2.Distance(pos, ground);
        _p.Ellipse(sb, ground, Math.Max(1.5f, 5f - height * 0.05f), 1.2f, Color.Black * Math.Max(0.08f, 0.3f - height * 0.004f));

        // Streak along the path just travelled.
        const int trail = 4;
        for (int i = 1; i <= trail; i++)
        {
            float u0 = Math.Max(0f, t - i * 0.025f), u1 = Math.Max(0f, t - (i - 1) * 0.025f);
            _p.Line(sb, At(u0), At(u1), (proj.IsLongbow ? Color.White : new Color(255, 245, 220)) * (0.22f / i), proj.IsLongbow ? 2f : 1.4f);
        }

        float len = proj.IsLongbow ? 15f : 11f;
        var tail = pos - dir * len * 0.5f;
        var tip = pos + dir * len * 0.5f;
        _p.Line(sb, tail, tip, proj.IsLongbow ? new Color(80, 50, 25) : new Color(150, 105, 55), proj.IsLongbow ? 1.8f : 1.5f);
        _p.Line(sb, tip - dir * 3f, tip + dir * 0.5f, new Color(215, 220, 230), 2.4f);
        var fletch = proj.IsLongbow ? new Color(250, 250, 250) : new Color(200, 60, 50);
        _p.Line(sb, tail, tail + dir * 3.5f + perp * 2f, fletch, 1.2f);
        _p.Line(sb, tail, tail + dir * 3.5f - perp * 2f, fletch, 1.2f);

        if (t > 0.92f)
        {
            float f = (t - 0.92f) / 0.08f;
            _p.Disc(sb, b, 2f + f * 4f, new Color(180, 160, 120) * (0.35f * (1f - f)));
        }
    }

    // ------------------------------------------------------------------ rally points

    private void DrawRallyPoint(SpriteBatch sb, Building b, bool selected, float zoom)
    {
        if (b.RallyPoint is not { } rally) return;
        var at = rally.ToPixels();
        var color = Palette.Player(b.Owner);
        float scale = Math.Clamp(1f / zoom, 1f, 2.5f);
        if (selected)
        {
            // Dashed path from the door to the marker.
            var from = b.Center.ToPixels();
            var d = at - from;
            float length = d.Length();
            if (length > 1f)
            {
                d /= length;
                for (float u = 0; u < length; u += 14f * scale)
                    _p.Line(sb, from + d * u, from + d * Math.Min(length, u + 8f * scale), color * 0.8f, 2f * scale);
            }
        }
        _p.Ellipse(sb, at, 7f * scale, 3f * scale, Color.Black * 0.3f);
        _p.Circle_(sb, at, 6f * scale, color);
        var top = at + new Vector2(0, -22f * scale);
        _p.Line(sb, at, top, new Color(90, 65, 40), 2f * scale);
        for (int i = 0; i < 5; i++)
        {
            float u = i / 5f;
            float wave = MathF.Sin(_time * 5f - u * 5f) * 1.5f * u * scale;
            _p.Rect(sb, top + new Vector2(u * 12f * scale, wave), new Vector2(12f * scale / 5 + 0.5f, 7f * scale * (1f - u * 0.3f)), color);
        }
    }

    // ------------------------------------------------------------------ math

    private static Vector2 Rotate(Vector2 v, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
    private static float EaseInOut(float t) => t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;
}
