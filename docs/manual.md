# Player's manual

Everything the game does, what it costs you, and what to watch out for. Numbers come from `config/balance.json`
and can be edited there.

## Contents

- [Controls](#controls)
- [The screen](#the-screen)
- [Territory: where you may build](#territory-where-you-may-build)
- [Workers, jobs and Food](#workers-jobs-and-food)
- [Resources and goods](#resources-and-goods)
- [Buildings](#buildings)
- [Roads and logistics](#roads-and-logistics)
- [Soldiers and Regiments](#soldiers-and-regiments)
- [Formations](#formations)
- [Fighting](#fighting)
- [Fog of war](#fog-of-war)
- [Fortifications: Outposts, Strongholds, Walls and Gates](#fortifications)
- [Sieges and fire](#sieges-and-fire)
- [The AI opponent](#the-ai-opponent)
- [Settings](#settings)
- [Things that catch players out](#things-that-catch-players-out)

## Controls

| Input | Does |
|---|---|
| Left-click / drag | Select Regiments, or click a building |
| Right-click | Move there and attack whatever the Regiment spots on the way |
| Right-click and **hold** | Preview the formation: markers show where each man will stand; drag to set the facing |
| **Ctrl** + right-click | March past enemies without fighting (retreats, repositioning) |
| Right-click an enemy | Attack that Regiment or building |
| Right-click your Outpost or Stronghold | Station the selected Regiments inside |
| Right-click with a Barracks, Archery or Stable selected | Set its rally point |
| 1 – 4 | Formation: Line, Column, Wedge, Loose |
| M | Merge selected Regiments of the same type (up to 24 Soldiers, 12 Knights) |
| U | Upgrade selected Spearmen to Pikemen (next to a Barracks) |
| T | Hold Ground on/off |
| G | Battle Line: selected archers form up behind the selected infantry |
| H | Halt |
| Del | Demolish the selected building, or cancel its construction |
| Space | Pause |
| Esc | Cancel placement or selection; with nothing selected, open the menu |
| + / − | Game speed (x1, x2, x4) |
| W A S D / arrows / mouse at screen edge / middle-drag | Move the camera |
| Mouse wheel | Zoom (work sounds get louder as you zoom in) |
| F1 – F5 | Debug: flow field, Presence, Territory, reveal map, building art |

To build: pick a building in the bottom-right menu and left-click the map. Hold **Shift** to keep placing.
Roads, Walls and Gates are **dragged** in lines. **Demolish** is in the same menu: click a building or drag
across roads.

## The screen

- **Top bar:** your stored resources and goods, Workers and Soldiers, the clock, game speed, map seed and AI
  difficulty. Warnings appear under it: jobs waiting for Workers, Houses full, Workers hungry.
- **Event log (right):** completed buildings, attacks, Regiments ready. Click an entry to jump the camera there.
- **Bottom left:** the minimap. Left-click to look, right-click to send the selection.
- **Bottom middle:** details of whatever is selected, and its actions.
- **Bottom right:** the build menu.

## Territory: where you may build

Your Keep and every Storehouse project **Territory** around them (16 and 10 tiles). You may only build inside
your own Territory, so **expanding means building Storehouses further out**. Where two players' claims meet the
ground is Contested and nobody may build.

A building outside your Territory (because you lost the Storehouse that covered it) starts **Burning** and is
destroyed after two minutes unless the Territory comes back. Destroyed Storehouses leave a **Ruin** you can
repair.

Each Storehouse costs more than the last, and more again the further it is from your Keep.

## Workers, jobs and Food

Workers appear on their own when a building needs one, at most one per second, as long as Houses have room (a
House holds 6). They take one of three jobs:

- **Builders** fetch materials to construction sites, build them, and repair damaged buildings.
- **Producers** work one building: gathering, farming or making goods.
- **Carriers** belong to a Storehouse and move goods between Storehouses. Buy more in a Storehouse's panel.

**Food.** Bringing in a new Worker costs 1 Food. Every Worker then walks to a Storehouse to eat 1 Food every 2
minutes. A Worker who cannot find Food keeps working at **half speed** and shows a food icon; the top bar warns
you. Soldiers cost Food once, when recruited.

**Farms** don't make Food by themselves: the Farmer sows Fields on open ground within 4 tiles, the crop grows
for about 50 seconds, then he harvests it. Each Farm keeps 8 Fields going. Give Farms room — trees, roads and
buildings all take Field space away.

A gold **"!"** on a building means it is waiting for a Worker or a Carrier.

## Resources and goods

**Raw:** Wood (Woodcutter, regrows and is replanted), Stone (Quarry), Iron (Iron Mine), Food (Farm), Gold (Tax
Office, scaled by how many Houses you have).

**Goods** are made from raw resources:

| Goods | Made at | From | Used for |
|---|---|---|---|
| Bows | Fletcher | Wood | Archers, Longbowmen |
| Shields | Shield Maker | Wood + Iron | Men-at-Arms, Knights |
| Swords | Smithy | Iron | Men-at-Arms, Knights |
| Pikes | Smithy | Iron + Wood | Pikemen |
| Armour | Smithy | Iron | Pikemen, Men-at-Arms, Knights |

Workshops **make to order, then to stock**: they first make what queued recruits and pending upgrades need, then
top each item up to 10 spare, then stop. You can queue recruits you cannot afford — the missing goods become
orders and the workshops get to work. A workshop's panel shows what is ordered and what is in stock.

**Iron is the bottleneck.** Swords, Pikes, Armour and Shields all want it. Two Iron Mines will not carry a big
Men-at-Arms army.

## Buildings

| Building | Cost | What it does |
|---|---|---|
| House | Wood 20, Stone 5 | Room for 6 Workers |
| Woodcutter | Wood 15 | Fells trees, plants saplings |
| Quarry | Wood 15, Stone 5 | Cuts Stone |
| Iron Mine | Wood 20, Stone 10 | Digs Iron |
| Farm | Wood 25, Stone 5 | Sows and harvests Fields for Food |
| Tax Office | Wood 30, Stone 20 | Gold, more with more Houses |
| Storehouse | Wood 25, Stone 15 (rises with count and distance) | Stores goods, extends Territory, hosts Carriers |
| Smithy | Wood 25, Stone 20 | Swords, Pikes, Armour |
| Fletcher | Wood 25, Stone 10 | Bows |
| Shield Maker | Wood 25, Stone 15 | Shields |
| Barracks | Wood 40, Stone 30 | Spearmen, Pikemen, Men-at-Arms |
| Archery | Wood 45, Stone 20 | Archers, Longbowmen |
| Stable | Wood 50, Stone 30, Iron 10 | Knights |
| Outpost | Wood 30, Stone 20 | Buildable outside Territory; shelters Regiments, projects Presence |
| Stronghold | upgrade from Outpost | Garrison of archers, room for 5 Regiments, shoots hard |
| Wall | Stone 4 per tile | Blocks everyone; only melee can break it |
| Gate | Stone 4, Wood 8 per tile | Your units pass, the enemy must break it |

Buildings need **one edge a Worker can reach**; the game refuses a spot that is walled in. You do not need roads
all the way around a building — one connected side is enough.

## Roads and logistics

Dirt Roads are free (Builders lay them) and **double Worker speed**; Stone Roads cost 1 Stone per tile, need
your own Territory, and are faster again. Workers plan routes that stick to roads even if the detour is longer.

Storehouses relay goods to each other by **Demand**: a Storehouse that is short asks its neighbours, up to four
hops. Set a **Minimum Stock** in a Storehouse's panel to keep goods forward, near your Barracks for instance. If
a Storehouse warns that it is **Starved**, buy it more Carriers.

## Soldiers and Regiments

Recruits arrive as small Regiments (6 Soldiers, 3 Knights) that you **merge** with M into Regiments of up to 24.
A Regiment moves as one, under a banner, and keeps a formation.

| Soldier | Trained at | Costs | Good at | Weak to |
|---|---|---|---|---|
| Spearmen | Barracks | Food 6 | Cheap bodies, some use against cavalry | Everything else; they are levies |
| Pikemen | Barracks (or upgrade Spearmen with U) | Pikes 6, Armour 6, Food 6 | Long reach, deadly against Knights | Archers |
| Men-at-Arms | Barracks | Swords 6, Shields 6, Armour 6, Food 6, Gold 3 | Melee against infantry; shields blunt arrows | Pikes, being flanked |
| Archers | Archery | Bows 6, Food 5 | Fast volleys at range, chasing Workers | Anything that reaches them |
| Longbowmen | Archery | Bows 12, Food 5, Gold 3 | Nearly double range, punches through armour | Cavalry that closes the gap |
| Knights | Stable | Swords 3, Shields 3, Armour 3, Food 9, Gold 6 | Charges, flanking, running down archers | Pikemen and Spearmen |

**Upgrading Spearmen:** stand them next to a Barracks and press **U**. Each Soldier costs 1 Pike + 1 Armour. If
you are short, the upgrade waits as an order and the workshops make the goods.

**Rally points:** select a Barracks, Archery or Stable and right-click the map. New Regiments march to the flag.

**Stationing:** right-click your own Outpost or Stronghold with Regiments selected. An Outpost holds 1 Regiment,
a Stronghold 5. Stationed archers shoot faster and cannot be reached by melee.

## Formations

| Formation | Shape | Effect |
|---|---|---|
| Line | Wide, 8 per rank | Solid: takes a charge best, flanks and rear still soft |
| Column | 2 wide, deep | Fastest march, weak if caught (use it to travel, not to fight) |
| Wedge | Triangle (Knights, Men-at-Arms) | Extra damage on the charge |
| Loose | Spread out | Takes 40% less from arrows, but more from melee |

Hold the right mouse button before ordering a move: markers show exactly where the men will stand, and dragging
turns the formation. The markers stay on the ground until the Regiment arrives.

Move several Regiments at once and they form a **Battle Group**: melee in front, archers behind, Knights on the
flanks. The front rank swaps automatically — Spearmen and Pikemen step forward against cavalry, Men-at-Arms
against infantry.

**Hold Ground (T):** the Regiment will not chase. Standing archers shoot faster, further and straighter.

## Fighting

- **Facing matters.** Hits from the flank and rear do far more damage. Getting behind a Regiment is worth more
  than out-numbering it.
- **Armour against penetration.** Longbows and Pikes punch through armour; Archers scratch it.
- **Charges.** Knights (and Men-at-Arms in a Wedge) do extra damage when they arrive at speed, then it wears off.
- **Shields.** Men-at-Arms and Knights raise shields when arrows are in the air and take roughly half damage.
- **Archers who have just moved** shoot 3 tiles shorter, coming back over 5 seconds of standing still. Set them
  up before the enemy arrives; archers shuffled around mid-battle are worth much less.
- **Archers in melee** fall back on their own and keep shooting, unless you told them to Hold Ground.
- **Cohesion.** Losses and hits from odd angles wear a Regiment down; a Broken Regiment fights badly until it
  recovers.
- **Workers can be killed.** Enemy Soldiers cut down Workers, and a raid on your Woodcutters hurts for minutes.

## Fog of war

You see your own Territory, around your Regiments (at least as far as they can shoot), and around your forts and
buildings. The map itself is always visible — the fog hides what is happening, not the ground.

- Enemy buildings you have seen stay on the map as faded **last seen** markers.
- Enemy units outside your sight are not drawn at all.
- Anyone who attacks you — in melee, with arrows or with a fort's archers — becomes visible for 6 seconds.
- You cannot order an attack on something you cannot see.

The AI plays by the same rules and has to scout for you.

## Fortifications

- **Outpost:** the one building you can put up outside your Territory. It projects Presence, which blocks enemy
  building, and shelters one Regiment.
- **Stronghold:** upgrade an Outpost. A free garrison of archers that respawns, room for 5 Regiments, and 16
  shooting positions.
- **Walls:** drag a line. They cost Stone, can be raised straight through forest, and block **everyone**,
  including you. Only melee attackers can break them; arrows cannot touch them.
- **Gates:** drag them into the line. Your units walk through; the enemy has to break them down.

**Why walls are worth it:** your archers shoot over your own wall without penalty, while an enemy shooting over
it loses 40% of their range and half their accuracy. A wall with archers behind it beats the same archers in the
open. Attackers must come to the wall, break it with melee, and be shot while doing it.

Watch out: a wall across your own paths slows your Workers down just as much, so leave Gates.

## Sieges and fire

Melee attackers throw **torches**. Each hit adds Fire in proportion to the damage, so a House catches quickly and
a Keep takes a real siege. Fire burns HP away by itself.

- If the attackers leave, a small fire dies down on its own.
- Past about 60%, the fire keeps spreading and the building burns down.
- Your **Builders repair** damaged buildings once they have not been attacked for 10 seconds, and put fires out.
- Arrows never damage buildings, so an army of archers alone cannot take your town.

Losing your **Keep** loses the match.

## The AI opponent

Three difficulties (chosen in New Game):

| | Easy | Normal | Hard |
|---|---|---|---|
| Peace at the start | 5 minutes | 2 minutes | 2 minutes |
| Attacks grow | slowly | steadily | fast |
| Opening | usually economy | economy or rush | usually rush |

It picks an opening each match, builds a town, scouts for your Keep, then raids your economy with small groups
before gathering bigger waves at a staging point. Attacking its town early **provokes** it into escalating
faster. It reacts only to what it has seen.

## Settings

Main menu or Esc → Settings: game speed, brightness (map only, not the UI), camera scroll speed, edge scrolling,
fullscreen, the Territory overlay, building art (painted or the original Kenney sprites), and master, effects and
shout volumes. Settings are saved to `%APPDATA%\BannerAndBarrow\settings.json`.

## Things that catch players out

- **No roads, no economy.** Roads double Worker speed. A town without them starves its workshops.
- **Food is not free.** Every Worker eats, and Houses without Farms mean hungry Workers at half speed.
- **Iron runs out.** Shields and Swords compete for it. Build the second and third Iron Mine earlier than feels
  necessary.
- **Storehouses are your borders.** If you cannot build somewhere, you need a Storehouse there first.
- **Spearmen are levies, not soldiers.** They are meant to be upgraded to Pikemen or used as cheap bodies.
- **Moving archers are weak archers.** Place them and leave them.
- **Keep a home guard.** The AI raids Workers, not just buildings.
- **Repair is automatic but not instant.** Builders come when the attack stops, and they are busy if you are
  building at the same time.
- **A wall without Gates walls you in too.**
