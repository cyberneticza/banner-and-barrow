using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Pathfinding;

/// <summary>TUNING: steering and avoidance weights. Loaded from balance.json "movement.steering".</summary>
public sealed class SteeringSettings
{
    /// <summary>Max change of velocity per second, in tiles/s².</summary>
    public Fix Acceleration { get; set; } = Fix.FromInt(12);
    /// <summary>How hard agents push away from neighbours they overlap or nearly overlap.</summary>
    public Fix SeparationWeight { get; set; } = Fix.FromRatio(3, 2);
    /// <summary>Extra gap (tiles) beyond the sum of radii where separation starts.</summary>
    public Fix SeparationPadding { get; set; } = Fix.FromRatio(1, 5);
    /// <summary>Weight of predictive sidestepping when two agents are on a collision course.</summary>
    public Fix AvoidanceWeight { get; set; } = Fix.One;
    /// <summary>Seconds ahead to predict collisions.</summary>
    public Fix AvoidanceLookahead { get; set; } = Fix.One;
    /// <summary>Within this distance and with a clear line, agents seek the goal directly instead of the flow field.</summary>
    public Fix DirectSeekMaxDistance { get; set; } = Fix.FromInt(10);
    /// <summary>Civilians only cut straight to their goal this close; further out they follow the (road-preferring) flow field.</summary>
    public Fix CivilianDirectSeekMaxDistance { get; set; } = Fix.FromInt(2);
    /// <summary>Max positional correction per tick when resolving overlaps (tiles).</summary>
    public Fix MaxPushPerTick { get; set; } = Fix.FromRatio(1, 8);
    /// <summary>Ticks without progress before an agent is considered stuck and starts jittering.</summary>
    public int StuckTicks { get; set; } = 20;
    /// <summary>A stuck agent this close to a point goal simply accepts it as reached (crowded destination).</summary>
    public Fix StuckAcceptDistance { get; set; } = Fix.FromRatio(3, 2);
    /// <summary>How much more an idle (arrived) agent yields to a moving one during overlap resolution.</summary>
    public Fix IdleYieldFactor { get; set; } = Fix.FromInt(3);
    /// <summary>Civilians pass through each other; this scales their remaining separation push.</summary>
    public Fix CivilianSeparationFactor { get; set; } = Fix.FromRatio(1, 4);
}

/// <summary>
/// Moves every agent one tick: flow-field or direct goal seeking, separation, predictive avoidance,
/// overlap resolution and terrain push-out. Independent of game rules so it can be tested in isolation.
/// </summary>
public sealed class MovementSolver
{
    private readonly INavGrid _grid;
    private readonly FlowFieldCache _cache;
    private readonly List<int> _neighbours = new(64);
    private FixVec2[] _newVelocity = new FixVec2[256];
    private FixVec2[] _push = new FixVec2[256];

    public SteeringSettings Settings { get; }
    public SpatialHash Hash { get; }
    public FlowFieldCache Cache => _cache;

    public MovementSolver(INavGrid grid, FlowFieldCache cache, SteeringSettings settings)
    {
        _grid = grid;
        _cache = cache;
        Settings = settings;
        Hash = new SpatialHash(grid.Width, grid.Height, 2);
    }

    public void Step(IReadOnlyList<NavAgent> agents, Fix dt)
    {
        int count = agents.Count;
        if (_newVelocity.Length < count)
        {
            _newVelocity = new FixVec2[count * 2];
            _push = new FixVec2[count * 2];
        }

        Hash.Clear();
        for (int i = 0; i < count; i++)
        {
            var a = agents[i];
            a.PreviousPosition = a.Position;
            if (a.NavActive) Hash.Insert(i, a.Position);
        }

        for (int i = 0; i < count; i++)
        {
            var a = agents[i];
            _newVelocity[i] = a.NavActive ? ComputeVelocity(agents, i, dt) : FixVec2.Zero;
        }

        for (int i = 0; i < count; i++)
        {
            var a = agents[i];
            if (!a.NavActive) continue;
            a.Velocity = _newVelocity[i];
            a.Position += a.Velocity * dt;
        }

        ResolveOverlaps(agents);

        for (int i = 0; i < count; i++)
        {
            var a = agents[i];
            if (!a.NavActive) continue;
            ResolveTerrain(a);
            UpdateStuck(a, dt, i);
        }
    }

