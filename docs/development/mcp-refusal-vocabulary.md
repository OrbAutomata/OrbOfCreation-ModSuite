# Game MCP refusal vocabulary

[Game MCP index](mcp-tools.md)

A failure names what stopped the request and what the caller can do. [The decision vocabulary](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpDecisionReason.cs) owns code classification and sentences.

## Refusal vocabulary

A refusal has a class and one actionable sentence. The sentence names the target, the unmet fact and
what can change it. A read that could not serve an entity also carries its unavailable envelope;
a fact inside an otherwise successful row uses verdict words rather than pretending a press occurred.

The following examples are **partial**. The source vocabulary owns the complete classification;
these rows illustrate the caller's next step and are checked against that shipping mapping.

| Class | Example internal codes |
| --- | --- |
| `ERR_INPUT` | `slot_out_of_range` |
| `ERR_NOT_FOUND` | `unknown_uuid` |
| `ERR_STATE` | `already_developing` |
| `ERR_LIMIT` | `glyph_usages_exceeded` |
| `ERR_UNAFFORDABLE` | `unaffordable` |
| `ERR_LOCKED` | `research_leeway_exhausted` |
| `ERR_UNAVAILABLE` | `contract_unavailable` |
| `ERR_REFUSED` | `native_rejected` |

Fix an input error by changing the argument. A missing entity calls for another lookup. State and
limit refusals call for a state change or a smaller request. Affordability identifies the resources
that fall short; a lock identifies progression or authored prerequisites. Unavailable means the fact
could not be read or served. A native refusal is the game's rejection when no more specific published
fact explains it.

A UUID supplied to the wrong tool is diagnosed with the tool that can act on that entity. A tree's
roll discovers its offers; an arbitrary entity UUID does not grant a discover action.

A blocker sentence must agree with its verdict. Research may be admitted through leeway or through
its cap predicates, so exhausted leeway alone is not evidence that the complete develop gate failed.
A mutation refused by a gate the detail read already explains uses that same gate's reason.

`maximumAmount` describes what **this call** admits, not a remaining cross-call budget. Its sentence
says whether affordability, capacity or a structural action limit supplied the bound. A later call
revalidates and can admit a different amount.

## Fault causes and retries

[GameActionReason](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/GameActionReason.cs) keeps the actionable sentence and cause reference separate. [GameActionFaultLog](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/GameActionFaultLog.cs) owns the full exception. The wire does not expose raw exception messages as reasons or duplicate the fault-log schema here.

Each failed attempt records its own cause. A repeated warning or health condition can be coalesced
without suppressing the next attempt's fault record. A later player request revalidates live rather
than inheriting an MCP-owned lifecycle quarantine. Automation retry policy belongs to the shared
runtime and its feature, as described by the [boundary doctrine](../runtime-architecture/game-boundary-doctrine.md).
