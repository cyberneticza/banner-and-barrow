using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Config;
using BannerAndBarrow.Simulation.Data;
using BannerAndBarrow.Simulation.Entities;
using BannerAndBarrow.Simulation.Math;
using BannerAndBarrow.Simulation.Pathfinding;
using BannerAndBarrow.Simulation.Territory;
using BannerAndBarrow.Simulation.World;

namespace BannerAndBarrow.Tests;

/// <summary>Builders for small hand-made worlds, so rules can be tested without procedural maps.</summary>
public static class TestWorld
{
    private static GameConfig? _config;

    public static GameConfig Config()
    {
        if (_config != null) return _config;
        var dir = ConfigLoader.FindConfigDirectory(AppContext.BaseDirectory)
                  ?? throw new InvalidOperationException("config/ not copied to test output");
        _config = ConfigLoader.LoadFromDirectory(dir);
        return _config;
    }

    /// <summary>A flat grass map with no Keeps. Players still exist so buildings can be owned.</summary>
    public static GameState FlatState(int width = 96, int height = 64, int players = 2)
    {
        var config = Config();
        var map = new TileMap(width, height, config.Movement) { KeepSites = new[] { new TileCoord(4, 4), new TileCoord(width - 8, height - 8) } };
        return new GameState(config, map, players, seed: 42);
    }

    public static Building AddKeep(GameState state, int player, TileCoord origin)
    {
        var keep = EntityOps.CreateBuilding(state, player, BuildingType.Keep, origin, underConstruction: false);
        state.Players[player].KeepId = keep.Id;
        // Workers cost Food to arrive and eat to keep working.
        keep.Store!.Stock[(int)ResourceType.Food] = 100;
        TerritorySystem.Recompute(state);
        return keep;
    }

    public static Building AddActive(GameState state, int player, BuildingType type, TileCoord origin)
    {
        var b = EntityOps.CreateBuilding(state, player, type, origin, underConstruction: false);
        TerritorySystem.Recompute(state);
        return b;
    }

    public static Match MatchFor(GameState state) => new(state);

    /// <summary>Simple nav grid from an ASCII picture: '#' = wall, anything else = open.</summary>
    public sealed class AsciiGrid : INavGrid
    {
        private readonly bool[] _open;
        public int Width { get; }
        public int Height { get; }
        public int MajorVersion => 1;
        public int MinorVersion => 1;

        public AsciiGrid(params string[] rows)
        {
            Height = rows.Length;
            Width = rows[0].Length;
            _open = new bool[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                _open[y * Width + x] = rows[y][x] != '#';
        }

        public bool IsPassable(int x, int y) => _open[y * Width + x];
        public Fix GetSpeedMultiplier(int x, int y, MovementClass movementClass) => Fix.One;
    }
}
