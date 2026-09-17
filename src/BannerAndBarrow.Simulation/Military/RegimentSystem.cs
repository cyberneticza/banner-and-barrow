using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;

namespace BannerAndBarrow.Simulation.Military;

/// <summary>
/// Regiment orders, engagement and group movement. The Regiment's banner (Anchor) follows a shared flow field;
/// each Soldier steers to its formation slot around the banner, falling back to the flow field when far away.
/// </summary>
public static class RegimentSystem
{
    private static readonly Fix AnchorArriveDistance = Fix.FromRatio(3, 20);
    private static readonly Fix StationDistance = Fix.FromRatio(7, 2);
    private static readonly Fix s_holdReach = Fix.One;

    // ------------------------------------------------------------------ orders

    public static void OrderMove(GameState state, Regiment r, FixVec2 destination, FixVec2 facing, bool attackMove)
    {
        LeaveFortIfStationed(state, r);
        ClearTargets(r);
        r.Order = attackMove ? RegimentOrder.AttackMove : RegimentOrder.Move;
        SetDestination(state, r, destination, facing);
    }

    public static void OrderAttackRegiment(GameState state, Regiment r, Regiment target)
    {
        LeaveFortIfStationed(state, r);
        ClearTargets(r);
        r.Order = RegimentOrder.AttackRegiment;
        r.TargetRegimentId = target.Id;
        r.EngagedRegimentId = target.Id;
        var c = state.RegimentCentroid(target);
        SetDestination(state, r, c, (c - state.RegimentCentroid(r)).Normalized());
    }

    public static void OrderAttackBuilding(GameState state, Regiment r, Building target)
    {
        LeaveFortIfStationed(state, r);
        ClearTargets(r);
        r.Order = RegimentOrder.AttackBuilding;
        r.TargetBuildingId = target.Id;
        r.EngagedBuildingId = target.Id;
        var access = target.NearestAccessTile(state.Map, state.RegimentCentroid(r)).Center;
        SetDestination(state, r, access, (target.Center - access).Normalized());
    }

    public static void OrderStation(GameState state, Regiment r, Building fort)
    {
        LeaveFortIfStationed(state, r);
        ClearTargets(r);
        r.Order = RegimentOrder.Station;
        r.TargetBuildingId = fort.Id;
        var access = fort.NearestAccessTile(state.Map, state.RegimentCentroid(r)).Center;
        SetDestination(state, r, access, (fort.Center - access).Normalized());
    }

    /// <summary>"Archers behind infantry": keep station behind <paramref name="leader"/>.</summary>
    public static void OrderFollow(GameState state, Regiment follower, Regiment leader)
    {
        LeaveFortIfStationed(state, follower);
        ClearTargets(follower);
        follower.Order = RegimentOrder.Follow;
        follower.FollowRegimentId = leader.Id;
        if (follower.Formation != leader.Formation && state.Config.Soldier(follower.Type).Formations.Contains(FormationType.Line))
            follower.Formation = FormationType.Line;
        UpdateFollowDestination(state, follower, leader, force: true);
    }

    public static void OrderStop(GameState state, Regiment r)
    {
        if (r.IsStationed) return;
        ClearTargets(r);
        r.Order = RegimentOrder.Idle;
        SetDestination(state, r, r.Anchor, r.Facing);
    }

    /// <summary>
    /// Pours same-type Regiments into each other, largest first, up to the type's size cap.
    /// Returns how many Regiments were absorbed.
    /// </summary>
    public static int MergeAll(GameState state, List<Regiment> sameType)
    {
        if (sameType.Count < 2) return 0;
        int max = state.Config.Soldier(sameType[0].Type).MaxRegimentSize;
        var queue = sameType.OrderByDescending(r => r.SoldierIds.Count).ThenBy(r => r.Id).ToList();
        int absorbed = 0;
        for (int t = 0; t < queue.Count; t++)
        {
            var target = queue[t];
            if (!state.Regiments.ContainsKey(target.Id) || target.SoldierIds.Count >= max) continue;
            for (int d = queue.Count - 1; d > t && target.SoldierIds.Count < max; d--)
            {
                var donor = queue[d];
                if (!state.Regiments.ContainsKey(donor.Id)) continue;
                var cohesionWeight = target.Cohesion * target.SoldierIds.Count;
                int moved = 0;
                while (donor.SoldierIds.Count > 0 && target.SoldierIds.Count < max)
                {
                    var id = donor.SoldierIds[^1];
                    donor.SoldierIds.RemoveAt(donor.SoldierIds.Count - 1);
                    target.SoldierIds.Add(id);
                    if (state.GetSoldier(id) is { } s)
                    {
                        s.RegimentId = target.Id;
                        s.Squad = target.Id;
                    }
                    moved++;
                }
                if (moved > 0)
                {
                    target.Cohesion = (cohesionWeight + donor.Cohesion * moved) / target.SoldierIds.Count;
                    target.StartingSize = System.Math.Max(target.StartingSize, target.SoldierIds.Count);
                }
                if (donor.SoldierIds.Count == 0)
                {
                    EntityOps.RemoveRegiment(state, donor);
                    absorbed++;
                }
            }
        }
        return absorbed;
    }

