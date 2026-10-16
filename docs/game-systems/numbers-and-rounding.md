# Numbers and rounding

Numbers are stored as a **mantissa and an exponent**. That representation holds enormous values
comfortably but keeps only a fixed number of significant digits.

## How the screen spells a number

The settings menu offers five number notations; **Scientific** is the one the suite keeps the game
on, and the rule below is that notation's.

From a tenth up to a thousand the number is written plainly, and its decimals narrow as it grows:
three below one, two below ten, one below a hundred, none from a hundred up. A whole number takes
no decimals at any of those widths. Outside that window — below a tenth, and from a thousand up —
it is a mantissa of exactly two decimals against a lowercase `e` exponent. Zero is `0`.

| value | on screen |
|---|---|
| `0.7` | `0.700` |
| `4.5` | `4.50` |
| `12.34` | `12.3` |
| `123.456` | `123` |
| `4400` | `4.40e3` |

The mantissa is rounded where it stands and never carried into the next exponent, so a hair under a
million reads `10.00e5` rather than `1.00e6`.

Changing the notation from the settings menu mid-run moves the screen without moving the suite's
answers, which stay in Scientific; the next load writes the setting back and the two agree again.

## Spending to zero

**Spending your entire stock can round the remainder to literal zero.** When a purchase price is many
orders of magnitude below your holdings, the subtraction has no digits left to record the leftover
and the remainder collapses to 0 instead of a small number. The Reverb Rate and Replenish Ratio
growth terms exist as protection against this; see [growth-terms.md](growth-terms.md).

## Two significant digits, two rules

Prices round to two significant digits, and the game uses two different rounding rules:

- **Attributes** use an early-rounding form that only alters values in the range `[10, 100)`. Outside
  that window an Attribute price is whatever the pipeline produced, unrounded.
- **Upgrades** use the full form, which snaps at every magnitude. An upgrade price is always two
  significant digits.

Discovery pool prices and spell level costs also snap to two significant digits, which is why ladders
read as clean numbers like 90 / 900 / 9,000.
