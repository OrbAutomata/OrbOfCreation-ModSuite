# Quality of Life

An opt-in automation feature that applies selected conveniences through the shared ServiceCycle
host. Its page and gameplay toggle use the common feature settings and status controls.

**Mute passive notifications** silences the reflective Learning mutagen popups whose native
passive descriptions offer notification muting. It waits until a passive has been encountered and
leaves hidden, silent, already-muted, and unrelated passives alone. The game continues executing
resource effects; this preference controls notification presentation.

Muting uses the native saved `PassiveAbilitySO.muted` preference. While enabled, Quality of Life
reapplies that preference if the player unmutes a supported passive. Disabling the feature or its
notification option stops applying changes and leaves saved mute choices in place; the player can
then unmute them with the game's own control.

The worker reads one complete immutable passive publication. On the Unity main thread, the one
GameAction resolves the supported UUID and exact native type again, checks current eligibility and
ownership, sets the saved mute preference, and verifies that preference. Shared lifecycle management
rebinds the complete contract and discards prepared work across run changes. Native failures use the
shared target retry gate and remain visible in feature health.
