# Game Fixes

Patches that repair the game's own code where it crashes, makes needless noise, does needless work
or keeps time wrongly. Game Fixes is not automation: it has no service and no GameAction, and it
never starts a game action. The rules every fix follows are the
[game fix exception](../../docs/runtime-architecture/game-boundary-doctrine.md#game-fix-exception).

A class A fix prevents a game fault or removes noise, and a class B fix computes the game's own
result with less work. Neither changes gameplay outcomes: every resource, level, unlock, purchase
and saved value is what an unpatched game that did not fault would have produced. A class C fix
changes an outcome on purpose. It exists only by explicit ruling, is off by default, and its setting
says in player terms what changes. The precise run clock is the one class C fix.

## How a fix is built

Each fix is one `GameFix` in its own files, listed once in `GameFixCatalog`. The fix owns its
setting, its patches and a counter of how often it changed what the game did; a fix that acts on
every tick keeps none (`GameFix.Fired` stays zero). The catalog supplies
the settings to the Game Fixes page and installs every fix once per Harmony instance when the
suite installs its own patches. A patch whose target is missing, or whose transpiler meets a body other
than the audited one, is not installed: the fix stays off, the log warns once, and Game Fixes reports
**Contract unavailable** while that fix is selected.

A game fix is not automation, so Game Fixes registers no service and lives outside the automation
runtime. Its contribution supplies a standing feature (`CreateStanding`), which the plugin composes
with its patches and drives every frame and on every configuration change, also while **Automation
enabled** is off and the automation runtime is never composed. `GameFixesHost` switches each installed
fix on exactly while its setting is selected and Game Fixes is Active; **Automation enabled** and
**STOP ALL** leave it running, and `StopsWithAutomation` keeps the feature status from reporting it
held by either or faulted with the automation runtime. Once gameplay is ready it reports the fixes
running, reads the counters every `ActivityPollFrames` frames and publishes them as the feature
detail; nothing logs inside the game's tick.

## Sound pool guard

On by default. `SoundManager.GetAudioElement` returns null once every pooled element is playing a
loop, and `SoundManager.Play` and `PlayLoop` then fault on it, which ends that game tick for every
element after the caller. The guard is a postfix on `GetAudioElement`:

- A null answer becomes the one silent overflow element (`SoundOverflowElement`). It is cloned from
  the prefab the game builds its pool from and kept outside the pool. Its own `AudioSource` is muted
  when it is cloned, and an `AudioElement.Play` prefix keeps it from starting that source; the mute
  still holds where Mono inlined the 13-byte `Play` into a caller compiled before the patch. The game
  never sets `mute`, and `AudioSource.set_mute` is declared against the checked-in
  `UnityEngine.AudioModule` reference. Everything the game chains on a played sound, including a later
  `FadeOutDestroy`, works on it.
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
argument is the volume, and a delay is set later on the returned element.

A sound that ends up looping is never merged. `PlayLoop` is not patched. `Spell.StartCharging` and
`UIResearchItem.LoopResearchSound` start their loops as `Play().SetLooping()` and keep the chained
result, so a postfix on `AudioElement.SetLooping(true)` that meets the overflow element of a merged
hand-out plays the remembered clip on a real element and returns that one instead. Every other
hand-out of the overflow element forgets the merged clip first. `SetLooping` is 21 bytes of IL,
over Mono's 20-byte inline limit, so its callers always reach the postfix.

`StructureSO.CompleteQueuedQuantity` plays one power-build sound per power fire and sets a pitch and
a delay on each, so every merged repeat still starts a delayed-play coroutine on the overflow element.
The merge silences all but the first, so `PowerBuildSoundCap` bounds that loop at
`PowerBuildSoundCap.Cap` while this fix is on: the batch plays its first power-build sound and starts
nothing else. Its transpiler accepts only the audited loop shape: one `GetPowerBuildSfx` call
entered from a branch to the compare and closed by `ldloc.1; blt`. The batch, its power fires and
its popup are unchanged.

## Lighter auto-development

Off by default. `EngagedEffectInstance.GetAndApplyCreationTimes` counts the developments one tick
completes by subtracting the build time from the elapsed duration, at most 10,000 times, and calls
`getMaxDuration.Calculate()` after every subtraction. For auto-development that is
`AutoPurchaseInstance.CalcTotalTime`, which runs `StructureSO.GetNextCost` each time. The transpiler
routes that one call through `AutoDevelopmentCostOnce.CalculateOnce` with a flag local: the first call
in a run of the loop calculates, and while the fix is on every later one returns the build time that
call stored. The loop, its cap and its subtractions are the game's, so the completions and the
leftover duration are bit-identical. The game's dead closed form (one division and floor) is not used,
because it can differ from repeated subtraction by one completion.

The parity proof is in the game's IL. The loop body writes only its counter, `duration` and
`maxDuration`. The game stores only two kinds of build time in `getMaxDuration`: a constant
(`CalculatedValue.Static`, from the constructor and `SetMaxDuration(BigDouble)`, which crafting uses)
and `CalcTotalTime` (from `AutoPurchaseInstance.Initialize`); `SetMaxDuration(Func)` has no caller.
`CalcTotalTime` reaches nothing in `EngagedEffectInstance` or `ObservingCache`, draws no random
number and writes nothing it reads: the only writes on its way are lazily refreshed caches
(`ValueModifierRecord.GetValue`, `IdObjectRef.GetRef`) that the first call settles, and fresh
temporaries. The transpiler accepts only the audited 26-instruction loop after the closed form's
`ret`, with one `Calculate` in the method; the branch targets are pinned by the contracts.

## Precise run clock

Off by default; class C. `PersistentResetManager.ManagerUpdate` advances the run clock as
`resetTimePassed.SetValue(resetTimePassed.AsFloat() + deltaTime)`, so every tick rounds the sum to
single precision. At the game's 0.02-second tick the clock runs 2.3% slow from 2^13 seconds (about
2.3 hours) to about 18 hours, then alternately fast and slow by more, and at 2^19 seconds (about
145.6 hours) a tick is below half a step and the clock stops. The transpiler replaces that one
statement's `AsFloat`, add and conversion with `PreciseRunClock.Advance`, which adds the tick to
`DoubleVariable.AsBigDouble()`, the value `AsFloat` narrows, while the fix is on, and adds in single
precision exactly as the game does while it is off. It keeps no counter: it acts on every tick, so
a count would tell the player nothing and would republish the status at every activity poll.

