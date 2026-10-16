# Time Advancements

Time Advancements (TA) are the currency that buys rune levels. Two facts define them:

- They are **refunded on every world reset**. TA is a pure allocation currency — you are deciding
  where it sits, never whether to consume it.
- The TA bar's **maximum is the TA this run started with**, so a full bar means everything is
  unallocated and an empty one means it is all sitting in runes. The maximum only moves at a reset:
  the whole pool is refunded and the ceiling rises by what the run's masteries added.
- The Reset page shows **three** TA numbers, and only one of them is the bar's ceiling.
  **Previous** is what *this* run started with and is the number the bar maxes out at; **new** is
  what this run has added so far; and **starting** is the projection for *next* run, previous plus
  new. Straight after a reset new reads `0` and previous equals starting. So mid-run the bar ceiling
  tracks **previous**, not the larger **starting** figure beside it — the gap between the two is
  what this run has earned and not yet been given.
- Your starting TA next run is the sum of your rune mastery (`+1` per rune mastery level), your spell
  mastery (`+1` per spell mastery level) and your Achievement Strength (`+1` per point).

Because per-level rune costs rise while returns diminish, TA tends to be spread over several runes
rather than poured into one; see [time-runes.md](time-runes.md).
