# Shared flow fields with banner-anchored formations

Movement uses one Dijkstra flow field per destination (shared by every agent going there, cached with LRU eviction and a per-Tick rebuild budget) plus steering, separation and predictive sidestepping, instead of A* per unit. Regiments move a virtual banner along the field and each Soldier steers to its formation slot around the banner, falling back to the field when far from the slot. This scales to hundreds of agents, keeps formations intact on the march, and keeps the pathfinding module independent of game rules (`INavGrid`) so it can be unit tested and swapped.

## Consequences

- Map changes do not rebuild fields synchronously; stale fields keep serving agents until the budget allows. Tree felling is a "minor" change that rebuilds lazily.
- A full 192×192 build costs ~9 ms, so the rebuild budget (`flowFieldRebuildsPerTick`) is the main knob for frame spikes.
- Chasing moving targets re-keys a Regiment's field only when the target tile moves more than 2 tiles, trading accuracy for cache hits.
