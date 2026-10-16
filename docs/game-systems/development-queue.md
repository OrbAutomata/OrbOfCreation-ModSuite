# The development queue

Buying an attribute or an upgrade does not give it to you. It **queues** it, and it develops over
time. The game names this queue **Active Attributes**, and it is the only queue this page is about;
plot actions run in a separate one of their own (see [agromancy.md](agromancy.md)), so a slot count
read off that one says nothing about what a purchase can do.

- **Attributes develop slowly; upgrades develop more slowly still**, and they share the **same**
  queue, so a slow upgrade occupies a slot an attribute could have used.
- Default capacity is **8** slots. Purchases raise it — e.g., *Improved Development* takes it from 8
  to 10 and adds +10 % development speed, and *Greater Mental Acuity* adds +2. Late-game capacity has
  been observed as high as **304**.
- Capacity and development *speed* are separate, separately upgradeable knobs.
- Early on the queue, not the resource, is what binds: even when a resource is abundant, the order in
  which you spend it matters.
- **A full queue is not a refusal you can argue with.** With no free slot the purchase simply takes
  nothing more, and only time clears it — asking for fewer levels does not fit them in.

## Occupancy counts stack units

Queue occupancy counts stack units, not distinct entries, because a multi-level purchase occupies
several units. E.g., one observed frame held 35 distinct queued objects and 131 queued stack units.

## Bulk Development

**Bulk Development** is a live game value (e.g., observed at 2 on one save and 16 on another).
Attributes request that many levels per purchase; **upgrades are always one level per action**.

It is raised mostly by research, and research moves two queue knobs that are easy to conflate: some
nodes add **parallel development slots** (more different purchases developing at once), while others
raise **bulk** (queued levels of the *same* attribute processed together in one slot's pass).
