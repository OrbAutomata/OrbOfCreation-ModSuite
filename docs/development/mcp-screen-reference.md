# Game MCP screen reference

[Game MCP index](mcp-tools.md)

Navigate the live UI and inspect request-specific native facts. [Screen descriptors](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpScreenTools.cs) own names, arguments and fixed probes. These examples are partial; UI handles are volatile.

`game_navigate` is classified **UI-only, no gameplay/save mutation**. It is not read-only because
selecting a screen, a subtab path, or a tile commits live UI state. Success returns `activeScreen` and every
independent `subtabStrips[{active,labels}]` state, read exactly once, from the settled destination.
A hierarchy still assembling one frame after the click still carries the departed screen's strip, so
it is never a source.

**A strip belongs to the screen the game is drawing, not to whatever is alive.** The strip capture
asks `UIRenderGroup.IsActive()` up each radio button's ancestry, the same question
`game_screen_elements` asks of a hover element, because a departed screen's strip is never
deactivated and stayed on the wire for every screen after it. Settlement is built from this very
capture, so the predicate is also what settlement settles on: a stale strip that is persistently
present is perfectly stable, and waiting longer would have confirmed the wrong one forever. If the navigation shell is gone by the time arrival settles, the response says
`subtabStripsUnavailable` with that reason rather than publishing a strip nobody read. A screen or subtab match refusal returns the exact
live label candidates it compared. A subtab refusal reached its screen before it failed, and its
sentence says so: the screen change is a committed effect the caller can see in `activeScreen`. It carries no static mutation-scope label or counter ceremony. Navigation
never authorizes a gameplay or save mutation.

## Screenshots and navigation

`game_screenshot` has no required parameters and returns an MCP `image` content block with
`mimeType: image/png`. A capture costs its reader whole 28-pixel patches — `ceil(width / 28)` x
`ceil(height / 28)` tokens — so pixels are the entire price and neither the image format nor its
compression enters it. That is why the PNG path is unconditional and there is no quality knob:
lossy encoding would buy wire bytes, which are free, at the cost of readability, which is not.
Every capture arrives 896 pixels wide — 32 patch columns, the narrowest width at which every class
of on-screen text stays readable through the suite's resampler. There is no width parameter,
because that choice has one answer: a narrower capture stops being readable and a wider one costs
patch columns for pixels no reader gains anything from. The response reports the encoded `width`
and `height`, which are therefore the exact cost of what it just sent, plus `scene` and whatever
native modal is covering the board, and echoes nothing else. There is deliberately no crop or
region parameter: cropping risks removing what the caller actually needed, and seeing more of the
board than was asked for is how an agent notices what it did not know to look for.
`game_screenshot` and `game_navigate` both carry `openModals` — the game-written title of every open
`UIModal` — whenever at least one is open, and `openModalsUnavailable` with the reason when that read
is not possible. Neither field appears when the read succeeded and no modal is open.
`{"save":true}` additionally writes a server-generated, collision-resistant name under the current
trace folder. The caller supplies no basename, and there is no per-process filename cap.

```sh
tools/game-mcp-client.py screenshot --output artifacts/current-screen.png
```

`game_continue` is deliberately separate from tab navigation. On `Start` it invokes the audited
native `SaveStateManager.StartGame` method for the save the player has already selected. It cannot
select, delete, reset, import, or rewrite a save, and it accepts no native type, method, or UI input
from the caller. Its success waits for the transition and returns the new `scene` and
`runtimeAvailable` state. A load slower than that wait still answers `scene: Start`, so that answer
also carries `pressed: Continue landed; the scene has not changed yet` — the press is a fact the
verb owns whether or not the scene has caught up, and without the clause a slow load reads exactly
like a call that did nothing.

The same load leaves the game in the shape every documented verb assumes: Research Queue Mode on,
Cancellable Spells on, and number notation `Scientific`. Each is the exact write the settings
dropdown performs on the settings variable the game reads back, verified afterwards, and skipped
when the game already holds it; nothing is persisted to the settings file, because the modal's
close is what persists and no modal was opened. Without them a queued develop and a spell toggle-off
refuse for reasons a caller cannot see coming, and the numbers the game draws stop matching the
numbers the wire carries. The response says nothing about any of it — an unattended caller should
never need to know a settings screen exists. A normalization that does not land is the exception:
it costs one `agent settings:` line on `suite_health` naming why, present exactly while the last
load's normalization is the one that failed and gone once a load succeeds or the run ends, because
the refusals it causes are otherwise unexplainable from the wire.

