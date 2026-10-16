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
most agromancy actions you can have going at once, starting at one and raised by upgrades (observed
at 2). Filling one queue leaves the other untouched.

A harvested plot **regrows on its own**. A short action such as collecting fruit takes the plot from
2 to 1, resolves without further input, and the plot is back at 2 a minute later — so a plot count
seen mid-action is not the plot's settled size.

Growth continues while the screen is closed. The one screen-bound detail: **whether an action can be
taken is only re-evaluated while the Agromancy screen is open**. A plot can therefore look stale —
an action that has actually become available does not show until you open the screen — and once an
action has shown as available it stays available.

Two authored quirks, not corruption: a plot can offer the same action twice, and it can keep
running instances of an action that has been removed from its list.