    private FixVec2 ComputeVelocity(IReadOnlyList<NavAgent> agents, int index, Fix dt)
    {
        var a = agents[index];
        var tile = a.Position.ToTile();
        var terrainMult = _grid.IsPassableSafe(tile.X, tile.Y, a.NavOwner)
            ? _grid.GetSpeedMultiplier(tile.X, tile.Y, a.MovementClass)
            : Fix.One;
        var maxSpeed = a.MaxSpeed * a.SpeedFactor * terrainMult;
        a.EffectiveSpeed = maxSpeed;

        var desired = FixVec2.Zero;
        if (a.GoalKind != NavGoalKind.None && a.Arrived)
        {
            // Pushed well away from a reached goal (e.g. shoved by a passing column): walk back.
            var reseek = Fix.Max(a.ArrivalRadius * 3, Settings.StuckAcceptDistance + Fix.Half);
            if (FixVec2.DistanceSquared(a.Position, a.GoalPoint) > reseek * reseek) a.Arrived = false;
        }
        if (a.GoalKind != NavGoalKind.None && !a.Arrived)
        {
            var toGoal = a.GoalPoint - a.Position;
            var distSq = toGoal.LengthSquared;
            if (distSq <= a.ArrivalRadius * a.ArrivalRadius)
            {
                a.Arrived = true;
            }
            else
            {
                var dist = Fix.Sqrt(distSq);
                FixVec2 dir;
                var directRange = a.MovementClass == MovementClass.Civilian ? Settings.CivilianDirectSeekMaxDistance : Settings.DirectSeekMaxDistance;
                bool direct = a.GoalKind == NavGoalKind.Point ||
                              (dist <= directRange && _grid.HasLineOfSight(a.Position, a.GoalPoint));
                if (direct)
                {
                    dir = toGoal / dist;
                }
                else
                {
                    var field = _cache.Get(a.FlowKey, a.FlowGoals);
                    dir = field.Sample(a.Position);
                    if (dir.IsZero) dir = toGoal / dist; // on a goal tile, or unreachable: head straight for it
                }
                // Slow down on approach so agents settle into slots instead of orbiting them.
                var speed = dist < Fix.One ? maxSpeed * Fix.Max(dist, Fix.FromRatio(3, 10)) : maxSpeed;
                desired = dir * speed;
            }
        }

        var steering = desired - a.Velocity;
        steering += Separation(agents, index, maxSpeed);
        if (!desired.IsZero) steering += Avoidance(agents, index, desired, maxSpeed);

        var newVel = a.Velocity + steering.Truncated(Settings.Acceleration * dt);
        if (desired.IsZero && a.Arrived)
        {
            // Idle agents bleed off speed quickly but still accept separation pushes.
            newVel = newVel * Fix.FromRatio(1, 2);
        }
        return newVel.Truncated(Fix.Max(maxSpeed, Fix.FromRatio(1, 2)));
    }

    private FixVec2 Separation(IReadOnlyList<NavAgent> agents, int index, Fix maxSpeed)
    {
        var a = agents[index];
        var reach = a.Radius * 2 + Settings.SeparationPadding + Fix.Half;
        _neighbours.Clear();
        Hash.Query(a.Position, reach, _neighbours);

        var force = FixVec2.Zero;
        foreach (var j in _neighbours)
        {
            if (j == index) continue;
            var b = agents[j];
            var range = a.Radius + b.Radius + Settings.SeparationPadding;
            var offset = a.Position - b.Position;
            var dSq = offset.LengthSquared;
            if (dSq >= range * range) continue;
            var d = Fix.Sqrt(dSq);
            FixVec2 away = d.Raw == 0 ? TieBreak(a.Id, b.Id) : offset / d;
            var strength = (range - d) / range;
            if (a.Arrived && !b.Arrived) strength *= Settings.IdleYieldFactor;
            if (PassThrough(a, b)) strength *= Settings.CivilianSeparationFactor;
            force += away * strength;
        }
        return force * Settings.SeparationWeight * Fix.Max(maxSpeed, Fix.One);
    }

