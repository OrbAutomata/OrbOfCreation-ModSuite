# Pool unlockers

Unlocker glyphs — the game also calls them Recipe Books — are option-space expanders. They contribute
no rate and no power of their own; they change what the discovery pools *contain*. A rollable enters
a pool only once **all** of its glyphs are unlocked.

E.g., observed unlockers: Learn Insight grants the Manifestation glyph and unlocks the Insight
resource; Learn Psionic grants the Elemental glyph and opens a new spell and augment pool; Learn
Storm grants the Storm and Spark books.

Because a rollable needs every one of its glyphs, unlockers control *when* it is worth paying to
roll: a pool enriched before you draw from it gives better options for the same price.

Glyph Discoveries is its own discovery tree with its own price ladder, separate from Spell
Discoveries; see [discovery-pricing.md](discovery-pricing.md).

The socketable augment is called a glyph too. Tell them apart by the keyword on the glyph's type
line: unlockers carry **Elemental**, **Forging**, **Alchemical** or **Manifestation**, while
augments carry a keyword beginning `Spell `. See [vocabulary.md](vocabulary.md).

Observed on one endgame save: 25 of the 47 glyphs are unlockers, split Elemental 7, Forging 10,
Alchemical 5, Manifestation 3. Most carry the name of a same-named entry in the recipe-book list —
the glyph *Arcane* and the recipe book *Arcane* are the two faces of one unlocker — though the book
list is longer than the glyph list, so the pairing is not one-to-one.

**Code shape:** the Spellcraft picker asks `GlyphSO.IsAvailable()`. Discoverable glyphs answer
from their discovery state; non-discoverable pool unlockers answer from their authored `Learn X`
prerequisite. The raw glyph discovery field is therefore not a universal learned-state signal.
