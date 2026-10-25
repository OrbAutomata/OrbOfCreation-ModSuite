# Game MCP suite reference

[Game MCP index](mcp-tools.md)

The [suite descriptors](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpSuiteTools.cs)
own diagnostic/configuration arguments. Examples below are partial. Read configuration metadata and
tool schemas instead of using a copied setting, feature or field inventory.

## Health

`suite_health` takes no arguments. It reports build/scene/lifecycle, world publication and STOP state,
then adds unavailable runtime/contracts, failed action bindings, committed-after-throw cause references,
and feature problems where relevant. Health groups registered features/services by their condition;
it does not poll a second feature roster. The
[health projection](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpHealthProjection.cs)
owns the exact lines and typed evidence sources.

A lifecycle transition count is an invalidation token, not a count of saves, loads or player resets.
One player operation can produce several accepted observations. World publication counts describe
collection, while the save's world-reset count describes progression. Those values are not
interchangeable. A lifecycle boundary invalidates the departed run's world; a missing first
publication is not an empty save. The
[lifecycle monitor](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/GameLifecycleMonitor.cs)
owns observations.

## Configuration and breakers

Read committed values with `suite_configuration`. `mode="describe"` adds type, domain and purpose;
`section` narrows either mode using the exact section printed by the read. The domain gives accepted
values or a numeric range, so a caller does not parse constraints out of prose.

```sh
tools/game-mcp-client.py call suite_configuration --arguments '{"section":"AutoBuy"}'
tools/game-mcp-client.py call suite_configuration --arguments '{"mode":"describe","section":"AutoBuy"}'
tools/game-mcp-client.py call suite_breakers --arguments '{"mode":"list"}'
```

`suite_config_set` takes the section/key/value obtained from that metadata and commits through the
same store as the in-game controls. Parse and domain checks happen before publication. A successful
write reports the changed value; a refusal reports its constraint and actionable reason. Exact
allowlisting and type conversion belong to the
[configuration projection](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpConfigurationProjection.cs).

Feature mode is written through `suite_breakers`, with `mode="set"`, a registered `feature` and
boolean `on`. A generic settings write refuses a breaker and points to this operation; the values
remain readable through configuration. Setting an already-held mode refuses as unchanged. Enabling
a feature returns a policy sentence derived from committed configuration, including when that
configuration leaves it no eligible work. Configured ON and runtime readiness remain distinct.

Feature-owned declarations supply registered settings, published-value readers and breaker metadata
through [the settings seam](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/Configuration/AutomationFeatureSettings.cs).
For example, describe `AutoLevels` or `QualityOfLife` with `suite_configuration` before changing a
budget or convenience option. Their modes use the registered breakers; tuning uses `suite_config_set`.
Policy explanation appears separately in health details, so a holding level dial does not imply that
the entire service is unavailable. [Operational details](../runtime-architecture/observability.md#feature-health-and-policy-details)
owns that distinction.

STOP uses `suite_emergency_stop` with `mode="engage"` or `"resume"`. It is immediate and separate
from staged tuning or feature mode. Resume still requires the host's lifecycle and native contracts.
[Configuration](../user-guide/configuration.md) owns the player's control semantics.

An Auto Buy manual-slot reserve can be refused against the published queue capacity when a loaded
world establishes that capacity. Before a queue is published, the server does not invent a native
ceiling. [Queue publication](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldActionQueue.cs)
and the configuration boundary own that check.

## Checking the suite's math against the game

`suite_check_game_math` takes no arguments. It runs the same differential check exposed by Runtime's
**Check game math**, comparing owned answers with the native oracle on the loaded game. It does not
purchase, navigate or unlock content. A running, stable lifecycle is required; lifecycle replacement
invalidates the attempted check rather than producing a report for mixed runs.

```sh
tools/game-mcp-client.py call suite_check_game_math
```

The first line states the verdict. Agreement can combine named checks, but no executed check or
compared count disappears. Disagreement, incomplete coverage and inconclusive evidence remain named
findings. Repeated identical findings are grouped without losing affected identities. Empty-on-purpose
checks explain why they sampled nothing. Numeric comparisons use the same display convention as
other responses, distinguishing a hidden precision difference from visually different values.

The final window line contains the capture/measurement facts that can vary between checks. Those
are diagnostic facts, not a replacement world publication or evidence that an unchecked capability
works. The [verifier](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/tree/main/src/Automata/Runtime/Verification)
owns the check inventory and output. [World collection](../runtime-architecture/world-collection.md#what-the-differential-passes-prove-and-what-they-do-not)
states the proof and its limits; [runtime validation](../testing/runtime-validation.md) owns live
validation procedure. No test or entity census is maintained in this reference.

## The strategist's bulletin

`strategist` reads the latest bulletin. Without `section` it summarizes goals, position and demand;
a named section reads that part, while `section="facts"` exposes the exact native-free inputs used
to derive it. The
[world tool descriptor](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpWorldTools.cs)
owns accepted section names.

```sh
tools/game-mcp-client.py call strategist
tools/game-mcp-client.py call strategist --arguments '{"section":"facts"}'
```

A goal cost is time on measured sustained income, not an instantaneous resource-rate quote. While
income is being measured, the bulletin says so; unreachable goals name the missing condition. Goals
and dependencies retain their identities, so callers can follow them into world reads. The
[Strategist](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/tree/main/src/Strategist) owns the
judgment and its factual record format. [Strategy rules](../runtime-architecture/goals-and-invariants.md#strategy-rules)
define its authority; a bulletin is not permission to bypass a GameAction's live admission.

## Trace health and probes

`trace_health` reports writer health separately from the collection spans attached to the latest
world. A missing writer does not erase the world's collection measurements; a missing world makes
those spans unavailable. Span names connect to the category a world reader can actually query,
including when a collector feeds several categories or several collectors feed one category.

Structural work reused from an earlier capture is named as reused, rather than measured as free
work in this pass. Unbound categories remain explicit. These are the pass's existing measurements;
means, medians and session distributions belong to offline analysis, not a second runtime
measurement loop. [Observability](../runtime-architecture/observability.md) owns recording and
retention; [fixed probes](mcp-screen-reference.md) inspect request-specific native facts.
