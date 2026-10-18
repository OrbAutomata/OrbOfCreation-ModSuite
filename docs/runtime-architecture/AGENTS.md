# Runtime architecture

How the suite is designed: boundaries, services, world collection and observability. [The game-boundary doctrine](game-boundary-doctrine.md) is the rule set; every other page describes the system as it is.

- Describe what is, not how it got there. A decision record keeps its entry only while source code cites its number; when the citation goes, the entry goes.
- The service roster in [architecture.md](architecture.md) is the one deliberate copy of `src/` here: the change that ships or removes a service updates it.
- Each rule is specified in exactly one file; other files point at it.
