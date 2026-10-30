# Game fixes

[Configuration](configuration.md)

**Game Fixes** repairs the game's own code where it crashes, makes needless noise or does needless
work. A fix only prevents a game error, removes noise or saves work: it never changes what you earn,
buy, level or unlock, and your save is the same as the game would have made without the error. Game
Fixes starts Active.
Each fix has its own switch on the **Game Fixes** page, and every switch takes effect immediately,
without **Apply**. Game fixes are not automation: **STOP ALL** and **Automation enabled** leave them
running. Only a fix's own switch, or setting Game Fixes to Disabled, returns the game to its own code.

## Sound pool guard

On by default. The game plays sounds through a fixed number of channels. At very high speeds,
looping sounds (spell charge and hold loops, research and brewing loops) can fill every channel.
The game then throws an error on every tick that tries to play another sound, and development,
managers and spells stall. With the guard on, a sound that finds no free channel plays silently
instead, and the guard releases looping sounds the game would otherwise leave running. The only
difference you hear is that some sounds stay silent while the channels are full.

## Merge repeated sounds

Off by default. When the game asks for the same sound several times in one frame, it plays once.
A development batch plays one power-build sound instead of one per power fire, which keeps very
large Bulk Development batches from stuttering. A sound the game keeps looping, such as a spell's
charge or a research item's hum, always plays. Only the sounds change.

## Lighter auto-development

Off by default. At high speed an auto-developed attribute can complete thousands of developments in
one tick, and the game works out the attribute's cost again for each of them, up to 10,000 times per
tick, although the cost cannot change until the tick's developments are added. With this fix on, the
game works it out once per tick. You get the same results with less work at high speed: the same
number of developments, the same progress toward the next one, to the last digit.

## Reading its status

The Game Fixes header and the **Runtime** page show how often each fix acted, for example how many
sounds played silently because every channel was busy. If a game update moves code a fix relies on,
that fix is not installed, the game runs its own code, and Game Fixes shows **Contract unavailable**
with the fix's name while that fix is selected.
