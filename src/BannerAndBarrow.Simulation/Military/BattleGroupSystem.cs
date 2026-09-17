using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Military;

/// <summary>Several Regiments moving as one, arranged in ranks by Soldier type.</summary>
public sealed class BattleGroup
{
    public int Id;
    public int Owner;
    public readonly List<int> RegimentIds = new();
    public FixVec2 Destination;
    public FixVec2 Facing = FixVec2.North;
    public bool AttackMove;
    /// <summary>The melee type currently holding the front rank; the other melee type stands behind it.</summary>
    public SoldierType FrontType = SoldierType.Spearmen;
    public long NextThreatCheckTick;
}

/// <summary>
/// Lays out a Battle Group and keeps it sensible as enemies approach:
/// front rank of one melee type, second rank of the other, archers behind, longbowmen at the back,
/// Knights on the flanks. Men-at-Arms take the front against infantry; Spearmen take it against Knights.
/// </summary>
public static class BattleGroupSystem
{
    public static void OrderGroupMove(GameState state, IReadOnlyList<Regiment> regiments, FixVec2 target, FixVec2 facing, bool attackMove)
    {
        foreach (var r in regiments) Leave(state, r);
        if (regiments.Count == 1)
        {
            RegimentSystem.OrderMove(state, regiments[0], target, facing, attackMove);
            return;
        }

        var group = new BattleGroup
        {
            Id = state.NextId(),
            Owner = regiments[0].Owner,
            Destination = target,
            Facing = facing.IsZero ? FixVec2.North : facing.Normalized(),
            AttackMove = attackMove,
        };
        foreach (var r in regiments.OrderBy(r => r.Id))
        {
            group.RegimentIds.Add(r.Id);
            r.GroupId = group.Id;
        }
        group.FrontType = ChooseFront(state, group, NearestThreat(state, group, out _));
        state.BattleGroups[group.Id] = group;

        var slots = Layout(state, group);
        foreach (var r in regiments)
        {
            RegimentSystem.OrderMove(state, r, group.Destination + FixVec2.LocalToWorld(slots[r.Id], group.Facing), group.Facing, attackMove);
            r.GroupId = group.Id;
        }
    }

    public static void Leave(GameState state, Regiment r)
    {
        if (r.GroupId == 0) return;
        if (state.BattleGroups.TryGetValue(r.GroupId, out var group))
        {
            group.RegimentIds.Remove(r.Id);
            if (group.RegimentIds.Count <= 1)
            {
                foreach (var id in group.RegimentIds)
                    if (state.GetRegiment(id) is { } last) last.GroupId = 0;
                state.BattleGroups.Remove(group.Id);
            }
        }
        r.GroupId = 0;
    }

    public static void Update(GameState state)
    {
        var cfg = state.Config.Combat.BattleGroups;
        foreach (var group in state.BattleGroups.Values.ToList())
        {
            group.RegimentIds.RemoveAll(id => state.GetRegiment(id) == null);
            if (group.RegimentIds.Count <= 1)
            {
                foreach (var id in group.RegimentIds)
                    if (state.GetRegiment(id) is { } last) last.GroupId = 0;
                state.BattleGroups.Remove(group.Id);
                continue;
            }
            if (state.Tick < group.NextThreatCheckTick) continue;
            group.NextThreatCheckTick = state.Tick + state.Config.SecondsToTicks(cfg.ThreatCheckSeconds);

            var threat = NearestThreat(state, group, out var threatDistance);
            var front = ChooseFront(state, group, threat);
            if (front == group.FrontType)
            {
                ReturnStraysToSlots(state, group);
                continue;
            }
            // Too late to reshuffle once the enemy is already in the ranks.
            if (threat != null && threatDistance < cfg.MinSwapDistance) continue;

            group.FrontType = front;
            Reposition(state, group);
            state.AddEvent(group.Owner, GameEventKind.Info, $"{front} step forward to the front rank.", group.Destination);
        }
    }

    private static void ReturnStraysToSlots(GameState state, BattleGroup group)
    {
        var slots = Layout(state, group);
        foreach (var id in group.RegimentIds)
        {
            var r = state.GetRegiment(id);
            if (r == null || r.IsStationed || r.Order != RegimentOrder.Idle || r.EngagedRegimentId != 0 || state.Tick < r.KiteUntilTick) continue;
            var slot = group.Destination + FixVec2.LocalToWorld(slots[id], group.Facing);
            if (FixVec2.DistanceSquared(r.Anchor, slot) <= Fix.FromInt(36)) continue;
            RegimentSystem.SetDestination(state, r, slot, group.Facing);
            r.Order = group.AttackMove ? RegimentOrder.AttackMove : RegimentOrder.Move;
        }
    }

    /// <summary>Sends each Regiment that isn't locked in melee to its (possibly new) slot.</summary>
    private static void Reposition(GameState state, BattleGroup group)
    {
        var slots = Layout(state, group);
        foreach (var id in group.RegimentIds)
        {
            var r = state.GetRegiment(id);
            if (r == null || r.IsStationed) continue;
            if (r.Order is not (RegimentOrder.Idle or RegimentOrder.Move or RegimentOrder.AttackMove)) continue;
            if (r.EngagedRegimentId != 0 && state.Config.Soldier(r.Type).Ranged == null) continue;
            var order = r.Order;
            RegimentSystem.SetDestination(state, r, group.Destination + FixVec2.LocalToWorld(slots[id], group.Facing), group.Facing);
            r.Order = order == RegimentOrder.Idle ? (group.AttackMove ? RegimentOrder.AttackMove : RegimentOrder.Move) : order;
        }
    }