    public static bool SetFormation(GameState state, Regiment r, FormationType formation)
    {
        if (!state.Config.Soldier(r.Type).Formations.Contains(formation)) return false;
        r.Formation = formation;
        return true;
    }

    private static void ClearTargets(Regiment r)
    {
        r.TargetRegimentId = 0;
        r.TargetBuildingId = 0;
        r.EngagedRegimentId = 0;
        r.EngagedBuildingId = 0;
        r.FollowRegimentId = 0;
    }

    private static void LeaveFortIfStationed(GameState state, Regiment r)
    {
        if (!r.IsStationed) return;
        var fort = state.GetBuilding(r.StationedInBuildingId);
        if (fort != null) FortSystem.Eject(state, fort, r);
        else r.StationedInBuildingId = 0;
    }

    public static void SetDestination(GameState state, Regiment r, FixVec2 destination, FixVec2 facing)
    {
        destination = ClampToPassable(state, destination);
        r.Destination = destination;
        r.DestinationFacing = facing.IsZero ? r.Facing : facing.Normalized();
        r.HoldPoint = destination;
        r.AnchorArrived = false;
        RekeyDestination(state, r);
    }

    private static void RekeyDestination(GameState state, Regiment r)
    {
        var tile = r.Destination.ToTile();
        r.DestinationGoals = new[] { tile };
        r.DestinationKey = FlowGoalKey.ForTile(tile, state.Map.Width, MovementClass.Military, r.Owner);
    }

    /// <summary>Moves the goal without resetting the hold point; only rebuilds the flow key when the tile moved noticeably.</summary>
    private static void TrackGoal(GameState state, Regiment r, FixVec2 goal)
    {
        goal = ClampToPassable(state, goal);
        r.Destination = goal;
        r.AnchorArrived = false;
        var tile = goal.ToTile();
        if (r.DestinationGoals.Length == 0 || TileCoord.ChebyshevDistance(r.DestinationGoals[0], tile) > 2) RekeyDestination(state, r);
    }

    public static FixVec2 ClampToPassable(GameState state, FixVec2 p)
    {
        var map = state.Map;
        var x = Fix.Clamp(p.X, Fix.Half, Fix.FromInt(map.Width) - Fix.Half);
        var y = Fix.Clamp(p.Y, Fix.Half, Fix.FromInt(map.Height) - Fix.Half);
        p = new FixVec2(x, y);
        var t = p.ToTile();
        if (map.IsPassable(t.X, t.Y)) return p;
        for (int r = 1; r <= 8; r++)
        for (int oy = -r; oy <= r; oy++)
        for (int ox = -r; ox <= r; ox++)
        {
            if (System.Math.Max(System.Math.Abs(ox), System.Math.Abs(oy)) != r) continue;
            if (map.InBounds(t.X + ox, t.Y + oy) && map.IsPassable(t.X + ox, t.Y + oy)) return new TileCoord(t.X + ox, t.Y + oy).Center;
        }
        return p;
    }

    // ------------------------------------------------------------------ per tick

    public static void Update(GameState state)
    {
        foreach (var r in state.Regiments.Values.ToList())
        {
            if (!state.Regiments.ContainsKey(r.Id)) continue;
            if (r.SoldierIds.Count == 0)
            {
                EntityOps.RemoveRegiment(state, r);
                continue;
            }
            RecoverCohesion(state, r);
            if (r.IsStationed) continue;

            UpdateOrder(state, r);
            if (r.IsStationed) continue;
            UpdateAnchor(state, r);
            AssignSoldierGoals(state, r);
        }
    }

