# The spell loadout

The loadout is bound by **two independent budgets**, and confusing them is the most common modelling
mistake here.

**Spots** are how many spells can be loaded at once. **Weight** is a capacity budget spent by what
you load. A weight-0 spell still consumes a spot. E.g., one observed loadout sat at 4/4 spots with
weight only 4/6 — completely full and completely unable to accept another spell; a weight-3 spell
there is double-blocked, needing both a free spot and three free weight.

**Spots are parallel cooldown lanes** (throughput); **weight is capacity** (what you may carry).

A spell's weight is its authored upkeep while its "use effectively" requirements are met, and its
upkeep plus an authored penalty until they are, minus the weight reduction of each of its own spell
types. E.g., in one observed run every Cantrip loaded one lighter than its authored upkeep (Channel
Blaze 2 rather than 3).

Spot count is authored: base **3**, the **Bandwidth** upgrade adds one, and the **Casting Mastery**
research adds one, for a maximum of **five** in the authored data. The weight budget is raised by
`Improved Spell Weight`. The same budget appears under three different names; see
[vocabulary.md](vocabulary.md).

A spell whose tooltip says **"Insufficient req. to use effectively."** (its `Req.` lines name
Expert research levels it has not reached) carries a **requirement penalty** on its weight. The Loadout grid
prints the doubled number in red. E.g., Sanctify (base weight 6) showed `12` while Expert Storm
sat at 17/21, so the game refused to load it with 7 weight free. The tooltip prints the penalty as its own lines, and it is not always that
doubling: Ralochs's Cube and Power Strike each showed `+3` Spell Capacity, ×2 cost and ×2 cooldown.

## Rules that follow from the two binders

- **A newly discovered spell is loaded for you when a spot is free.** The first spell of a run
  arrives already sitting in slot 1; adding it again makes a second copy rather than moving it, so
  check the bar before paying to load something you already have.
- **Duplicate slotting is legal, and cooldowns are per copy.** Two copies of one spell run two
  independent cooldowns and roughly double both the casts per charm window and the XP accrual. Four
  copies of one spell is a legal loadout.
- **You cannot swap a spell while it is on cooldown**, so loadout changes cost cast downtime on the
  lane being edited.
- **Fitting a new spell may require evicting one** — the game refuses with "No available spell spot".
- **Glyphs are baked in when a spell is added**, raising its load cost; there is no in-place edit.
  See [augments.md](augments.md).
- **Loading a discovered spell is a pick from a list.** Magic > Spellbook > Loadout shows the spell bar above
  and every spell you have discovered below; choosing one from that list drops it into a free spot.
  Acquiring a spell in the first place is the discovery press, not a separate composing step; see
  [discovery.md](discovery.md).

Owning a spell and loading it are separate decisions against separate budgets: the library holds
spells you are paying neither weight nor a spot for.

## Saved loadouts and the Loadouts panel

The **Duplicate Spellbook** upgrade gives a second saved loadout to switch between. It does **not**
enable dual-casting — only one loadout is live at a time — you cannot switch while effects are
ongoing, and **cooldowns freeze in place while a loadout is unloaded**, so switching away and back
does not launder a cooldown.

Saved loadouts are managed in a **Loadouts panel opened by the book button at the top right of the
window**. It belongs to no screen and is not reachable from any tab or subtab strip. The panel
holds:

- the active loadout's **colour, icon and editable name**, all three cosmetic;
- **eight loadout rows**, each with its own icon and a row of eight spell slots;
- two section rows, **Equipment** and **Alchemy**, each with an include/exclude toggle.

Those two toggles are the part that matters: a saved loadout can reach beyond spells into your
artifacts (see [artifacts.md](artifacts.md)) and your alchemy grid (see [alchemy.md](alchemy.md)),
and each section is opted in or out per loadout. What an included section does across a switch is
untested; see [open-questions.md](open-questions.md).
