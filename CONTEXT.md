# Banner & Barrow

A single-player, top-down 2D medieval real-time strategy game: a Settlers-style economy with a stronger military layer, set in 12th–14th century Europe.

## Language

### Match & World

**Match**:
One game, played from map generation until one player's Keep is destroyed.
_Avoid_: Game session, round

**Player**:
One side in a Match: the human or the AI opponent. Owns buildings, units, stockpiles and territory.
_Avoid_: Faction, team, civ

**Keep**:
A Player's single headquarters building. It is also their first Storehouse and reaches further than any other. Losing it loses the Match.
_Avoid_: HQ, castle, town center

**Tile**:
One cell of the map grid. The map is 192×192 Tiles.
_Avoid_: Cell, square

**Tick**:
One fixed step of the simulation. All game rules advance in whole Ticks.
_Avoid_: Frame, update

### Territory

**Territory**:
The Tiles within reach of a Player's Storehouses (the Keep included). A Player may only build inside their own Territory, and only Storehouses make it grow.
_Avoid_: Borders, land, realm, influence

**Contested Tile**:
An empty Tile both Players' Storehouses reach. It belongs to neither Player until one of those Storehouses is destroyed. A Tile with a building on it stays with the building's owner.
_Avoid_: Disputed land, no-man's land, overlap

**Burning**:
The state of a building left outside all of its Player's Territory after a Storehouse is destroyed. Unless the Storehouse is Repaired within the Rebuild Window, the building is destroyed.
_Avoid_: Abandoned, decaying, orphaned

**Ruin**:
What a destroyed Storehouse leaves behind. The Player can Repair it only when no enemy Presence covers it.
_Avoid_: Rubble, wreck, remains

**Rebuild Window**:
The time a Player has to Repair a Ruin before everything Burning because of it is destroyed.
_Avoid_: Grace period, countdown

**Presence**:
The area around a Player's Regiments, Outposts and Strongholds. No other Player may build on a Tile inside it, not even in their own Territory. It exists only while its source is there.
_Avoid_: Influence, control zone, zone of control

### Economy

**Wood**, **Stone**, **Iron**, **Food**, **Gold**:
The five raw resources.

**Goods**:
Items produced from resources in a production chain, as opposed to gathered directly.
_Avoid_: Products, items, materials

**Bows**, **Shields**, **Swords**, **Pikes**, **Armour**:
The five Goods. A Fletcher makes Bows, a Shield Maker makes Shields, and a Smithy makes Swords, Pikes and Armour. Recruiting and upgrading Regiments consumes them.
_Avoid_: Weapons, arms, equipment

**Workshop**:
A building that makes Goods (Fletcher, Shield Maker, Smithy). Each cycle it picks what to make: Orders first, then Stock.
_Avoid_: Factory, manufacturer, crafter

**Order**:
Goods needed by queued recruits and pending Upgrades that aren't in stock yet. Workshops make Orders before anything else.
_Avoid_: Request, demand (Demand means a Storehouse shortfall)

**Stock Target**:
How many of each Goods Workshops keep on hand beyond open Orders (10). A Workshop with no Orders and every product at its Stock Target stops working.
_Avoid_: Surplus, buffer, reserve

**Worker**:
A civilian who does the job at a production building. Workers appear automatically when a building needs one, as long as Houses have room.
_Avoid_: Villager, peasant, settler, serf

**Carrier**:
A Worker who belongs to a Storehouse and moves resources and Goods between Storehouses. Production Workers fetch and deliver their own.
_Avoid_: Porter, hauler, courier

**Storehouse**:
A building where resources and Goods are dropped off and stored.
_Avoid_: Warehouse, depot, stockpile, secondary keep

**Demand**:
A Storehouse's request for a resource or Goods it is short of. If the nearest Storehouse cannot fill it, that Storehouse passes the Demand on to its own neighbours, so stock is relayed Storehouse to Storehouse.
_Avoid_: Order, request, job

**Reservation**:
Stock set aside for a particular Demand. No other need may take it, including a need at a Storehouse it passes through.
_Avoid_: Lock, hold, allocation

**Field**:
A tile next to a Farm that the Farmer has sown. The crop grows over time, and the Farmer harvests it for Food once it is ripe. Each Farm keeps a set number of Fields sown.
_Avoid_: Crop tile, plot, farmland

**Meal**:
A Worker walking to a Storehouse to eat Food. Every Worker needs one Meal per meal interval, and bringing a new Worker in costs Food as well. Soldiers only cost Food once, when recruited.
_Avoid_: Upkeep, ration, feeding

