#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpStructureLifecycleCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.StructureLifecycle,
        static (command, scope) => ExecuteStructureLifecycle(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) =>
                GameMcpWorldQuery.ProjectStructureLifecycleDelta(state, command),
            RequestedOutcome,
            TimedOut),
        nativeType: "StructureSO");

    private static GameMcpCommandResult ExecuteStructureLifecycle(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var structureLifecycle = gameActions.Find<StructureLifecycleGameAction>();
        if (structureLifecycle is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared structure GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var kind = command.Mode switch
        {
            "enable" => StructureLifecycleActionKind.Enable,
            "disable" => StructureLifecycleActionKind.Disable,
            _ => throw new ArgumentException("unsupported structure mode " + command.Mode),
        };
        var action = new StructureLifecycleAction(
            kind, command.TargetId, command.ExpectedLifecycleGeneration);
        var submission = structureLifecycle.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpStructureLifecycleProjection.Project(in submission));
    }

    private static bool RequestedOutcome(GameMcpFrameContext state, GameMcpCommand command) =>
        WorldLookup.TryFind(
                state.World!.Snapshot.Structures,
                command.TargetId,
                out var structure) &&
            structure.Reading.Disabled ==
                string.Equals(command.Mode, "disable", System.StringComparison.Ordinal);

    private static GameMcpValue TimedOut(GameMcpCommand command, GameMcpFrameContext? latest) =>
        GameMcpWorldQuery.PostStateUnavailable(
            "requested_state_not_reached",
            "the settled attribute did not show the requested enabled state");
}
#endif
