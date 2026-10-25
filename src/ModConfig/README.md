# Mod Config UI

This folder is the in-game configuration and runtime-diagnostics surface of the suite. It is not a separate plugin and carries no version of its own; everything here compiles into `OrbModSuite.dll` and loads under the suite's single plugin GUID.

The page edits the suite's own settings and, because it discovers loaded plugins generically, the typed settings of other BepInEx plugins installed beside it. The registered plugin title is displayed verbatim; the UI does not shorten or rewrite it.

## Settings editor

- Discovers loaded plugins and typed BepInEx configuration entries.
- Groups settings into Runtime, General, seven automation feature pages, and Advanced while preserving the original section/key contract.
- Renders feature mode once in the page header as an immediate committed-store command; mode is not repeated in the staged setting list.
- Supports booleans, enums, bounded and unbounded numbers, strings, and keyboard shortcuts.
- Replaces the Auto Items temporary allowlist's generic string editor with a whitelist-only picker
  of discovered native items while retaining the same staged string setting and Apply/Revert path.
- Stages edits until Apply, supports per-setting Default and selected-mod Revert, and rolls back earlier writes if Apply fails.
- Honors optional presentation metadata for labels, dependencies, restart guidance, and hidden compatibility keys.
- Keeps unstaged fields synchronized with external changes.
- Detects a staged/live conflict and requires **Keep mine** or **Take live** before Apply.
- Generation-stamps catalog definitions, preserving navigation across one rebuild when plugins or entries change.
- Preserves same-page scroll position and remeasures variable-height rows when the available width changes.
- Removes its owned Unity objects and listeners on scene exit or plugin unload.

The Mods tab is cloned from native top-level navigation, remains last, and selects a mod-owned
overlay rather than modifying native content panels. Selecting Mods again while it is active keeps
it open, matching the native `ViewRadio` reselect behavior; selecting another native tab closes it.
`[Interface] EnableButtonShell = false` disables that integration if the game UI changes
incompatibly.

