# Auto Items

Auto Items is the disabled-by-default Scroll, Relic, and temporary-item automation service compiled into
`OrbModSuite.dll`. It has no independent plugin identity, cadence, or configuration store. Its one
feature-wide quick control is owned by the shared automation control registry.

The worker consumes immutable shared-world consumable facts and preserves native family membership
as a set. A sole supported membership selects its operation; the game's authored `Fruit + Relic`
topology selects the permanent Relic operation, while every other cross-operation combination
fails closed. Eligible Relics have priority over exact-UUID-approved temporary items and Scrolls,
and the worker plans at most one action per publication. All evaluator paths wait for another world
or configuration publication; the engine prevents a second attempt against the same world reading.

`AutoItemsConsumableUseGameAction` owns the native boundary. It validates its complete reflection
schema at lifecycle scope, re-resolves stable UUID plus exact type, rechecks family, visibility,
global live targeting idleness, native inventory idleness, `CanFire()`, Scroll randomization/live
targeting, and temporary
duration/toxicity-only cost vectors. It captures ownership permits, submits through
`SelectAndFire()` under native multi-buy quantity one, and verifies one outcome: the exact item
entered the native preparation queue. It does not snapshot stock, randomization, or usage counts as
parallel postconditions. An absent Scroll/Relic transition quarantines the whole action; an absent
temporary-item transition quarantines only that exact UUID.

One lifecycle-scoped follow-up observes the committed temporary item through later publications.
Exactly one usage must engage before disappearing. Multiple usages, premature expiry, or missing
engagement evidence quarantines the exact item loudly. Any temporary usage excludes Scroll/Relic
and other temporary uses; native Scroll/Relic preparation excludes a temporary use.

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
