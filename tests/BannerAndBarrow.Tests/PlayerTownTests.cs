using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Commands;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.World;
using Xunit.Abstractions;

namespace BannerAndBarrow.Tests;

/// <summary>Reproduces how a human player builds: buildings packed tightly around the Keep, no roads.</summary>
public class PlayerTownTests
{
    private readonly ITestOutputHelper _output;

    public PlayerTownTests(ITestOutputHelper output) => _output = output;

    private static (GameState State, Match Match, Building Keep) PackedTown()
    {
        var state = TestWorld.FlatState(80, 60, players: 1);
        var map = state.Map;
        for (int y = 18; y <= 22; y++)
        for (int x = 28; x <= 32; x++)
        {
            int i = map.Index(x, y);
            map.Terrain[i] = Terrain.Hills;
            map.Deposits[i] = ResourceType.Stone;
            map.ResourceAmount[i] = 400;
        }
        var keep = TestWorld.AddKeep(state, 0, new TileCoord(10, 10));
        foreach (var (r, a) in state.Config.Economy.StartingStock) keep.Store!.Stock[(int)r] = a;
        var match = TestWorld.MatchFor(state);

        // Houses shoulder to shoulder against the Keep, a Farm touching it, a Quarry, a second Storehouse.
        foreach (var origin in new[] { new TileCoord(14, 10), new TileCoord(16, 10), new TileCoord(14, 12), new TileCoord(16, 12), new TileCoord(18, 10) })
            match.Commands.Enqueue(new PlaceBuildingCommand(0, BuildingType.House, origin));
        match.Commands.Enqueue(new PlaceBuildingCommand(0, BuildingType.Farm, new TileCoord(10, 14)));
        match.Commands.Enqueue(new PlaceBuildingCommand(0, BuildingType.Quarry, new TileCoord(23, 18)));
        match.Commands.Enqueue(new PlaceBuildingCommand(0, BuildingType.Storehouse, new TileCoord(20, 14)));
        return (state, match, keep);
    }

    private string Describe(GameState state)
    {
        var lines = new List<string>();
        foreach (var b in state.Buildings.Values)
            lines.Add($"{b.Type}#{b.Id} {b.State} at {b.Origin} workers={b.WorkerIds.Count} access={b.GetAccessTiles(state.Map).Length} " +
                      $"delivered={string.Join(",", b.Delivered)}/{string.Join(",", b.Required)} work={b.WorkDone}/{b.WorkRequired}" +
                      (b.Store != null ? $" stock={string.Join(",", b.Store.Stock)} carriers={b.Store.CarrierIds.Count}/{b.Store.CarriersPurchased}" : ""));
        foreach (var g in state.Workers.Values.GroupBy(w => (w.Job, w.Task)))
            lines.Add($"workers {g.Key.Job}/{g.Key.Task}: {g.Count()}");
        lines.Add($"workers {state.WorkerCount(0)}/{state.WorkerRoom(0)}");
        foreach (var w in state.Workers.Values.Where(w => w.Task is WorkerTask.GoToPickup or WorkerTask.GoToSite or WorkerTask.GoToDeliver))
            lines.Add($"  worker#{w.Id} {w.Job}/{w.Task} pos={w.Position} goal={w.GoalPoint} kind={w.GoalKind} arrived={w.Arrived} stuck={w.StuckTicks} site={w.TargetSiteId} pickup={w.PickupStorehouseId} target={w.TargetBuildingId}");
        foreach (var e in state.Events.Where(e => e.Kind == GameEventKind.CommandRejected)) lines.Add("rejected: " + e.Message);
        var text = string.Join("\n", lines);
        _output.WriteLine(text);
        return text;
    }

    [Fact]
    public void Packed_town_gets_built_staffed_and_supplied()
    {
        var (state, match, keep) = PackedTown();
        int stoneBefore = state.TotalStock(0)[(int)ResourceType.Stone];
        match.Run(20 * 60 * 8);
        var report = Describe(state);

        Assert.All(state.Buildings.Values, b => Assert.True(b.IsActive, $"{b.Type} at {b.Origin} not finished\n{report}"));
        var farm = state.Buildings.Values.Single(b => b.Type == BuildingType.Farm);
        Assert.True(farm.WorkerIds.Count == 1, $"Farm has no farmer\n{report}");
        var store = state.Buildings.Values.Single(b => b.Type == BuildingType.Storehouse);
        Assert.True(store.Store!.CarrierIds.Count == store.Store.CarriersPurchased, $"Storehouse carriers not assigned\n{report}");
        Assert.True(state.WorkerCount(0) > 10, $"Worker count never grew\n{report}");
        Assert.True(state.TotalStock(0)[(int)ResourceType.Food] > state.Config.Economy.StartingStock[ResourceType.Food] - 1, $"Farm food never arrived\n{report}");
        var quarry = state.Buildings.Values.Single(b => b.Type == BuildingType.Quarry);
        Assert.True(state.TotalStock(0)[(int)ResourceType.Stone] > stoneBefore - 60, $"Quarry stone never arrived\n{report}");
        _ = keep;
        _ = quarry;
    }
}
