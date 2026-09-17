using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Military;

/// <summary>Melee strikes, ranged volleys and damage application for Soldiers on the map.</summary>
public static class CombatSystem
{
    /// <summary>Rebuilds the Soldier spatial index. Call once per tick before any combat query.</summary>
    public static void RebuildSoldierIndex(GameState state)
    {
        state.SoldierList.Clear();
        state.SoldierHash.Clear();
        foreach (var s in state.Soldiers.Values)
        {
            if (s.Stationed) continue;
            state.SoldierHash.Insert(state.SoldierList.Count, s.Position);
            state.SoldierList.Add(s);
        }
        state.WorkerList.Clear();
        state.WorkerHash.Clear();
        foreach (var w in state.Workers.Values)
        {
            state.WorkerHash.Insert(state.WorkerList.Count, w.Position);
            state.WorkerList.Add(w);
        }
    }

    public static Worker? NearestEnemyWorker(GameState state, int owner, FixVec2 position, Fix radius)
    {
        if (!state.Config.Combat.SoldiersAttackWorkers) return null;
        var nearby = state.QueryBuffer;
        nearby.Clear();
        state.WorkerHash.Query(position, radius, nearby);
        Worker? best = null;
        Fix bestD = radius * radius;
        foreach (var idx in nearby)
        {
            var w = state.WorkerList[idx];
            if (w.Owner == owner || !state.Workers.ContainsKey(w.Id)) continue;
            var d = FixVec2.DistanceSquared(w.Position, position);
            if (d <= bestD)
            {
                bestD = d;
                best = w;
            }
        }
        return best;
    }

    public static void DamageWorker(GameState state, Worker w, Fix damage)
    {
        w.Hp -= damage;
        if (w.Hp.Raw <= 0) EntityOps.KillWorker(state, w);
    }

    public static void Update(GameState state)
    {
        var cfg = state.Config;
        foreach (var s in state.SoldierList.ToList())
        {
            if (s.Stationed || state.Tick < s.NextAttackTick || !state.Soldiers.ContainsKey(s.Id)) continue;
            var r = state.GetRegiment(s.RegimentId);
            if (r == null) continue;
            var def = cfg.Soldier(s.Type);

            // 1. Anyone in reach gets hit, whatever the order (Soldiers defend themselves).
            var meleeReach = def.Melee.Range + s.Radius + Fix.FromRatio(1, 2);
            if (def.Ranged != null) meleeReach = Fix.Max(meleeReach, Fix.Min(cfg.Combat.RangedMeleeFallbackDistance, def.Ranged.MinRange));
            var adjacent = NearestEnemy(state, s, meleeReach);
            if (adjacent != null && FixVec2.Distance(adjacent.Position, s.Position) <= def.Melee.Range + s.Radius + adjacent.Radius + Fix.FromRatio(1, 5)
                || adjacent != null && def.Ranged != null)
            {
                MeleeStrike(state, s, r, def, adjacent!);
                continue;
            }

            // 1b. Enemy Workers within reach.
            var worker = NearestEnemyWorker(state, s.Owner, s.Position, def.Melee.Range + s.Radius + Fix.FromRatio(1, 2));
            if (worker != null)
            {
                DamageWorker(state, worker, def.Melee.Damage);
                state.RevealAttacker(worker.Owner, s);
                s.NextAttackTick = state.Tick + cfg.SecondsToTicks(def.Melee.CooldownSeconds);
                continue;
            }

            if (r.Order == RegimentOrder.Move) continue;

            // 2. Ranged fire.
            if (def.Ranged != null)
            {
                var range = RangeOf(state, s, def.Ranged, r);
                var cooldownTicks = RangedCooldownTicks(state, def.Ranged, r);
                var spread = SpreadMultiplier(state, r);
                var target = PickRangedTarget(state, s, r, def.Ranged, range);
                if (target != null)
                {
                    var shotSpread = spread;
                    if (EnemyWallBetween(state, s.Owner, s.Position, target.Position)) shotSpread *= cfg.Combat.WallShotSpreadMultiplier;
                    FireAt(state, s.Owner, s.Position, target.Position, target.Velocity, def.Ranged, s.Id, 0, s.Type == SoldierType.Longbowmen, shotSpread);
                    state.RevealAttacker(target.Owner, s);
                    s.NextAttackTick = state.Tick + cooldownTicks;
                    continue;
                }
                var civilian = NearestEnemyWorker(state, s.Owner, s.Position, range);
                if (civilian != null && r.Order is not RegimentOrder.AttackBuilding && state.CanSee(s.Owner, civilian.Position))
                {
                    FireAt(state, s.Owner, s.Position, civilian.Position, civilian.Velocity, def.Ranged, s.Id, 0, s.Type == SoldierType.Longbowmen, spread);
                    state.RevealAttacker(civilian.Owner, s);
                    s.NextAttackTick = state.Tick + cooldownTicks;
                    continue;
                }
                // Arrows don't harm buildings: ranged Soldiers shoot at whoever defends them instead.
                continue;
            }

            // 3. Melee against buildings.
            var siege = state.GetBuilding(r.EngagedBuildingId);
            if (siege != null && siege.DistanceToFootprint(s.Position) <= def.Melee.Range + s.Radius + Fix.FromRatio(1, 2))
            {
                var dmg = def.Melee.Damage * cfg.Combat.BuildingMeleeDamageMultiplier;
                if (RegimentSystem.IsBroken(state, r)) dmg *= cfg.Combat.BrokenDamageDealtMultiplier;
                EntityOps.DamageBuilding(state, siege, dmg, s.Owner);
                state.RevealAttacker(siege.Owner, s);
                FireSystem.IgniteFromDamage(state, siege, dmg, cfg.Combat.FirePerTorchDamage);
                s.NextAttackTick = state.Tick + cfg.SecondsToTicks(def.Melee.CooldownSeconds);
            }
        }
    }