Restyled suite controls sample the audited native `UIViewRadioButton` subview frame pair. OFF uses
`baseImage`; configured ON uses `activeImage`, so intent differs structurally before the
suite-owned gray/green/red/orange icon color is applied. Quick controls close to an emergency-stop
button and one disclosure under `Canvas/HelpButtons`. The disclosure opens the seven registered
feature controls to the right; when closed, faults and blocks add an exclamation marker as well as
red color. General invokes the same immediate STOP/resume command instead of staging
`EmergencyDisable`. The anchor is resolved through the declared scene-bound `UIContentArea.canvas`
contract. Missing required types, fields, sprites, or structural paths are
suite defects on the matching audited baseline: installation retries bounded startup timing, then
logs the exact terminal reason and a typed-candidate census at error level and publishes it to
Runtime. There is no cloned native toggle, text quick-control, or alternate shell fallback. See the
[live UI protocol](../../docs/testing/runtime-validation.md#runtime-read-only-surfaces).

Suite-owned UI objects request `RectTransform` explicitly from the native
`GameObject(string, Type[])` constructor. Quick controls and Mods share one startup-readiness gate.
After Main's first end-of-frame boundary it checks the six required direct top-bar icon candidates
at 100 ms cadence for at most 30 seconds. The game's own delayed list renderer creates and
populates those entries on a machine/load-dependent, seconds-scale schedule after suite scene entry;
the suite observes rather than predicts that moment. Zero or partial-zero counts remain startup
waiting and do not increment either failure state. All six populated icons admit both suite
surfaces in the same Update, so normal appearance is within 100 ms of native icon readiness.

Duplicates, null icons, wrong fields/types, or continued absence after that bounded window bypass
the startup lane. Each surface then retains the same five-second retry cadence and three-attempt
waiting-to-terminal diagnostic policy; the first genuine failure logs that installation will
retry, and type failures name the member plus expected and actual managed types.

The Mods shell uses the subview-radio sample for a left-hand navigation rail,
active and inactive page frames, the outer detail frame, settings controls, and footer controls.
The cloned native behavior is removed from Mods navigation, and suite buttons have no
`Selectable.targetGraphic`; suite rendering remains the only pixel writer.
Native-skinned suite panel frames remain raycast targets: the settings viewport therefore routes
wheel input to its own `ScrollRect` across rows, text, gutters, and padding, while the quick-control
drawer frame owns its complete rectangle so input cannot pass through its grid gaps to native UI.
Decorative text, icons, and inset fills remain non-raycasting.

The Auto Items picker uses those same captured base/active frames for raised/recessed approval
rows. Its suite-owned images display icon sprites captured from the item's audited
`TooltipableObject.GetIcon()` contract; no live native UI object is retained, cloned, or mutated.
Item identity, discovery visibility, family, name, and stock are validated as one complete read
before a healthy catalog is rendered. A missing contract, invalid item, or read exception produces
the picker's explicit failure panel instead of an empty list or a fallback label/icon.

At installation, the BepInEx log reports exactly:

- `Quick controls: native state frames and icons active`
- `Mods rail: native visuals active`

A terminal failure replaces the corresponding success line with
`Quick controls: native state frames or icons failed: <reason>` or
`Mods rail: native visuals failed: <reason>`. Both surfaces are also registered as Suite UI
capabilities on Runtime.

## Runtime page

Runtime is separate from staged configuration. It joins evidence by exact plugin GUID and service ID and does not treat a successful config save as proof that behavior applied immediately.

It presents:

- a compact feature-health grid with failures and attention states first;
- configuration-schema and feature-health status;
- per-service capability state and current reason;
- latest scheduling and cycle evidence;
- one bug-report action that packages already-held evidence into a capped zip;
- a separate player-facing game-math check;
- start/stop controls for profiling builds;
- read-only rolling decision-journal health; and
- one 30-minute automation timeline of committed work, followed by one quiet average/worst
  processing-time line.

The page receives neutral status and command ports only. It has no pump, trace buffer, writer, storage adapter, or filesystem authority. The health grid is a presentation over the same joined feature snapshots used elsewhere; detailed plugin cards remain sorted by severity. Cards update through the existing open-page cadence. Changed feature details schedule a refresh even when the condition stays the same; identical snapshots schedule no refresh and detail-only changes emit no condition transition. Pending refresh work has a hard 30-frame admission bound and the Runtime footer exposes pending and last-refresh age.
The action timeline excludes `Source`-shaped infrastructure such as World collection. It also excludes
Mentor by its exact registered service identity: Mentor's per-recipient mastery grants remain available
to health and trace consumers, but that steady fan-out is not a discrete player-facing automation event
for this chart. The view renders 30 fixed one-minute baseline slots and stacks committed action counts
for the remaining `Ordinary` services, using one deterministic suite-palette color per service. The
linear height scale is the largest visible bucket. Only active included services enter the compact color
legend. Empty minutes remain quiet baseline slots; a fully quiet window is the single line
`No automation activity in the last 30 minutes`. Waiting, skipped, rejected, and merely planned work is
not charted. Any fault in an included automation bucket adds one small red triangular base marker so
fault presence does not depend on color alone.

The plot labels its linear scale `Completed actions / minute`, with zero and the current visible maximum.
Selecting any minute highlights its slot and shows the included services' outcomes for that minute below
the legend. Release copy uses `completed`, `not applied`, `skipped`, and `failed`; the profile build uses
the exact diagnostic terms `committed`, `rejected`, `skipped`, and `faulted`. A selected minute with no
outcomes says `No automation outcomes in this minute`. These outcome lines do not turn rejected or skipped
work into bar height, and Mentor and `Source` services remain excluded from both chart and detail.

The timeline revision changes only at a minute boundary, for committed work, for a newly visible fault
marker, or for lifecycle clear. Ordinary within-minute no-commit cycles leave the rendered presentation
byte-identical. Skipped, rejected, and repeated-fault counts accumulate behind that stable revision and
become visible on the next commit, first fault, or minute boundary rather than repainting at cycle cadence.
A lifecycle change
discards all 30 buckets, and the existing journal monotonic timestamp supplies fixed minute keys; no
UI timer, coroutine, second clock, game read, or disk path participates.

## Configuration behavior

A successful Apply publishes exact plugin GUID plus section/key invalidations through Common. Validation failure, save failure, or rollback publishes nothing. A failed or future suite schema remains selectable as a read-only status-only tab without exposing configuration paths or serialized values.

Unsupported custom setting types remain read-only. Closing the panel preserves staged values for the current scene; Revert explicitly discards only the selected mod's staged values. The Mods shell and General safety settings remain available when `General.Enabled` is false.
