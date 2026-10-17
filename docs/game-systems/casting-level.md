# Casting Level

Casting Level is a global proficiency track, fed by **all** casts.

- **Casting XP is itself a resource** and carries its own "Maxed in" clock like any other resource.
  The `+1/s` it displays is an observation, and may be the average of XP gained over a recent window
  rather than a true passive rate — whether idle time feeds the track at all is not established.
- **Per level: ×1.04 Cantrip Spell Power and ×1.004 Charm Spell Power**, both compounding — a slow
  global multiplier on the primitives rather than a one-off.
- Higher-level spells generate significantly more Casting XP per cast.
- A purchasable upgrade raises the maximum Casting Level, and the level can be tuned down again.

Casting Level appears in the requirement graph in its own right: the `Spell Output Level` upgrade
stays locked until Casting Level 2, and each further `Raise Output Lv` waits on the next Casting
Level (e.g., the second purchase needs Casting Level 3).

Requirement rows naming "Output Lv" read the **dial's raised maximum**, not its current setting —
tuning the dial up does not satisfy a gate; buying `Raise Output Lv` does. Casting Level and Output
Lv are separate counters and are easy to conflate.
