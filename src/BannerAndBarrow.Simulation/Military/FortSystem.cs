using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Economy;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Territory;

namespace BannerAndBarrow.Simulation.Military;

/// <summary>
/// Outposts shelter Stationed Soldiers and raise alerts. Strongholds also fight back: a small free Garrison,
/// plus up to N active shooter slots filled by Stationed ranged Soldiers (faster) and then by Stationed melee
/// Soldiers throwing a weak, short-ranged missile.
/// </summary>
public static class FortSystem
{
    public static int CapacityRegiments(GameState state, Building fort) =>
        fort.Type == BuildingType.Stronghold ? state.Config.Forts.StrongholdCapacityRegiments : state.Config.Forts.OutpostCapacityRegiments;

    public static bool TryStation(GameState state, Building fort, Regiment r)
    {
        var data = fort.Fort!;
        if (fort.State is BuildingState.UnderConstruction or BuildingState.Ruin) return false;
        if (data.StationedRegimentIds.Count >= CapacityRegiments(state, fort))
        {
            state.AddEvent(r.Owner, GameEventKind.CommandRejected, $"{fort.Type} is full.", fort.Center);
            return false;
        }
        data.StationedRegimentIds.Add(r.Id);
        r.StationedInBuildingId = fort.Id;
        r.Order = RegimentOrder.Idle;
        r.EngagedRegimentId = 0;
        r.EngagedBuildingId = 0;
        r.Anchor = fort.Center;
        foreach (var id in r.SoldierIds)
        {
            var s = state.GetSoldier(id);
            if (s == null) continue;
            s.Stationed = true;
            s.NavActive = false;
            s.Position = fort.Center;
            s.PreviousPosition = fort.Center;
            s.Velocity = FixVec2.Zero;
            s.ClearGoal();
        }
        state.MarkAgentsDirty();
        return true;
    }

    public static void Eject(GameState state, Building fort, Regiment r)
    {
        fort.Fort?.StationedRegimentIds.Remove(r.Id);
        r.StationedInBuildingId = 0;
        var access = fort.GetAccessTiles(state.Map);
        var exit = access.Length > 0 ? access[r.Id % access.Length].Center : fort.Center + new FixVec2(Fix.Zero, Fix.FromInt(fort.Size));
        int i = 0;
        foreach (var id in r.SoldierIds)
        {
            var s = state.GetSoldier(id);
            if (s == null) continue;
            s.Stationed = false;
            s.NavActive = true;
            var jitter = FlowFieldDirections(i++) * Fix.FromRatio(3, 10);
            s.Position = exit + jitter;
            s.PreviousPosition = s.Position;
        }
        r.Anchor = exit;
        var facing = (exit - fort.Center).Normalized();
        RegimentSystem.SetDestination(state, r, exit + facing * 2, facing);
        r.Order = RegimentOrder.Idle;
        state.MarkAgentsDirty();
    }

    private static FixVec2 FlowFieldDirections(int i) => Pathfinding.FlowField.Directions8[i % 8] * Fix.FromInt(1 + i / 8);

    public static void UnstationAll(GameState state, Building fort)
    {
        if (fort.Fort == null) return;
        foreach (var id in fort.Fort.StationedRegimentIds.ToList())
        {
            var r = state.GetRegiment(id);
            if (r != null) Eject(state, fort, r);
        }
    }

    public static bool StartUpgrade(GameState state, Building fort, out string reason)
    {
        reason = "";
        if (fort.Type != BuildingType.Outpost || fort.State != BuildingState.Active)
        {
            reason = "Only a finished Outpost can be upgraded.";
            return false;
        }
        var cost = StockOps.ToArray(state.Config.Forts.StrongholdUpgradeCost);
        if (!StockOps.TryWithdraw(state, fort.Owner, fort.Center, cost))
        {
            reason = $"Need {StockOps.Describe(cost)}.";
            return false;
        }
        fort.State = BuildingState.Upgrading;
        fort.Fort!.UpgradeCompleteTick = state.Tick + state.Config.SecondsToTicks(state.Config.Forts.StrongholdUpgradeSeconds);
        return true;
    }