    private FixVec2 Avoidance(IReadOnlyList<NavAgent> agents, int index, FixVec2 desired, Fix maxSpeed)
    {
        var a = agents[index];
        var lookaheadDist = maxSpeed * Settings.AvoidanceLookahead + a.Radius * 2;
        _neighbours.Clear();
        Hash.Query(a.Position, lookaheadDist, _neighbours);

        var forward = desired.Normalized();
        var sidestep = FixVec2.Zero;
        foreach (var j in _neighbours)
        {
            if (j == index) continue;
            var b = agents[j];
            if (PassThrough(a, b)) continue;
            var relPos = b.Position - a.Position;
            if (FixVec2.Dot(relPos, forward) <= Fix.Zero) continue; // only care about what's ahead
            var relVel = a.Velocity - b.Velocity;
            var relSpeedSq = relVel.LengthSquared;
            if (relSpeedSq.Raw < 64) continue;
            var t = FixVec2.Dot(relPos, relVel) / relSpeedSq;
            if (t <= Fix.Zero || t > Settings.AvoidanceLookahead) continue;
            var closest = relPos - relVel * t;
            var rsum = a.Radius + b.Radius;
            if (closest.LengthSquared >= rsum * rsum) continue;

            // Steer to the side the obstacle is not on. Exactly head-on: both keep right, which always separates.
            var side = FixVec2.Cross(forward, relPos);
            var right = forward.Right;
            var dir = side.Raw > 0 ? -right : side.Raw < 0 ? right : right;
            var urgency = Fix.One - t / Settings.AvoidanceLookahead;
            sidestep += dir * urgency;
        }
        return sidestep * Settings.AvoidanceWeight * maxSpeed;
    }