**Hungry**:
Describes a Worker whose Meal is overdue. Hungry Workers work at half speed until they eat. Food is the only need for now.
_Avoid_: Starving, unfed

**Repair**:
Builders restoring a damaged building's HP and putting out its Fire, once it hasn't been attacked for a short while. Repairs cost no materials. Different from repairing a Ruin, which is a construction site.
_Avoid_: Fix, mend, heal

**Shield**:
Carried by Men-at-Arms and Knights. They raise it when arrows are in the air and take much less damage from them.
_Avoid_: Block, guard

**Shout**:
A voice line your own Soldiers call out: charging, answering an attack order, loosing arrows, rallying each other, cheering a victory, or reporting for duty. Enemy Soldiers only roar when they charge you.
_Avoid_: Bark, callout, voice line

**Wall**:
A one-tile building dragged out in lines. It blocks movement for everyone and can only be broken by melee attackers. Shooting over a Wall that isn't yours costs range and accuracy.
_Avoid_: Barrier, palisade, fortification

**Gate**:
A Wall piece that its owner's units walk through and enemies cannot. Enemies have to break it down.
_Avoid_: Door, portcullis

**Rally Point**:
The spot a Barracks, Archery or Stable sends its newly trained Regiments to, marked with a flag.
_Avoid_: Waypoint, spawn point, gather point

**Fire**:
How badly a building is burning, from 0 to 1. Torches thrown by melee attackers (and, more weakly, arrows) raise it in proportion to the damage. Fire burns away HP. Small fires die down once the attack stops, but a fire past the self-sustaining level spreads until the building is gone. Not the same as Burning, which is about losing Territory.
_Avoid_: Flames, blaze, ignition

**Batch**:
The amount of output a production building collects before its Worker carries all of it to a Storehouse. Until then the output sits at the building and isn't in storage.
_Avoid_: Load, stack, pile

**Minimum Stock**:
A quantity the player sets for a resource or Goods at a Storehouse. Carriers move stock to keep that Storehouse at or above it.
_Avoid_: Quota, threshold, supply route

**Demolition**:
Builders tearing down a building or road the Player marked, returning part of the cost.
_Avoid_: Deconstruction, removal, scrapping

**Under Siege**:
Describes a Barracks, Stable or Keep covered by enemy Presence: it can't train Regiments or spawn Workers until the enemy leaves.
_Avoid_: Besieged, blockaded, contested

**Starved**:
Describes a Storehouse whose incoming stock is waiting because it has no free Carriers.
_Avoid_: Blocked, stalled

**Builder**:
A Worker who builds buildings and Roads, carrying the materials from the nearest Storehouse to the site.
_Avoid_: Constructor, mason

**House**:
A building that provides room for Workers. The Worker cap is the total room in all Houses.
_Avoid_: Dwelling, residence

**Road**:
Player-built Tiles that change how fast things move across them. A Road is either a **Dirt Road** or a **Stone Road**, and a Dirt Road can be upgraded to a Stone Road.
_Avoid_: Path, street

### Military

**Regiment**:
A persistent group of Soldiers of one kind under one banner. The player selects it, moves it and gives it Formation orders as a whole. Recruits arrive as small Regiments that can be merged into bigger ones.
_Avoid_: Squad, group, army, company

**Soldier**:
One individual fighting member of a Regiment. Soldiers fight and die individually.
_Avoid_: Unit (too broad), trooper

**Spearmen**, **Pikemen**, **Men-at-Arms**, **Archers**, **Longbowmen**, **Knights**:
The six Soldier types. Spearmen cost only Food and are weak; Pikemen are armoured Spearmen with Pikes.

**Upgrade**:
Turning a Regiment into a better type next to the building that recruits it, paying Goods per Soldier (Spearmen to Pikemen: 1 Pike and 1 Armour each). If the Goods are short, the Upgrade waits as an Order.
_Avoid_: Promote, convert, level up

**Barracks**:
The building where Spearmen, Pikemen and Men-at-Arms are recruited.

**Archery**:
The building where Archers and Longbowmen are recruited.
_Avoid_: Archery range

**Stable**:
The building where Knight Regiments are recruited.

**Vision**:
The tiles a Player can currently see: their own Territory and Presence, plus the area around their own buildings. Enemy Soldiers and Workers outside Vision are hidden. Both humans and the AI play by it.
_Avoid_: Sight, line of sight, visibility

