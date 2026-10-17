# Research

Research is unlocked by the *Innovation* upgrade and gets its own tab.

- Each node costs **typed advancement points** — the school-specific currencies in
  [advancement-currencies.md](advancement-currencies.md).
- A node **takes time** to develop, and the selected node's panel prints that duration (observed
  from 4 seconds on a deep node to roughly 15–20 seconds on shallow ones).
- A node also **drains a resource** for the duration of its development, listed under the duration
  as a total and a per-second rate. The drain is not the price: Develop pays the points up front and
  the drain follows (e.g., Greater Spell Capacity costs 1 Magical Advancement and drains 20
  Knowledge). These drains are usually negligible relative to production and only occasionally bind.
- **A node's cap is authored per node**, and its card reads `Lv <level>/<cap>`. A node with no cap
  prints `–` in place of the number. Shallow nodes commonly cap at 1; whole schools are uncapped and
  climb into the tens of levels (observed: every Expert node reads `/–`).
- **Completing a node may reveal further research.** The game does not preview what a node will open;
  you find out by finishing it.
- **Bonus levels count towards other nodes' requirements, but not towards finishing this one.** A
  research node with bonus levels can satisfy a prerequisite of `≥ n` elsewhere while still counting
  as incomplete and below its own maximum level, because completion counts only the levels this node
  itself has taken. **Code shape:** the prerequisite path dispatches `GetLevel()`, whose research
  override includes bonus levels, while `IsMaxLevel()` reads `GetBaseLevel()` and excludes them.
- **Free bonus levels are a pool per school, not per node.** Every node in a school shows the same
  remaining count, and pressing a node's bonus button spends one from that shared pool (observed:
  three Expert bonus levels, taken as two on Expert Scholar and one on Expert Augments, after which
  every other Expert node refuses). They cost no advancement points, so spend the pool on the school's
  node whose requirements you want satisfied first.

The research page shows five spendable school-point balances across the top, and each Develop button
carries its per-school point cost, rendered red when you are short. Below the balances the nodes are
split by **school tabs** — observed: Expert, Technology, Innovation — so the grid shows one school at
a time.

Because the point supply is finite and run-global, every research node competes with every other for
the same pool. Spending a school down to **zero simply stops development in that school** — the
Develop button goes dead and no node in it can be started until the balance recovers, even where the
node's own drained resources are abundant. So the school balance, not the resource cost, is usually
what gates how many levels you take in a sitting.

A develop is a **start, not an instant purchase**: pressing Develop begins the node and the level
lands when the timer finishes, so the level number does not move at the moment you press it. The
panel switches to a progress bar with **Pause Development** and **Cancel** while one is running, and
the node's investment bars re-price to the next level once it lands.
