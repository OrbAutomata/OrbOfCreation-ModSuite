#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellLevelCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.SpellLevel,
        GameMcpCommandDispatcher.FeatureExecutor,
        GameMcpPostStateRule.Settled(
            static (state, command, _) => string.Equals(
                command.Mode,
                "all",
                StringComparison.Ordinal)
                ? GameMcpWorldQuery.ProjectSpellLevelAllPostState(state, command)
                : GameMcpWorldQuery.ProjectSpellLevelDelta(state, command)));
}
#endif
