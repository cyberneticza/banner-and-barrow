using BannerAndBarrow.Simulation.Commands;
using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Military;

namespace BannerAndBarrow.Simulation.AI;

/// <summary>
/// Escalating, reactive military AI:
/// - a peace window at the start, then attacks whose size grows every minute and whose interval shrinks;
/// - small attacks are raids straight at the economy, big ones stage short of the town and assault together;
/// - the opening strategy (Economy or Rush) shapes the curve;
/// - being attacked at home ("provocation") speeds up escalation, most strongly early on, up to a cap.
/// </summary>
public sealed class ScriptedMilitaryPolicy : IMilitaryPolicy
{
    public void Decide(AiContext ctx)
    {
        var state = ctx.State;
        ctx.Memory.WaveRegimentIds.RemoveWhere(id => state.GetRegiment(id) == null);

        UpdateProvocation(ctx);
        Recruit(ctx);
        if (ctx.Memory.DecisionCount % 10 == 5) UpgradeSpearmen(ctx);
        Scout(ctx);
        if (ctx.Memory.DecisionCount % 10 == 0) MergeHomeSquads(ctx);
        if (Defend(ctx)) return;
        UpdateWaves(ctx);
        Rally(ctx);
    }

    // ------------------------------------------------------------------ escalation maths

    /// <summary>Soldiers the AI wants in its next attack at this moment.</summary>
    public static int TargetWaveSoldiers(AiContext ctx)
    {
        var p = ctx.Profile;
        var strategy = ctx.Strategy;
        var minutes = Fix.Max(Fix.Zero, (ctx.SecondsElapsed - PeaceSeconds(ctx)) / 60);
        var growth = p.WaveSoldiersPerMinute * p.Aggression * strategy.EscalationMultiplier * (Fix.One + ctx.Memory.Provocation);
        var target = p.FirstWaveSoldiers * strategy.FirstWaveMultiplier + growth * minutes;
        return System.Math.Clamp(target.RoundToInt(), 1, p.MaxWaveSoldiers);
    }

    public static Fix WaveIntervalSeconds(AiContext ctx)
    {
        var p = ctx.Profile;
        var minutes = Fix.Max(Fix.Zero, (ctx.SecondsElapsed - PeaceSeconds(ctx)) / 60);
        var interval = Fix.Max(p.MinWaveIntervalSeconds, p.FirstWaveIntervalSeconds - p.WaveIntervalShrinkPerMinute * minutes);
        interval = interval * ctx.Strategy.IntervalMultiplier / (Fix.One + ctx.Memory.Provocation / 2);
        return Fix.Max(p.MinWaveIntervalSeconds / 2, interval);
    }

    private static Fix PeaceSeconds(AiContext ctx) => ctx.Profile.PeaceSeconds + ctx.Strategy.ExtraPeaceSeconds;

    /// <summary>The enemy army in our town makes the AI angrier; early aggression counts double. Slowly forgiven.</summary>
    private static void UpdateProvocation(AiContext ctx)
    {
        var state = ctx.State;
        var p = ctx.Profile;
        int intruders = 0;
        foreach (var enemy in ctx.EnemyRegiments)
        {
            if (enemy.IsStationed || !NearTown(state, ctx.Player, state.RegimentCentroid(enemy), p.DefenseRadius)) continue;
            intruders += enemy.SoldierIds.Count;
        }
        var dt = p.DecisionIntervalSeconds;
        if (intruders > 0)
        {
            var gain = p.ProvocationPerSoldierSecond * intruders * dt;
            if (ctx.SecondsElapsed < p.EarlyProvocationSeconds) gain *= 2;
            if (ctx.Memory.Provocation.Raw == 0)
                state.AddEvent(ctx.Player, GameEventKind.Info, "AI provoked: its attacks will escalate faster.");
            ctx.Memory.Provocation = Fix.Min(p.MaxProvocation, ctx.Memory.Provocation + gain);
        }
        else
        {
            ctx.Memory.Provocation = Fix.Max(Fix.Zero, ctx.Memory.Provocation - p.ProvocationDecayPerMinute * dt / 60);
        }
    }

