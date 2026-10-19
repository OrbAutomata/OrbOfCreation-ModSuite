#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpTargetingCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Targeting,
        static (command, scope) => ExecuteTargeting(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, _, committed) => GameMcpWorldQuery.ProjectTargetingPostState(
                state,
                GameMcpTargetingProjection.SubmittedTarget(committed.Details))));

    private static GameMcpCommandResult ExecuteTargeting(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(
            command,
            command.Mode == "submit" ? "StructureSO" : "TargetingManager+TargetLink");
        var targeting = gameActions.Find<TargetingGameAction>();
        if (targeting is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable", "the shared targeting GameAction was not composed",
                lifecycle, configurationGeneration);
        var kind = command.Mode switch
        {
            "submit" => TargetingActionKind.Submit,
            "randomize" => TargetingActionKind.Randomize,
            "cancel" => TargetingActionKind.Cancel,
            _ => throw new ArgumentException("unsupported targeting mode " + command.Mode),
        };
        var action = new TargetingAction(kind, command.TargetId, command.ExpectedLifecycleGeneration);
        var submission = targeting.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(
            in result, lifecycle, configurationGeneration, GameMcpTargetingProjection.Project(in submission));
    }
}
#endif