`game_return_to_menu` is the opposite lifecycle boundary. On `Main` it invokes the visible
`UIBackToMenuButton.BackToMenu` callback, which raises the game's authored manual-save event before
requesting the literal `Start` destination. Back to Main Menu lives inside a panel rather than on
the board, so the tool takes the player's whole route: with the panel shut it presses that panel's
own button first — the one panel whose contents actually hold the control, matched by containment
and never by caption — and then presses the control. A panel that will not open, or an opened panel
without one interactable control, answers `faulted`, not refused: pressing the panel's button already
changed the game, and the sentence says whether the panel is now open. The
response is completed as soon as the native screen
fade becomes active, before scene teardown can invalidate the HTTP operation. Its compact success
is `status: committed, scene: Start` — which controls the tool pressed to get there is how it drove
the UI, not a fact about the game; the scene transition then clears every lifecycle-retained
world, identity, binding, and lease through the ordinary lifecycle observer. The tool cannot choose
a save, suppress the save event, select another scene, or run while another transition is active.

There is deliberately no process-exit tool. Both installed quit entry points call
`UnityEngine.Application.Quit` directly and expose no game-written state that can be verified while
the process remains able to deliver an MCP response. See
`docs/reverse-engineering/clean-exit-boundary.md` for the audited drop.

`game_modal(mode, title?)` uses the game's panels.

`mode="open"` presses the one chrome control that puts up the panel `title` names — the controls
`game_screen_elements` prints as `opens: modal`. The press is `UIModalActivator.OpenModal()` rather
than the icon's own `ToggleModal`, so it has one outcome whatever the panel was doing, and the game
sets the panel's open flag inside that call, so the action reads the pressed control's own panel
with `UIModal.IsOpen()` in the same frame and answers `opened: <panel>`. A press after which that
panel does not read open faults `verification_failed`; a game error during the press commits when the
panel reads open and faults `post_commit_fault` with a log reference when it does not. A different
panel already covering the board refuses
`modal_already_open`, because the chrome beneath a modal takes no presses; asking for the panel
that is already up answers with the state it is in. A name no control on this screen opens refuses
`no_modal_named` and names the panels it does offer; two controls answering to one name refuse
`ambiguous_modal`.

**The panels are the player's own chrome and nothing else: Player and Settings.** Two filters
produce that list, and the verb's list, the `opens: modal` rows in `game_screen_elements` and the
`no_modal_named` sentence all read the one enumeration, so no two of them can name different panels.
The first filter is reach: an activator nested inside a closed `UIModal` is not on this screen,
which matters because closing a panel only drops its canvas group's alpha and raycasts and leaves
every panel the session ever opened alive in the hierarchy. The second is the named set itself —
Reset World, Game Complete! and Dev Console are never listed and never pressed, and a title outside
the set refuses `modal_not_offered` (`ERR_UNAVAILABLE`) before the scene is read at all, so the
sentence reads the same on every screen: the suite will not serve this press, and the game refused
nothing. There is no achievements panel to add, because `PlayerStatsAchievements` is a tab inside
Player rather than a panel of its own.

`mode="dismiss"` drives the visible close control on the one open native `UIModal`.
It refuses `no_open_modal` when there is no modal, `multiple_modals_open` when more than one makes
the target ambiguous, and `modal_close_not_ready` while the native grace period still disables
closing. The tool names no entity, so the
caller submits no lifecycle: the boundary reads the live lifecycle itself and pins it for the settled
read. The action invokes `UIModal.CloseModal()`, verifies
the game-owned closing flag, then watches that exact modal for up to the shared one-second
settlement bound. A close that leaves the flag unset faults `verification_failed` rather than
refusing, because the only way to put a modal back is the game's own open, which reruns the panel's
scene-wired open events; a game error during the close commits when the flag reads set and faults
`post_commit_fault` with a log reference when it does not. A settled read the game cannot answer
faults `modal_state_unavailable`. A completed close answers `dismissed: <modal title>`, read off the
control before it shut, so the caller confirms which modal went away without a screenshot; an
untitled modal publishes no `dismissed` rather than an empty string. Timeout remains committed, carries the same
`dismissed`, and says the post-state is unavailable because the verified close already began. It
does not click modal-specific confirm, purchase, reset, or destructive buttons.

