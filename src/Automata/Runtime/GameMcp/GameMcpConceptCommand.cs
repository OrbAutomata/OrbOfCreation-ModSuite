#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpConceptCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Concept,
        GameMcpCommandDispatcher.FeatureExecutor,
        GameMcpPostStateRule.Settled(
            static (state, command, _) => GameMcpWorldQuery.ProjectConceptDelta(state, command),
            ConceptAmountReached,
            TimedOut));

    /// <summary>
    /// The game writes an Alchemy queue decrease straight through but holds an increase for a
    /// quarter second before submitting it, so the queued quantity moves a settlement ahead of the
    /// active one. Waiting on the active quantity is what keeps the response in the same
    /// coordinate the concept projection publishes.
    /// </summary>
    private static bool ConceptAmountReached(GameMcpFrameContext state, GameMcpCommand command)
    {
        var beforeWorld = command.FrameContext?.World?.Snapshot;
        if (beforeWorld is null) return false;
        var before = WorldAlchemyInstanceLookup.TryFind(
            beforeWorld.AlchemyInstances, command.TargetId, out var previous)
            ? previous.Quantity
            : 0;
        var after = WorldAlchemyInstanceLookup.TryFind(
            state.World!.Snapshot.AlchemyInstances, command.TargetId, out var current)
            ? current.Quantity
            : 0;
        return command.Mode switch
        {
            "add" => after == checked(before + command.Amount),
            "remove" => after == checked(before - command.Amount),
            "rotate_out" => after == 0,
            _ => false,
        };
    }

    private static GameMcpValue TimedOut(GameMcpCommand command, GameMcpFrameContext? latest)
    {
        if (latest?.World is not null)
        {
            var observed = WorldAlchemyInstanceLookup.TryFind(
                latest.World.Snapshot.AlchemyInstances, command.TargetId, out var concept)
                ? concept.Quantity.ToString()
                : "not published";
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "the assigned concepts did not reach the requested amount; " + observed +
                " are assigned now.");
        }
        return GameMcpPostStateSettlement.PostStateTimeout();
    }
}
#endif
