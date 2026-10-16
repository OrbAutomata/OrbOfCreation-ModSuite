# Crafting

**Workshop > Crafting** is a list of recipe rows. Each row shows its complete cost line — every
resource, not a summary — rendered **red when you cannot afford it**, and a row can be greyed out
entirely when the recipe is not currently craftable.

The page carries a **Manual | Automate** split at the top. The game itself draws the line between
one-off crafting you drive by hand and standing production, and the Manual side works on its own
regardless of what the Automate side is set to.

Each side is a strip of slots, and the two strips are counted separately. Observed on Workshop:
**three Manual slots and a single Automate slot**, so exactly one recipe can be standing production
here at a time and automating a second is refused until the first is cleared. An Automate slot
carries a **number badge**, which is the amount queued on that entry rather than a count of slots;
cancelling steps that badge down and only removes the card when it reaches zero.

Scribing is its own surface with its own page, and its own separate pair of strips with different
slot counts; see [scribing.md](scribing.md).

## Crafts pay at submission

Crafts **pay when you submit them**, not when they complete. A queue that is already full will still
accept another craft of a recipe that can stack with what is already queued. What happens to the
finished item is governed entirely by the carry rules, and a paid craft can be lost there; see
[carry-limits.md](carry-limits.md).
