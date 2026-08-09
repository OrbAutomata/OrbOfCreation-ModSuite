using System;
using System.Reflection;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The native execution boundary for Auto Cast. The pure worker plans at most one cast or one charge
/// release; this adapter revalidates that decision against the live game on the main thread and
/// submits it.
/// </summary>
/// <remarks>
/// <para>
/// Four guards run before the native port is asked for anything. The configuration is re-checked, so a
/// feature switched off between planning and execution rejects without a penalty. The action-family
/// lease is re-read, because another plugin can take it mid-cycle and a spell cast without it is this
/// suite acting on content it has stood down from. The native world epoch is compared with the epoch
/// the plan carries by value, refusing a plan made against another run of the game. Then the manual
/// pause is refreshed and consulted, which is what stops a cast planned a moment before the player
/// cast something by hand from landing a moment after it.
/// </para>
/// <para>
/// After that the port owns the verdict, and most of its refusals are ordinary. Auto Cast's planner
/// sees strictly less than this boundary does — whether a target request is open, whether the caster
/// is free, and whether the spell has anything to aim at are all live-only (W60) — so those three
/// reject penalty-free and say so. What is not ordinary is an attempted mutation the verifier could
/// not confirm: the fire hook's epoch not advancing by exactly one means a cast that spent something
/// other than what was planned, and that faults.
/// </para>
/// </remarks>
internal sealed class AutoCastCycleActionAdapter : IAutoCastCycleActionPort
{
    private readonly IAutoCastNativePort _casts;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly AutoCastManualPauseState _manualPause;

    public AutoCastCycleActionAdapter(
        IAutoCastNativePort casts,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        AutoCastManualPauseState manualPause)
    {
        _casts = casts ?? throw new ArgumentNullException(nameof(casts));
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        _manualPause = manualPause ?? throw new ArgumentNullException(nameof(manualPause));
    }

