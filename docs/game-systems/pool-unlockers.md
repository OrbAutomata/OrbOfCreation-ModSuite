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

25 of the 47 glyphs are unlockers, split Elemental 7, Forging 10, Alchemical 5, Manifestation 3. Each
carries a same-named entry in the recipe-book list — the glyph *Arcane* and the recipe book *Arcane*
are the two faces of one unlocker, linked by `GlyphSO.associatedRecipeBook`. 34 books ship, so nine
(Compulsion, Death, Dismantle, Electric, Life, Occultic, Principle, Spirit, Tempered) have no glyph
behind them at all.

**Code shape:** what the player presses is the book tile, and `UIRecipeBookItem.IsVisible()` asks
`RecipeBookSO.IsAvailable()` — the *book's* own prerequisite container, not the glyph's. The two
containers disagree on 16 of the 25 pairs, and on Gloves and Herbalize they disagree in substance, so
reading the glyph's is reading the wrong `Learn X`. The Spellcraft picker separately asks
`GlyphSO.IsAvailable()`, which for all 22 augments is just their discovery state.
