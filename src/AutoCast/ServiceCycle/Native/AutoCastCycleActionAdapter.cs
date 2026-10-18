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
    private readonly AutomationCycleGuard<AutoCastCycleAction> _guard = new(
        static action => "Auto Cast failed to " + action.Kind switch
        {
            AutoCastActionKind.ReleaseCharge => $"release the charged spell in slot {action.SlotIndex + 1}",
            AutoCastActionKind.ToggleOff => $"turn off the toggled spell in slot {action.SlotIndex + 1}",
            _ => $"cast the spell in slot {action.SlotIndex + 1}",
        },
        static message => Plugin.Log?.LogAutomataWarning(message));
    private HeldCharge? _serviceHold;

    /// <summary>
    /// The submission this adapter last produced, so a caller can have the refusal's own sentence.
    /// A <see cref="ServiceActionResult"/> carries a class and no prose by design, and the sentence
    /// is where the fact a caller acts on lives — which spell occupies the slot it planned for.
    /// </summary>
    internal AutoCastSubmission LastSubmission { get; private set; }

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
        LastSubmission = default;

        // Letting go of a charge input is never held back by the feature being off or the family
        // lease being lost: those are the moments a hold most needs releasing.
        var release = action.Kind == AutoCastActionKind.ReleaseCharge;
        if (!release && requireAutomationPolicy && !AutoCastConfigurationPolicy.IsOperational(config))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);

        if (!release && !_guard.Owns(_ownsActionFamily))
            return ServiceActionResult.Rejected(AutoCastActionResultCodes.ActionFamilyUnavailable);

        if (!_guard.TryAdmitEpoch(_readLifecycleEpoch, action.CollectedAtEpoch, in action, out var refusal))
            return refusal;

        // The pause is the service standing down after the player cast by hand. A press the
        // player asked for through the verb is that manual cast, not something held back by one.
        if (requireAutomationPolicy) _manualPause.Refresh(context.AttemptedAt, config);

        // A release or toggle-off is never held back by the pause. The player casting by hand is a
        // reason to stop starting casts, not a reason to retain a held input or active toggle.
        if (requireAutomationPolicy &&
            action.Kind == AutoCastActionKind.Fire &&
            _manualPause.IsPaused(context.AttemptedAt))
            return ServiceActionResult.Rejected(AutoCastActionResultCodes.ManualPause);

        if (requireAutomationPolicy &&
            action.Kind == AutoCastActionKind.Fire &&
            action.Ritual != AutoCastRitualCondition.None)
        {
            if (_casts.IsRitualRunning() is not { } running)
                return ServiceActionResult.Faulted(CommonActionResultCodes.AdapterFault);
            if (running != (action.Ritual == AutoCastRitualCondition.InRitual))
                return ServiceActionResult.Skipped(AutoCastActionResultCodes.RitualChanged);
        }

        var serviceHold = requireAutomationPolicy &&
            action.Kind == AutoCastActionKind.Fire &&
            AutoCastConfigurationPolicy.HoldsFullCharge(config) &&
            action.Belief.Chargeable;
        AutoCastSubmission submission;
        try
        {
            submission = action.Kind switch
            {
                AutoCastActionKind.ReleaseCharge =>
                    _casts.ReleaseCharge(action.SlotIndex, action.SpellRecipeId),
                AutoCastActionKind.ToggleOff =>
                    _casts.ToggleOff(action.SlotIndex, action.SpellRecipeId),
                _ => _casts.Fire(action.SlotIndex, action.SpellRecipeId, action.ChargeHold || serviceHold),
            };
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return _guard.Fault(ex, in action).ToResult();
        }

        LastSubmission = submission;
        if (serviceHold && submission.Outcome.Verified)
            _serviceHold = new HeldCharge(action.SlotIndex, action.SpellRecipeId);
        else if (release && submission.Outcome.Verified && _serviceHold?.SpellRecipeId == action.SpellRecipeId)
            _serviceHold = null;
        return _guard.Finish(submission.Outcome, in action);
    }

    /// <summary>Whether a full-charge hold this service took is still pressed down.</summary>
    internal bool HoldsServiceCharge => _serviceHold is not null;

    /// <summary>
    /// Lets go of the hold this service took, on the main thread, when the service may no longer
    /// hold it. A hold the player took through <c>game_cast</c> is theirs and is never touched.
    /// The hold is forgotten only once the release lands, so a refused release is tried again.
    /// </summary>
    internal void ReleaseServiceHold()
    {
        if (_serviceHold is not { } held) return;
        AutoCastSubmission submission;
        try
        {
            submission = _casts.ReleaseCharge(held.SlotIndex, held.SpellRecipeId);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            Plugin.Log?.LogAutomataWarning(
                $"Auto Cast failed to release the charged spell in slot {held.SlotIndex + 1}: adapter fault ({ex.GetBaseException().Message}).");
            return;
        }

        if (submission.Outcome.Verified) _serviceHold = null;
    }

    /// <summary>A lifecycle boundary replaced every spell, and with them every charge input.</summary>
    internal void ForgetServiceHold() => _serviceHold = null;

    private readonly record struct HeldCharge(int SlotIndex, Guid SpellRecipeId);
}