    private static void RecoverCohesion(GameState state, Regiment r)
    {
        var combat = state.Config.Combat;
        if (state.Tick - r.LastDamagedTick < state.Config.SecondsToTicks(combat.CohesionRecoveryDelaySeconds)) return;
        r.Cohesion = Fix.Min(combat.CohesionMax, r.Cohesion + combat.CohesionRecoveryPerSecond * state.Dt);
    }

    public static bool IsBroken(GameState state, Regiment r) => r.Cohesion < state.Config.Combat.BrokenThreshold;

    private static Fix EngageRadius(GameState state, Regiment r)
    {
        var def = state.Config.Soldier(r.Type);
        var engage = state.Config.Combat.EngageRadius;
        return def.Ranged != null ? Fix.Max(engage, def.Ranged.Range) : engage;
    }

    private static void UpdateOrder(GameState state, Regiment r)
    {
        var combat = state.Config.Combat;
        var centroid = state.RegimentCentroid(r);

        switch (r.Order)
        {
            case RegimentOrder.AttackRegiment:
            {
                var target = state.GetRegiment(r.TargetRegimentId);
                if (target == null || target.IsStationed)
                {
                    ClearTargets(r);
                    r.Order = RegimentOrder.Idle;
                    SetDestination(state, r, r.Anchor, r.Facing);
                }
                else
                {
                    r.EngagedRegimentId = target.Id;
                }
                return;
            }
            case RegimentOrder.AttackBuilding:
            {
                var target = state.GetBuilding(r.TargetBuildingId);
                if (target == null || target.State == BuildingState.Ruin)
                {
                    ClearTargets(r);
                    r.Order = RegimentOrder.Idle;
                    SetDestination(state, r, r.Anchor, r.Facing);
                }
                else
                {
                    r.EngagedBuildingId = target.Id;
                    AutoEngageRegiment(state, r, centroid); // defend yourself while sieging
                }
                return;
            }
            case RegimentOrder.Station:
            {
                var fort = state.GetBuilding(r.TargetBuildingId);
                if (fort == null || !fort.IsFort || fort.Owner != r.Owner || fort.State is BuildingState.UnderConstruction or BuildingState.Ruin)
                {
                    ClearTargets(r);
                    r.Order = RegimentOrder.Idle;
                    return;
                }
                if (fort.DistanceToFootprint(centroid) <= StationDistance)
                {
                    if (!FortSystem.TryStation(state, fort, r))
                    {
                        ClearTargets(r);
                        r.Order = RegimentOrder.Idle;
                        SetDestination(state, r, r.Anchor, r.Facing);
                    }
                }
                return;
            }
            case RegimentOrder.Move:
                r.EngagedRegimentId = 0;
                r.EngagedBuildingId = 0;
                return;
            case RegimentOrder.Follow:
            {
                var leader = state.GetRegiment(r.FollowRegimentId);
                if (leader == null || leader.IsStationed)
                {
                    ClearTargets(r);
                    r.Order = RegimentOrder.Idle;
                    return;
                }
                if (r.EngagedRegimentId == 0) UpdateFollowDestination(state, r, leader, force: false);
                AutoEngageRegiment(state, r, centroid);
                return;
            }
        }

        // Idle and AttackMove: engage what comes near, but melee Regiments don't chase beyond the leash.
        AutoEngageRegiment(state, r, centroid);
        if (r.Order == RegimentOrder.AttackMove && r.EngagedRegimentId == 0)
        {
            var b = state.GetBuilding(r.EngagedBuildingId);
            if (b == null || b.State == BuildingState.Ruin) r.EngagedBuildingId = 0;
            if (r.EngagedBuildingId == 0) r.EngagedBuildingId = NearestEnemyBuilding(state, r, centroid, combat.EngageRadius)?.Id ?? 0;
        }

        bool ranged = state.Config.Soldier(r.Type).Ranged != null;
        if (r.EngagedRegimentId != 0 && !ranged && FixVec2.DistanceSquared(centroid, r.HoldPoint) > combat.LeashRadius * combat.LeashRadius)
        {
            r.EngagedRegimentId = 0;
            if (r.Order == RegimentOrder.Idle) TrackGoal(state, r, r.HoldPoint);
        }
    }

