# Game fixes

[Configuration](configuration.md)

**Game Fixes** repairs the game's own code where it crashes or makes needless noise. A fix only
prevents a game error or removes noise: it never changes what you earn, buy, level or unlock, and
your save is the same as the game would have made without the error. Game Fixes starts Active.
Each fix has its own switch on the **Game Fixes** page, and every switch takes effect immediately,
without **Apply**. **STOP ALL** and **Automation enabled** turn the fixes off too; the game then
runs its own code untouched.

## Sound pool guard

On by default. The game plays sounds through a fixed number of channels. At very high speeds,
looping sounds (spell charge and hold loops, research and brewing loops) can fill every channel.
The game then throws an error on every tick that tries to play another sound, and development,
managers and spells stall. With the guard on, a sound that finds no free channel plays silently
instead, and the guard releases looping sounds the game would otherwise leave running. The only
difference you hear is that some sounds stay silent while the channels are full.

## Merge repeated sounds

Off by default. When the game asks for the same sound several times in one frame, it plays once.
After a development batch, a few power-build sounds play instead of one per power fire,
which keeps very large Bulk Development batches from stuttering. Only the sounds change.

## Reading its status

The Game Fixes header and the **Runtime** page show how often each fix acted, for example how many
sounds played silently because every channel was busy. If a game update moves code a fix relies on,
that fix is not installed, the game runs its own code, and Game Fixes shows **Contract unavailable**
with the fix's name while that fix is selected.
