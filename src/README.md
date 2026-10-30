# Source layout

Builds reference the committed audited assemblies under `lib/game-refs` by default. Set `OOC_GAME_DIR`
to an Orb Of Creation install root to build against that copy instead; it must have this layout (use a
gitignored staged tree on non-Windows platforms):

```text
$OOC_GAME_DIR/
  BepInEx/core/BepInEx.dll
  BepInEx/core/0Harmony.dll
  Orb Of Creation_Data/Managed/Assembly-CSharp.dll
  Orb Of Creation_Data/Managed/Assembly-CSharp-firstpass.dll
  Orb Of Creation_Data/Managed/UnityEngine.CoreModule.dll
```

The suite ships as one BepInEx 5 DLL built by the single project `OrbModSuite.csproj` at this directory's root, which compiles every folder below it. `Plugin.cs` and `SuiteConfiguration.cs` at this root are the one `BaseUnityPlugin` and the one configuration transaction that bind them together. In perf-debug builds `Plugin.cs` builds the one `GameMcpFrameExecutor`, which owns the MCP frame loop, and drains it once per `Update`.

`Common` holds what every feature stands on. `Common/Runtime/World` is the shared game model: the one collected picture of the game that features and the MCP surface read. The rest of `Common` is gameplay-neutral safety and runtime orchestration. It must not own a feature's policy or retain native game objects, and it does not become a gameplay feature because several services use its scheduler.

A capability folder owns exactly one GameAction for one native capability: its action, submission and native bindings. Every submission carries a `NativeActionOutcome`, whose `ToResult()` is the one mapping to a service-cycle result. Features, tooling, and tests mutate the game through that GameAction.

Shared and feature folders:

- `AutoBuy` — buys Structures and Upgrades by cost ratio within the reserve and spending-margin rules.
- `AutoCast` — casts equipped spells.
- `AutoConcept` — assigns acquired Concepts to compatible Active Concept slots, then deepens quantity up to mastery limits.
- `AutoHarvest` — submits harvest actions on fruit and treasure trees.
- `AutoItems` — uses Relics, Scrolls, and allowlisted temporary items through the game's consumable queue.
- `AutoScribe` — queues Scroll Scribe recipes that cover Structure enchantment deficits.
- `Automata` — the automation host: ServiceCycle composition and frame pump, emergency STOP and quick controls, and `Runtime/GameMcp`, the MCP server.
- `GameFixes` — patches that keep the game's own code from crashing, making needless noise or doing needless work, never changing gameplay outcomes; it registers no service.
- `Common` — the shared game model in `Runtime/World`, plus the ServiceCycle scheduler, configuration, diagnostics, tracing, identity resolution, and game math.
- `Mentor` — shares a configurable share of earned mastery XP from spells, artifacts, and recipes to lower-mastery ones.
- `ModConfig` — the in-game Mods tab: strategist advice, staged settings and runtime diagnostics.
- `SpellLeveling` — buys spell levels as an Auto Buy sub-feature.
- `Strategist` — derives [progression advice](../docs/runtime-architecture/strategist.md) and an advisory
  goal preference from a pinned world and sustained-income window.

Capabilities:

- `AlchemyLoadout` — adds and removes alchemy recipes in the active loadout's alchemy slots.
- `Challenge` — selects, queues, abandons, and rerolls challenges.
- `CraftingLifecycle` — automates a crafting recipe or cancels a craft of it.
- `DiscoveryTree` — opens a Discovery Tree offer, then selects, confirms, or rerolls the offered discoveries.
- `EquipmentLoadout` — equips and unequips equipment in the active loadout.
- `GenericLevel` — buys levels for a native level target.
- `HarvestLifecycle` — adds and removes harvest elements and their harvest actions.
- `Loadout` — selects, renames, and restyles loadouts, and saves, loads, and clears their snapshots.
- `PlotLifecycle` — adds and removes actions on a plot.
- `Prestige` — performs the native prestige.
- `Research` — develops, pauses, resumes, cancels, and bonuses research projects.
- `RitualLifecycle` — selects, levels, activates, and cancels rituals, and ends battle.
- `RunTransition` — returns from a run to the main menu.
- `SpellComposition` — sets the four level dials: Output, Reserve, Druidry, and Alchemy.
- `SpellLoadout` — removes equipped spells or moves them to another slot.
- `SpellWorkbench` — crafts a spell from a recipe and augment glyphs.
- `StructureLifecycle` — enables and disables individual structures.
- `Targeting` — answers pending target requests with a chosen or random target, or cancels them.

