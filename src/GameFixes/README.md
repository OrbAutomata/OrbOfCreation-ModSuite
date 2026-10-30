# Game Fixes

Patches that repair the game's own code where it crashes or makes needless noise. Game Fixes is not
automation: it has no service and no GameAction, and it never starts a game action. The rules every
fix follows are the [game fix exception](../../docs/runtime-architecture/game-boundary-doctrine.md#game-fix-exception).

A game fix may only prevent a game fault or remove noise. It never changes gameplay outcomes: every
resource, level, unlock, purchase and saved value is what an unpatched game that did not fault would
have produced. A fix that would change an outcome is not a game fix and does not belong here.

## How a fix is built

Each fix is one `GameFix` in its own files, listed once in `GameFixCatalog`. The fix owns its
setting, its patches and a counter of how often it changed what the game did. The catalog supplies
the settings to the Game Fixes page and installs every fix once per Harmony instance when the
gameplay runtime is composed. A patch whose target is missing, or whose transpiler meets a body other
than the audited one, is not installed: the fix stays off, the log warns once, and Game Fixes reports
**Contract unavailable** while that fix is selected.

`GameFixesRuntime` switches each installed fix on exactly while its setting is selected, Game Fixes
is Active, **Automation enabled** is on and **STOP ALL** is clear. It reads the counters every
`ActivityPollFrames` frames and publishes them as the feature detail; nothing logs inside the game's
tick.

## Sound pool guard

On by default. `SoundManager.GetAudioElement` returns null once every pooled element is playing a
loop, and `SoundManager.Play` and `PlayLoop` then fault on it, which ends that game tick for every
element after the caller. The guard is a postfix on `GetAudioElement`:

- A null answer becomes the one silent overflow element (`SoundOverflowElement`). It is cloned from
  the prefab the game builds its pool from, kept outside the pool, and an `AudioElement.Play` prefix
  keeps it from ever starting its source. Everything the game chains on a played sound, including a
  later `FadeOutDestroy`, works on it.
- A pooled element first stops its coroutines and cancels its fade. Otherwise a delayed play still
  pending from its previous owner restarts whatever it plays next, and a loop restarted that way holds
  its channel for good. This sits on the hand-out, not on `AudioElement.Initialize`, so music, which
  initializes its own elements, is untouched.

It also releases loops the game leaves holding a channel, each through the game's own half-second
fade: the hold loop `Spell.InitializePersistence` and the charge loop `Spell.StartCharging` are about
to overwrite, and a research or brewing item's loop when the item is disabled or destroyed
(`UIRenderGroupElement.OnDisable`, through `UIResearchItem.DestroyResearchSound` and
`UIBrewingStation.DestroyLoopingAudio`). A disabled item that renders again starts its loop again.

## Merge repeated sounds

Off by default. A prefix on `SoundManager.Play` plays each one-shot clip once per frame and hands
every repeat in the same frame the overflow element. The clip is the whole key: `Play`'s second
argument is the volume, and a delay is set later on the returned element. Looping sounds are not
merged.

Per-frame merging does not cover a large development batch: `StructureSO.CompleteQueuedQuantity`
plays one power-build sound per power fire, each with its own pitch, delay and coroutine, so the
repeats differ and land in later frames. `PowerBuildSoundCap` bounds that loop at `PowerBuildSoundCap.Cap`
sounds while this fix is on. Its transpiler accepts only the audited loop shape: one `GetPowerBuildSfx` call
entered from a branch to the compare and closed by `ldloc.1; blt`. The batch, its power fires and
its popup are unchanged.

## Contracts

Every patched method, injected field and directly called game member is declared in
`data/native-contracts.json` under `game-fixes.*`. `GameFixesSoundContractTests` pins the facts the
patches rely on: who takes elements from the pool, the one `Play` overload, the prefab field, the
only place an element starts its source, where a spell stores its loops, which disable the UI items
inherit, and the power-build loop shape.
