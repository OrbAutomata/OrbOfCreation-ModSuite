# Pool unlockers

**Recipe Book** is the name on screen; the unlocker glyph behind a book is the same thing seen from
the discovery side. Unlockers are option-space expanders: they contribute no rate and no power of
their own, they change what the discovery pools *contain*. A rollable enters a pool only once
**all** of its glyphs are unlocked.

E.g., observed unlockers: Learn Insight grants the Manifestation glyph and unlocks the Insight
resource; Learn Psionic grants the Elemental glyph and opens a new spell and augment pool; Learn
Storm grants the Storm and Spark books.

Because a rollable needs every one of its glyphs, unlockers control *when* it is worth paying to
roll: a pool enriched before you draw from it gives better options for the same price.

An unlocker is **bought once**: the purchase grants the glyph and widens the pools it belongs to,
and there is no per-glyph level to raise afterwards. How many copies of one unlocker a spell may
use, and what raises that number, is unrecorded; on augments the same count limits how many copies
one spell can socket, see [augments.md](augments.md).

Glyph Discoveries is its own discovery tree with its own price ladder, separate from Spell
Discoveries; see [discovery-pricing.md](discovery-pricing.md).

The socketable augment is called a glyph too. Tell them apart by the keyword on the glyph's type
line: unlockers carry **Elemental**, **Forging**, **Alchemical** or **Manifestation**, while
augments carry a keyword beginning `Spell `. See [vocabulary.md](vocabulary.md).

25 of the 47 glyphs are unlockers, split Forging 10, Elemental 7, Alchemical 5, Manifestation 3; the
other 22 are augments. Every one of the 25 carries a same-named Recipe Book — the glyph *Arcane* and
the book *Arcane* are the two faces of one unlocker. 34 books ship, so nine (Compulsion, Death,
Dismantle, Electric, Life, Occultic, Principle, Spirit, Tempered) are book-only, with no glyph behind
them.

**Code shape:** the tile Magic > Spellbook > Unlock draws is the book's (a display, not a button),
and it is the book's own prerequisite container that decides whether the tile shows (`UIRecipeBookItem.IsVisible()` asks
`RecipeBookSO.IsAvailable()`), not the paired glyph's. The two containers disagree on 16 of the 25
pairs, and on Gloves and Herbalize they disagree in substance, so the glyph's prerequisites are the
wrong place to read what a book needs. The Spellcraft picker asks `GlyphSO.IsAvailable()` instead,
which for all 22 augments is just their discovery state.