    public static void Update(GameState state)
    {
        foreach (var fort in state.Buildings.Values.ToList())
        {
            if (!fort.IsFort || fort.Fort == null || !state.Buildings.ContainsKey(fort.Id)) continue;
            if (fort.State == BuildingState.Upgrading && state.Tick >= fort.Fort.UpgradeCompleteTick) CompleteUpgrade(state, fort);
            if (fort.State is not (BuildingState.Active or BuildingState.Upgrading)) continue;

            if (state.Tick % 10 == fort.Id % 10) CheckAlert(state, fort);
            if (fort.Type == BuildingType.Stronghold && fort.State == BuildingState.Active)
            {
                RespawnGarrison(state, fort);
                Shoot(state, fort);
            }
        }
    }

    private static void CompleteUpgrade(GameState state, Building fort)
    {
        var cfg = state.Config.Forts;
        fort.Type = BuildingType.Stronghold;
        fort.State = BuildingState.Active;
        var extra = Fix.FromInt(cfg.StrongholdHp) - fort.MaxHp;
        fort.MaxHp = Fix.FromInt(cfg.StrongholdHp);
        fort.Hp = Fix.Min(fort.MaxHp, fort.Hp + Fix.Max(Fix.Zero, extra));
        fort.Fort!.GarrisonAlive = cfg.GarrisonSize;
        fort.Fort.GarrisonNextShotTick = new long[cfg.GarrisonSize];
        state.AddEvent(fort.Owner, GameEventKind.Info, "Stronghold completed.", fort.Center);
    }

    private static void CheckAlert(GameState state, Building fort)
    {
        if (state.Tick < fort.Fort!.NextAlertTick) return;
        var radius = Fix.FromInt(PresenceSystem.PresenceRadius(state, fort));
        var enemy = CombatSystem.NearestEnemy(state, fort.Owner, fort.Center, radius);
        if (enemy == null) return;
        fort.Fort.NextAlertTick = state.Tick + state.Config.SecondsToTicks(state.Config.Forts.AlertCooldownSeconds);
        state.AddEvent(fort.Owner, GameEventKind.Alert, $"Enemy spotted near your {fort.Type}!", fort.Center);
    }

    private static void RespawnGarrison(GameState state, Building fort)
    {
        var cfg = state.Config.Forts;
        var data = fort.Fort!;
        if (data.GarrisonNextShotTick.Length != cfg.GarrisonSize) data.GarrisonNextShotTick = new long[cfg.GarrisonSize];
        if (data.GarrisonAlive >= cfg.GarrisonSize) return;
        if (state.Tick - fort.LastAttackedTick < state.Config.SecondsToTicks(cfg.GarrisonQuietSeconds)) return;
        if (state.Tick < data.GarrisonNextRespawnTick) return;
        data.GarrisonAlive++;
        data.GarrisonNextRespawnTick = state.Tick + state.Config.SecondsToTicks(cfg.GarrisonRespawnSeconds);
    }

    /// <summary>Stationed Soldiers occupying shooter slots: ranged first, then melee.</summary>
    public static List<Soldier> ActiveShooters(GameState state, Building fort)
    {
        var slots = state.Config.Forts.StrongholdActiveShooterSlots;
        var ranged = new List<Soldier>();
        var melee = new List<Soldier>();
        foreach (var rid in fort.Fort!.StationedRegimentIds)
        {
            var r = state.GetRegiment(rid);
            if (r == null) continue;
            bool isRanged = state.Config.Soldier(r.Type).Ranged != null;
            foreach (var sid in r.SoldierIds)
            {
                var s = state.GetSoldier(sid);
                if (s != null) (isRanged ? ranged : melee).Add(s);
            }
        }
        var result = ranged.Take(slots).ToList();
        if (result.Count < slots) result.AddRange(melee.Take(slots - result.Count));
        return result;
    }

