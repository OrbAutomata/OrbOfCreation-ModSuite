# Agromancy

Agromancy is the game's farming and harvesting system, on the World tab.

- The screen shows **plots**. A plot offers a list of **actions** and holds **nodes** that move
  through phases over time — growing, resting, idle. Harvestable nodes fill to a ready count (e.g.,
  `2/2` ready to harvest) and are then collected.
- **Fruit trees** and **treasure trees** are the two node kinds observed early.
- Agromancy both **produces and consumes** resources: an allocation problem with inputs and outputs,
  not just a timer you tap.
- The cadence is **slow**. Many minutes pass between meaningful actions, and it is normal for a save
  to sit with trees planted and nothing ripe.

## Two layers, two prices

Harvesting has a setup layer and an action layer, and they are paid for out of different budgets.

- **Harvest elements** are the things you can harvest at all — one flat list for the whole game, not
  a per-screen one. Activating an element is a standing allocation against a **per-element Harvest
  Capacity** resource (observed: Lumber Harvest Capacity, `0/1`, one Oak Tree's worth). With the
  capacity full, the next instance simply cannot be activated, and the game names the true maximum
  rather than quietly clamping.
- **Element/action pairs** — an element plus one of its actions, e.g. Oak Tree × Woodcutting — carry
  a **standing per-second drain** that scales with how many are active. Two active instances drain
  roughly twice one. Not every element offers every action.
- **Plot actions** are the diamonds on a selected plot's panel, and they are paid in **plot
  quantity**. The small badge on each diamond is how many of the plot that action consumes; the
  diamond turns red and the action is refused when the plot holds fewer than the badge (observed:
  Enrich at cost 3 going red once its plot dropped to 2).

Plot actions run in their **own queue**, separate from the one attributes and upgrades develop in
(see [development-queue.md](development-queue.md)). Its size is **Parallel Agromancy Slots** — the
most agromancy actions you can have going at once, starting at one; the **Parallel Agromancy**
research (2 Ability per level) adds one per level. A chop, a planting, a peel or a weave each hold a
slot for as long as they run. Filling one queue leaves the other untouched.

A harvested plot **regrows on its own**. A short action such as collecting fruit takes the plot from
2 to 1, resolves without further input, and the plot is back at 2 a minute later — so a plot count
seen mid-action is not the plot's settled size.

A phase's tooltip timer counts **down**: `15.5s/09:44` is time left over the phase's full length.
Growth continues while the screen is closed. The one screen-bound detail: **whether an action can be
taken is only re-evaluated while the Agromancy screen is open**. A plot can therefore look stale —
an action that has actually become available does not show until you open the screen — and once an
action has shown as available it stays available.

Two authored quirks, not corruption: a plot can offer the same action twice, and it can keep
running instances of an action that has been removed from its list.

## Druidry Lv

Once **Unlock Druidry Levels** is bought, the screen draws a **Druidry Lv** −/+ adjuster. It sets
the level in effect between 1 and **Max Druidry Lv**, which **Raise Druidry Lv** raises by one per
purchase (each purchase also adds ×1.02 plot yield and ×1.75 plot recovery size). A higher level
runs agromancy faster at a higher cost (observed: ×2.25 speed at ×2 cost per level), so turning it
down is how a player buys cheaper, slower actions. The dial and its cap share the shape described in
[attributes-and-upgrades.md](attributes-and-upgrades.md).

## The oak loop

Wood comes only from oaks:

1. **Plant Sapling** on an Empty Plot (costs Verdant Energy while it plants). The Oak Tree takes
   2 Plot Capacity and grows for 2:00. At most 5 saplings plant at once, however many plots are
   free.
2. **Chop Down Oak Tree** drains **Skill** per second while it runs and pays Wood; with no Skill the
   chop stalls rather than failing. The tree is destroyed.
3. The chopped plot is **Recovering** — 40 s per plot, one plot at a time — before it is an Empty
   Plot again. Only recovered plots take a sapling, so a planting order is clamped to them.

**Druidry Lv** is a dial beside the plots: each level multiplies active agromancy speed ×2.25 and
cost ×2, and weave growth size ×1.75 and weave cost ×5.
**Raise Druidry Lv** raises its maximum (and adds ×1.02 plot yield and ×1.75 plot recovery size). Wood Axe (Agromancy Power for
harvests) and Clearcutting (harvest speed) are the Workshop levers on the same loop; see
[workshop.md](workshop.md). Observed yield: ~130 Wood per oak at the start, ~1.1e3 after eight Wood
Axe levels at Druidry Lv 3, ~5.7e3 at Wood Axe 10 with Improved Tools (×1.40 Agromancy Power and
Speed), ~1.3e7 at Oak Mastery Lv 10 late in a run.

## Oaks are the stock for other trees

A grown oak carries conversion actions besides Chop, each paid as a drain while it runs and
turning that oak into a new kind of tree:

| Oak action | Unlocked by | Drain (observed, Mastery Lv 2) | Becomes |
|---|---|---|---|
| **Grow Fruit** | Create Fruit Trees (World) | 1.51e5 Verdant Energy over 28.8 s | Fruit Tree |
| **Energize** | Energy Trees (World) | 7.91e4 Spark + 5.93e4 Verdant Energy over 14.4 s | Force Tree |

A Force Tree offers **Peel Bark** (Force Bark, tree kept) and **Chop Down Force Tree** (Force Bark
and Wood, tree destroyed). An Empty Plot's other action, **Plant Seedling**, paid in Thaumaturgy,
grows **Void Moss**.

Conversions are paid in plot quantity too (the diamond's badge): Energize takes 2 Oak Tree units
and yields 1 Force Tree unit, which Grows for 2:35 before it can be used (observed). With the
Ironwood Trees upgrade a Force Tree gains **Imbue** (badge 2, a 17–24 s weave draining Organic and
Occult Essence, ~100–140 of each), which turns it into an **Ironwood Tree**: 8 Plot Capacity, Grows
9:44, and **Chop Down Ironwood** (30 s) pays the **Ironwood** resource. So one Ironwood Tree costs
four oaks. Observed harvests: 9.5e3, 1.7e4, 2.1e4, 2.6e4, 4.7e4 Ironwood as the plot's mastery rose from 0
to 13. Ironwood in the resource list unlocks **Ironwood Druidry** (observed 7.3e4–8.4e4 Ironwood +
5.45e10 Verdant Energy), which opens the Druidry attributes Wellspring (spring yield) and
Florescence (blooming resources), both paid partly in Ironwood. **Herbalism** (Organic Essence +
Verdant Energy) opens two more, **Irrigation** (+8% Natural resource rate, −1% weave cost; Organic
Essence + Water) and **Fertilizer** (+5% herb size; Organic Essence + Verdant Energy), and the
**Improved Herbalism** upgrade (observed 4e4 Magebloom + 5e7 Water).

**Empty Plots are the unused Plot Capacity.** Every grown thing weighs Plot Capacity (an Oak Tree
2, Void Moss 1), and the Empty Plot count is whatever is left over: raising the capacity or using
**Destroy** (a 5 s utility action that removes a plant, recovering and growing ones first) adds
Empty Plots at once (observed 2 → 14 after +10 capacity and four Void Moss destroyed).

## Shaped Ground and weaves

**Shape Land** (the World upgrade unlocks it) is an Empty Plot weave that drains **Space** while it
runs and turns the plot into **Shaped Ground**; with Space at zero it crawls at whatever Space income
allows. Shaped Ground offers **Create Spring** (Water Spring), **Create Elementia Spring** (Elementia;
locked until the Elementia Spring research, which itself waits for Elementia in the resource list)
and **Create Craggy Spire**. A **Water Spring** passively generates Water and enhances every plant
(+20% growing speed, ×1.10 yield); observed: one spring at Druidry Lv 7 took Water from −1.4e4/s to
+4.9e5/s. Springs stack: with two, an Ironwood Tree grew in 6:45 against 9:44 with none and 7:58
with one (observed). Each Shaped Ground takes a **Shape Land** on 5 Empty Plots. These weaves drain as they grow, and not only common resources:
Create Spring drained **Alchemic Scroll** at ~94/s at Druidry Lv 7 (observed), enough to empty a
stock of several hundred before the spring finished. Void Moss offers its own weaves — **Create Magebloom**, **Create Dark
Thistle** and **Create Dreamberry** — each locked until something else is learned; Create Magebloom
opens with the Grow Magic Herbs upgrade and drains Verdant Energy and Organic Essence while it runs
(observed ~2.3e7 VE per plant at Druidry Lv 7).

A Magebloom plant offers **Gather Magebloom**: a Control price up front, then Magebloom per second
for the action's duration (observed +4.1/s for 78 s at Mastery Lv 11–12), after which the plant
is Recovering for ~53 s and then Growing again for ~15 min; the plant itself stays. A gather of
several plants is one action card with a count badge, and the rate stays the same (observed ~4.1/s
for one, two or three plants), so the ceiling on Magebloom is one gather running all the time.
