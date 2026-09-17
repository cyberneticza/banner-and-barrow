# Technical specification

How Banner & Barrow is built: the shape of the code, the rules it holds itself to, and how each system works.
For the domain vocabulary see [CONTEXT.md](../CONTEXT.md); for day-to-day working agreements see

## 1. Overview

| | |
|---|---|
| Language / runtime | C# 13, .NET 9 |
| Rendering | MonoGame 3.8 (DesktopGL, OpenGL + SDL2 + OpenAL) |
| Solution | `BannerAndBarrow.slnx` |
| Tests | xUnit, 85 tests (`dotnet test`) |
| Target | Windows x64 desktop; the simulation itself is platform-agnostic |
| Distribution | Self-contained single-file publish + Velopack per-user installer |

### Projects

| Project | Depends on | Role |
|---|---|---|
| `BannerAndBarrow.Simulation` | nothing (no MonoGame) | The whole game: rules, economy, combat, pathfinding, AI, config |
| `BannerAndBarrow.Game` | Simulation, MonoGame | Window, rendering, input, sound, menus. Issues commands; never mutates state directly |
| `BannerAndBarrow.Sandbox` | Simulation | Headless runner: AI-vs-AI matches, map dumps, pathfinding benchmarks, diagnostics |
| `BannerAndBarrow.Tests` | Simulation | xUnit tests, including a build-breaking check for floating point in the simulation |

The split is the main architectural decision: the simulation is a pure, deterministic library that can be run
thousands of times faster than real time in tests and balance runs, and the game project is a thin shell around
it.

## 2. Determinism

The simulation must produce identical results from identical inputs on any machine
([ADR 0001](adr/0001-fixed-point-simulation.md)). This makes tests reliable, balance runs comparable and future
lockstep multiplayer possible.

- **Fixed point, not floating point.** `Fix` is a 64-bit value with 16 fractional bits; `FixVec2` is its vector.
  One unit is one Tile. `NoFloatingPointTests` scans the simulation assembly and fails the build if a `float` or
  `double` appears there. Floats exist only in `BannerAndBarrow.Game`, via `FixExtensions`.
- **Seeded randomness only.** Everything random comes from `GameState.Rng` (a deterministic PRNG). No
  `Random.Shared`, no time-based seeds.
- **Stable iteration.** Entities live in `SortedDictionary`, so iteration order never depends on hashing.
- **No static scratch state.** Shared buffers live on `GameState` (e.g. `QueryBuffer`), because tests run matches
  in parallel.

## 3. Tick pipeline

`Match.Step()` advances one Tick (20 per second). Order matters:

1. **Commands** queued since the last Tick are validated and applied.
2. **AI** controllers decide (their commands land next Tick).
3. **Flow fields**: rebuild budget for the Tick.
4. **Territory**, **Presence**, **Vision**.
5. **Economy**: Worker spawning, logistics, Worker jobs, recruitment, upgrades, regrowth.
6. **Military**: soldier spatial index, Battle Groups, Regiments, combat, forts, fire, projectiles.
7. **Movement** for every agent.
8. **Victory check.**

Rendering interpolates between Ticks using `PreviousPosition` and an alpha, so 20 Hz simulation looks smooth at
any frame rate.

## 4. State

`GameState` owns everything: `TileMap`, entity dictionaries (`Buildings`, `Workers`, `Soldiers`, `Regiments`,
`RoadSites`, `Projectiles`), `Players`, `Territory`, `Presence`, `Vision`, `Connectivity`, `FlowFields`,
`Movement`, spatial hashes, the RNG and the event log.

`EntityOps` is the only place entities are created or destroyed, so map occupancy, Territory dirtiness and
back-references stay consistent.

Per-player state (`Player`) holds the Keep, defeat flag, Worker spawn timing, pending Pikemen upgrades, known
enemy buildings (fog of war memory) and revealed enemy Regiments.

## 5. Commands

Every action — human or AI — is a record in `Commands.cs`, validated in `CommandProcessor` and applied at the
start of the next Tick: place or demolish buildings, lay roads and walls, move, attack, station, set formation
or stance, merge, upgrade, recruit, set rally points, buy Carriers, set Minimum Stock, and so on.

The UI never mutates simulation state. Rejections come back as `CommandResult.Fail(reason)` and surface in the
event log. This is also what makes the AI honest: it drives the same API a player does.

## 6. Map and pathfinding

`TileMap` (192×192 by default) holds terrain, roads, building occupancy, resource deposits and amounts, regrowth
timers, crop state and gate ownership. It implements `INavGrid`, the only view pathfinding has of the world.

