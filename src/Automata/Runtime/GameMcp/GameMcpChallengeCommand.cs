#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpChallengeCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Challenge,
        static (command, scope) => ExecuteChallenge(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) =>
                GameMcpChallengeProjection.ProjectChallengePostState(state, command)),
        nativeType: "ChallengeSO",
        prepare: Prepare);

    private static GameMcpCommandResult ExecuteChallenge(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var challenges = gameActions.Find<ChallengeGameAction>();
        if (challenges is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared challenge GameAction was not composed", lifecycle,
                configurationGeneration);
        if (command.TargetId != Guid.Empty)
            GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var kind = command.Mode switch
        {
            "select" => ChallengeActionKind.Select,
            "queue" => ChallengeActionKind.Queue,
            "abandon" => ChallengeActionKind.Abandon,
            "reroll" => ChallengeActionKind.Reroll,
            _ => throw new ArgumentException("unsupported challenge mode " + command.Mode),
        };
        var action = new ChallengeAction(kind, command.TargetId, command.SecondaryId,
            command.ExpectedLifecycleGeneration);
        var submission = challenges.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpChallengeProjection.Project(in submission));
    }

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        if (request.Mode == "select" && context.World is not null)
            draft.SecondaryId = GameMcpChallengeProjection.ChallengeSelectionToReplace(
                context.World.Snapshot, draft.TargetId);
    }
}
#endif