    public enum FireStance : byte
    {
        Normal,
        /// <summary>Hold Ground stance and standing still.</summary>
        Steady,
        /// <summary>Shooting while the Regiment is on the move.</summary>
        Advancing,
    }

    public static FireStance StanceOf(Regiment r) =>
        !r.AnchorArrived ? FireStance.Advancing : r.HoldGround ? FireStance.Steady : FireStance.Normal;

    public static Fix RangeOf(GameState state, Soldier s, AttackDef ranged, Regiment? r = null)
    {
        var tile = s.Position.ToTile();
        bool onHills = state.Map.InBounds(tile) && state.Map.TerrainAt(tile) == Terrain.Hills;
        var range = onHills ? ranged.Range * state.Config.Combat.HillsRangeMultiplier : ranged.Range;
        if (r == null) return range;
        var stance = state.Config.Combat.Stance;
        if (StanceOf(r) == FireStance.Steady) range *= stance.HoldRangeMultiplier;
        // Moving costs range: archers shoot short until they have set themselves again.
        return Fix.Max(ranged.MinRange + Fix.One, range - stance.MovedRangePenaltyTiles * (Fix.One - Settled(state, r)));
    }

    /// <summary>0 right after the Regiment moved, 1 once it has stood still for the settle time.</summary>
    public static Fix Settled(GameState state, Regiment r)
    {
        var settleTicks = state.Config.SecondsToTicks(state.Config.Combat.Stance.SettleSeconds);
        if (settleTicks <= 0) return Fix.One;
        var still = state.Tick - r.LastMovedTick;
        return still >= settleTicks ? Fix.One : Fix.FromRatio((int)System.Math.Max(0, still), (int)settleTicks);
    }

    public static int RangedCooldownTicks(GameState state, AttackDef ranged, Regiment r)
    {
        var stance = state.Config.Combat.Stance;
        var seconds = StanceOf(r) switch
        {
            FireStance.Steady => ranged.CooldownSeconds * stance.HoldCooldownMultiplier,
            FireStance.Advancing => ranged.CooldownSeconds * stance.AdvanceCooldownMultiplier,
            _ => ranged.CooldownSeconds,
        };
        return state.Config.SecondsToTicks(seconds);
    }

    /// <summary>
    /// True if a wall or gate that isn't <paramref name="owner"/>'s stands between the two points. Shooting over
    /// someone else's wall costs range and accuracy; your own walls don't hinder you, so archers behind your own
    /// wall out-shoot attackers in front of it.
    /// </summary>
    public static bool EnemyWallBetween(GameState state, int owner, FixVec2 from, FixVec2 to)
    {
        var map = state.Map;
        int x = from.X.FloorToInt(), y = from.Y.FloorToInt();
        int endX = to.X.FloorToInt(), endY = to.Y.FloorToInt();
        int dx = System.Math.Abs(endX - x), dy = System.Math.Abs(endY - y);
        int stepX = endX > x ? 1 : -1, stepY = endY > y ? 1 : -1;
        int err = dx - dy;
        for (int guard = 0; guard < 512; guard++)
        {
            if (x == endX && y == endY) return false;
            int doubled = err * 2;
            if (doubled > -dy)
            {
                err -= dy;
                x += stepX;
            }
            if (doubled < dx)
            {
                err += dx;
                y += stepY;
            }
            if (!map.InBounds(x, y)) return false;
            if (x == endX && y == endY) return false;
            var wall = state.GetBuilding(map.BuildingIds[map.Index(x, y)]);
            if (wall != null && wall.Owner != owner && (wall.Type is BuildingType.Wall or BuildingType.Gate) && wall.State != BuildingState.UnderConstruction)
                return true;
        }
        return false;
    }