## Automatic save backup

`Plugin.Awake` runs the automatic save-backup gate before the assembly audit, configuration bind,
Harmony patches, lifecycle subscriptions, feature controls, or ServiceCycle composition. The save
root is Unity's runtime `Application.persistentDataPath`; no platform-specific user path is
embedded. The gate reads only top-level `*.sav` files and `steam_autocloud.vdf`, matching the
supported installer, and opens each file for reading without write sharing. It reads each source
twice, verifies the copied bytes, rechecks the complete source set and contents, and publishes the
backup directory only after every file agrees.

Completed backups are `<save root>/backups/auto-modsuite-backup-yyyyMMddTHHmmssZ`. Incomplete work
uses a distinct `.partial-*` staging name and cannot be mistaken for a completed backup. Retention
keeps five completed directories and deletes only names that match that exact automatic-backup
grammar; installer, manual, malformed, and partial directories are never candidates. A pruning
failure is a visible degraded-health warning but does not disarm automation because the new backup
already exists.

The last successful suite version, normalized save root, verified backup path, and file count are
stored in a strict stamp beside the BepInEx suite configuration. A missing, unreadable, malformed,
wrong-version, wrong-root, or missing-backup stamp triggers another backup. Copy or stamp failure
leaves automation disarmed for the launch and does not publish a success stamp, so the next launch
retries. The release Start card reports the transaction's created/ready outcome without a count or
path; the performance-debug card keeps that outcome and the verified file count. Runtime
diagnostics retain the completed path and count. Both Start shapes and Runtime health show the
exact blocking failure; accepting an unverified game build and clearing STOP cannot bypass this
gate.

## Shared configuration schemas

`OrbModding.Common.ConfigurationSchemaTransaction` is the supported pre-bind configuration boundary. A plugin declares ordered one-version steps and a hidden `[OrbModding] ConfigurationSchemaVersion`; the transaction snapshots the original file, creates a non-overwriting sibling backup, disables `SaveOnConfigSet`, consumes only reviewed source keys, runs normal typed binding, writes the marker last, and saves once. Current schema files bypass mutation. Malformed, negative, future, bind, and save failures return no usable configuration and restore the original file exactly before publishing a sanitized exact-GUID status.

Migration code must use explicit known-key maps and closed safe failure codes. It must not surface arbitrary exception messages, file paths, serialized values, or infer meaning from unknown settings.

## Deterministic runtime foundation

`OrbModding.Common.Runtime` holds monotonic time, catalogs, configuration and strategy publications, the shared world collection, diagnostics, and tracing. The contracts contain no Unity objects or gameplay policy, and the configuration UI consumes their typed projections.

[Common ServiceCycle](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/tree/main/src/Common/Runtime/ServiceCycle) is the runtime foundation. [Architecture](../docs/runtime-architecture/architecture.md) owns the service roster and [Plugin](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Plugin.cs) supplies application dependencies to the [feature catalog](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/AutomataFeatureCatalog.cs); each feature owns its typed composition and metadata contribution. The [testing guide](../docs/testing/README.md) defines the evidence for its execution and recording contracts.

## Shared gameplay controls

[AutomationFeatureControlRegistry](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/AutomationFeatureControlRegistry.cs) is the one ordered roster for the Mods feature headers and the
gameplay quick controls. The closed surface is one native-width compound control under the native
top-left gear and character buttons: a full-size emergency stop followed by an attached,
separately framed disclosure footer. The square's size, alignment, and vertical stack step are
captured from the adjacent native buttons instead of guessed; its icon is the exact shipped
`power-lightning` Sprite, enlarged inside that unchanged square rather than enlarging the control.
The disclosure enumerates the
registry into a transient, native-framed panel below the compound control; [QuickControlColumn](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/QuickControlColumn.cs) owns the layout. It never
maintains a second feature list. [QuickControlsHost](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/QuickControlsHost.cs) owns the installed column, lifecycle reset, retry cadence and diagnostic reporting.
The closed disclosure carries a separate exclamation marker plus
red color whenever a contained feature is faulted or blocked. The surface anchors through the
declared, scene-bound `UIContentArea.canvas` contract and exact `Canvas/HelpButtons` structure. The
footer and panel reuse the audited recessed `UIViewRadioButton` frame as sliced border
dressing;
the panel surrounds an opaque suite-owned background.
Every suite-created UI node requests `RectTransform` through
`GameObject(string, Type[])`; a plain `GameObject(string)` exposes only the declared
`Transform` contract and is never cast to a UI transform.

