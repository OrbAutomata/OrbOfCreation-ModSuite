#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

internal static class GameMcpDiscoveryTreeOfferCommand
{
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.DiscoveryTreeOffer,
        static (command, scope) => ExecuteDiscoveryTreeOffer(
            scope.GameActions, command, scope.Lifecycle, scope.ConfigurationGeneration),
        GameMcpPostStateRule.Settled(
            ProjectDelta,
            static (state, command) => GameMcpWorldQuery.HasDiscoveryPostState(
                state,
                command.TargetId,
                command.Mode,
                command.SecondaryId)));

    private static GameMcpCommandResult ExecuteDiscoveryTreeOffer(
        LifecycleGameActions gameActions,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "DiscoveryTreeSO");
        var discoveryTreeOffers = gameActions.Find<DiscoveryTreeOfferGameAction>();
        if (discoveryTreeOffers is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared Discovery Tree offer GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var kind = command.Mode switch
        {
            "initiate" => DiscoveryTreeOfferActionKind.Initiate,
            "select" => DiscoveryTreeOfferActionKind.Select,
            "confirm" => DiscoveryTreeOfferActionKind.Confirm,
            "reroll" => DiscoveryTreeOfferActionKind.Reroll,
            _ => throw new ArgumentException("unsupported Discovery Tree offer mode " + command.Mode),
        };
        var action = new DiscoveryTreeOfferAction(
            kind,
            command.TargetId,
            command.SecondaryId,
            command.ExpectedLifecycleGeneration);
        var submission = discoveryTreeOffers.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
            GameMcpDiscoveryTreeOfferProjection.Project(kind, in submission));
    }

    private static GameMcpValue ProjectDelta(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed) => command.Mode switch
        {
            "select" => GameMcpWorldQuery.WithoutOffers(
                GameMcpWorldQuery.ProjectPostState(state, "discovery-trees", command.TargetId)),
            // The mode reaching this switch is the one the request's `offer_` prefix was stripped
            // from, the same word the settlement rule waits on. Matching the tool's spelling here
            // meant every live confirm and reroll fell through to the plain tree row while the
            // fixtures that spelled it the other way went on passing.
            "confirm" => GameMcpWorldQuery.ProjectDiscoveryOfferConfirmDelta(state, command),
            "reroll" => GameMcpWorldQuery.ProjectDiscoveryRerollDelta(state, command),
            _ => GameMcpWorldQuery.ProjectPostState(state, "discovery-trees", command.TargetId),
        };
}
#endif