    private static void AutoEngageRegiment(GameState state, Regiment r, FixVec2 centroid)
    {
        var radius = EngageRadius(state, r);
        var current = state.GetRegiment(r.EngagedRegimentId);
        if (current != null && !current.IsStationed)
        {
            var keep = radius + Fix.FromInt(4);
            if (FixVec2.DistanceSquared(state.RegimentCentroid(current), centroid) <= keep * keep) return;
        }
        r.EngagedRegimentId = 0;

        Regiment? best = null;
        Fix bestD = radius * radius;
        foreach (var other in state.Regiments.Values)
        {
            if (other.Owner == r.Owner || other.IsStationed || other.SoldierIds.Count == 0) continue;
            var d = FixVec2.DistanceSquared(state.RegimentCentroid(other), centroid);
            if (d <= bestD)
            {
                bestD = d;
                best = other;
            }
        }
        if (best != null) r.EngagedRegimentId = best.Id;
    }

    private static Building? NearestEnemyBuilding(GameState state, Regiment r, FixVec2 from, Fix radius)
    {
        Building? best = null;
        Fix bestD = radius;
        foreach (var b in state.Buildings.Values)
        {
            if (b.Owner == r.Owner || b.State == BuildingState.Ruin) continue;
            var d = b.DistanceToFootprint(from);
            if (b.NeedsConstruction) d += Fix.FromInt(6); // prefer finished buildings over construction sites
            if (d <= bestD)
            {
                bestD = d;
                best = b;
            }
        }
        return best;
    }

    private static void UpdateFollowDestination(GameState state, Regiment follower, Regiment leader, bool force)
    {
        var cfg = state.Config;
        var basePos = leader.AnchorArrived ? leader.Anchor : leader.Destination;
        var baseFacing = leader.AnchorArrived ? leader.Facing : leader.DestinationFacing;
        var gap = FormationLayout.Depth(cfg.Formation(leader.Formation), leader.SoldierIds.Count) / 2
                  + FormationLayout.Depth(cfg.Formation(follower.Formation), follower.SoldierIds.Count) / 2
                  + follower.FollowGap;
        var desired = basePos - baseFacing * gap;
        if (force || FixVec2.DistanceSquared(desired, follower.HoldPoint) > Fix.One)
        {
            SetDestination(state, follower, desired, baseFacing);
        }
    }

