# Mentor

Mentor is a feature area inside `OrbModSuite.dll`, not a separate plugin. It
shares a configurable percentage of earned native mastery XP with
lower-mastery recipients without subtracting XP from the source.

## Sharing policy

Mentor handles three independent domains:

- Spells are enabled with Mentor. `EquippedSpells` (the default) lets each
  equipped spell share with discovered spells below that source's mastery.
  `HighestDiscovered` instead accepts only sources at the highest discovered
  spell mastery.
- Artifacts are opt-in. A highest-mastery created artifact may share XP earned
  through its equipped `IncrementActive` path with lower-mastery created
  artifacts.
- Alchemy is opt-in. A highest-mastery discovered ordinary recipe may share
  with lower-mastery discovered ordinary recipes. Scholar Concepts are excluded
  by exact recipe/type identity and Concept membership.

`SharedPool` (the default) divides the configured percentage across the eligible
recipients. `PerRecipient` gives that percentage to every recipient, so the
total bonus scales with collection size. Every grant carries the source's
exclusive mastery ceiling; no recipient at or above that mastery is eligible.

Fresh installs start with `General.Mode=Disabled`. Set it to `Active`, press
`Left Alt + M`, or use Mentor's Mods feature command or drawer control. Its
recessed/raised native frame shows saved intent structurally; color is secondary.
Waiting, unavailable contracts, emergency blocking, partial capability loss, and
faults are a separate runtime-health axis in its tooltip and the Mods Runtime
page.

## Service-cycle shape

Mentor is an ordinary ServiceCycle service. It never reads BepInEx
`ConfigEntry` values or Unity objects on its worker:

1. Four deliberate Harmony patch contracts record exact spell, artifact, and
   alchemy XP inputs into a bounded, lifecycle-scoped journal.
2. World collection publishes those value-only inputs together with discovery,
   creation, mastery, equipped-spell, alchemy-type, Concept-membership, and
   progression-view facts.
3. The Mentor worker consumes each sequence once, selects and orders recipients
   from the immutable world, applies the configured economy, and returns typed
   grant actions.
4. The Unity-thread action boundary resolves the recipient again, revalidates
   lifecycle, UUID/type, ownership, eligibility, and mastery ceiling, invokes
   the native mastery path, and verifies one progress sentinel: spell/alchemy
   XP increased, or artifact level/XP advanced.

The spell and alchemy postfixes retain the exact native XP argument.
`EquipmentSO.IncrementActive` prefix/finalizer plus the
`ExperienceContainer.GainExperience` prefix associate an artifact's exact gain
with one successful equipped tick. Discovery, creation, reset, loadout, and
apply-mastery signal patches are gone because the next world publication already
contains those facts. Mentor suppresses observations caused by its own grants.

Those four patches are inputs, not a parallel runtime, and they exist because
earned XP is not a snapshot delta: a native mastery rollover consumes saved XP
and Mentor's own grant writes the same value, so subtracting two world
publications cannot recover what was earned. The journal is main-thread and
value-only, its rows are sequence-stamped, collection copies them onto the frame,
the worker consumes each sequence once even when later generations repeat a row,
and the sequence resets when the collected epoch changes.

ServiceCycle owns scheduling, lifecycle retirement, fair action turns, status,
and trace projection. There are no Mentor operations-per-frame or CPU-budget
settings. Configuration schema 4 discards those obsolete keys. The remaining
controls are mode, shortcut, emergency disable, economy, spell source policy,
the three sharing percentages, artifact/alchemy enablement, and diagnostics.

## Safety and lifecycle

The game remains authoritative at the action boundary. Spell and alchemy grants
must produce the expected saved-XP transition. Artifact grants predict the
native `ExperienceContainer` level and residual-XP transition on a clone, then
verify mastery, container, and saved XP together. A throw, no-op, partial,
unexpected, unsaved, or unobservable mutation faults the action rather than
claiming success.

Mastery plus the relevant spellbook, artifact-workshop, or alchemy view must be
available before that domain plans work. Scene, save-load, reset, NG+, and
registry replacement retire stale cycles and native references. Exact stable
UUID plus expected native type owns identity; names are diagnostics only.
Mentor never changes source XP, loadouts, recipe activity, costs, quantities,
discovery state, or action queues.

The mastery-input journal is bounded. If the worker falls behind its retained
history, the missed sequence is projected explicitly instead of reconstructing
XP from snapshots. Traces name the service `Mentor` and expose last input
sequence, missed inputs, planned actions, and recipient count without changing
the trace wire format.

## Verification

Portable evaluator, action adapter, native adapter, composition, status, patch,
and trace-schema coverage lives under
`tests/OrbModding.Tests/Services/Mentor/Runtime/ServiceCycle` and the shared
ServiceCycle suites. Installed-game tests audit every capture, action, and patch
contract against the supported assembly. Real Unity/Harmony wiring, visible
controls, recursion suppression, save transitions, and exact in-game XP remain
the Mentor V4, persistence, and combined-suite gates in
`docs/testing/runtime-validation.md`.