    public static Fix SpreadMultiplier(GameState state, Regiment r)
    {
        var stance = state.Config.Combat.Stance;
        return StanceOf(r) switch
        {
            FireStance.Steady => stance.HoldSpreadMultiplier,
            FireStance.Advancing => stance.AdvanceSpreadMultiplier,
            _ => Fix.One,
        };
    }

    private static Soldier? PickRangedTarget(GameState state, Soldier s, Regiment r, AttackDef ranged, Fix range)
    {
        var engaged = state.GetRegiment(r.EngagedRegimentId);
        if (engaged != null && engaged.SoldierIds.Count > 0)
        {
            // Spread fire across the enemy Regiment instead of everyone shooting the same man.
            int n = engaged.SoldierIds.Count;
            int start = (int)((s.Id * 31L + state.Tick / 40) % n);
            for (int k = 0; k < System.Math.Min(n, 6); k++)
            {
                var t = state.GetSoldier(engaged.SoldierIds[(start + k) % n]);
                if (t == null || t.Stationed || !state.CanSee(s.Owner, t.Position) && !state.IsRevealed(s.Owner, t.RegimentId)) continue;
                var d = FixVec2.Distance(t.Position, s.Position);
                    if (d >= ranged.MinRange && d <= RangeTo(state, s, range, t.Position)) return t;
            }
        }
        if (r.Order is RegimentOrder.Idle or RegimentOrder.AttackMove or RegimentOrder.Follow or RegimentOrder.AttackBuilding)
        {
            var nearest = NearestEnemy(state, s, range);
            if (nearest != null && FixVec2.Distance(nearest.Position, s.Position) >= ranged.MinRange &&
                FixVec2.Distance(nearest.Position, s.Position) <= RangeTo(state, s, range, nearest.Position) &&
                (state.CanSee(s.Owner, nearest.Position) || state.IsRevealed(s.Owner, nearest.RegimentId))) return nearest;
        }
        return null;
    }

    /// <summary>The range that actually applies to a given target: shooting over someone else's wall is shorter.</summary>
    private static Fix RangeTo(GameState state, Soldier s, Fix range, FixVec2 target) =>
        EnemyWallBetween(state, s.Owner, s.Position, target) ? range * state.Config.Combat.WallShotRangeMultiplier : range;

    public static Soldier? NearestEnemy(GameState state, Soldier s, Fix radius) =>
        NearestEnemy(state, s.Owner, s.Position, radius);

    public static Soldier? NearestEnemy(GameState state, int owner, FixVec2 position, Fix radius)
    {
        var nearby = state.QueryBuffer;
        nearby.Clear();
        state.SoldierHash.Query(position, radius, nearby);
        Soldier? best = null;
        Fix bestD = radius * radius;
        foreach (var idx in nearby)
        {
            var o = state.SoldierList[idx];
            if (o.Owner == owner || !state.Soldiers.ContainsKey(o.Id)) continue;
            var d = FixVec2.DistanceSquared(o.Position, position);
            if (d <= bestD)
            {
                bestD = d;
                best = o;
            }
        }
        return best;
    }

    private static void MeleeStrike(GameState state, Soldier attacker, Regiment attackerRegiment, SoldierDef def, Soldier target)
    {
        var cfg = state.Config;
        bool isCharge = false;
        if (def.ChargeMultiplier > Fix.One && state.Tick >= attacker.ChargeReadyTick &&
            attacker.Velocity.LengthSquared >= (def.Speed * cfg.Combat.ChargeMinSpeedFraction) * (def.Speed * cfg.Combat.ChargeMinSpeedFraction))
        {
            isCharge = true;
            attacker.ChargeReadyTick = state.Tick + cfg.SecondsToTicks(cfg.Combat.ChargeCooldownSeconds);
        }

        var attack = new CombatMath.AttackContext(
            def.Melee.Damage, def.Melee.Penetration, IsRanged: false, isCharge, attacker.Position, def,
            cfg.Formation(attackerRegiment.Formation), RegimentSystem.IsBroken(state, attackerRegiment),
            AttackerIsRangedUnitInMelee: def.Ranged != null);
        ApplyHit(state, attack, target);
        state.RevealAttacker(target.Owner, attacker);
        attacker.NextAttackTick = state.Tick + cfg.SecondsToTicks(def.Melee.CooldownSeconds);
    }