**Flow fields** ([ADR 0003](adr/0003-flow-fields-with-banner-anchored-formations.md)): one Dijkstra field per
goal, shared by every agent heading there, cached in an LRU (`FlowFieldCache`) with a per-Tick rebuild budget so
placing a building never spikes a frame. A field is keyed by `FlowGoalKey`: goal (entity or tile), movement class
(Civilian or Military) and **Player** — the last because Gates are passable only for their owner
(`INavGrid.IsPassableFor`, `TileMap.GateOwner`, `NavAgent.NavOwner`).

Cost per tile is inverse to speed, times `GetRouteCostMultiplier`, which is how Workers strongly prefer roads
(off-road costs 2.5× to plan even though it is only 2× slower).

`MovementSolver` moves every `NavAgent` (Workers and Soldiers): flow-field steering, local separation,
predictive avoidance, overlap resolution, terrain push-out and stuck detection (measured against
`EffectiveSpeed`, so slow terrain is not mistaken for being stuck). Civilians pass through each other and through
their own side's Soldiers; Soldiers of the same owner but different Regiments pass through each other so Battle
Group ranks can swap.

`Connectivity` keeps a flood-filled region id per tile (recomputed when the map version changes) to answer "can
Workers get there at all?" without a path query — used when placing buildings and when Builders pick sites.

## 7. Territory, Presence, Vision

- **Territory** ([ADR 0002](adr/0002-storehouse-territory-and-relayed-logistics.md)): Keeps and Storehouses claim
  a radius. Ties go to whoever already has a building there, then to the Keep, otherwise the tile is Contested.
  Recomputed when marked dirty. Buildings outside their owner's Territory burn on a deadline; lost Storehouses
  leave repairable Ruins.
- **Presence**: a bitmask per tile of which Players have Regiments, Outposts or Strongholds nearby. It blocks
  enemy building and halts training and Worker spawning in a besieged building.
- **Vision** (`Territory/Vision.cs`): a per-Player bitmask — own Territory, own Regiments (their Presence radius
  or past their longest possible shot, whichever is larger), forts, and a ring around own buildings. Enemy
  buildings seen are stored per Player as `BuildingSighting` (last seen), and attackers are revealed for a few
  seconds. Ranged targeting, attack commands, the renderer, the minimap and the AI all go through
  `GameState.CanSee(...)` / `KnownBuilding(...)`.

## 8. Economy

- **Storehouse stock** with `Reserved` (promised to someone) and `Incoming` (on its way) counters, so two
  Workers never spend the same plank.
- **Logistics** turns shortfalls into Demands and relays Shipments Storehouse to Storehouse (up to 4 hops) using
  the requesting Storehouse's Carriers. A route that cannot be served warns as Starved.
- **Worker jobs**: Builder (fetch, build, repair), Producer (gather, farm, manufacture), Carrier. Each is a small
  state machine in `WorkerSystem`, with reservations released centrally in `MakeJobless`.
- **Production** is batched: output piles up at the building until `BatchSize`, then one trip carries it to a
  Storehouse. Ordered goods skip the wait.
- **Manufacturing** (`Economy/Manufacturing.cs`): a workshop with `Recipes` picks one per cycle — orders first
  (unpaid recruit queue heads plus pending upgrades), then whatever is furthest below `GoodsStockTarget`.
  `Pipeline` counts stored, piled, carried and in-progress goods so workshops do not overproduce.
- **Farming**: Farmers sow Field tiles around the Farm (stored on the map as crop sown/ripe ticks), wait for the
  crop, then harvest. Fields are cleared if something is built or paved over them.
- **Food needs**: spawning a Worker costs Food; each Worker walks to a Storehouse to eat on an interval; a Worker
  who cannot eat works at `HungryWorkSpeed`.

## 9. Military

- **Regiments** own Soldiers, a Formation, a stance, a banner (`Anchor`) and an order. `FormationLayout` gives
  each Soldier a slot; the banner moves along the flow field and Soldiers seek their slots, so a Regiment holds
  shape while moving.
- **Battle Groups** (`BattleGroupSystem`): moving several Regiments at once assigns rank slots — melee front,
  ranged behind, cavalry on the flanks — and swaps the front melee type depending on the threat (spears against
  cavalry, swords against infantry).
