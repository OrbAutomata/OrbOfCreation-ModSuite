#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpCastCommand : IGameMcpServiceCommand
{
    private readonly AutoCastFeatureRuntime _runtime;

    internal GameMcpCastCommand(AutoCastFeatureRuntime runtime) =>
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public GameMcpCommandKind Kind => GameMcpCommandKind.Cast;
    public ServiceId Service => AutoCastServicePolicies.ServiceId;

    public GameMcpCommandResult Execute(
        GameMcpCommand command, GameMcpCommandScope scope, in ServiceActionContext context)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, Descriptor.NativeType);
        var kind = command.Mode switch
        {
            "release" => AutoCastActionKind.ReleaseCharge,
            "toggle_off" => AutoCastActionKind.ToggleOff,
            _ => AutoCastActionKind.Fire,
        };
        // Live slot identity and charged-cast capability are revalidated by the native boundary.
        var action = new AutoCastCycleAction(
            kind, checked(command.Amount - 1), command.TargetId, scope.World.Snapshot.CollectedAtEpoch,
            chargeHold: string.Equals(command.PayloadValue, "charge", StringComparison.Ordinal));
        var config = scope.Configuration.Snapshot;
        var result = _runtime.TryExecuteGameMcp(in action, in config, in context);
        var submission = _runtime.LastGameMcpSubmission;
        var reason = GameMcpCastProjection.CastRefusalReason(
            command, scope.World.Snapshot, in result,
            submission.Outcome.Verified, submission.Outcome.Reason);
        GameMcpValue? details = null;
        if (submission.Occupant != Guid.Empty)
            details = new GameMcpObjectBuilder
            {
                ["occupant"] = submission.Occupant.ToString("D"),
            }.Freeze();
        return GameMcpCommandResult.FromAction(
            result.WithReason(reason), scope.Lifecycle, scope.ConfigurationGeneration, details);
    }

    internal static GameMcpCommandDescriptor Descriptor { get; } = new(
        GameMcpCommandKind.Cast,
        GameMcpCommandDispatcher.FeatureExecutor,
        GameMcpPostStateRule.Settled(
            static (state, command, _) => GameMcpCastProjection.ProjectCastDelta(state, command),
            RequestedOutcome,
            TimedOut),
        nativeType: "SpellRecipeSO",
        prepare: Prepare);

    private static bool RequestedOutcome(GameMcpFrameContext state, GameMcpCommand command)
    {
        if (string.Equals(command.Mode, "toggle_off", System.StringComparison.Ordinal))
        {
            return WorldSpellSlotLookup.TryFind(
                    state.World!.Snapshot.SpellSlots,
                    command.Amount - 1,
                    out var slot) &&
                slot.Occupied &&
                slot.SpellRecipeId == command.TargetId &&
                slot.Toggled &&
                !slot.Casting;
        }
        if (string.Equals(command.Mode, "fire", System.StringComparison.Ordinal))
        {
            return FireStarted(state, command);
        }
        return true;
    }

    /// <summary>
    /// The cast this press started, seen in the settled world.
    /// </summary>
    /// <remarks>
    /// A fire had no sentinel at all, so the first world one generation newer than the press was
    /// accepted — and a live round's committed fire answered <c>active: no  castReady: yes</c> with
    /// no recharge block, describing a world that had not yet seen the cast.
    /// <para>
    /// The game spends the charge at the END of the cast, not at the press:
    /// <c>Spell.Cast()</c> loads a cooldown and sets the prep state, and only
    /// <c>Spell.EndCasting</c>/<c>Spell.ExecuteSpell</c> call <c>Spell.ConsumeCharge()</c>. So a
    /// spell slower than the world cadence is still readying when the next world lands and its
    /// charge has not moved, while a spell faster than it has already finished and dropped one.
    /// Those are the two faces of one fact — this press started a cast — and either proves it.
    /// Waiting for both would time out half of all casts; waiting for neither is what shipped.
    /// </para>
    /// <para>
    /// An instant spell with no recharge to wait out shows neither: it is over and its charge is
    /// back before the next world. <c>Spell.ExecuteSpell()</c> counts the manual cast in
    /// <c>numCasts</c>, so a risen count is the trace that cast leaves.
    /// </para>
    /// </remarks>
    private static bool FireStarted(GameMcpFrameContext state, GameMcpCommand command)
    {
        if (!WorldSpellSlotLookup.TryFind(
                state.World!.Snapshot.SpellSlots, command.Amount - 1, out var after) ||
            !after.Occupied ||
            after.SpellRecipeId != command.TargetId)
        {
            return false;
        }
        if (after.ReadyingCast || after.Casting) return true;
        var before = command.FrameContext?.World?.Snapshot;
        return before is not null &&
            WorldSpellSlotLookup.TryFind(before.SpellSlots, command.Amount - 1, out var previous) &&
            previous.Occupied &&
            (after.CurrentCharges < previous.CurrentCharges ||
             after.CastCount > previous.CastCount);
    }

    private static GameMcpValue TimedOut(GameMcpCommand command, GameMcpFrameContext? latest)
    {
        if (string.Equals(command.Mode, "toggle_off", System.StringComparison.Ordinal))
        {
            return GameMcpRowKit.PostStateUnavailable(
                "requested_state_not_reached",
                "the settled spell slot did not show the requested toggle as off");
        }
        if (string.Equals(command.Mode, "fire", System.StringComparison.Ordinal))
        {
            return GameMcpCastProjection.FireWithoutSettledWorld(command);
        }
        return GameMcpPostStateSettlement.PostStateTimeout();
    }

    private static void Prepare(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        GameMcpCommandDraft draft)
    {
        draft.Amount = request.SlotIndex;
        draft.PayloadValue = request.SerializedValue;
    }
}
#endif
