# Modding hooks

[Back to index](README.md)

Where to attach, what not to touch, and how to prove a mutation landed. BepInEx already ships
Harmony — never bundle your own copy.

## Entry points

| Target | Use | Risk |
|---|---|---|
| `GameManager.InitGame()` postfix | the registry is rebuilt and the runtime is ready; the reliable first-resolve point | Low |
| `GameManager.StartGame` postfix | a session began | Low |
| `SaveStateManager.ImplementLoadedJson()` prefix/postfix | bracket a save load; act once objects are populated | Low–medium |
| `GameManager.BeforeSave` / `AfterSave` triggers | synchronize state a mod owns with a normal save | Low–medium |
| `PersistentResetManager.PersistentResetLogic()` prefix | NG+ started | Low–medium |
| `GameManager.ResetGameState()` prefix | a reset started | Low–medium |
| `StructureSO.QueueBuild(int)` / `UpgradeSO.Purchase()` postfix | observe queue mutations, including ones you did not cause | Medium |
| `StructureSO.CompleteAction()` / `UpgradeSO.CompleteAction()` postfix | observe completion | Medium |
| `GameManager+GameElementIterator.Increment(float)` / `ViewManager.CheckViews()` | the per-element tick and per-view check; neither catches, so one throwing element stops every element after it each tick | High; runs for every element every tick |
| `ResourceSO.Gain` prefix/postfix | global or selective gain multipliers | Medium; called very frequently |
| `InputManager.Update` | the existing input path | usually unnecessary — a plugin `Update` is simpler |

The four lifecycle boundaries in [architecture.md](architecture.md) are the ones that make cached
references invalid. Drop every resolved object, price, capacity reading, and prerequisite cache
state at each of them, and re-resolve from the stable UUID.

## Members that are not a supported call

- **`ResourceSO.MakeVisible()` is private.** Change visibility through a proven public gameplay
  path, or through a deliberately labelled reflection/Harmony operation that you own the risk for.
  It is not a Toolbox-grade call.
- **A `CraftingInstanceListVariable` does not retain caller ownership.** The game's own automation
  puts its repeating instances in these lists, so an instance you find there is not yours to edit,
  remove, or claim. Observe it as external supply.
- **`Prerequisites.Container.Check()` — the no-argument overload — permanently caches `true`**
  until `Reset()`. Re-reading it will not notice a condition that later became false. The per-level
  `Check(ConditionInfo)` overload is uncached and is the one to use when the answer must be
  current.
- **Anything a page is the only caller of.** See [ui-internals.md](ui-internals.md).

## Proving a mutation landed

Capture → call → verify delta, with the postcondition chosen to prove *identity and outcome*, not
bookkeeping:

1. Resolve the target from its stable UUID **and** confirm the exact managed type.
2. Capture the specific observable that the call is supposed to move — queued quantity, queued
   purchase level, stock, usage count.
3. Make exactly one call.
4. Capture again and require the expected delta.

Then classify the result honestly. A call that completed cleanly and moved nothing is a **benign
skip**, not a fault — the game refused, which it is entitled to do. Reserve the fault
classification for a call that threw, or one whose after-state cannot be read, because those are
the genuinely ambiguous cases where you do not know whether a side effect landed. A partial group
that stopped early because the game stopped admitting is a partial success.

An ambiguous mutation should block that target until a lifecycle boundary replaces it. There is no
way to un-ring the bell by re-reading.

## The global multi-buy override

Some purchase paths honour a global multiplier rather than taking a count
(see [native-action-surfaces.md](native-action-surfaces.md)). Driving them for a specific count
means writing a global:

1. Resolve `GlobalVariables.GetMultiBuy()` and read its current value with `IntVariable.AsInt()`.
2. `SetValue(int)` the count you want, then read it back and verify.
3. Make the call.
4. Restore the original value and verify the restoration **on every exit path**, including the
   exception path.

If entry or restoration cannot be verified, do not mutate at all — a multiplier left set is a
global change to the player's manual purchases. Remember the player's value from before the entry
and retry the restoration on a back-off; never capture the value you left behind as the player's.
Take a process-wide lease on it so two features cannot interleave writes.

The multiplier is the `MultiBuy` `IntVariable` asset. It has `saveValue` set and `dontResetValue`
clear, so the save carries it and `ResetData` clears it to its base value of 1.

`IntVariable.AsInt()` reaches `GetValue()`, which recalculates and re-stamps an observable when the
record is dirty — a write on a call that reads like a plain scalar. It is acceptable at this
boundary, where a write is already being made; a read-only pass takes the multi-buy and
`Player.GetBulkDevelopment()` values off the world snapshot instead, by the uuids pinned in
[`data/known-entities.tsv`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/known-entities.tsv), under the memo rule. Nothing offline can
prove the asset carrying a uuid is the one the singleton returns, so
`AutomataWorldCollectionCheck.CheckGlobalSingletons` calls both in the running game and compares;
an unreadable value falls back to 1, which buys one level at a time.

## The existing developer console

`DevConsoleEngine` already contains `ResourceRefresh`, `ResourceVisible`, `ResourceGain`, and
`ResourceSetQuantity`. `ResourceGain` calls `ResourceSO.Gain`; `ResourceSetQuantity` calls
`ResourceSO.SetQuantity`. They are worth reading as worked examples of how the game itself drives
the resource API, and they are a possible command surface in their own right — their parameter
parsing has not been decompiled.