    /// <summary>Applies an attack to a Soldier, handling formation facing, cohesion and death.</summary>
    public static void ApplyHit(GameState state, in CombatMath.AttackContext attack, Soldier target)
    {
        var cfg = state.Config;
        var regiment = state.GetRegiment(target.RegimentId);
        var targetDef = cfg.Soldier(target.Type);
        var formation = cfg.Formation(regiment?.Formation ?? FormationType.Line);
        bool formationActive = regiment != null && !RegimentSystem.IsBroken(state, regiment);
        var defender = new CombatMath.DefenderContext(targetDef, target.Position, regiment?.Facing ?? FixVec2.North, formation, formationActive);

        var (damage, zone) = CombatMath.ComputeDamage(cfg, attack, defender);
        target.Hp -= damage;
        if (state.CollectStats)
        {
            var ownKeep = state.GetKeep(target.Owner);
            var enemyKeep = state.GetKeep(1 - target.Owner);
            var region = ownKeep != null && FixVec2.DistanceSquared(target.Position, ownKeep.Center) < Fix.FromInt(2500) ? "home"
                : enemyKeep != null && FixVec2.DistanceSquared(target.Position, enemyKeep.Center) < Fix.FromInt(2500) ? "atEnemyTown" : "mid";
            state.AddStat($"P{target.Owner} {state.DamageSource} {region}", damage);
        }
        if (regiment != null)
        {
            regiment.LastDamagedTick = state.Tick;
            if (zone != HitZone.Front)
                regiment.Cohesion = Fix.Max(Fix.Zero, regiment.Cohesion - cfg.Combat.CohesionLossPerFlankHit * formation.CohesionLossMultiplier);
        }
        if (target.Hp.Raw <= 0) EntityOps.KillSoldier(state, target);
    }

    public static void FireAt(GameState state, int owner, FixVec2 origin, FixVec2 targetPosition, FixVec2 targetVelocity,
        AttackDef ranged, int sourceSoldierId, int sourceBuildingId, bool longbow, Fix? spreadMultiplier = null, Fix? damageMultiplier = null)
    {
        var cfg = state.Config;
        var distance = FixVec2.Distance(origin, targetPosition);
        var speed = Fix.Max(ranged.ProjectileSpeed, Fix.One);
        int flightTicks = System.Math.Max(1, (distance / speed * cfg.Simulation.TicksPerSecond).RoundToInt());
        var lead = targetPosition + targetVelocity * (cfg.TickSeconds * flightTicks);
        var aim = lead + state.Rng.InsideUnitDisc() * (ranged.SpreadPerTile * distance * (spreadMultiplier ?? Fix.One));
        state.Projectiles.Add(new Projectile
        {
            Owner = owner,
            Origin = origin,
            Target = aim,
            LaunchTick = state.Tick,
            ImpactTick = state.Tick + flightTicks,
            Damage = ranged.Damage * (damageMultiplier ?? Fix.One),
            Penetration = ranged.Penetration,
            SourceSoldierId = sourceSoldierId,
            SourceBuildingId = sourceBuildingId,
            IsLongbow = longbow,
        });
    }
}

/// <summary>Resolves projectiles when they land: forest cover, the nearest Soldier at the impact point, or a building.</summary>
/// <summary>
/// Buildings under attack catch fire. Fire burns HP away over time; small fires die down once the attack stops,
/// but a fire past <see cref="Config.CombatConfig.FireSelfSustaining"/> keeps spreading until the building is gone.
/// </summary>
public static class FireSystem
{
    public static void IgniteFromDamage(GameState state, Building b, Fix damage, Fix perDamage)
    {
        if (b.MaxHp.Raw <= 0) return;
        Ignite(state, b, damage * perDamage / b.MaxHp);
    }

    public static void Ignite(GameState state, Building b, Fix amount)
    {
        if (b.State == Entities.BuildingState.Ruin || !state.Buildings.ContainsKey(b.Id)) return;
        b.Fire = Fix.Min(Fix.One, b.Fire + amount);
    }

