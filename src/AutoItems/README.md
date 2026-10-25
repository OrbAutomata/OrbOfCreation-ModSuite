# Auto Items

Auto Items is the disabled-by-default Scroll, Relic, and temporary-item automation service compiled into
`OrbModSuite.dll`. It has no independent plugin identity, cadence, or configuration store. Its one
feature-wide quick control is owned by the shared automation control registry.

The worker consumes immutable shared-world consumable facts and preserves native family membership
as a set. A sole supported membership selects its operation; the game's authored `Fruit + Relic`
topology selects the permanent Relic operation, while every other cross-operation combination
fails closed. Eligible Relics come first, then Scrolls, then exact-UUID-approved temporary items,
and the worker plans at most one action per publication. A Scroll whose last attempt found no
target is passed over for the next eligible Scroll; once every eligible Scroll has found none, the
rotation starts over. All evaluator paths wait for another world or configuration publication;
the engine prevents a second attempt against the same world reading.

`AutoItemsConsumableUseGameAction` owns automation admission on the shared `NativeGameAction`
shell. Its complete binding set extends the same consumable-use bindings the explicit player/MCP
action uses, while keeping family, target, duration and cost policy in automation. Both callers use
one native transaction and queue sentinel; explicit player use preserves the player's randomization
choice and does not acquire automation's family or target-selection rules. Composition registers
every action once in `LifecycleGameActions`, which owns rebinding and disposal.

Automation re-resolves stable UUID plus exact type and rechecks family, visibility,
global live targeting idleness, native inventory idleness, `CanFire()`, Scroll randomization/live
targeting, and temporary
duration/toxicity-only cost vectors. It captures ownership permits, submits through
`SelectAndFire()` under native multi-buy quantity one, and verifies one outcome: the exact item
entered the native preparation queue. It does not snapshot stock, randomization, or usage counts as
parallel postconditions. An absent transition is a `use_not_verified` fault for that exact item: it
backs off through the shared
[per-target retry gate](../../docs/runtime-architecture/service-cycle-runtime.md#per-target-retry-gate)
and is tried again once due. The scan ends an item's backoff when it stops considering the item at
all: not approved, not visible, out of stock, or of an unusable shape.

A failed ownership read, live Scroll-target check, or temporary-item catalog read writes its full
original exception to the suite fault log on every attempt. Its visible reason is an authored
sentence plus that entry's reference. Repeated ownership failures keep the same health revision
while updating the displayed reference; another publication or attempt still performs the read.

One lifecycle-scoped follow-up observes the committed temporary item through later publications.
Receipt reconciliation records a committed use even when the next capture fails. Pending and
player-paused tracking survive missing, unavailable or skipped consumables readings unchanged;
only a clean `CollectionCategories` result permits usage inference or another candidate scan.
The service waits for the next publication, and capture failures keep their existing collector log
cause without adding a feature fault. Exactly one usage must engage before disappearing. A queued
usage that vanishes before it engaged is
cancelled: the worker reports it to the retry gate, so the item backs off with doubling until one of
its usages engages. A second usage of the item belongs to the player and pauses that exact item until
no usage of it remains, then the item resumes; that pause logs one warning each way, ends any backoff
of the item, and the feature stays Operational. While any item backs off and its retry is not yet due, and Auto Items is on, the
feature reports Degraded with the latest item and reason, under a contract reason when the fault was a
contract or multi-buy failure before the native call. The first failure of an episode logs one
warning and the end of the backoff logs one more; the failures between them stay quiet. A temporary item still
preparing holds every use back; once its effect is active, only another temporary use waits, and
Relics and Scrolls keep firing. Native Scroll/Relic preparation excludes a temporary use.

Configuration is additive and uses the suite's committed publication:

- `AutoItems.Mode` defaults to `Disabled`;
- `AutoItems.UseScrolls` defaults to `true`;
- `AutoItems.UseRelics` defaults to `true`.
- `AutoItems.TemporaryItemAllowlist` defaults empty and accepts comma-separated exact UUIDs only.

An empty allowlist leaves temporary items inert. The allowlist is parsed once per committed
configuration generation and pinned to the cycle; actions carry no configuration key. There is no
current-configuration reader in the adapter or GameAction. Committed master disable releases the
ownership lease as a fast backstop.

The Mods page is the only in-game editor for exact temporary-item approval. It sends each discovered
item through the worker's shared exact-topology family resolver and lists only resolved Fruit,
Potion, and Thread operations, ordered by operation then displayed name. Fruit + Relic items therefore
remain Relics and are not listed. Each row shows the native icon, its resolved operation followed by
every other authored family name, and current stock beside a raised/recessed approval row.

That display capture runs on the Unity main thread and validates its complete read set before it
enumerates one item: `ConsumableSO.All`, each entry's exact `GetGuid()` identity, the visible
discovery flag, the `consumableTypes` relationship with every type's own `GetGuid()`, the item's
native `GetIcon()`, and the current private `quantity` stock field. Names are deliberately not in
that set. Every item and family label comes from Common's already-bound live entity catalog, so the
picker owns no parallel `GetName()` reflection contract and the manifest carries no picker-owned
name binding; an item or family the catalog cannot name is a loud failure rather than a
UUID-labelled row. The capture keeps only immutable facts plus each item's captured sprite — no
consumable, family, or native UI object survives the call.

The state line always says how many
discovered items are approved. Stored UUIDs that do not resolve remain explicit removable rows; a
failed native discovery read is a red failure state and never masquerades as the healthy
`No discovered temporary items yet` state; stored entries remain visible and removable alongside
that failure. Every click changes only the staged serialized value,
so Apply, Revert, external-conflict handling, and persistence remain the ordinary Mod Config path.
There is no raw-text, family, select-all, or blacklist control.

The shared quick control changes only `AutoItems.Mode`; exact temporary-item approval remains on
the Mods page. Native evidence and remaining live-validation limits are documented in the
[native action surfaces](../../docs/reverse-engineering/native-action-surfaces.md).
