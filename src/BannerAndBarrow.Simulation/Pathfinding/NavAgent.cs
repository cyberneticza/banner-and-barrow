using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Pathfinding;

public enum NavGoalKind : byte
{
    None,
    /// <summary>Walk straight at a point (with obstacle push-out). Used for short hops such as formation slots.</summary>
    Point,
    /// <summary>Follow a shared flow field towards goal tiles, then arrive at a final point.</summary>
    Flow,
}

/// <summary>Anything that moves on the map. Soldiers and Workers derive from this.</summary>
public class NavAgent
{
    public int Id;
    public FixVec2 Position;
    public FixVec2 PreviousPosition;
    public FixVec2 Velocity;
    public Fix Radius = Fix.FromRatio(3, 10);
    public Fix MaxSpeed = Fix.FromInt(2);

    /// <summary>Top speed on the current tile after terrain, roads and <see cref="SpeedFactor"/>. Set by the solver each tick.</summary>
    public Fix EffectiveSpeed = Fix.FromInt(2);

    /// <summary>Extra multiplier applied on top of terrain (e.g. formation speed).</summary>
    public Fix SpeedFactor = Fix.One;

    public MovementClass MovementClass;
    /// <summary>Which Player this agent belongs to; Gates of that Player are open to it. -1 ignores Gates.</summary>
    public int NavOwner = -1;

    /// <summary>
    /// Agents with the same non-zero PassGroup but a different Squad walk through each other
    /// (e.g. ranks of one army swapping places). Soldiers: owner + 1 and their Regiment id.
    /// </summary>
    public int PassGroup;
    public int Squad;

    /// <summary>Inactive agents (e.g. Soldiers stationed inside a Stronghold) are skipped by the solver.</summary>
    public bool NavActive = true;

    public NavGoalKind GoalKind { get; private set; }
    public FixVec2 GoalPoint { get; private set; }
    public FlowGoalKey FlowKey { get; private set; }
    public TileCoord[] FlowGoals { get; private set; } = Array.Empty<TileCoord>();
    public bool Arrived { get; internal set; } = true;
    public int StuckTicks { get; internal set; }

    /// <summary>Tolerance for considering the goal reached. Crowded destinations need a bigger value.</summary>
    public Fix ArrivalRadius = Fix.FromRatio(1, 4);

    public void SetPointGoal(FixVec2 point)
    {
        if (GoalKind == NavGoalKind.Point && GoalPoint == point && Arrived) return;
        GoalKind = NavGoalKind.Point;
        GoalPoint = point;
        Arrived = false;
        StuckTicks = 0;
    }

    public void SetFlowGoal(FlowGoalKey key, TileCoord[] goals, FixVec2 finalPoint)
    {
        if (GoalKind == NavGoalKind.Flow && FlowKey == key && GoalPoint == finalPoint && Arrived) return;
        GoalKind = NavGoalKind.Flow;
        FlowKey = key;
        FlowGoals = goals;
        GoalPoint = finalPoint;
        Arrived = false;
        StuckTicks = 0;
    }

    public void ClearGoal()
    {
        GoalKind = NavGoalKind.None;
        Arrived = true;
        StuckTicks = 0;
    }

    public bool HasReached(FixVec2 point, Fix tolerance) => FixVec2.DistanceSquared(Position, point) <= tolerance * tolerance;
}
