using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Simulation.Economy;

/// <summary>
/// Make to order, then make to stock. Orders are the goods needed by queued recruits (not yet paid for) and
/// pending upgrades. A manufacturer first makes whatever is ordered but not already on its way, then tops up
/// each product to <see cref="EconomyConfig.GoodsStockTarget"/>, then idles.
/// </summary>
public static class Manufacturing
{
    public static int[] Orders(GameState state, int player)
    {
        var orders = new int[Resources.Count];
        foreach (var b in state.Buildings.Values)
        {
            if (b.Owner != player || b.Recruitment == null) continue;
            // Only the recruit that pays next is an order: counting the whole queue lets one workshop spend scarce
            // Iron on goods for recruits far down the queue while the first one waits for something else.
            var rec = b.Recruitment;
            int next = rec.Paid ? 1 : 0;
            if (next >= rec.Queue.Count) continue;
            foreach (var (r, amount) in state.Config.Soldier(rec.Queue[next]).RecruitCost)
                if (Resources.IsGoods(r)) orders[(int)r] += amount;
        }
        foreach (var id in state.Players[player].PendingUpgrades)
        {
            if (state.GetRegiment(id) is not { } regiment || UpgradeTarget(state, regiment.Type) is not { } target) continue;
            foreach (var (r, amount) in state.Config.Soldier(target).UpgradeCostPerSoldier)
                orders[(int)r] += amount * regiment.SoldierIds.Count;
        }
        return orders;
    }

    /// <summary>Goods the Player has or will soon have: stored, piled at workshops, being carried, or being made.</summary>
    public static int[] Pipeline(GameState state, int player)
    {
        var pipeline = state.TotalStock(player);
        foreach (var b in state.Buildings.Values)
            if (b.Owner == player)
                for (int i = 0; i < Resources.Count; i++) pipeline[i] += b.OutputStock[i];
        foreach (var w in state.Workers.Values)
        {
            if (w.Owner != player) continue;
            if (w.CarryAmount > 0 && Resources.IsGoods(w.CarryType) && w.Job == WorkerJob.Producer) pipeline[(int)w.CarryType] += w.CarryAmount;
            if (w.ActiveRecipe >= 0 && state.GetBuilding(w.WorkplaceId) is { } workplace)
            {
                var recipes = state.Config.Building(workplace.Type).Recipes;
                if (w.ActiveRecipe < recipes.Count) pipeline[(int)recipes[w.ActiveRecipe].Output] += recipes[w.ActiveRecipe].Amount;
            }
        }
        return pipeline;
    }

    /// <summary>Which recipe to make next, or -1 to idle. Orders first (largest shortfall), then the lowest stock.</summary>
    public static int ChooseRecipe(GameState state, Building workshop)
    {
        var recipes = state.Config.Building(workshop.Type).Recipes;
        if (recipes.Count == 0) return -1;
        var orders = Orders(state, workshop.Owner);
        var pipeline = Pipeline(state, workshop.Owner);

        int best = -1, bestShortfall = 0;
        for (int i = 0; i < recipes.Count; i++)
        {
            int shortfall = orders[(int)recipes[i].Output] - pipeline[(int)recipes[i].Output];
            if (shortfall > bestShortfall)
            {
                bestShortfall = shortfall;
                best = i;
            }
        }
        if (best >= 0) return best;

        // Orders covered: make to stock, but only up to the target (stock beyond outstanding orders).
        int target = state.Config.Economy.GoodsStockTarget;
        int lowest = int.MaxValue;
        for (int i = 0; i < recipes.Count; i++)
        {
            int spare = pipeline[(int)recipes[i].Output] - orders[(int)recipes[i].Output];
            if (spare < target && spare < lowest)
            {
                lowest = spare;
                best = i;
            }
        }
        return best;
    }

    public static SoldierType? UpgradeTarget(GameState state, SoldierType from)
    {
        foreach (var (type, def) in state.Config.Soldiers)
            if (def.UpgradeFrom == from) return type;
        return null;
    }
}

/// <summary>Turns Regiments into their upgraded type (Spearmen into Pikemen) next to the recruiting building.</summary>
public static class UpgradeSystem
{
    public static void Update(GameState state)
    {
        if (state.Tick % state.Config.Simulation.TicksPerSecond != 0) return;
        foreach (var player in state.Players)
        {
            foreach (var id in player.PendingUpgrades.ToList())
            {
                var r = state.GetRegiment(id);
                if (r == null || r.Owner != player.Index || Manufacturing.UpgradeTarget(state, r.Type) == null)
                {
                    player.PendingUpgrades.Remove(id);
                    continue;
                }
                // Done, or moved away from the building: the order is dropped.
                if (TryUpgrade(state, r, out var reason) || !reason.StartsWith("Waiting")) player.PendingUpgrades.Remove(id);
            }
        }
    }

    /// <summary>Upgrades immediately if near the right building and the goods are in stock.</summary>
    public static bool TryUpgrade(GameState state, Regiment r, out string reason)
    {
        reason = "";
        var target = Manufacturing.UpgradeTarget(state, r.Type);
        if (target == null)
        {
            reason = $"{r.Type} can't be upgraded.";
            return false;
        }
        var targetDef = state.Config.Soldier(target.Value);
        var centroid = state.RegimentCentroid(r);
        var radius = Fix.FromInt(state.Config.Economy.UpgradeRadius);
        bool nearBuilding = state.Buildings.Values.Any(b => b.Owner == r.Owner && b.Type == targetDef.RecruitedAt && b.IsActive &&
                                                             b.DistanceToFootprint(centroid) <= radius);
        if (!nearBuilding)
        {
            reason = $"Bring the Regiment next to a {targetDef.RecruitedAt} to upgrade it.";
            return false;
        }
        var cost = new int[Resources.Count];
        foreach (var (res, amount) in targetDef.UpgradeCostPerSoldier) cost[(int)res] = amount * r.SoldierIds.Count;
        if (!StockOps.TryWithdraw(state, r.Owner, centroid, cost))
        {
            reason = $"Waiting for {StockOps.Describe(StockOps.Missing(state, r.Owner, cost))}.";
            return false;
        }
        Convert(state, r, target.Value);
        state.AddEvent(r.Owner, GameEventKind.Info, $"Regiment upgraded to {target.Value}.", centroid);
        return true;
    }

    private static void Convert(GameState state, Regiment r, SoldierType to)
    {
        var oldDef = state.Config.Soldier(r.Type);
        var newDef = state.Config.Soldier(to);
        r.Type = to;
        if (!newDef.Formations.Contains(r.Formation) && newDef.Formations.Count > 0) r.Formation = newDef.Formations[0];
        foreach (var id in r.SoldierIds)
        {
            if (state.GetSoldier(id) is not { } s) continue;
            s.Type = to;
            s.Hp = s.Hp * newDef.Hp / oldDef.Hp;
            s.MaxSpeed = newDef.Speed;
            s.Radius = newDef.Radius;
        }
    }
}