Each live feature control requires its audited native glyph plus both
`UIViewRadioButton.baseImage` and `activeImage`. OFF uses the recessed inactive frame; configured ON
uses the raised active frame. Gray, green, red, and orange remain secondary health channels, and
tooltips carry the same joined feature status as Mods Runtime. A missing anchor, glyph, or frame
pair creates no corresponding live control and publishes the exact failure. STOP is separately
visible and uses the exact audited `power-lightning` Sprite with immediate press-to-stop and
press-to-resume `Safety/EmergencyDisable` semantics. Its native frame and the attached disclosure
footer use deep green while clear and deep red while stopped; frame structure, glyphs, and tooltips
keep state from depending on color alone. General uses that same command instead of staging the
safety switch behind Apply. Capture/construction failures name the member plus expected
and actual types, log that installation will retry on the shared five-second UI cadence, and become
terminal diagnostics only on the third failed attempt, matching the Mods rail.

## Shared alchemy gameplay-domain classifier

`OrbModding.Common.AlchemyGameplayDomainClassifier` distinguishes ordinary alchemy from Scholar Concepts without reading internal or display names. Initialize it once per lifecycle after `IdScriptableObject.RuntimeLookup` contains the exact `ConceptRecipes` UUID/type asset. Recipe classification then requires exact `AlchemyRecipeSO`, stable recipe UUID, concept-registry membership or verified exclusion, exact `AlchemyTypeSO`, and one of the audited ordinary or Scholar type UUIDs. Missing or contradictory evidence returns `Unknown` with a shared evidence level, named sources, detailed flags, and a diagnostic reason. Active consumers require `IsMutationGrade` rather than trusting the domain label alone.

The classifier binds its native members once at construction, on its own strict binding set; a member the game lacks blocks it for good, naming the contract. It caches the verified registry snapshot and per-recipe results. Call `InvalidateLifecycle()` on scene, save-load, reset, and NG+ changes; do not initialize or reflect inside a per-frame or native-XP hook. See [goals and invariants](../docs/runtime-architecture/goals-and-invariants.md) for the evidence and mutation-grade contract, and [naming traps](../docs/reverse-engineering/naming-traps.md) for why runtime type alone cannot identify a Concept.

## Shared typed registry resolver

`OrbModding.Common.TypedRegistryResolver` is the suite boundary for `IdScriptableObject.RuntimeLookup`. Resolve by non-empty UUID plus exact expected native type, retain the returned lifecycle generation with cached native references, and use `IsRetryable` rather than parsing reason strings. Names are diagnostics only.

Common's suite-internal `KnownEntities` is a checked-in generated declaration set for the small, explicitly selected supported-domain subset in `data/known-entities.tsv`. Each `KnownEntity<TContract>` has a suite-owned type marker plus UUID, expected managed type name, and diagnostic asset name; generated signatures never embed fragile game types. The build verifies it against the [canonical mapping](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/entity-mappings.tsv), while runtime consumers still resolve through `TypedRegistryResolver` and validate the installed game.

`OrbModding.Common.ActionFamilyOwnershipRegistry` is a small process-local safety boundary, not a gameplay framework. Suite features atomically claim only the native mutation families they own, release them with configuration and lifecycle teardown, and recheck their lease immediately before mutation. Exact known external conflicts revoke overlaps; unknown unregistered callers remain an explicit limitation.

## Mentor

`OrbMentor` shares mastery in three independent domains: spells, artifacts, and alchemy. Spells use the native `SpellRecipeSO.GainMasteryExp(BigDouble)` boundary; the optional artifact and alchemy domains use their separately audited native hooks and grant paths. Each domain fails closed independently on contract or lifecycle errors.

## Test build

[The gate](../docs/testing/README.md#the-gate) builds against `tests/OrbModding.GameStubs` in place of the game references. Stub builds are never used for production builds or runtime validation.