**Revealed**:
Describes an enemy Regiment that recently attacked a Player: that Player can see it for a few seconds even outside their Vision. A fort that shoots at a Player becomes a Last Seen building for them.
_Avoid_: Spotted, exposed, detected

**Fog**:
Everything outside a Player's Vision. It is drawn dimmed, not black: the map itself is always visible.
_Avoid_: Shroud, darkness

**Last Seen**:
An enemy building as a Player last saw it. It stays shown, faded, until the Player sees that spot again.
_Avoid_: Ghost, memory, snapshot

**Scout**:
A Regiment sent to find the enemy. The AI guesses the enemy Keep is in the corner opposite its own and sends a Scout until it has seen it.
_Avoid_: Explorer, recon

**Outpost**:
A small fortification that gives Presence and shelters Soldiers but cannot fight on its own. It does not add Territory.
_Avoid_: Secondary keep, lookout, watchtower, fort

**Stronghold**:
An upgraded Outpost that fights back from its Garrison and its ranged Stationed Soldiers. It does not add Territory.
_Avoid_: Castle, fortress, secondary keep

**Garrison**:
The small, free, permanent force that belongs to a Stronghold. It can die, and it returns after a while if the Stronghold is not under attack.
_Avoid_: Stationed Soldiers, defenders

**Stationed**:
Describes a player's Soldiers placed inside an Outpost or Stronghold, as opposed to its Garrison.
_Avoid_: Garrisoned, housed, sheltered

**Formation**:
The arrangement a Regiment holds its Soldiers in: **Line**, **Column**, **Wedge**, **Shield Wall** or **Loose**. It affects combat outcomes.
_Avoid_: Stance, layout

**Banner**:
The point a Regiment's Formation is laid out around; it leads the Regiment when it moves.
_Avoid_: Leader, anchor, centre

**Merge**:
Combining Regiments of the same Soldier type into fewer, larger Regiments, up to a size cap.
_Avoid_: Combine, join, consolidate

**Fall Back**:
A ranged Regiment stepping away from approaching enemy melee, then shooting again.
_Avoid_: Kite, retreat, skirmish

**Battle Group**:
Several Regiments moving as one, arranged in ranks by type: a front melee rank, a second melee rank, Archers, then Longbowmen, with Knights on the flanks. Men-at-Arms take the front against enemy infantry; Spearmen take it against Knights.
_Avoid_: Army, blob, squad group

**Hold Ground**:
A Regiment stance: it doesn't chase, and its ranged Soldiers shoot faster, further and more accurately while standing still.
_Avoid_: Stand ground, defensive stance, hold position

**Battle Line**:
An infantry Regiment with one or more ranged Regiments keeping position behind it.
_Avoid_: Combined formation, archers-behind

**Front**, **Flank**, **Rear**:
The side of a Regiment an attack comes from, judged by the Regiment's facing. Flank and Rear hits do more damage and cost Cohesion.
_Avoid_: Side, back

**Cohesion**:
How well a Regiment holds together. Casualties and Flank or Rear hits lower it; it recovers when the Regiment is left alone.
_Avoid_: Morale, discipline

**Broken**:
A Regiment whose Cohesion has fallen too low: it loses its Formation effects and fights badly.
_Avoid_: Routed, shattered

**Charge**:
The first melee strike of a mounted Soldier arriving at speed, which does extra damage.
_Avoid_: Impact, rush

**Active Shooter**:
A Stationed Soldier occupying one of a Stronghold's firing positions.
_Avoid_: Slot, defender

### AI

**Build Order**:
The AI profile's ordered list of buildings to reach.
_Avoid_: Script, queue

**Attack Wave**:
A large group of the AI's Soldiers sent together against the enemy. It gathers at a staging point short of the enemy town, then assaults.

**Raid**:
A small AI attack aimed at the enemy's economy rather than the town, with no staging.
_Avoid_: Harass, skirmish

**Peace Window**:
The opening minutes of a Match during which the AI never attacks.
_Avoid_: Grace period, truce

**Escalation**:
The AI's attacks growing in size and frequency as the Match goes on.
_Avoid_: Scaling, ramp-up

**Provocation**:
How angry the AI is about enemy Soldiers in its town. Higher Provocation makes Escalation faster, up to a cap, and it fades over time.
_Avoid_: Aggro, threat, anger

**Strategy**:
The opening plan an AI picks at the start of a Match, such as Economy (boom first) or Rush (early army).
_Avoid_: Personality, build, doctrine

**Difficulty**:
Which AI profile the opponent uses: Easy, Normal or Hard.
_Avoid_: Level, preset, mode
_Avoid_: Raid, push, rush
