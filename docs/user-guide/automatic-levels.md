# Automatic level settings

[Configuration](configuration.md)

**Auto Levels** adjusts the global Output, Reserve, Druidry and Alchemy level dials. It starts
Disabled. Turn it on from its Mods page or gameplay quick control; its budget settings require
**Apply**. Turning it off leaves the current dial values in place.

## How levels are chosen

- **Output** checks every loaded spell separately. Its immediate resource cost must fit the
  configured **Spell cost (seconds of income)** budget, which starts at one second of net
  regeneration after current drains. One cheap spell cannot offset another spell's expensive cost.
  A current level above budget is lowered; a higher level is tried only when its exact next cost
  fits. Allocation costs must fit available points instead of an income estimate.
- **Reserve** moves toward the maximum the game currently allows.
- **Druidry and Alchemy** compare running work's resource drain with the whole resource's drain.
  If the domain accounts for more than half of that drain and current stock cannot cover the
  remaining depletion for the configured **Drain buffer (seconds)**, its level is lowered. The
  default buffer is 300 seconds. A higher level needs a complete next-level quote and any additional
  allocation; its prospective drain is checked against the same dominance and buffer rules. Existing
  buffer pressure from other activities holds the dial instead of lowering this domain to compensate.

The buffer measures how long stock covers net depletion, rather than treating the entire drain as
unfunded when production already covers part of it. Exactly half of a resource's drain does not
count as owning most of it. A dial that needs a quote holds when evidence is missing, its control is
locked or no loaded work uses it, and explains why.

Auto Levels changes at most one dial by one level for each fresh world reading. Reductions take precedence;
otherwise eligible dials take turns. It re-evaluates after the game has been read again, rather than
jumping straight to a calculated level. A manual dial change made after that reading cancels the
prepared adjustment; later readings evaluate the new value normally. Disable Auto Levels to keep
all level choices manual.

## Reading its status

The feature header, quick-control tooltip and Runtime detail show the decision for each dial,
including the resource comparison or the unavailable evidence. A healthy feature can hold one dial
while another changes. Those explanations are separate from a failure that blocks the service.

These budgets govern dial selection. They do not reserve resources for purchases or prevent other
features and your own actions from spending them.