    private static void UpdateAnchor(GameState state, Regiment r)
    {
        var cfg = state.Config;
        var def = cfg.Soldier(r.Type);
        var formation = cfg.Formation(r.Formation);
        var centroid = state.RegimentCentroid(r);
        bool engaged = false;

        if (r.Order != RegimentOrder.Move)
        {
            var enemy = state.GetRegiment(r.EngagedRegimentId);
            var building = state.GetBuilding(r.EngagedBuildingId);
            if (enemy != null)
            {
                engaged = true;
                var enemyPos = state.RegimentCentroid(enemy);
                var toEnemy = enemyPos - centroid;
                var dist = toEnemy.Length;
                var dir = dist.Raw == 0 ? r.Facing : toEnemy / dist;
                r.Facing = SmoothFacing(r.Facing, dir);
                if (def.Ranged != null && TryKite(state, r, centroid))
                {
                    // Falling back from melee; resume shooting once there.
                }
                else if (def.Ranged != null)
                {
                    var range = def.Ranged.Range;
                    bool mayAdvance = r.Order is RegimentOrder.AttackRegiment or RegimentOrder.AttackMove && !r.HoldGround;
                    if (mayAdvance && dist > range * Fix.FromRatio(9, 10)) TrackGoal(state, r, enemyPos - dir * (range * Fix.FromRatio(4, 5)));
                    else HoldAnchor(r);
                }
                else if (!HoldsGround(r))
                {
                    TrackGoal(state, r, enemyPos);
                }
            }
            else if (building != null)
            {
                engaged = true;
                var dir = (building.Center - centroid).Normalized();
                if (!dir.IsZero) r.Facing = SmoothFacing(r.Facing, dir);
                if (def.Ranged != null)
                {
                    var dist = FixVec2.Distance(building.Center, centroid);
                    var range = def.Ranged.Range;
                    if (dist > range * Fix.FromRatio(9, 10)) TrackGoal(state, r, building.Center - dir * (range * Fix.FromRatio(4, 5)));
                    else HoldAnchor(r);
                }
                else if (building.DistanceToFootprint(r.Destination) > Fix.FromInt(2))
                {
                    // Melee must stand at the walls. Also recovers after a detour to fight a Regiment moved the goal away.
                    TrackGoal(state, r, building.NearestAccessTile(state.Map, centroid).Center);
                }
            }
        }

        // Still marching: ranged Soldiers need to stand a while before they shoot at full range again.
        if (r.AnchorArrived) return;
        r.LastMovedTick = state.Tick;

        var toGoal = r.Destination - r.Anchor;
        var distSq = toGoal.LengthSquared;
        if (distSq <= AnchorArriveDistance * AnchorArriveDistance)
        {
            r.Anchor = r.Destination;
            r.AnchorArrived = true;
            if (!engaged)
            {
                r.Facing = r.DestinationFacing;
                if (r.Order is RegimentOrder.Move or RegimentOrder.AttackMove)
                {
                    r.Order = RegimentOrder.Idle;
                    r.HoldPoint = r.Destination;
                }
            }
            return;
        }

        var goalDist = Fix.Sqrt(distSq);
        FixVec2 direction;
        if (goalDist <= Fix.FromInt(12) && state.Map.HasLineOfSight(r.Anchor, r.Destination))
        {
            direction = toGoal / goalDist;
        }
        else
        {
            var field = state.FlowFields.Get(r.DestinationKey, r.DestinationGoals);
            direction = field.Sample(r.Anchor);
            if (direction.IsZero) direction = toGoal / goalDist;
        }

        var tile = r.Anchor.ToTile();
        var terrain = state.Map.IsPassableSafe(tile.X, tile.Y) ? state.Map.GetSpeedMultiplier(tile.X, tile.Y, MovementClass.Military) : Fix.One;
        var speed = def.Speed * formation.SpeedMultiplier * terrain;
        if (r.AverageLag > cfg.Movement.RegimentMaxLag) speed *= cfg.Movement.LagSlowdown;
        var step = Fix.Min(goalDist, speed * state.Dt);
        var next = r.Anchor + direction * step;
        var nextTile = next.ToTile();
        if (state.Map.IsPassableSafe(nextTile.X, nextTile.Y))
        {
            r.Anchor = next;
        }
        else if (goalDist <= Fix.FromInt(2))
        {
            // The last step is blocked (e.g. a building went up on the spot): close enough, stop here.
            r.Destination = r.Anchor;
            r.AnchorArrived = true;
            if (!engaged && r.Order is RegimentOrder.Move or RegimentOrder.AttackMove) r.Order = RegimentOrder.Idle;
            return;
        }
        if (!engaged) r.Facing = SmoothFacing(r.Facing, direction);
    }

    private static void HoldAnchor(Regiment r)
    {
        r.AnchorArrived = true;
    }

    /// <summary>
    /// Fire and fall back: a ranged Regiment that isn't holding ground steps away from enemy melee that gets too
    /// close, then shoots again from the new spot.
    /// </summary>
    private static bool TryKite(GameState state, Regiment r, FixVec2 centroid)
    {
        var combat = state.Config.Combat;
        if (r.HoldGround || r.Order == RegimentOrder.Move) return false;
        if (state.Tick < r.KiteUntilTick) return !r.AnchorArrived;

        Regiment? threat = null;
        Fix bestSq = combat.KiteTriggerDistance * combat.KiteTriggerDistance;
        foreach (var other in state.Regiments.Values)
        {
            if (other.Owner == r.Owner || other.IsStationed || other.SoldierIds.Count == 0) continue;
            if (state.Config.Soldier(other.Type).Ranged != null) continue;
            var d = FixVec2.DistanceSquared(state.RegimentCentroid(other), centroid);
            if (d < bestSq)
            {
                bestSq = d;
                threat = other;
            }
        }
        if (threat == null) return false;

        var away = (centroid - state.RegimentCentroid(threat)).Normalized();
        if (away.IsZero) away = -r.Facing;
        TrackGoal(state, r, centroid + away * combat.KiteStepDistance);
        r.KiteUntilTick = state.Tick + state.Config.SecondsToTicks(combat.KiteCooldownSeconds);
        return true;
    }

    /// <summary>Hold Ground keeps a Regiment in place unless it was explicitly ordered to attack something.</summary>
    public static bool HoldsGround(Regiment r) =>
        r.HoldGround && r.Order is not (RegimentOrder.AttackRegiment or RegimentOrder.AttackBuilding);

