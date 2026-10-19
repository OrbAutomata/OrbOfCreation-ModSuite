#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpResearchCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Research,
        static (command, scope) => ExecuteResearch(
            scope.GameActions,
            command,
            scope.World.Snapshot,
            scope.Lifecycle,
            scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            static (state, command, _) => GameMcpWorldQuery.ProjectResearchDelta(state, command),
            RequestedOutcome,
            TimedOut),
        nativeType: "ResearchSO");

    private static GameMcpCommandResult ExecuteResearch(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        GameWorldState world,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var researchAction = gameActions.Find<ResearchGameAction>();
        if (researchAction is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared research GameAction was not composed", lifecycle,
                configurationGeneration);
        var kind = command.Mode switch
        {
            "develop" => ResearchActionKind.Develop,
            "pause" => ResearchActionKind.Pause,
            "resume" => ResearchActionKind.Resume,
            "cancel" => ResearchActionKind.Cancel,
            "bonus" => ResearchActionKind.Bonus,
            _ => throw new ArgumentException("unsupported research mode " + command.Mode),
        };
        var action = new ResearchAction(kind, command.TargetId, command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = researchAction.Submit(in action);
        var result = submission.Outcome.ToResult();

        // The read block and this refusal answer the same gate, so they say the same sentence.
        // The GameAction sees an affordability verdict and no cost rows; the published world holds
        // the rows, so the sentence naming them is composed here, where both surfaces reach it.
        var reason =
            submission.Outcome.Code == ResearchActionResultCodes.Unaffordable &&
            WorldLookup.TryFind(world.Research, command.TargetId, out var research)
                ? GameMcpWorldQuery.ShortfallReason(world, research.Decision.DevelopmentCosts)
                : submission.Outcome.Code == ResearchActionResultCodes.Locked
                    ? GameMcpEntityExplainer.LockedResearchReason(world, command.TargetId)
                    : submission.Outcome.Reason;
        return GameMcpCommandResult.FromAction(result.WithReason(reason), lifecycle,
            configurationGeneration,
            GameMcpResearchProjection.Project(in submission));
    }

    private static bool RequestedOutcome(GameMcpFrameContext state, GameMcpCommand command) =>
        command.Mode != "develop" || ResearchAmountReached(state, command);

    private static bool ResearchAmountReached(GameMcpFrameContext state, GameMcpCommand command)
    {
        var beforeWorld = command.FrameContext?.World?.Snapshot;
        return beforeWorld is not null &&
            WorldLookup.TryFind(beforeWorld.Research, command.TargetId, out var before) &&
            WorldLookup.TryFind(state.World!.Snapshot.Research, command.TargetId, out var after) &&
            (before.Decision.QueueMode
                ? PendingResearchLevels(after) == checked(PendingResearchLevels(before) + command.Amount)
                : !before.IsDeveloping && after.IsDeveloping);
    }

    private static int PendingResearchLevels(in WorldResearch research) =>
        checked(research.Level + research.QueuedLevels + (research.IsDeveloping ? 1 : 0));

    private static GameMcpValue TimedOut(GameMcpCommand command, GameMcpFrameContext? latest)
    {
        if (string.Equals(command.Mode, "develop", System.StringComparison.Ordinal) &&
            latest?.World is not null)
        {
            var beforeWorld = command.FrameContext?.World?.Snapshot;
            var before = default(WorldResearch);
            var hasBefore = beforeWorld is not null && WorldLookup.TryFind(
                beforeWorld.Research, command.TargetId, out before);
            var hasAfter = WorldLookup.TryFind(
                latest.World.Snapshot.Research, command.TargetId, out var after);
            var queueMode = hasBefore
                ? before.Decision.QueueMode
                : hasAfter && after.Decision.QueueMode;
            if (queueMode)
            {
                var observed = hasAfter ? PendingResearchLevels(after).ToString() : "not published";
                if (!hasBefore)
                {
                    return GameMcpWorldQuery.PostStateUnavailable(
                        "requested_state_not_reached",
                        "the research queue before state was not published; the settled queue has " +
                        observed + " pending levels, so the requested increase cannot be verified");
                }
                var expected = checked(PendingResearchLevels(before) + command.Amount);
                return GameMcpWorldQuery.PostStateUnavailable(
                    "requested_state_not_reached",
                    "the settled research queue has " + observed +
                    " pending levels, not the requested " + expected);
            }
            var beforeState = hasBefore
                ? before.IsDeveloping ? "developing" : "idle"
                : "not published";
            var afterState = hasAfter
                ? after.IsDeveloping ? "developing" : "idle"
                : "not published";
            return GameMcpWorldQuery.PostStateUnavailable(
                "requested_state_not_reached",
                "The develop press did not start this research: it was " + beforeState +
                " before and it is " + afterState + " now. Check its price and its caps on the " +
                "Research screen.");
        }
        return GameMcpPostStateSettlement.PostStateTimeout();
    }
}
#endif
