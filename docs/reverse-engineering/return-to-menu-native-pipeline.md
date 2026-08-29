# Back to Main Menu native pipeline

[Back to reverse engineering](README.md)

The playable-scene Back to Main Menu button is a save-and-scene lifecycle boundary. The MCP invokes
the button callback itself; calling `SaveStateManager.BackToMainMenu` directly would skip the
authored manual-save event.

The button is not on the board. It sits inside a panel the player raises first, and the suite
performs that step too rather than refusing while the panel is shut. The button's own label is a UI
asset the audited copy does not ship: `Assembly-CSharp` contains no `"Menu"` string literal at all,
and `artifacts/game-v105` carries only `Managed/`. The resolver therefore never matched a caption —
it binds `UIBackToMenuButton` by type and reads `UnityEngine.Object.name` only to enumerate a true
ambiguity. Prose says "Back to Main Menu" because that is the caption the live round observed.

## Audited v1.0.5 route

```text
UIBackToMenuButton.BackToMenu()                       0x0600280D
  manualSave                                          0x0400148C
  → VoidEventChannel.Raise()                          0x06000BB2
  → SaveStateManager.instance                         0x0400047D
  → SaveStateManager.BackToMainMenu()                 0x060006FF
      ldstr "Start"
      → AnimateChangeScene(string)                    0x06000700
          → UIScreenFlash.instance                    0x0400130A
          → FadeIn(float, float)                      0x060026A3
              writes UIScreenFlash.isActive = true    0x04001308
          → SetLoadingAnim(bool)                      0x060026A0
          → OnAnimComplete(Action)                    0x060026A5
```

The callback raises `manualSave` before it asks the manager to return. `BackToMainMenu` is exactly
the 12-byte sequence `ldarg.0; ldstr "Start"; call AnimateChangeScene; ret`. The fade registers the
later scene-load callback, but writes `isActive` synchronously before `AnimateAlpha`. That flag is
the one outcome sentinel available while an MCP response can still be delivered.

## Audited v1.0.5 panel route

```text
UIModalActivator.Awake()                   button.onClick += ToggleModal
UIModalActivator.Start()                   createdModal = modalFrame.PrepModal(modalTitle, modalContent)
                                           modalCreated = true
  → UIModal.PrepModal(string, RectTransform)
      → Instantiate(this, GlobalVariables.GetPopupContainer().transform)
      → UIModal.SetContent(RectTransform)
          → Instantiate(content, contentArea)      // the live copy of the panel's contents
UIModalActivator.OpenModal()               if (modalCreated) createdModal.Open()
  → UIModal.Open() → PerformOpen()
      → SetElementVisibility(true)         canvasGroup.interactable/blocksRaycasts/alpha, isOpen
```

`SetContent` instantiates the authored content, so a prepared panel leaves two `UIBackToMenuButton`
instances loaded: the authored template and the live copy under `contentArea`. Neither is
interactable while the panel is shut, which is why a live-control resolver finds zero. `PerformOpen`
writes the CanvasGroup and `isOpen` synchronously, so the copy becomes interactable in the same
frame the panel is opened.

## Action boundary

`ReturnToMenuGameAction` binds `UIBackToMenuButton.BackToMenu`, `UIScreenFlash.instance`, the
private `isActive` field, and the panel set — `UIModal.IsOpen`, `UIModalActivator.createdModal`,
`.modalCreated`, `.button`, `.OpenModal`, and `Component.transform`/`Transform.IsChildOf` — once per
lifecycle. On Unity's main thread it requires the current scene to be `Main` and no active screen
transition.

With exactly one live Back to Main Menu control it takes the `RunTransition` family permit, invokes
the button, and verifies only that `isActive` became true. With none, it first resolves the panel
that holds one: the single `UIModalActivator` whose `modalCreated` prepared modal is shut, whose own
button is live, and whose modal transform the control hangs under. Containment is the whole test —
no authored caption participates, so renaming the panel cannot move the control out of reach. It
then takes the permit, presses that activator's `OpenModal`, requires the modal to report itself
open, and re-resolves. A panel that does not open, or an opened panel with no single interactable
control, refuses and says so in the same sentence that reports the panel is now open. More than one
live control, or more than one closed panel offering one, enumerates what it found. No save data,
file, timer, scene coroutine, or secondary receipt is inspected.

Success is returned before scene teardown as `status: committed, scene: Start`, with the two
controls the action operated named as the game names them: `pressedControl`, and `openedPanel`
which is empty when the control was already on screen. The later scene
transition advances the shared lifecycle generation; that boundary clears the identity catalog,
world references, action bindings, leases, and every other lifecycle-retained object through the
ordinary scene-transition observer. The MCP does not wait for a post-transition world because the
operation that owns the response is itself destroyed by that transition.

## Supervised validation

1. Start in `Main` with the panel that holds the control shut, no screen fade active, and note the
   current save timestamp in the UI only.
2. Invoke `game_return_to_menu` once and require the panel to open and the response to complete
   before the screen changes.
2a. Repeat with that panel already open and require the same terminal response without a second
   open.
3. Require the response to contain only `status: committed`, `scene: Start`, the pressed
   control's name, and the opened panel's name — empty on the already-open repeat.
4. Observe the native fade/loading presentation and arrival on `Start`.
5. Confirm a newly connected MCP read reports `Start` and no retained Main-world publication.
6. Continue the selected save and confirm the manual-save event preserved a harmless disposable
   UI-state change.
7. Invoke from `Start` and require the player-language wrong-scene refusal.
8. Invoke during an already active scene fade and require `transition_in_progress` with no second
   transition.

No game or save was touched while deriving this contract.
