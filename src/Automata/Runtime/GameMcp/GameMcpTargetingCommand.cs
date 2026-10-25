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
            static (state, _, committed) => GameMcpTargetingProjection.ProjectTargetingPostState(
                state,
                GameMcpTargetingProjection.SubmittedTarget(committed.Details))),
        prepare: Prepare);

    private static GameMcpCommandResult ExecuteTargeting(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, NativeTypeFor(command.Mode));
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

    private static string NativeTypeFor(string mode) =>
        mode == "submit" ? "StructureSO" : "TargetingManager+TargetLink";

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        draft.NativeType = NativeTypeFor(request.Mode);
    }
}
#endif
