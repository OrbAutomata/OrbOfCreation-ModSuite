#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpPurchaseCommand : IGameMcpServiceCommand
{
    private readonly AutoBuyFeatureRuntime _runtime;

    internal GameMcpPurchaseCommand(AutoBuyFeatureRuntime runtime) =>
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public GameMcpCommandKind Kind => GameMcpCommandKind.Purchase;
    public ServiceId Service => AutoBuyServicePolicies.ServiceId;

    public GameMcpCommandResult Execute(
        GameMcpCommand command, GameMcpCommandScope scope, in ServiceActionContext context)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, NativeTypeFor(command.Mode));
        var action = new AutoBuyCycleAction(
            command.Mode == "structure" ? AutoBuyCandidateKind.Structure : AutoBuyCandidateKind.Upgrade,
            command.TargetId, scope.World.Snapshot.CollectedAtEpoch, command.Amount);
        var config = scope.Configuration.Snapshot;
        var result = _runtime.TryExecuteGameMcp(in action, in config, in context);
        var submission = _runtime.LastGameMcpSubmission;
        var refusal = GameMcpPurchaseRefusal.ProjectPurchaseRefusal(
            command, scope.World.Snapshot, in result, scope.Lifecycle,
            scope.ConfigurationGeneration, submission.Diagnosis);
        if (refusal is not null) return refusal;
        string? reason = GameMcpCastProjection.ExactGameMcpReason(command, scope.World.Snapshot, in result) ??
            result.Reason;
        GameMcpValue? details = null;
        GameMcpPurchaseRefusal.ProjectPurchaseDetails(
            command, scope.World.Snapshot, in submission, ref reason, ref details);
        return GameMcpCommandResult.FromAction(
            result.WithReason(reason), scope.Lifecycle, scope.ConfigurationGeneration, details);
    }

    /// <remarks>
    /// A purchase answers from the queued-level delta its own native verifier observed, so there is
    /// nothing left for a settled world to add: it queues levels the game drains over the following
    /// seconds, and every world after the press describes a queue already draining rather than the
    /// press. Waiting for one could only turn a purchase that verifiably committed into a
    /// <c>post_state_timeout</c>, which is the same lie in a slower costume.
    /// </remarks>
    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Purchase,
        GameMcpCommandDispatcher.FeatureExecutor,
        GameMcpPostStateRule.FromPress,
        prepare: Prepare);

    internal static string NativeTypeFor(string mode) =>
        mode == "structure" ? "StructureSO" : "UpgradeSO";

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        if (context.World is not null)
        {
            var structure = WorldLookup.TryFind(
                context.World.Snapshot.Structures, request.Uuid, out _);
            var upgrade = WorldLookup.TryFind(
                context.World.Snapshot.Upgrades, request.Uuid, out _);
            if (structure != upgrade)
            {
                draft.Mode = structure ? "structure" : "upgrade";
                draft.NativeType = NativeTypeFor(draft.Mode);
            }
        }
    }
}
#endif