- **Combat** (`CombatSystem`): melee in reach first (including against Workers), then ranged fire, then melee
  against buildings. `CombatMath` computes damage from base damage, armour vs penetration, formation modifiers,
  hit zone (front/flank/rear from the defender's facing), charge bonuses, shields against arrows, and the
  ranged-unit-in-melee penalty.
- **Ranged fire** creates real `Projectile`s with travel time, target leading, spread that grows with distance,
  friendly fire, forest cover, a hills bonus, a settle timer after moving, and range and spread penalties for
  shooting over someone else's wall (`EnemyWallBetween`). Arrows never damage buildings.
- **Forts** (`FortSystem`): Outposts shelter a Regiment; Strongholds add a respawning garrison, active shooter
  slots, faster fire for stationed archers and a weak missile for stationed melee.
- **Fire** (`FireSystem`): melee hits add Fire in proportion to damage ÷ max HP. Fire damages the building each
  second; below the self-sustaining level it dies down once attacks stop, above it spreads until the building is
  gone. Builders repair and douse.

## 10. AI

`AiController` picks a seeded opening Strategy on its first decision, builds an `AiContext` once per decision
(stock, building counts, own and *visible* enemy Regiments, known enemy buildings, the enemy Keep if seen or a
guess at the mirrored corner), then calls three swappable policies:

- `IEconomyPolicy` — build order, housing, Farms per Worker, roads, Storehouse minimums, Carriers.
- `ITerritoryPolicy` — expansion Storehouses, chokepoint Outposts and Strongholds, repairs.
- `IMilitaryPolicy` — peace window, escalation (`TargetWaveSoldiers`, `WaveIntervalSeconds`), raids vs staged
  waves, provocation, scouting, recruitment toward a composition, merging, Pikemen upgrades, breaking through
  walls.

All tuning lives in `config/ai_profiles.json` (Easy, Normal, Hard, each with weighted strategies). The AI is
subject to fog of war exactly like a player.

## 11. Rendering and audio

- `WorldRenderer` draws terrain (cached textures), tile detail, Territory and Presence overlays, roads, fields,
  buildings, fog, units, banners and projectiles, all in world space with a camera transform.
- `BuildingArt` paints every building as a texture at startup (one per type and Player) from shape primitives in
  tile units — team-coloured roofs, lit windows, trade props — with a fallback to the Kenney sprites (F5).
- `WorldRenderer.Animation` adds walking (bob, sway, shadow, feet, facing), tool swings timed to the work cycle,
  growing crops, smoke, forges, flags and scaffolding.
- `WorldRenderer.Combat` adds weapon swings and thrusts on each blow, bow draw and release, arrows with
  fletching and shadows, thrown torches, building fire, shields raised against incoming arrows and rally flags.
- `Audio/AudioMixer` loads clips grouped into variants, caps overlapping one-shots and fades named loops;
  `Audio/SoundDirector` turns simulation state into positioned sound — work sounds scale with zoom, combat
  carries further, own Soldiers shout, enemies roar and drum, and anything you cannot see stays silent.
- Assets are generated by `tools/audio/build_audio.py` (Kenney CC0 packs, synthesized horns/drums/fire, and
  placeholder Windows TTS voice lines).

## 12. Configuration

All balance lives in `config/*.json`, loaded into typed classes with defaults in `Config/GameConfig.cs` and
`Config/AiConfig.cs`:

- `balance.json`: simulation rates, movement, territory, economy (including recipes and Food), buildings,
  soldiers, formations, combat, forts.
- `ai_profiles.json`: the three AI profiles.
- `map.json`: map generation.

Values likely to need iteration are tagged `TUNING`. The files are copied next to the executable, so they ship
with the game and can be edited without rebuilding.

## 13. Tooling

| Command | Purpose |
|---|---|
| `dotnet test` | 85 tests: math, pathfinding, rules, economy, combat, fog, walls, AI |
| `dotnet run --project src/BannerAndBarrow.Sandbox -c Release -- match 20 [seed] [profile]` | Headless AI-vs-AI match with a per-minute timeline and an end-state dump |
| `... -- map [seed]` / `... -- flowbench` | ASCII map dump; pathfinding timings |
| `SANDBOX_TRACE=1` | Per-Regiment order, position and reachability each minute |
| `SANDBOX_CHECK=1` | Construction diagnostics: stalled sites, stuck Builders, bad Incoming counters, long meal trips |
| `SANDBOX_CONFIG=<dir>` | Run against a copy of `config/` for A/B balance runs |
| `BANNER_CAPTURE=<dir>` | Play AI-vs-AI and save screenshots (optionally `_UNTIL=fire`, `_AT`, `_SEED`, `_SIZE`) |
| `BANNER_MENU_CAPTURE=<dir>` | Screenshots of the menus |
| `BANNER_SOUND_TEST` / `BANNER_SOUND_LOG` | Play a match from a given minute; log which sounds fire |
| `pwsh scripts/publish.ps1 -Version x.y.z -Pack` | Self-contained build plus the Velopack installer |

Balance is judged by running several seeds in parallel and asking whether matches *end*, not by a single game.

## 14. Known limitations

- Single-player only; no save/load. The simulation is deterministic and command-driven, which is the groundwork
  for lockstep multiplayer and replays, but neither exists.
- Balance is rough: Iron is the usual bottleneck and some matches stalemate.
- The multi-Regiment Battle Group arranges by troop type, so the formation preview (which queues Columns behind
  each other) does not yet match what a moving group does.
- Melee Regiments do not yet fall back behind friendly archers automatically.
- Voice lines are text-to-speech placeholders.
- Builds are unsigned, so Windows SmartScreen warns on first run.