    private void ResolveOverlaps(IReadOnlyList<NavAgent> agents)
    {
        int count = agents.Count;
        for (int i = 0; i < count; i++) _push[i] = FixVec2.Zero;

        for (int i = 0; i < count; i++)
        {
            var a = agents[i];
            if (!a.NavActive) continue;
            _neighbours.Clear();
            Hash.Query(a.Position, a.Radius * 2 + Fix.One, _neighbours);
            foreach (var j in _neighbours)
            {
                if (j <= i) continue;
                var b = agents[j];
                if (PassThrough(a, b)) continue;
                var offset = a.Position - b.Position;
                var rsum = a.Radius + b.Radius;
                var dSq = offset.LengthSquared;
                if (dSq >= rsum * rsum) continue;
                var d = Fix.Sqrt(dSq);
                var normal = d.Raw == 0 ? TieBreak(a.Id, b.Id) : offset / d;
                var overlap = rsum - d;

                // Moving agents shove idle ones out of the way rather than the other way around.
                Fix wa = a.Arrived && !b.Arrived ? Settings.IdleYieldFactor : Fix.One;
                Fix wb = b.Arrived && !a.Arrived ? Settings.IdleYieldFactor : Fix.One;
                var total = wa + wb;
                _push[i] += normal * (overlap * wa / total);
                _push[j] -= normal * (overlap * wb / total);
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (!agents[i].NavActive || _push[i].IsZero) continue;
            agents[i].Position += _push[i].Truncated(Settings.MaxPushPerTick);
        }
    }

    private void ResolveTerrain(NavAgent a)
    {
        int tx = a.Position.X.FloorToInt(), ty = a.Position.Y.FloorToInt();
        if (!_grid.IsPassableSafe(tx, ty, a.NavOwner))
        {
            // Centre ended inside a wall: step back to the last valid position, or to the nearest open neighbour.
            var prevTile = a.PreviousPosition.ToTile();
            if (_grid.IsPassableSafe(prevTile.X, prevTile.Y, a.NavOwner))
            {
                a.Position = a.PreviousPosition;
            }
            else
            {
                var best = FindNearestOpenTile(tx, ty, a.NavOwner);
                if (best.HasValue) a.Position = best.Value.Center;
            }
            a.Velocity = FixVec2.Zero;
            return;
        }

        for (int oy = -1; oy <= 1; oy++)
        for (int ox = -1; ox <= 1; ox++)
        {
            if (ox == 0 && oy == 0) continue;
            int nx = tx + ox, ny = ty + oy;
            if (_grid.IsPassableSafe(nx, ny, a.NavOwner)) continue;
            // Closest point on the blocked tile's box to the agent's centre.
            var minX = Fix.FromInt(nx);
            var minY = Fix.FromInt(ny);
            var cx = Fix.Clamp(a.Position.X, minX, minX + Fix.One);
            var cy = Fix.Clamp(a.Position.Y, minY, minY + Fix.One);
            var offset = a.Position - new FixVec2(cx, cy);
            var dSq = offset.LengthSquared;
            if (dSq >= a.Radius * a.Radius || dSq.Raw == 0) continue;
            var d = Fix.Sqrt(dSq);
            a.Position += offset / d * (a.Radius - d);
        }
    }

    private TileCoord? FindNearestOpenTile(int tx, int ty, int player = -1)
    {
        for (int r = 1; r <= 4; r++)
        for (int oy = -r; oy <= r; oy++)
        for (int ox = -r; ox <= r; ox++)
        {
            if (System.Math.Max(System.Math.Abs(ox), System.Math.Abs(oy)) != r) continue;
            if (_grid.IsPassableSafe(tx + ox, ty + oy, player)) return new TileCoord(tx + ox, ty + oy);
        }
        return null;
    }

    private void UpdateStuck(NavAgent a, Fix dt, int index)
    {
        if (a.Arrived || a.GoalKind == NavGoalKind.None)
        {
            a.StuckTicks = 0;
            return;
        }
        var moved = FixVec2.DistanceSquared(a.Position, a.PreviousPosition);
        // Compare against the speed the agent can actually reach here (off-road civilians are 4x slower than their base speed).
        var expected = a.EffectiveSpeed * dt * Fix.FromRatio(1, 5);
        if (moved < expected * expected) a.StuckTicks++;
        else a.StuckTicks = System.Math.Max(0, a.StuckTicks - 2);

        if (a.StuckTicks < Settings.StuckTicks) return;

        if (a.HasReached(a.GoalPoint, Settings.StuckAcceptDistance))
        {
            a.Arrived = true;
            a.StuckTicks = 0;
            return;
        }
        // Deterministic sideways nudge to break symmetric jams in chokepoints.
        var dir = (a.GoalPoint - a.Position).Normalized();
        var side = ((a.Id + a.StuckTicks / Settings.StuckTicks) & 1) == 0 ? dir.Right : -dir.Right;
        a.Velocity += side * a.EffectiveSpeed * Fix.Half;
        if (a.StuckTicks > Settings.StuckTicks * 4) a.StuckTicks = Settings.StuckTicks / 2;
    }

    /// <summary>
    /// Civilians walk through each other (as in Settlers): two Workers meeting head-on in a one-tile gap between
    /// buildings would otherwise deadlock. They still separate lightly so crowds spread out.
    /// </summary>
    private static bool PassThrough(NavAgent a, NavAgent b) =>
        (a.MovementClass == MovementClass.Civilian && b.MovementClass == MovementClass.Civilian) ||
        (a.PassGroup != 0 && a.PassGroup == b.PassGroup && a.Squad != b.Squad);

    private static FixVec2 TieBreak(int idA, int idB) =>
        FlowField.Directions8[((idA * 7 + idB * 3) & 0x7fffffff) % 8];
}
