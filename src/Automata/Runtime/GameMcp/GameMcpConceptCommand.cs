#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpConceptCommand : IGameMcpServiceCommand
{
    private readonly AutoConceptFeatureRuntime _runtime;

    internal GameMcpConceptCommand(AutoConceptFeatureRuntime runtime) =>
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public GameMcpCommandKind Kind => GameMcpCommandKind.Concept;
    public ServiceId Service => AutoConceptServicePolicies.ServiceId;

    public GameMcpCommandResult Execute(
        GameMcpCommand command, GameMcpCommandScope scope, in ServiceActionContext context)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var kind = command.Mode switch
        {
            "add" => AutoConceptActionKind.Add,
            "remove" => AutoConceptActionKind.RemoveOwned,
            "rotate_out" => AutoConceptActionKind.RotateOut,
            _ => throw new ArgumentException("unsupported concept mode " + command.Mode),
        };
        var world = scope.World.Snapshot;
        if (!AutoConceptPlanBeliefProjection.TryCreate(world, command.TargetId, out var belief, out var reason))
            return GameMcpCommandResult.Rejected(
                "concept_world_facts_unavailable", reason, scope.Lifecycle, scope.ConfigurationGeneration);
        if (!AutoConceptPlanBeliefProjection.TryResolveGameMcpTarget(
                kind, command.Amount, in belief, out var targetOrDelta, out var code, out reason))
            return GameMcpCommandResult.Rejected(code, reason, scope.Lifecycle, scope.ConfigurationGeneration);
        var action = new AutoConceptCycleAction(
            kind, command.TargetId, targetOrDelta, command.SecondaryId, world.CollectedAtEpoch, in belief);
        var config = scope.Configuration.Snapshot;
        var result = _runtime.TryExecuteGameMcp(in action, in config, in context);
        var submission = _runtime.LastGameMcpSubmission;
        var exactReason = GameMcpCastProjection.ExactGameMcpReason(command, world, in result);
        if (!submission.Outcome.Verified && !string.IsNullOrEmpty(submission.Outcome.Reason))
            exactReason = submission.Outcome.Reason;
        GameMcpValue? details = null;
        if (submission.MaximumAmount >= 0 &&
            submission.Outcome.Code == AutoConceptActionResultCodes.AmountUnavailable)
            details = new GameMcpObjectBuilder { ["maximumAmount"] = submission.MaximumAmount }.Freeze();
        return GameMcpCommandResult.FromAction(
            result.WithReason(exactReason), scope.Lifecycle, scope.ConfigurationGeneration, details);
    }

    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Concept,
        GameMcpCommandDispatcher.FeatureExecutor,
        GameMcpPostStateRule.Settled(
            static (state, command, _) => GameMcpAlchemyLoadoutProjection.ProjectConceptDelta(state, command),
            ConceptAmountReached,
            TimedOut),
        nativeType: "AlchemyRecipeSO");

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
            return GameMcpRowKit.PostStateUnavailable(
                "requested_state_not_reached",
                "the assigned concepts did not reach the requested amount; " + observed +
                " are assigned now.");
        }
        return GameMcpPostStateSettlement.PostStateTimeout();
    }
}
#endif
