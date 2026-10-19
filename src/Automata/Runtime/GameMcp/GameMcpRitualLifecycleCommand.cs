#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpRitualLifecycleCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.RitualLifecycle,
        static (command, scope) => ExecuteRitualLifecycle(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) =>
                GameMcpWorldQuery.ProjectRitualLifecycleDelta(state, command)),
        nativeType: "RitualSO");

    private static GameMcpCommandResult ExecuteRitualLifecycle(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var ritualLifecycle = gameActions.Find<RitualLifecycleGameAction>();
        if (ritualLifecycle is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared Ritual GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var kind = command.Mode switch
        {
            "select" => RitualLifecycleActionKind.Select,
            "deselect" => RitualLifecycleActionKind.Deselect,
            "set_level" => RitualLifecycleActionKind.SetLevel,
            "activate" => RitualLifecycleActionKind.Activate,
            "cancel_duration" => RitualLifecycleActionKind.CancelDuration,
            "end" => RitualLifecycleActionKind.EndBattle,
            _ => throw new ArgumentException("unsupported Ritual mode " + command.Mode),
        };
        var action = new RitualLifecycleAction(kind, command.TargetId,
            kind == RitualLifecycleActionKind.SetLevel ? command.Amount - 1 : 0,
            command.ExpectedLifecycleGeneration);
        var submission = ritualLifecycle.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpRitualLifecycleProjection.Project(in submission));
    }
}
#endif
