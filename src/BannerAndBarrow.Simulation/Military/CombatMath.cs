using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Military;

public enum HitZone : byte
{
    Front,
    Flank,
    Rear,
}

/// <summary>Pure damage rules, kept free of state so they can be unit tested and tuned.</summary>
public static class CombatMath
{
    public static Fix PenetrationFactor(CombatConfig cfg, Fix penetration, Fix armour) =>
        Fix.Clamp(Fix.One + (penetration - armour) * cfg.PenetrationScale, cfg.MinDamageFactor, cfg.MaxDamageFactor);

    /// <summary>Which side of the defending Regiment the attack comes from.</summary>
    public static HitZone Zone(CombatConfig cfg, FixVec2 defenderFacing, FixVec2 defenderPosition, FixVec2 attackerPosition)
    {
        var toAttacker = (attackerPosition - defenderPosition).Normalized();
        if (toAttacker.IsZero || defenderFacing.IsZero) return HitZone.Front;
        var dot = FixVec2.Dot(toAttacker, defenderFacing);
        if (dot >= cfg.FrontDotThreshold) return HitZone.Front;
        if (dot <= cfg.RearDotThreshold) return HitZone.Rear;
        return HitZone.Flank;
    }

    public readonly record struct AttackContext(
        Fix BaseDamage,
        Fix Penetration,
        bool IsRanged,
        bool IsCharge,
        FixVec2 AttackerPosition,
        SoldierDef? AttackerDef,
        FormationDef? AttackerFormation,
        bool AttackerBroken,
        bool AttackerIsRangedUnitInMelee);

    public readonly record struct DefenderContext(
        SoldierDef Def,
        FixVec2 Position,
        FixVec2 RegimentFacing,
        FormationDef Formation,
        bool FormationActive);

    public static (Fix Damage, HitZone Zone) ComputeDamage(GameConfigView cfg, in AttackContext attack, in DefenderContext defender)
    {
        var combat = cfg.Combat;
        var damage = attack.BaseDamage * PenetrationFactor(combat, attack.Penetration, defender.Def.Armour);

        var zone = defender.FormationActive
            ? Zone(combat, defender.RegimentFacing, defender.Position, attack.AttackerPosition)
            : HitZone.Flank;

        var f = defender.Formation;
        if (defender.FormationActive)
        {
            damage *= attack.IsRanged ? f.AllRangedMultiplier : f.AllMeleeMultiplier;
            damage *= zone switch
            {
                HitZone.Front => attack.IsRanged ? f.FrontRangedMultiplier : f.FrontMeleeMultiplier,
                HitZone.Flank => f.FlankMultiplier,
                _ => f.RearMultiplier,
            };
        }

        if (attack.IsRanged && defender.Def.Shield) damage *= combat.ShieldRangedDamageMultiplier;

        if (!attack.IsRanged)
        {
            if (attack.AttackerFormation != null) damage *= attack.AttackerFormation.OutgoingMeleeMultiplier;
            if (attack.AttackerDef != null && defender.Def.IsMounted) damage *= attack.AttackerDef.BonusVsMounted;
            if (attack.IsCharge && attack.AttackerDef != null)
            {
                var chargeBonus = attack.AttackerDef.ChargeMultiplier - Fix.One;
                if (attack.AttackerFormation != null) chargeBonus *= attack.AttackerFormation.ChargeBonusMultiplier;
                if (defender.FormationActive && zone == HitZone.Front) chargeBonus *= f.AntiChargeMultiplier;
                damage *= Fix.One + Fix.Max(Fix.Zero, chargeBonus);
            }
            if (attack.AttackerIsRangedUnitInMelee) damage *= combat.RangedMeleePenalty;
        }

        if (attack.AttackerBroken) damage *= combat.BrokenDamageDealtMultiplier;
        return (Fix.Max(Fix.Zero, damage), zone);
    }
}

/// <summary>Narrow view so CombatMath only depends on the config sections it uses.</summary>
public readonly record struct GameConfigView(CombatConfig Combat)
{
    public static implicit operator GameConfigView(GameConfig c) => new(c.Combat);
}
