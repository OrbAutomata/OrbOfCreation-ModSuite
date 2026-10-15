# The spell loadout

The loadout is bound by **two independent budgets**, and confusing them is the most common modelling
mistake here.

**Spots** are how many spells can be loaded at once. **Weight** is a capacity budget spent by what
you load. A weight-0 spell still consumes a spot. E.g., one observed loadout sat at 4/4 spots with
weight only 4/6 — completely full and completely unable to accept another spell; a weight-3 spell
there is double-blocked, needing both a free spot and three free weight.

**Spots are parallel cooldown lanes** (throughput); **weight is capacity** (what you may carry).

A spell's weight is its authored upkeep while its "use effectively" requirements are met, and its
upkeep plus an authored penalty until they are; spell types can take weight off.

Spot count is authored: base **3**, the **Bandwidth** upgrade adds one, and the **Casting Mastery**
research adds one, for a maximum of **five** in the authored data. The weight budget is raised by
`Improved Spell Weight`. The same budget appears under three different names; see
[vocabulary.md](vocabulary.md).

## Rules that follow from the two binders

- **Duplicate slotting is legal, and cooldowns are per copy.** Two copies of one spell run two
  independent cooldowns and roughly double both the casts per charm window and the XP accrual. Four
  copies of one spell is a legal loadout.
- **You cannot swap a spell while it is on cooldown**, so loadout changes cost cast downtime on the
  lane being edited.
- **Fitting a new spell may require evicting one** — the game refuses with "No available spell spot".
- **Glyphs are baked in when a spell is added**, raising its load cost; there is no in-place edit.
  See [augments.md](augments.md).

Owning a spell and loading it are separate decisions against separate budgets: the library holds
spells you are paying neither weight nor a spot for.

## The second loadout

The **Duplicate Spellbook** upgrade gives a second saved loadout to switch between. It does **not**
enable dual-casting — only one loadout is live at a time — you cannot switch while effects are
ongoing, and **cooldowns freeze in place while a loadout is unloaded**, so switching away and back
does not launder a cooldown.
