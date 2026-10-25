using System;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoItemsCycleActionAdapter : IAutoItemsCycleActionPort
{
    private readonly AutoItemsConsumableUseGameAction _gameAction;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly Func<string> _readOwnershipFailure;
    private readonly AutoItemsActionHealth _health;
    private readonly AutomationCycleGuard<AutoItemsCycleAction> _guard = new(
        "Auto Items",
        static action => $"Auto Items failed to use {EntityIdentityFormatter.Format(action.ItemId)}",
        static message => Plugin.Log?.LogAutomataWarning(message));

    internal AutoItemsCycleActionAdapter(
        AutoItemsConsumableUseGameAction gameAction,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        Func<string> readOwnershipFailure,
        AutoItemsActionHealth health)
    {
        _gameAction = gameAction ?? throw new ArgumentNullException(nameof(gameAction));
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
        _health = health ?? throw new ArgumentNullException(nameof(health));
    }

    public ServiceActionResult TryExecute(
        in AutoItemsCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        // This is the cycle-pinned policy reading by design. There is no current-configuration
        // reader in this adapter or in the GameAction beneath it.
        if (!AutoItemsConfigurationPolicy.IsOperational(config) ||
            !AutoItemsConfigurationPolicy.Allows(
                config.AutoItems,
                action.Family,
                action.ItemId))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (!_guard.Owns(_ownsActionFamily))
        {
            var reason = ReadOwnershipFailure();
            _health.ObserveOwnership(reason);
            return ServiceActionResult.Rejected(
                AutoItemsActionResultCodes.ActionFamilyUnavailable);
        }
        if (!_guard.TryAdmitEpoch(_readLifecycleEpoch, action.CollectedAtEpoch, in action, out var refusal))
            return refusal;

        NativeActionOutcome submission;
        try
        {
            submission = _gameAction.Submit(in action);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            submission = _guard.Fault(ex, in action);
        }

        _health.Observe(in submission);
        return _guard.Finish(in submission, in action);
    }

    private GameActionReason ReadOwnershipFailure()
    {
        try
        {
            var reason = _readOwnershipFailure();
            return string.IsNullOrWhiteSpace(reason)
                ? "Auto Items does not own ConsumableUse and NativeMultiBuyOverride."
                : reason;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return GameActionFaultLog.Reason("Auto Items ownership evidence could not be read.", ex, "Auto Items ownership");
        }
    }
}
