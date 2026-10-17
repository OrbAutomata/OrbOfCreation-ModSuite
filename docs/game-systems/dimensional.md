# Dimensional

Dimensional is the World screen's attribute list, priced in **Space**, a Spacial Essence resource.

## How it opens

1. **Learn Formation** (Thaumaturgy + Spark) opens the Formation book, which puts **Conjure Space**
   (Expansion, Conjuration, Cantrip) in the spell pool.
2. Casting Conjure Space pays Space. Space capacity starts small (observed 133), so the first
   casts overflow it.
3. **The World** (80 Space) opens the World screen's Dimensional tab, plus three upgrades:
   **Dimensional Shaper** (20 Space + 100 Knowledge), **Tech Advancements** (800 Space, opens a
   use for Technology Advancements) and **Duplicate Spellbook II** (160 Space + mana).
4. Dimensional Shaper opens the first two attributes.

## The first two attributes

| Attribute | Effect per level (observed at Lv 0) | Price |
|---|---|---|
| Urn of Holding | +0.0684 Space **capacity per second**, ×1.033 per level | Space |
| Urn of Expansion | +0.626 base Space per second, ×1.04 per level | Space + Spark |

**Urn of Holding grows the cap over time** rather than adding a fixed amount: its capacity keeps
rising while you wait. That makes Space a time-gated currency.

## The Shaper upgrades

Each Shaper upgrade is a one-off World-screen purchase that opens two more attributes:

| Upgrade | Price | Opens | What they do (observed, cumulative at the level shown) |
|---|---|---|---|
| Improved Shaping | 200 Space + 400 Knowledge | Containment (Space + mana) | Lv 5: +3.49% Spacial resources gained, +2.68% **all** resource capacities |
| | | Urn of Urns (Space + Thaumaturgy) | Lv 5: +3.98% Urn of Holding effect, +13.3% Urn of Expansion effect |
| Cognitive Shaping | 600 Space + 200 Psi | Memory (Space + Psi) | Lv 3: +5.24% base Mental and Energetic capacity |
| | | Sorcery (Space + Knowledge) | Lv 2: +5.14% base Magic and Elemental capacity |
| Physical Shaping | 4e3 Space + 1e3 Verdant Energy | Physicality (Space + Verdant Energy) | Lv 0: +5.14% base Spacial, Natural and Essence capacity |
| | | Spirality (Space + Skill) | Lv 0: +3.61% base Spacial rate |

Physicality and Spirality read "Insufficient req. to use effectively" below Expert Expansion 2 and
6 respectively. Physicality still took the Space cap from 7.99e3 to 1.33e4 within a minute
(observed, four levels at Expert Expansion 1).

**Gather Space** grants the first World Aspect Slot; see [aspects.md](aspects.md).

These are the cap levers for every price above the Space cap ([capacity.md](capacity.md)).
Conjure Space casts come further apart than the 3 s overcap timer ([overcap.md](overcap.md)), so
overflow drains before it can pay. Tech Advancements' 800 is reached by raising the cap through
Containment, Urn of Urns and Urn of Holding.

## Tech Advancements

Tech Advancements opens the Technology research tab. Its first two nodes are **Improved Auras**
(every Aura attribute: +2 bonus levels, +12 effect levels, −4% cost scaling) and **Improved
Charms** (+2 Spell Capacity, ×1.20 Charm Spell Power, +1 Free Glyph Upgrade, −1.5% Infusion cost
scaling). The first level of each costs 2 Technology Advancements, 1 Magical Advancement and
resources, so the Magical points ration them. Improved Auras' second level asks 2 Technology,
2 Magical and Expert Expansion 4.

The remaining Dimensional attributes (the Cores, the other Urns …) stay locked at this point.
