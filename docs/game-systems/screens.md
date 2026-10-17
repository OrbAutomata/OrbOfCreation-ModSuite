# Screens

Seven screens sit on the top bar, each with its own strip of subtabs directly under the header. A
screen the run has not unlocked is not drawn at all, so a young save shows fewer than seven and they
appear as the run earns them.

| Screen | Subtabs | What it is for |
|---|---|---|
| Magic | Casting · Spellbook · Augments · Wizardry | Casting spells and everything that shapes a cast |
| Scholar | Concepts · Research · Scribe · Scholar | The knowledge economy: passive scaling, timed research, scribing |
| World | Aspects · Dimensional · Agromancy · Druidry | The physical and natural domains, including harvesting |
| Workshop | Artifacts · Workshop · Artificer · Crafting | Gear and item production |
| Alchemy | Alchemy · Materials · Alchemist | Learned alchemical effects and the materials behind them |
| Rituals | Rituals · Discover · Mysticism | Ritual activation and discovery |
| Time | Reset · Challenges · Time Runes | The run-spanning layer: prestige, challenges, runes |

Only Magic is on the bar from the start. Each of the other six arrives with a purchase from the
Upgrades list, and a screen that has not arrived takes every page under it with it — those pages do
not exist yet rather than existing and refusing.

| Screen | Arrives with |
|---|---|
| Magic | present from the start |
| Scholar | Scholarism |
| World | The World |
| Workshop | Aspect: Workshop |
| Alchemy | Aspect: Alchemy Lab |
| Rituals | Aspect: Rituals |
| Time | the reset unlock |

## A subtab is not always the last level

Several pages carry a second strip of their own, drawn above the screen's strip, so the chrome can
be three strips deep. The inner strip belongs to the page: leaving the page takes it away, and a
sibling page that has none simply shows one strip fewer. Observed inner strips:

| Page | Its own strip |
|---|---|
| Magic > Spellbook | Unlock · Loadout · Spells · Spell Types |
| Magic > Augments | Glyphcraft · Upgrade |
| Scholar > Research | the school tabs — Expert · Technology · Innovation |
| Workshop > Artifacts | Loadout · Create |
| Workshop > Crafting | Manual · Automate |
| Alchemy > Alchemy | Learn · Loadout |
| Time > Time Runes | Create · Upgrade · Archive |

An inner page is only reachable once its owning page is active — from Magic with Augments showing,
Loadout is not on screen at all until you pick Spellbook first.

Two strips are always present and belong to no single screen:

- **Upgrades | Inventory** — a persistent right-hand panel visible on every screen. Upgrades is the
  one-shot purchase list; Inventory is your carried items.
- **The casting bar and the bottom level bar** — spell buttons stay reachable while you browse other
  screens, and the bar along the very bottom of the window is Orb XP.
- **The top-right icons** — buttons rather than screens. Each puts a panel over whatever page you
  are on: Player is this run's numbers, and the settings and achievement panels sit beside it.
  A panel covers the board, and the buttons underneath it take no clicks until you close it.

The gear in the top-left corner is not a screen but a modal Settings panel (Main · Game · Graphics),
and it is the only place that holds **Manual Save**, **Export Save** and **Back to Main Menu** —
leaving a run means going through it. Returning to the menu fires the manual save on the way out.

## Which page holds what

| Page | Owning mechanic |
|---|---|
| Magic > Casting | [output-and-reserve.md](output-and-reserve.md) |
| Magic > Spellbook > Unlock | [discovery.md](discovery.md) |
| Magic > Spellbook > Loadout | [spell-loadout.md](spell-loadout.md), [augments.md](augments.md) |
| Magic > Spellbook > Spells | [spell-levels.md](spell-levels.md), [spell-mastery.md](spell-mastery.md) |
| Magic > Augments | [augments.md](augments.md) |
| Scholar > Concepts | [concepts.md](concepts.md) |
| Scholar > Research | [research.md](research.md) |
| Scholar > Scribe | [scribing.md](scribing.md) |
| World > Dimensional | [dimensional.md](dimensional.md) |
| World > Aspects | [aspects.md](aspects.md) |
| World > Agromancy | [agromancy.md](agromancy.md) |
| Workshop > Workshop · Artificer | [workshop.md](workshop.md) |
| Workshop > Artifacts | [artifacts.md](artifacts.md) |
| Workshop > Crafting | [crafting.md](crafting.md) |
| Alchemy > Alchemy | [alchemy.md](alchemy.md) |
| Rituals > Rituals | [rituals.md](rituals.md) |
| Rituals > Mysticism | [disciplines.md](disciplines.md) |
| Time > Reset | [reset-and-ng-plus.md](reset-and-ng-plus.md) |
| Time > Challenges | [challenges.md](challenges.md) |
| Time > Time Runes | [time-runes.md](time-runes.md) |
| Any discipline subtab | [attributes-and-upgrades.md](attributes-and-upgrades.md) |

Pages whose contents are unrecorded are listed in [open-questions.md](open-questions.md).
