#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpGenericLevelCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.GenericLevel,
        static (command, scope) => ExecuteGenericLevel(
            scope.GameActions,
            command,
            scope.World.Snapshot,
            scope.Lifecycle,
            scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) =>
                GameMcpWorldQuery.ProjectGenericLevelDelta(state, command)),
        prepare: Prepare);

    private static GameMcpCommandResult ExecuteGenericLevel(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        GameWorldState world,
        long lifecycle,
        ulong configurationGeneration)
    {
        var genericLevel = gameActions.Find<GenericLevelGameAction>();
        if (genericLevel is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared level GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, command.DerivedNativeType);
        var kind = command.Mode switch
        {
            "purchase" => GenericLevelActionKind.Purchase,
            "bonus" => GenericLevelActionKind.Bonus,
            _ => throw new ArgumentException("unsupported level mode " + command.Mode),
        };
        var action = new GenericLevelAction(kind, command.TargetId,
            command.DerivedNativeType, command.Amount, command.ExpectedLifecycleGeneration);
        var submission = genericLevel.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpGenericLevelProjection.Project(in submission, world));
    }

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        if (context.World is null)
            draft.Failure = GameMcpCommandResult.Rejected(
                "world_not_published", context.RuntimeNotAvailableReason);
        else if (!GameMcpEntityCapabilityMap.TryResolveGenericLevelType(
                     context.World.Snapshot, request.Uuid,
                     out draft.NativeType, out var levelReason))
            draft.Failure = GameMcpCommandResult.Rejected(
                "level_target_unavailable", levelReason);
    }
}
#endif