The clock is a `DoubleVariable` saved as `NumberSaveData.savedValue`, a `BigDouble`, so the precise
time is what the game saves and loads; nothing new is written. Only `PersistentResetLogic` writes the
clock otherwise, to zero. `ChallengeCondition.HasFailedTime` compares the clock with the limit as
`BigDouble`, so timed challenges with a limit between about 2.3 and 18 hours get about 2% stricter.
Milestone timers (`SaveStateManager` progression moments) and lifetime rates
(`ResourceSO.GetLifeTimeRate`) read the same clock. The scene wires the same asset,
Time Played this Reset (`9ea78fb7-5fbc-4022-8025-f0b413684761`), into `PersistentResetManager` and
`Player`, which is the variable the strategist clock and the MCP world overview publish.

## Contracts

Every patched method, injected field and directly called game member is declared in
`data/native-contracts.json` under `game-fixes.*`. `GameFixesSoundContractTests` pins the facts the
patches rely on: who takes elements from the pool, the one `Play` overload, the prefab field, the
element's own sound source, that the game never mutes it, and the only place it starts, who loops a played sound, where a spell
stores its loops, which disable the UI items inherit, and the power-build loop shape.
`GameFixesAutoDevelopmentContractTests` pins the completion loop byte for byte, every build time the
game can store in `getMaxDuration`, and that auto-development's build time reaches no write to what
it reads.
`GameFixesRunClockContractTests` pins the clock statement byte for byte, that only the reset writes
the clock otherwise, that `AsFloat` narrows the value the game saves as a `BigDouble`, and that the
timed-challenge check reads the clock at full precision.
