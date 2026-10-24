# Automata services

[Plugin](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Plugin.cs) owns explicit typed composition and activation.
[Architecture](../../../docs/runtime-architecture/architecture.md) owns the service roster;
[the runtime contract](../../../docs/runtime-architecture/service-cycle-runtime.md) owns scheduling,
lifecycle and deterministic action order. Features use their own sibling module's typed seams.