    private static void Shoot(GameState state, Building fort)
    {
        var cfg = state.Config;
        var forts = cfg.Forts;
        var data = fort.Fort!;
        var shooters = ActiveShooters(state, fort);

        var maxRange = cfg.Soldier(forts.GarrisonType).Ranged?.Range ?? Fix.FromInt(10);
        foreach (var s in shooters)
        {
            var ranged = cfg.Soldier(s.Type).Ranged;
            if (ranged != null) maxRange = Fix.Max(maxRange, ranged.Range);
        }

        var nearby = state.QueryBuffer;
        nearby.Clear();
        state.SoldierHash.Query(fort.Center, maxRange, nearby);
        var targets = new List<(Fix Dist, Soldier S)>();
        foreach (var idx in nearby)
        {
            var o = state.SoldierList[idx];
            if (o.Owner == fort.Owner || !state.Soldiers.ContainsKey(o.Id)) continue;
            var d = FixVec2.Distance(o.Position, fort.Center);
            if (d <= maxRange && (state.CanSee(fort.Owner, o.Position) || state.IsRevealed(fort.Owner, o.RegimentId))) targets.Add((d, o));
        }
        if (targets.Count == 0) return;
        targets.Sort((a, b) => a.Dist != b.Dist ? a.Dist.CompareTo(b.Dist) : a.S.Id.CompareTo(b.S.Id));
        if (targets.Count > 12) targets.RemoveRange(12, targets.Count - 12);

        int shot = 0;
        var garrisonAttack = cfg.Soldier(forts.GarrisonType).Ranged;
        if (garrisonAttack != null)
        {
            for (int g = 0; g < data.GarrisonAlive && g < data.GarrisonNextShotTick.Length; g++)
            {
                if (state.Tick < data.GarrisonNextShotTick[g]) continue;
                var target = PickTarget(targets, garrisonAttack.Range, shot++);
                if (target == null) continue;
                CombatSystem.FireAt(state, fort.Owner, fort.Center, target.Position, target.Velocity, garrisonAttack, 0, fort.Id, false, damageMultiplier: forts.ShooterDamageMultiplier);
                state.RevealAttacker(target.Owner, null, fort);
                data.GarrisonNextShotTick[g] = state.Tick + cfg.SecondsToTicks(forts.ShooterCooldownSeconds);
            }
        }

        foreach (var s in shooters)
        {
            if (state.Tick < s.NextAttackTick) continue;
            var ranged = cfg.Soldier(s.Type).Ranged;
            var attack = ranged ?? forts.MeleeMissile;
            var target = PickTarget(targets, attack.Range, shot++);
            if (target == null) continue;
            CombatSystem.FireAt(state, fort.Owner, fort.Center, target.Position, target.Velocity, attack, s.Id, fort.Id, s.Type == SoldierType.Longbowmen,
                damageMultiplier: ranged != null ? forts.ShooterDamageMultiplier : null);
            state.RevealAttacker(target.Owner, null, fort);
            var cooldown = ranged != null ? forts.ShooterCooldownSeconds / forts.StationedFireRateMultiplier : attack.CooldownSeconds;
            s.NextAttackTick = state.Tick + cfg.SecondsToTicks(cooldown);
        }
    }

    private static Soldier? PickTarget(List<(Fix Dist, Soldier S)> targets, Fix range, int salt)
    {
        int inRange = 0;
        while (inRange < targets.Count && targets[inRange].Dist <= range) inRange++;
        return inRange == 0 ? null : targets[salt % inRange].S;
    }

    /// <summary>A projectile hit a Stronghold: it may kill a shooter on the walls instead of damaging them.</summary>
    public static bool TryHitShooter(GameState state, Building fort, Projectile p)
    {
        var data = fort.Fort;
        if (data == null) return false;
        fort.LastAttackedTick = state.Tick;
        var shooters = ActiveShooters(state, fort);
        int total = data.GarrisonAlive + shooters.Count;
        if (total == 0 || !state.Rng.Chance(state.Config.Forts.ShooterHitChance)) return false;

        int pick = state.Rng.Next(total);
        if (pick < data.GarrisonAlive)
        {
            data.GarrisonAlive--;
            data.GarrisonNextRespawnTick = state.Tick + state.Config.SecondsToTicks(state.Config.Forts.GarrisonRespawnSeconds);
            return true;
        }
        var victim = shooters[pick - data.GarrisonAlive];
        var def = state.Config.Soldier(victim.Type);
        var damage = p.Damage * CombatMath.PenetrationFactor(state.Config.Combat, p.Penetration, def.Armour) * state.Config.Forts.StationedDamageTakenMultiplier;
        victim.Hp -= damage;
        if (victim.Hp.Raw <= 0) EntityOps.KillSoldier(state, victim);
        return true;
    }
}