`game_screen_catalog` reads the live Main-scene UI. Top tabs retain native rail order. Current
subtabs are active `UIViewRadioButton` controls under the current native content area. Inactive
popup templates are excluded. The response is a structured `scene` plus ordered `screens`, each with
its `label`, its `active` flag and its `locked` flag; the active screen additionally carries
`subtabStrips`, where every independent strip names its `active` label and its ordered `labels`.
`locked` is the game's own `ViewSO.IsAvailable()` negated, the same member every view's published
`available` reads, so the catalog and the world cannot disagree; it prints on every row, because a
table that promises a column owes it to each row and a missing marker reads as "unknown". A view
the shell cannot read reports locked. Mods is the suite's own screen, has no `ViewSO`, and
is never locked. Unity hierarchy paths and
unstable numeric indexes are deliberately absent. Inactive tab content is not instantiated, and the
audited v1.0.5 data and scene assets do not carry an authoritative tab-to-subtab roster. The catalog
therefore omits inactive subtabs rather than navigating speculatively or guessing labels.

`game_navigate(screen, subtab?, uuid?)` accepts exact labels only. It answers in words — the
arrived screen, its strips, and any open modal. `game_screenshot` is the only tool that captures the
framebuffer, so a caller that wants a picture of where it landed asks for one, and a caller that
does not is never charged patch columns for arriving. Name matching is
ordinal and closed-world: zero or multiple matches reject with the exact candidate labels, and a
screen the game has not unlocked is neither a candidate nor a destination — it refuses
`screen_locked`.

`subtab` is a path, parent first and `/`-separated — `Spellbook/Loadout` — because a strip exists
only once its parent is active, so a nested page has no address until the hop before it has landed.
The tool makes those clicks in order, settles between them, and the postcondition is the leaf.

`uuid` picks a tile on the two pages that draw selectable ones. On `World/Agromancy` it resolves a
published `PlotNodeSO` through the one audited active `UIPlotNodeList.OnNodeClick(PlotNodeSO)`. On
`Magic/Spellbook/Spells` it resolves a `SpellRecipeSO` and opens that spell's card through
`UISpellRecipeList.ClickRecipe(SpellRecipeSO)`; the game's click toggles, so the tool clicks only
when the card is not already open, and answers `selectedSpell` once the page's selected-recipe
variable holds it. The book tiles on `Magic/Unlock` are a display of what fills the Spellcraft
roll's pool, and no control on them answers a click, so they are not an argument. A tile asked for
anywhere else refuses `tile_destination_mismatch` naming both pages. A tile is always resolved against a settled page: the server selects the top screen and
any subtab path, waits up to one second for the active screen and complete live strip set to remain
stable across frames, and only then looks for the tile. Resolving against the settled hierarchy is what makes the
subtab candidates the matcher searched identical to the ones the catalog advertises for that screen.
It then waits for settlement again before answering. A timeout stays committed but returns only `postStateUnavailable`; it never labels a
mid-transition strip set as settled. The whole operation
still returns one terminal tool result; callers never split it into a retry sequence.
Mods is a suite-added screen, not one of the game's — see
[runtime architecture](../runtime-architecture/architecture.md#the-mods-screen-is-ours-not-the-games).
It uses that identical catalog-indexed button path. Selecting Mods while it is already active is
an idempotent screen reselect and leaves its page open; the MCP carries no Mods-only toggle case.

```sh
tools/game-mcp-client.py catalog
tools/game-mcp-client.py navigate World --subtab Agromancy --uuid PLOT_UUID
tools/game-mcp-client.py screenshot --output artifacts/agromancy.png
```

The navigation result describes the settled destination. Request `game_screenshot` afterwards when
a picture is needed; the screenshot CLI writes its inline image to the specified local output path.
The screenshot captures the framebuffer in its own operation and has its own result, separate from
the navigation result. [Screen descriptors](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpScreenTools.cs)
own the accepted arguments for both operations.

`game_probe` has exactly three names:

- `runtime`: current Unity scene/frame/time scale, lifecycle state, gameplay readiness, and Mods
  shell liveness;
- `action_queue_room`: live `ActionManager.GetRemainingRoom()`, the native boundary answer that a
  published occupancy snapshot cannot guarantee. The game's own upgrade button queues every level it
  bought without consulting the queue, so the reading goes negative from ordinary play; that is a
  full queue rather than a broken contract, so `remainingRoom` answers `0` and
  `entriesBeyondCapacity` names the overshoot beside it, appearing only when there is one. On the
  menu, before the first game or after leaving one, no game is loaded and the probe refuses with
  `native_probe_unavailable` saying so, instead of faulting or answering the last game's room; and
- `navigation`: live tab and active-subtab counts, useful for diagnosing catalog availability.

To add a probe, add one fixed name to the `game_probe` descriptor's schema and binder in `GameMcpScreenTools` and to closed-world policy, implement its
Unity-main-thread branch without accepting reflection/member input, declare every native type and
member in the schema-3 manifest, add installed-contract and portable behavior tests, and document
why the fact does not belong in published `WORLD`. Facts that workers or strategists generally need
belong in audited world collection instead.
