# Research

Research is unlocked by the *Innovation* upgrade and gets its own tab.

- Each node costs **typed advancement points** — the school-specific currencies in
  [advancement-currencies.md](advancement-currencies.md).
- A node **takes time** to develop; observed durations run roughly 15–20 seconds.
- A node also **drains a resource** for the duration of its development. These drains are usually
  negligible relative to production and only occasionally bind.
- **Most nodes cap at level 1.** A few go higher.
- **Completing a node may reveal further research.** The game does not preview what a node will open;
  you find out by finishing it.
- **Bonus levels count towards other nodes' requirements, but not towards finishing this one.** A
  research node with bonus levels can satisfy a prerequisite of `≥ n` elsewhere while still counting
  as incomplete and below its own maximum level, because completion counts only the levels this node
  itself has taken. **Code shape:** the prerequisite path dispatches `GetLevel()`, whose research
  override includes bonus levels, while `IsMaxLevel()` reads `GetBaseLevel()` and excludes them.

The research page shows five spendable school-point balances across the top, and each Develop button
carries its per-school point cost, rendered red when you are short.

Because the point supply is finite and run-global, every research node competes with every other for
the same pool.
