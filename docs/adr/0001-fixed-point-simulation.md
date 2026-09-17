# Fixed-point simulation math

The simulation uses a 64-bit fixed-point type (`Fix`, 16 fractional bits, world units = Tiles) instead of `float`/`double`, and runs on a fixed 20 Hz Tick separate from rendering. Multiplayer is out of scope for v1, but lockstep networking requires bit-identical simulation across machines, and retrofitting determinism into a float-based RTS means rewriting every system. Paying the cost up front keeps that door open and also makes replays and "same seed, same match" tests possible today.

## Consequences

- `BannerAndBarrow.Simulation` must never use `float`/`double`; `NoFloatingPointTests` enforces this on fields, signatures and locals. Config numbers are parsed as `decimal` and converted.
- No trigonometry: directions are unit vectors, formation slots are rotated with a forward/right basis, and aim spread uses a random point in a disc.
- Rendering converts to `float` only at the boundary (`FixExtensions` in `BannerAndBarrow.Game`) and interpolates between Ticks.
- Iteration order must be deterministic: entities live in `SortedDictionary`, randomness only comes from `GameState.Rng`.
