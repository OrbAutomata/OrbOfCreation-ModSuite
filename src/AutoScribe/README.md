# Auto Scribe

Auto Scribe is a disabled-by-default ServiceCycle feature compiled into `OrbModSuite.dll`. It
produces the six audited levelled Scroll recipes needed to cover native structure enchantment
deficits. Investment and Speed remain coverage-only identities because the audited Scribe registry
has no production recipe for them.

The worker consumes immutable Scribe relationship facts, levelled Scroll counts, pending Auto Items
Scroll uses, active one-shot work, and player-owned `AutoScribeInstances`. Unknown or contradictory
evidence for any enabled producible role blocks the complete service for that publication and names
the first exact role and reason. A healthy role is never produced around an unknown sibling.
Published `ActiveScribeInstances` occupancy is normal backpressure: a full queue emits no action and
waits for another world publication. The GameAction still revalidates live room immediately before
payment so a queue race is an ordinary refusal, not a feature fault. Action-family contention is
also publication-level backpressure: the service does not start a worker or schedule an action
until it owns `CraftingQueueSubmission`. A post-payment or verification quarantine is dead for the
current lifecycle: later publications do not start another worker or grow fault backoff, and
lifecycle replacement clears the quarantine before planning can resume.

Selection uses stable semantic roles and an audited cost rank as a fair rotating order:
Advancement, Power, Learning, Excellence, Development, then Echoing. The cursor survives across
world publications and resets when role configuration changes, so a permanently affordable cheap
recipe cannot starve later unlocked recipes. It plans at most one action per world publication.
Persistent native automation is external production pressure only; the suite never creates, edits,
or removes an `AutoScribeInstances` entry.

Each visible recipe owns an independent progression frontier derived from that Scroll's
`maxCreatedLv`, strongest owned level, queued work, and pending use. The shared Scribe
`maxStartingLevel` is not copied onto every recipe. A covered stable role probes its next level;
the guarded action then brackets and binary-searches the monotonic native affordability boundary
and crafts the strongest affordable level at or above that request. This advances cheaper and more
expensive Scroll families independently.

Scroll coverage is a consumption pipeline, not an inventory target. For a positive native carry
limit, desired supply is the smaller of uncovered eligible structures and carry capacity. Owned
Scrolls, queued work, and pending uses at or above the frontier subtract from that demand; gifts are
therefore absorbed by the next publication without special handling. A non-positive carry limit
blocks the role with `NonPositiveCarryLimit`, because audited native `Gain()` clamps positive output
to that limit and silently drops it.

The same audit establishes that native “weakest” is level-only. At capacity, a strictly stronger
Scroll replaces the weakest, an equal-level Scroll replaces without changing coverage, and a
strictly weaker Scroll is silently lost after payment. Queue and instant completion execute the
same gain path, and crafting cost is paid before that capacity decision. Demand-driven production
never requests equal-level stock churn: frontier crafts are needed for uncovered structures and
replace dead weaker stock for free when capacity is full. Auto Scribe never calls `Discard()` and
owns no cleanup action.

`AutoScribeOneShotCraftGameAction` owns the only mutation boundary. It resolves one complete
lifecycle-scoped binding set before use, re-resolves the action's recipe, Scroll, enchantment,
registry, and queue identities live, proves the complete role relationship, then preflights
visibility, queue room, competing supply, a valid native target, affordability, exact cost, and
ownership. `PurchaseQuantity` is the last risk taken before the native construction, initiation,
and queue-or-instant admission sequence. Affordability search is bounded, uses only the audited
`CanBuyAt(BigDouble)` predicate, and the chosen level is revalidated for target, competing supply,
and exact cost before payment.

The action verifies one outcome: either the newly constructed exact instance entered the native
queue or that exact instance reached native completion on the instant path. It does not read
resource balances, Scroll ledgers, reconstruct payment deltas, or assemble a receipt. After a native
exception it rereads that same sentinel; an observable outcome commits, while an absent transition
faults and quarantines this GameAction until lifecycle replacement. Ordinary pre-payment refusals
and readiness/ownership contention remain quiet. Contract and relationship contradictions,
wrong-thread execution, post-payment ambiguity, and failed verification enter action health and
warning logs. A warning is emitted when that failure state is entered or changes; a persistent
quarantine does not warn again on each publication. Nothing attempts rollback of game-owned
irreversible effects.

Live identity resolution preserves the registry's retryability verdict. `RegistryNotReady`,
`NotFound`, and `StaleGeneration` wait quietly for another publication; `WrongType`,
`AmbiguousEvidence`, and `ContractUnavailable` are persistent contract failures that enter action
health, warn once per failure state, and remain visible until verified recovery or lifecycle
replacement.

The same `AutoScribeOneShotCraftGameAction` also owns the manual one-shot player capability exposed
as `game_craft`. Its player overload leaves the Auto Scribe planner and mutation boundary unchanged, but
widens exact live revalidation to every concrete `CraftingRecipeSO`: native direct `Execute`, or the
authored page's stack/new/instant queue route. MCP success returns the named recipe plus the
smallest observable delta: queue length before and after, or direct completion. Payment accounting
is not a player-action success gate. Direct execution verifies the recipe's monotonic native
`craft` publication; stacking verifies a quantity increase; new work verifies exact-instance queue
membership; and instant work verifies exact-instance completion. The installed mechanism and live
checklist are documented in
[native action surfaces](../../docs/reverse-engineering/native-action-surfaces.md).

Configuration is additive:

- `AutoScribe.Mode` defaults to `Disabled`;
- `AutoScribe.Roles` is a comma-separated list of semantic keys, empty selects every audited
  producible role, and `none` selects no role.

Role narrowing is deliberately cycle-pinned under the runtime doctrine's bounded configuration
staleness rule. Actions contain recipe UUID, Scroll UUID, level, and collection lifecycle only;
they do not carry a role key or re-read current configuration. Every evaluator disposition wakes
only on another publication. The shared registry exposes one feature-wide mode quick control; this
lane adds no evaluation interval, timer, per-role control, temporary-item allowlist, installer
behavior, or persistent Scribe automation.

The installed evidence and remaining live-validation limits are documented in the
[native action surfaces](../../docs/reverse-engineering/native-action-surfaces.md).