    private static FixVec2 SmoothFacing(FixVec2 current, FixVec2 target)
    {
        var blended = (current * 4 + target).Normalized();
        return blended.IsZero ? target : blended;
    }


    private static void AssignSoldierGoals(GameState state, Regiment r)
    {
        var cfg = state.Config;
        var def = cfg.Soldier(r.Type);
        var formation = cfg.Formation(r.Formation);
        int count = r.SoldierIds.Count;
        bool meleeEngaged = def.Ranged == null && (r.EngagedRegimentId != 0 || r.EngagedBuildingId != 0) && r.Order != RegimentOrder.Move;
        // Holding ground: Soldiers only strike what reaches them instead of breaking ranks to chase.
        var aggro = HoldsGround(r) ? def.Melee.Range + s_holdReach : cfg.Combat.SoldierAggroRadius;
        Fix lagSum = Fix.Zero;
        int lagCount = 0;
        var stragglerDistance = cfg.Movement.StragglerDistance;

        for (int i = 0; i < count; i++)
        {
            var s = state.GetSoldier(r.SoldierIds[i]);
            if (s == null) continue;
            var slot = r.Anchor + FixVec2.LocalToWorld(FormationLayout.SlotOffset(formation, i, count), r.Facing);
            var slotDist = FixVec2.Distance(s.Position, slot);
            // Stragglers stuck far behind (or chasing an enemy) must not stall the whole Regiment; they catch up via the flow field.
            if (slotDist <= stragglerDistance)
            {
                lagSum += slotDist;
                lagCount++;
            }
            s.SpeedFactor = r.AnchorArrived ? Fix.One : formation.SpeedMultiplier * Fix.FromRatio(23, 20);
            s.TargetSoldierId = 0;

            if (meleeEngaged || r.Order != RegimentOrder.Move)
            {
                var enemy = NearestEnemySoldier(state, s, aggro);
                if (enemy != null && (meleeEngaged || def.Ranged == null))
                {
                    s.TargetSoldierId = enemy.Id;
                    var toEnemy = enemy.Position - s.Position;
                    var d = toEnemy.Length;
                    var reach = def.Melee.Range + s.Radius + enemy.Radius;
                    if (d > reach * Fix.FromRatio(4, 5)) s.SetPointGoal(enemy.Position - (d.Raw == 0 ? FixVec2.Zero : toEnemy / d) * (reach * Fix.FromRatio(7, 10)));
                    else s.ClearGoal();
                    continue;
                }
                var civilian = def.Ranged == null && !HoldsGround(r) ? CombatSystem.NearestEnemyWorker(state, s.Owner, s.Position, aggro) : null;
                if (civilian != null)
                {
                    s.SetPointGoal(civilian.Position);
                    continue;
                }
            }

            if (meleeEngaged && r.EngagedBuildingId != 0)
            {
                var b = state.GetBuilding(r.EngagedBuildingId);
                if (b != null && b.DistanceToFootprint(r.Anchor) < Fix.FromInt(4))
                {
                    var access = b.NearestAccessTile(state.Map, s.Position).Center;
                    s.SetPointGoal(access);
                    continue;
                }
            }

            var slotTile = slot.ToTile();
            bool slotOpen = state.Map.IsPassableSafe(slotTile.X, slotTile.Y);
            if (!slotOpen) slot = r.Anchor;
            if (slotDist <= cfg.Movement.SlotDirectDistance && state.Map.HasLineOfSight(s.Position, slot))
                s.SetPointGoal(slot);
            else
                s.SetFlowGoal(r.DestinationKey, r.DestinationGoals, slot);
        }

        r.AverageLag = lagCount == 0 ? Fix.Zero : lagSum / lagCount;
    }

    public static Soldier? NearestEnemySoldier(GameState state, Soldier s, Fix radius)
    {
        var nearby = state.QueryBuffer;
        nearby.Clear();
        state.SoldierHash.Query(s.Position, radius, nearby);
        Soldier? best = null;
        Fix bestD = radius * radius;
        foreach (var idx in nearby)
        {
            var o = state.SoldierList[idx];
            if (o.Owner == s.Owner || o.Stationed) continue;
            var d = FixVec2.DistanceSquared(o.Position, s.Position);
            if (d < bestD)
            {
                bestD = d;
                best = o;
            }
        }
        return best;
    }
}