    public static void Update(GameState state)
    {
        int tps = state.Config.Simulation.TicksPerSecond;
        if (state.Tick % tps != 0) return;
        var cfg = state.Config.Combat;
        var decayDelay = state.Config.SecondsToTicks(cfg.FireDecayDelaySeconds);
        foreach (var b in state.Buildings.Values.ToList())
        {
            if (b.Fire.Raw <= 0) continue;
            if (b.State == Entities.BuildingState.Ruin)
            {
                b.Fire = Fix.Zero;
                continue;
            }
            if (b.Fire >= cfg.FireSelfSustaining) b.Fire = Fix.Min(Fix.One, b.Fire + cfg.FireGrowthPerSecond);
            else if (state.Tick - b.LastAttackedTick >= decayDelay) b.Fire = Fix.Max(Fix.Zero, b.Fire - cfg.FireDecayPerSecond);
            if (b.Fire.Raw <= 0) continue;
            var damage = b.MaxHp * cfg.FireDamageFractionPerSecond * b.Fire;
            if (damage.Raw <= 0) continue;
            // Burning isn't a fresh attack: keep LastAttackedTick for the decay timer.
            var lastAttacked = b.LastAttackedTick;
            EntityOps.DamageBuilding(state, b, damage, b.Owner);
            b.LastAttackedTick = lastAttacked;
        }
    }
}

public static class ProjectileSystem
{
    public static void Update(GameState state)
    {
        var cfg = state.Config.Combat;
        var projectiles = state.Projectiles;
        int write = 0;
        for (int i = 0; i < projectiles.Count; i++)
        {
            var p = projectiles[i];
            if (state.Tick < p.ImpactTick)
            {
                projectiles[write++] = p;
                continue;
            }
            Resolve(state, cfg, p);
        }
        projectiles.RemoveRange(write, projectiles.Count - write);
    }

    private static void Resolve(GameState state, CombatConfig cfg, Projectile p)
    {
        var map = state.Map;
        var tile = p.Target.ToTile();
        if (!map.InBounds(tile)) return;
        if (map.TerrainAt(tile) == Terrain.Forest && state.Rng.Chance(cfg.ForestArrowBlockChance)) return;

        var nearby = state.QueryBuffer;
        nearby.Clear();
        state.SoldierHash.Query(p.Target, cfg.ProjectileHitRadius + Fix.One, nearby);
        Soldier? hit = null;
        Fix bestD = Fix.MaxValue;
        foreach (var idx in nearby)
        {
            var s = state.SoldierList[idx];
            if (!state.Soldiers.ContainsKey(s.Id)) continue;
            if (!cfg.FriendlyFire && s.Owner == p.Owner) continue;
            if (s.Id == p.SourceSoldierId) continue;
            var reach = cfg.ProjectileHitRadius + s.Radius;
            var d = FixVec2.DistanceSquared(s.Position, p.Target);
            if (d <= reach * reach && d < bestD)
            {
                bestD = d;
                hit = s;
            }
        }

        if (hit != null)
        {
            var attack = new CombatMath.AttackContext(p.Damage, p.Penetration, IsRanged: true, IsCharge: false, p.Origin,
                AttackerDef: null, AttackerFormation: null, AttackerBroken: false, AttackerIsRangedUnitInMelee: false);
            state.DamageSource = p.SourceBuildingId != 0 ? "fort" : "arrow";
            CombatSystem.ApplyHit(state, attack, hit);
            state.DamageSource = "melee";
            return;
        }

        var nearbyWorkers = state.QueryBuffer;
        nearbyWorkers.Clear();
        state.WorkerHash.Query(p.Target, cfg.ProjectileHitRadius + Fix.One, nearbyWorkers);
        foreach (var idx in nearbyWorkers)
        {
            var w = state.WorkerList[idx];
            if (!state.Workers.ContainsKey(w.Id) || (!cfg.FriendlyFire && w.Owner == p.Owner)) continue;
            var reach = cfg.ProjectileHitRadius + w.Radius;
            if (FixVec2.DistanceSquared(w.Position, p.Target) > reach * reach) continue;
            CombatSystem.DamageWorker(state, w, p.Damage);
            return;
        }

        var building = state.GetBuilding(map.BuildingIds[map.Index(tile)]);
        if (building == null || building.Owner == p.Owner || building.State == Entities.BuildingState.Ruin) return;
        // Arrows can pick off the shooters on a Stronghold's walls, but never damage the building itself:
        // only melee attackers with torches bring buildings down.
        if (building.Type == BuildingType.Stronghold) FortSystem.TryHitShooter(state, building, p);
    }
}