    public ServiceActionResult TryExecute(
        in AutoCastCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => TryExecuteCore(in action, in config, in context, requireAutomationPolicy: true);

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionResult TryExecuteGameMcp(
        in AutoCastCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => TryExecuteCore(in action, in config, in context, requireAutomationPolicy: false);
#endif

    private ServiceActionResult TryExecuteCore(
        in AutoCastCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context,
        bool requireAutomationPolicy)
    {
        if (requireAutomationPolicy && !AutoCastConfigurationPolicy.IsOperational(config))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);

        if (!Owns())
            return ServiceActionResult.Rejected(AutoCastActionResultCodes.ActionFamilyUnavailable);

        if (!NativeEpochMatches(action.CollectedAtEpoch))
            return ServiceActionResult.Rejected(CommonActionResultCodes.LifecycleReplaced);

        _manualPause.Refresh(context.AttemptedAt, config);

        // A release or toggle-off is never held back by the pause. The player casting by hand is a
        // reason to stop starting casts, not a reason to retain a held input or active toggle.
        if (action.Kind == AutoCastActionKind.Fire && _manualPause.IsPaused(context.AttemptedAt))
            return ServiceActionResult.Rejected(AutoCastActionResultCodes.ManualPause);

        AutoCastSubmission submission;
        try
        {
            submission = action.Kind switch
            {
                AutoCastActionKind.ReleaseCharge =>
                    _casts.ReleaseCharge(action.SlotIndex, action.SpellRecipeId),
                AutoCastActionKind.ToggleOff =>
                    _casts.ToggleOff(action.SlotIndex, action.SpellRecipeId),
                _ => _casts.Fire(
                    action.SlotIndex,
                    action.SpellRecipeId,
                    AutoCastConfigurationPolicy.HoldsFullCharge(config) && action.Belief.Chargeable),
            };
        }
        catch (Exception ex) when (
            ex is TargetInvocationException || ex is ArgumentException ||
            ex is InvalidOperationException || ex is TargetException || ex is MemberAccessException)
        {
            Plugin.Log?.LogAutomataWarning(
                $"Auto Cast failed to cast the spell in slot {action.SlotIndex + 1}: adapter fault ({ex.GetBaseException().Message}).");
            return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
        }

        Narrate(in action, in submission);
        return Map(in submission);
    }

    /// <summary>
    /// Preserves an unconditional warning when a native mutation was attempted but did not land.
    /// </summary>
    /// <remarks>
    /// Ordinary preflight refusals and successes belong to ServiceCycle observation products rather
    /// than the BepInEx log. A failure past preflight remains a warning.
    /// </remarks>
    private static void Narrate(
        in AutoCastCycleAction action,
        in AutoCastSubmission submission)
    {
        var what = action.Kind switch
        {
            AutoCastActionKind.ReleaseCharge =>
                $"release the charged spell in slot {action.SlotIndex + 1}",
            AutoCastActionKind.ToggleOff =>
                $"turn off the toggled spell in slot {action.SlotIndex + 1}",
            _ => $"cast the spell in slot {action.SlotIndex + 1}",
        };
        if (submission.Verified) return;

        var message =
            $"Auto Cast did not {what}: {submission.Reason} " +
            $"(planned from a snapshot with {action.Belief.EligibleSlots} eligible slots).";
        if (submission.Preflight == AutoCastPreflight.Proceeded)
            Plugin.Log?.LogAutomataWarning(message);
    }

    private bool Owns()
    {
        try
        {
            return _ownsActionFamily();
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether the game is still the run this cast was planned for.</summary>
    private bool NativeEpochMatches(long plannedEpoch)
    {
        long epoch;
        try
        {
            epoch = _readLifecycleEpoch();
        }
        catch (Exception ex) when (
            ex is TargetInvocationException || ex is ArgumentException ||
            ex is InvalidOperationException || ex is TargetException || ex is MemberAccessException)
        {
            return false;
        }

        return epoch > 0 && epoch == plannedEpoch;
    }

    private static ServiceActionResult Map(in AutoCastSubmission submission)
    {
        switch (submission.Preflight)
        {
            case AutoCastPreflight.ContractUnavailable:
                return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
            case AutoCastPreflight.TargetingInProgress:
                return ServiceActionResult.Rejected(AutoCastActionResultCodes.TargetingInProgress);
            case AutoCastPreflight.CasterBusy:
                return ServiceActionResult.Skipped(AutoCastActionResultCodes.NativeCasterBusy);
            case AutoCastPreflight.SlotIdentityChanged:
                return ServiceActionResult.Rejected(AutoCastActionResultCodes.SlotIdentityChanged);
            case AutoCastPreflight.NotReady:
                return ServiceActionResult.Skipped(AutoCastActionResultCodes.SpellNotReady);
            case AutoCastPreflight.NoValidTarget:
                return ServiceActionResult.Rejected(AutoCastActionResultCodes.NoValidTarget);
            case AutoCastPreflight.ChargeHoldRefused:
                return ServiceActionResult.Rejected(AutoCastActionResultCodes.ChargeHoldRefused);
            case AutoCastPreflight.NotToggleable:
                return ServiceActionResult.Rejected(AutoCastActionResultCodes.SpellNotToggleable);
            case AutoCastPreflight.AlreadyInactive:
                return ServiceActionResult.Rejected(AutoCastActionResultCodes.SpellAlreadyInactive);
            case AutoCastPreflight.CancellationDisabled:
                return ServiceActionResult.Rejected(AutoCastActionResultCodes.CancellationDisabled);
            case AutoCastPreflight.AlreadyCasting:
                // Ordinary snapshot staleness for the service, exactly like an unready spell: the
                // cast started between planning and pressing. No penalty, and the MCP caller reads
                // it as the refusal it is.
                return ServiceActionResult.Skipped(AutoCastActionResultCodes.SpellAlreadyCasting);
        }

        var evidence = ServiceNativeMutationEvidence.Observed(submission.Outcome, submission.CallOutcome);
        if (submission.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);

        // An attempted mutation the verifier could not confirm is a fault, not a skip. The native port
        // has already blocked that spell until the next lifecycle, so this is reported once and the
        // service backs off rather than re-probing a contract it no longer understands.
        return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault, evidence);
    }
}