    private static bool NearTown(GameState state, int player, FixVec2 position, Fix radius)
    {
        foreach (var b in state.Buildings.Values)
            if (b.Owner == player && !b.IsFort && b.DistanceToFootprint(position) <= radius) return true;
        return false;
    }

    // ------------------------------------------------------------------ army upkeep

    private static void Recruit(AiContext ctx)
    {
        var state = ctx.State;
        var cap = state.Config.Simulation.MaxSoldiersPerPlayer;
        var soldiersByType = ctx.Regiments.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.Sum(r => r.SoldierIds.Count));
        int soldiers = soldiersByType.Values.Sum();
        var composition = ctx.Strategy.ArmyComposition.Count > 0 ? ctx.Strategy.ArmyComposition : ctx.Profile.ArmyComposition;

        foreach (var b in state.Buildings.Values)
        {
            if (b.Owner != ctx.Player || b.Recruitment == null || !b.IsActive || b.Recruitment.Queue.Count > 0) continue;
            // Most under-represented type by Soldiers (relative to the weights). A cheaper type may stand in only
            // while it is not already well over its share.
            var ranked = composition
                .Where(kv => kv.Value > 0 && state.Config.Soldier(kv.Key).RecruitedAt == b.Type)
                .Select(kv => (Type: kv.Key, Ratio: Fix.FromRatio(soldiersByType.GetValueOrDefault(kv.Key), kv.Value * 6)))
                .OrderBy(x => x.Ratio).ThenBy(x => x.Type)
                .ToList();
            if (ranked.Count == 0) continue;
            var limit = ranked[0].Ratio + ctx.Profile.CompositionSlack;
            SoldierType? choice = ranked
                .Where(x => x.Ratio <= limit)
                .Select(x => (SoldierType?)x.Type)
                .FirstOrDefault(t => soldiers + state.Config.Soldier(t!.Value).RegimentSize <= cap && CanOrder(ctx, state.Config.Soldier(t.Value).RecruitCost));
            if (choice.HasValue)
            {
                ctx.Issue(new RecruitCommand(ctx.Player, b.Id, choice.Value));
                soldiers += state.Config.Soldier(choice.Value).RegimentSize;
            }
        }
    }

    /// <summary>
    /// Raw materials must be in stock; goods only need to be in stock or makeable by one of our workshops,
    /// because a queued recruit is an order the workshops fill.
    /// </summary>
    private static bool CanOrder(AiContext ctx, Dictionary<ResourceType, int> cost)
    {
        var state = ctx.State;
        var stock = ctx.Stock;
        foreach (var (r, amount) in cost)
        {
            if (stock[(int)r] >= amount) continue;
            if (!Resources.IsGoods(r)) return false;
            bool makeable = state.Buildings.Values.Any(b => b.Owner == ctx.Player && b.IsActive &&
                                                             state.Config.Building(b.Type).Recipes.Any(x => x.Output == r));
            if (!makeable) return false;
        }
        return true;
    }

    /// <summary>Home Spearmen near a Barracks are upgraded to Pikemen once a Smithy can make the goods.</summary>
    private static void UpgradeSpearmen(AiContext ctx)
    {
        var state = ctx.State;
        var composition = ctx.Strategy.ArmyComposition.Count > 0 ? ctx.Strategy.ArmyComposition : ctx.Profile.ArmyComposition;
        if (composition.GetValueOrDefault(SoldierType.Pikemen) <= 0 || state.Players[ctx.Player].PendingUpgrades.Count > 0) return;
        if (!state.Buildings.Values.Any(b => b.Owner == ctx.Player && b.IsActive && b.Type == BuildingType.Smithy)) return;
        var radius = Fix.FromInt(state.Config.Economy.UpgradeRadius);
        var candidate = ctx.Regiments.FirstOrDefault(r =>
            r.Type == SoldierType.Spearmen && !r.IsStationed && r.Order == RegimentOrder.Idle && !ctx.Memory.WaveRegimentIds.Contains(r.Id) &&
            state.Buildings.Values.Any(b => b.Owner == ctx.Player && b.IsActive && b.Type == BuildingType.Barracks &&
                                            b.DistanceToFootprint(state.RegimentCentroid(r)) <= radius));
        if (candidate != null) ctx.Issue(new UpgradeRegimentsCommand(ctx.Player, new[] { candidate.Id }));
    }

    /// <summary>
    /// Until the enemy Keep has been seen, one small Regiment walks to where it is guessed to be. Once it is
    /// found (or the scout dies) the scout comes home; a new one goes out after a pause.
    /// </summary>
    private static void Scout(AiContext ctx)
    {
        var state = ctx.State;
        var scout = state.GetRegiment(ctx.Memory.ScoutRegimentId);
        if (ctx.EnemyKeep != null)
        {
            // Found it: the scout reports home rather than picking a fight on its own.
            if (scout != null)
                ctx.Issue(new MoveRegimentsCommand(ctx.Player, new[] { scout.Id }, RegimentRally(ctx), ctx.TowardEnemy, AttackMove: false));
            ctx.Memory.ScoutRegimentId = 0;
            return;
        }
        if (scout != null)
        {
            // Arrived without finding it: the guess was wrong, so try another corner of the map.
            if (scout.Order == RegimentOrder.Idle && ctx.Memory.WaveRegimentIds.Count == 0)
            {
                var w = state.Map.Width;
                var h = state.Map.Height;
                var spot = new FixVec2(Fix.FromInt(state.Rng.Next(w / 5, w * 4 / 5)), Fix.FromInt(state.Rng.Next(h / 5, h * 4 / 5)));
                ctx.Issue(new MoveRegimentsCommand(ctx.Player, new[] { scout.Id }, RegimentSystem.ClampToPassable(state, spot), ctx.TowardEnemy, AttackMove: false));
            }
            return;
        }
        if (state.Tick < ctx.Memory.NextScoutTick) return;
        var candidate = ctx.Regiments
            .Where(r => !r.IsStationed && r.Order == RegimentOrder.Idle && !ctx.Memory.WaveRegimentIds.Contains(r.Id) && r.Id != ctx.Memory.ScoutRegimentId)
            .OrderByDescending(r => state.Config.Soldier(r.Type).Speed).ThenBy(r => r.SoldierIds.Count).ThenBy(r => r.Id)
            .FirstOrDefault();
        if (candidate == null) return;
        ctx.Memory.ScoutRegimentId = candidate.Id;
        ctx.Memory.NextScoutTick = state.Tick + ctx.SecondsToTicks(Fix.FromInt(60));
        ctx.Issue(new MoveRegimentsCommand(ctx.Player, new[] { candidate.Id }, RegimentSystem.ClampToPassable(state, ctx.EnemyTown), ctx.TowardEnemy, AttackMove: false));
        state.AddEvent(ctx.Player, GameEventKind.Info, "AI sends a scout.");
    }

    /// <summary>Squads waiting at the rally point merge into full Regiments.</summary>
    private static void MergeHomeSquads(AiContext ctx)
    {
        var state = ctx.State;
        var rally = RegimentRally(ctx);
        var radius = Fix.FromInt(20);
        foreach (var byType in ctx.Regiments
                     .Where(r => !r.IsStationed && r.Order == RegimentOrder.Idle && !ctx.Memory.WaveRegimentIds.Contains(r.Id) &&
                                 FixVec2.DistanceSquared(r.Anchor, rally) <= radius * radius)
                     .GroupBy(r => r.Type))
        {
            var max = state.Config.Soldier(byType.Key).MaxRegimentSize;
            var mergeable = byType.Where(r => r.SoldierIds.Count < max).Select(r => r.Id).ToArray();
            if (mergeable.Length > 1) ctx.Issue(new MergeRegimentsCommand(ctx.Player, mergeable));
        }
    }

    /// <summary>Sends home Regiments at enemies near the town. Returns true when defending.</summary>
    private static bool Defend(AiContext ctx)
    {
        var state = ctx.State;
        Regiment? threat = null;
        foreach (var enemy in ctx.EnemyRegiments)
        {
            if (enemy.IsStationed || !NearTown(state, ctx.Player, state.RegimentCentroid(enemy), ctx.Profile.DefenseRadius)) continue;
            threat = enemy;
            break;
        }
        if (threat == null) return false;

        var defenders = ctx.Regiments
            .Where(r => !r.IsStationed && !ctx.Memory.WaveRegimentIds.Contains(r.Id) && r.Order is RegimentOrder.Idle or RegimentOrder.Move)
            .Select(r => r.Id).ToArray();
        if (defenders.Length > 0) ctx.Issue(new AttackRegimentCommand(ctx.Player, defenders, threat.Id));
        return true;
    }

    private static void Rally(AiContext ctx)
    {
        var rally = RegimentRally(ctx);
        foreach (var r in ctx.Regiments)
        {
            if (r.IsStationed || r.Order != RegimentOrder.Idle || ctx.Memory.WaveRegimentIds.Contains(r.Id)) continue;
            if (FixVec2.DistanceSquared(r.Anchor, rally) <= Fix.FromInt(64)) continue;
            ctx.Issue(new MoveRegimentsCommand(ctx.Player, new[] { r.Id }, rally, ctx.TowardEnemy, AttackMove: true));
        }
    }

    // ------------------------------------------------------------------ attacks

    private static void UpdateWaves(AiContext ctx)
    {
        var state = ctx.State;
        var profile = ctx.Profile;
        if (ctx.Memory.NextWaveTick < 0)
            ctx.Memory.NextWaveTick = ctx.SecondsToTicks(PeaceSeconds(ctx));

        UpdateStaging(ctx);

        // Walled in: break the wall rather than trudge along it.
        foreach (var id in ctx.Memory.WaveRegimentIds)
        {
            var r = state.GetRegiment(id);
            if (r == null || r.Order == RegimentOrder.AttackBuilding || state.Config.Soldier(r.Type).Ranged != null) continue;
            if (BlockedByWall(ctx, r) is { } wall) ctx.Issue(new AttackBuildingCommand(ctx.Player, new[] { r.Id }, wall.Id));
        }

        // Attackers that finished their march go for forts, the Keep, then the nearest building.
        foreach (var id in ctx.Memory.WaveStaging ? Enumerable.Empty<int>() : ctx.Memory.WaveRegimentIds)
        {
            var r = state.GetRegiment(id);
            if (r == null || r.Order != RegimentOrder.Idle) continue;
            var siegeTarget = PickSiegeTarget(state, ctx, state.RegimentCentroid(r));
            if (siegeTarget == null)
            {
                // Nothing known nearby: search towards the enemy town.
                if (FixVec2.DistanceSquared(state.RegimentCentroid(r), ctx.EnemyTown) > Fix.FromInt(64))
                    ctx.Issue(new MoveRegimentsCommand(ctx.Player, new[] { r.Id }, RegimentSystem.ClampToPassable(state, ctx.EnemyTown), ctx.TowardEnemy, AttackMove: true));
                continue;
            }
            if (state.Config.Soldier(r.Type).Ranged == null)
                ctx.Issue(new AttackBuildingCommand(ctx.Player, new[] { r.Id }, siegeTarget.Id));
            else
                ctx.Issue(new MoveRegimentsCommand(ctx.Player, new[] { r.Id }, siegeTarget.Center, (siegeTarget.Center - state.RegimentCentroid(r)).Normalized(), AttackMove: true));
        }

        if (state.Tick < ctx.Memory.NextWaveTick) return;

        int target = TargetWaveSoldiers(ctx);
        bool raid = target <= profile.RaidMaxSoldiers;
        var available = ctx.Regiments
            .Where(r => !r.IsStationed && r.Order == RegimentOrder.Idle && !ctx.Memory.WaveRegimentIds.Contains(r.Id) && r.Id != ctx.Memory.ScoutRegimentId)
            .OrderByDescending(r => r.SoldierIds.Count).ThenBy(r => r.Id).ToList();
        int availableSoldiers = available.Sum(r => r.SoldierIds.Count) - (raid ? 0 : profile.HomeGuardSoldiers);

        // Not enough yet: wait, but after a while go with most of it rather than stall forever.
        if (ctx.Memory.WaveWaitStartTick < 0) ctx.Memory.WaveWaitStartTick = state.Tick;
        bool waitedLong = state.Tick - ctx.Memory.WaveWaitStartTick >= ctx.SecondsToTicks(profile.MaxWaveWaitSeconds);
        if (availableSoldiers < target && !(waitedLong && availableSoldiers * 10 >= target * 6)) return;
        if (availableSoldiers <= 0) return;

        var wave = new List<Regiment>();
        int picked = 0;
        foreach (var r in available)
        {
            if (picked >= System.Math.Min(target, availableSoldiers)) break;
            wave.Add(r);
            picked += r.SoldierIds.Count;
        }
        if (wave.Count == 0) return;

        var facing = ctx.TowardEnemy;
        foreach (var r in wave.Where(r => state.Config.Soldier(r.Type).IsMounted))
            ctx.Issue(new SetFormationCommand(ctx.Player, new[] { r.Id }, FormationType.Wedge));

        if (raid)
        {
            // Raid: hit the economy on the near side of the enemy town, no staging.
            var raidTarget = NearestEconomicBuilding(state, ctx)?.Center ?? ctx.EnemyTown;
            ctx.Issue(new MoveRegimentsCommand(ctx.Player, wave.Select(r => r.Id).ToArray(), RegimentSystem.ClampToPassable(state, raidTarget), facing, AttackMove: true));
            state.AddEvent(ctx.Player, GameEventKind.Info, $"AI raids with {picked} Soldiers.");
        }
        else
        {
            bool enemyHasArchers = ctx.EnemyRegiments.Count(r => state.Config.Soldier(r.Type).Ranged != null) >= 2;
            foreach (var r in wave.Where(r => state.Config.Soldier(r.Type).Ranged == null && !state.Config.Soldier(r.Type).IsMounted))
            {
                // Loose order spreads out against arrows; otherwise a solid line.
                var formation = enemyHasArchers && !state.Config.Soldier(r.Type).Shield && state.Config.Soldier(r.Type).Formations.Contains(FormationType.Loose)
                    ? FormationType.Loose : FormationType.Line;
                ctx.Issue(new SetFormationCommand(ctx.Player, new[] { r.Id }, formation));
            }
            if (!ctx.Memory.WaveStaging)
            {
                ctx.Memory.WaveStaging = true;
                ctx.Memory.StagePoint = RegimentSystem.ClampToPassable(state, ctx.EnemyTown - facing * profile.StagingDistance);
                ctx.Memory.StageDeadlineTick = state.Tick + ctx.SecondsToTicks(FixVec2.Distance(ctx.Keep.Center, ctx.Memory.StagePoint) + profile.StagingMaxWaitSeconds);
            }
            ctx.Issue(new MoveRegimentsCommand(ctx.Player, wave.Select(r => r.Id).ToArray(), ctx.Memory.StagePoint, facing, AttackMove: !profile.MarchWithoutEngaging));
            state.AddEvent(ctx.Player, GameEventKind.Info, $"AI attack wave with {picked} Soldiers.");
        }

        foreach (var r in wave) ctx.Memory.WaveRegimentIds.Add(r.Id);
        ctx.Memory.WavesSent++;
        ctx.Memory.WaveWaitStartTick = -1;
        ctx.Memory.NextWaveTick = state.Tick + ctx.SecondsToTicks(Jitter(ctx, WaveIntervalSeconds(ctx)));
    }

    private static void UpdateStaging(AiContext ctx)
    {
        var state = ctx.State;
        if (!ctx.Memory.WaveStaging) return;
        var alive = ctx.Memory.WaveRegimentIds.Select(state.GetRegiment).Where(r => r != null).Cast<Regiment>().ToList();
        var gatherRadius = Fix.FromInt(12);
        int gathered = alive.Count(r => FixVec2.DistanceSquared(state.RegimentCentroid(r), ctx.Memory.StagePoint) <= gatherRadius * gatherRadius);
        if (alive.Count == 0)
        {
            ctx.Memory.WaveStaging = false;
        }
        else if (gathered * 4 >= alive.Count * 3 || state.Tick >= ctx.Memory.StageDeadlineTick)
        {
            ctx.Memory.WaveStaging = false;
            ctx.Issue(new MoveRegimentsCommand(ctx.Player, alive.Select(r => r.Id).ToArray(), RegimentSystem.ClampToPassable(state, ctx.EnemyTown), ctx.TowardEnemy, AttackMove: true));
            state.AddEvent(ctx.Player, GameEventKind.Info, $"AI wave assaults with {alive.Sum(r => r.SoldierIds.Count)} Soldiers.");
        }
    }

    /// <summary>
    /// The enemy wall or gate in the way when a Regiment's destination can't be reached on foot. Melee attackers
    /// break through instead of milling about outside.
    /// </summary>
    private static Building? BlockedByWall(AiContext ctx, Regiment r)
    {
        var state = ctx.State;
        if (r.AnchorArrived) return null;
        var centroid = state.RegimentCentroid(r);
        var tile = centroid.ToTile();
        if (!state.FlowFields.TryPeek(r.DestinationKey, out var field) || field == null) return null;
        if (field.IsReachable(tile.X, tile.Y)) return null;

        Building? best = null;
        Fix bestD = Fix.FromInt(25);
        foreach (var b in ctx.KnownEnemyBuildings)
        {
            if (b.Type is not (BuildingType.Wall or BuildingType.Gate) || b.State == BuildingState.Ruin) continue;
            var d = b.DistanceToFootprint(centroid);
            if (d >= bestD) continue;
            bestD = d;
            best = b;
        }
        return best;
    }

    private static Building? NearestEconomicBuilding(GameState state, AiContext ctx)
    {
        Building? best = null;
        Fix bestD = Fix.MaxValue;
        foreach (var b in ctx.KnownEnemyBuildings)
        {
            if (b.IsFort || b.State == BuildingState.Ruin || b.Type == BuildingType.Keep) continue;
            if (state.Config.Building(b.Type).WorkerSlots == 0 && !b.IsStorehouseLike) continue;
            var d = FixVec2.DistanceSquared(b.Center, ctx.Keep.Center);
            if (d < bestD)
            {
                bestD = d;
                best = b;
            }
        }
        return best;
    }

    /// <summary>Forts that shoot back first, then the Keep once close (it ends the Match), otherwise the nearest building.</summary>
    private static Building? PickSiegeTarget(GameState state, AiContext ctx, FixVec2 from)
    {
        Building? fort = null;
        Fix fortD = Fix.FromInt(16);
        foreach (var b in ctx.KnownEnemyBuildings)
        {
            // Only finished forts shoot back; construction sites aren't worth breaking formation for.
            if (!b.IsFort || b.State is BuildingState.Ruin or BuildingState.UnderConstruction) continue;
            var d = b.DistanceToFootprint(from);
            if (d < fortD)
            {
                fortD = d;
                fort = b;
            }
        }
        if (fort != null) return fort;
        var keep = ctx.EnemyKeep;
        if (keep != null && keep.DistanceToFootprint(from) <= Fix.FromInt(40)) return keep;
        return NearestEnemyBuilding(ctx, from, Fix.FromInt(30)) ?? keep;
    }

    /// <summary>Randomises a timing by ±WaveTimingJitter (seeded, so still deterministic) so mirrored AIs don't attack in lockstep.</summary>
    private static Fix Jitter(AiContext ctx, Fix seconds)
    {
        var j = ctx.Profile.WaveTimingJitter;
        return seconds * (Fix.One - j + ctx.State.Rng.NextFix() * j * 2);
    }

    /// <summary>Rough fighting value of a Regiment: Soldiers weighted by HP and armour.</summary>
    public static Fix Strength(GameState state, Regiment r)
    {
        var def = state.Config.Soldier(r.Type);
        var perSoldier = def.Hp / 100 * (Fix.One + def.Armour / 5);
        return perSoldier * r.SoldierIds.Count;
    }

    private static FixVec2 RegimentRally(AiContext ctx) => ctx.Keep.Center + ctx.TowardEnemy * 14;

    private static Building? NearestEnemyBuilding(AiContext ctx, FixVec2 from, Fix radius)
    {
        Building? best = null;
        Fix bestD = radius;
        foreach (var b in ctx.KnownEnemyBuildings)
        {
            if (b.State == BuildingState.Ruin) continue;
            var d = b.DistanceToFootprint(from);
            if (d < bestD)
            {
                bestD = d;
                best = b;
            }
        }
        return best;
    }
}
