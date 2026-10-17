# Output and Reserve levels

The Casting page carries two global dials. Both are free to move in either direction at any time; the
purchase beside each raises their maximum.

## Output Level

**Output Level is a single global dial for all casting. There is no per-spell output level.** Each
level raises all spell power at a greater spell cost and a longer cooldown, so a higher setting is
**more per cast and worse per mana**. E.g., across one step from Output 1 to Output 2 a spell's cost
went ×2.80 against a yield of ×2.28 and a cooldown of ×1.25.

The ceiling is raised by `Raise Output Lv`, and buying it moves a dial that sat at the old ceiling
up to the new one by itself (see [attributes-and-upgrades.md](attributes-and-upgrades.md)). The
per-level curve has not been extracted, and the dial runs past 50; see
[open-questions.md](open-questions.md).

## Reserve Level

**Reserve Lv** is the passive counterpart. Its tooltip states the trade outright: it "increases spell
cost significantly in exchange for greatly improved passive generation of resources and spell power".
Output shapes what an active cast is worth; Reserve multiplies the standing economy. Per level, read
at one observed setting:

| Per Reserve Lv | Effect |
|---|---|
| ×1.92 | Spell Cost |
| ×1.92 | Spell Drain Cost |
| ×1.33 | Infusion Power |
| ×1.33 | Base All Resource Rate |
| ×1.17 | Cantrip Spell Power |
| ×1.04 | Cantrip Cooldown Speed |
| ×1.02 | Charm Spell Power |

The per-level factors compound cleanly across levels. Whether the exponent is the level or level − 1
is open.

The two dials pull the run in opposite directions: Reserve's resource-rate and infusion multipliers
work all the time, while its spell-cost penalty only bites what you actively cast.