    public static FixVec2 Center(GameState state, BattleGroup group)
    {
        var sum = FixVec2.Zero;
        int n = 0;
        foreach (var id in group.RegimentIds)
        {
            if (state.GetRegiment(id) is not { } r) continue;
            sum += state.RegimentCentroid(r);
            n++;
        }
        return n == 0 ? group.Destination : sum / n;
    }

    private static Regiment? NearestThreat(GameState state, BattleGroup group, out Fix distance)
    {
        var center = Center(state, group);
        var radius = state.Config.Combat.BattleGroups.ThreatRadius;
        Regiment? best = null;
        distance = Fix.MaxValue;
        foreach (var other in state.Regiments.Values)
        {
            if (other.Owner == group.Owner || other.IsStationed || other.SoldierIds.Count == 0) continue;
            if (state.Config.Soldier(other.Type).Ranged != null) continue; // ranged enemies don't change who holds the line
            var d = FixVec2.Distance(state.RegimentCentroid(other), center);
            if (d <= radius && d < distance)
            {
                distance = d;
                best = other;
            }
        }
        return best;
    }

    private static SoldierType ChooseFront(GameState state, BattleGroup group, Regiment? threat)
    {
        bool hasSpears = group.RegimentIds.Any(id => state.GetRegiment(id) is { } r && IsSpear(r.Type));
        bool hasSwords = group.RegimentIds.Any(id => state.GetRegiment(id)?.Type == SoldierType.MenAtArms);
        if (!hasSpears) return SoldierType.MenAtArms;
        if (!hasSwords) return SoldierType.Spearmen;
        if (threat == null) return group.FrontType;
        return state.Config.Soldier(threat.Type).IsMounted ? SoldierType.Spearmen : SoldierType.MenAtArms;
    }

    /// <summary>Local slot (X = right, Y = forward) of every Regiment's banner relative to the group destination.</summary>
    public static Dictionary<int, FixVec2> Layout(GameState state, BattleGroup group)
    {
        var cfg = state.Config.Combat.BattleGroups;
        var regiments = group.RegimentIds.Select(state.GetRegiment).Where(r => r != null).Cast<Regiment>().ToList();
        bool spearsFront = group.FrontType == SoldierType.Spearmen;

        var rows = new List<List<Regiment>>
        {
            regiments.Where(r => spearsFront ? IsSpear(r.Type) : r.Type == SoldierType.MenAtArms).ToList(),
            regiments.Where(r => spearsFront ? r.Type == SoldierType.MenAtArms : IsSpear(r.Type)).ToList(),
            regiments.Where(r => r.Type == SoldierType.Archers).ToList(),
            regiments.Where(r => r.Type == SoldierType.Longbowmen).ToList(),
        };
        var flanks = regiments.Where(r => state.Config.Soldier(r.Type).IsMounted).ToList();
        rows.RemoveAll(row => row.Count == 0);

        var slots = new Dictionary<int, FixVec2>();
        Fix y = Fix.Zero;
        Fix frontWidth = Fix.Zero;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var widths = row.Select(r => Width(state, r)).ToList();
            var total = widths.Aggregate(Fix.Zero, (a, b) => a + b) + cfg.RegimentGap * (row.Count - 1);
            if (rowIndex == 0) frontWidth = total;
            var depth = row.Select(r => Depth(state, r)).Aggregate(Fix.Zero, Fix.Max);
            y -= depth / 2;
            var x = -total / 2;
            for (int i = 0; i < row.Count; i++)
            {
                slots[row[i].Id] = new FixVec2(x + widths[i] / 2, y);
                x += widths[i] + cfg.RegimentGap;
            }
            y -= depth / 2 + cfg.RankGap;
        }

        // Knights on alternating flanks of the front rank.
        Fix leftEdge = -frontWidth / 2, rightEdge = frontWidth / 2;
        var frontY = rows.Count > 0 ? slots[rows[0][0].Id].Y : Fix.Zero;
        for (int i = 0; i < flanks.Count; i++)
        {
            var w = Width(state, flanks[i]);
            if (i % 2 == 0)
            {
                slots[flanks[i].Id] = new FixVec2(rightEdge + cfg.RegimentGap + w / 2, frontY);
                rightEdge += cfg.RegimentGap + w;
            }
            else
            {
                slots[flanks[i].Id] = new FixVec2(leftEdge - cfg.RegimentGap - w / 2, frontY);
                leftEdge -= cfg.RegimentGap + w;
            }
        }

        // Centre the whole group on the destination (front-to-back).
        var mid = y / 2;
        foreach (var id in slots.Keys.ToList()) slots[id] = new FixVec2(slots[id].X, slots[id].Y - mid);
        return slots;
    }

    /// <summary>Spearmen and Pikemen share the anti-cavalry rank.</summary>
    public static bool IsSpear(SoldierType t) => t is SoldierType.Spearmen or SoldierType.Pikemen;

    private static Fix Width(GameState state, Regiment r)
    {
        var def = state.Config.Formation(r.Formation);
        int count = System.Math.Max(1, r.SoldierIds.Count);
        int perRank = def.Shape == FormationShape.Wedge ? 7 : System.Math.Clamp(def.Width > 0 ? def.Width : 4, 1, count);
        return def.Spacing * (perRank - 1) + Fix.One;
    }

    private static Fix Depth(GameState state, Regiment r) =>
        FormationLayout.Depth(state.Config.Formation(r.Formation), r.SoldierIds.Count) + Fix.One;
}
