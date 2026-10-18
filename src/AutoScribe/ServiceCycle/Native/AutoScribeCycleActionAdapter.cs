using System;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoScribeCycleActionAdapter : IAutoScribeCycleActionPort
{
    private readonly AutoScribeOneShotCraftGameAction _gameAction;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _ownsActionFamily;
    private readonly AutoScribeActionHealth _health;
    private readonly AutomationCycleGuard<AutoScribeCycleAction> _guard = new(
        "Auto Scribe",
        static action => $"Auto Scribe failed to craft {EntityIdentityFormatter.Format(action.RecipeId)}",
        static message => Plugin.Log?.LogAutomataWarning(message));

    internal AutoScribeCycleActionAdapter(
        AutoScribeOneShotCraftGameAction gameAction,
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        AutoScribeActionHealth health)
    {
        _gameAction = gameAction ?? throw new ArgumentNullException(nameof(gameAction));
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _ownsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        _health = health ?? throw new ArgumentNullException(nameof(health));
    }

    public ServiceActionResult TryExecute(
        in AutoScribeCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
    {
        // Roles are intentionally not re-read here. This config is the cycle-pinned operational
        // policy; the worker pinned Roles when the cycle opened.
        if (!AutoScribeConfigurationPolicy.IsOperational(config))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);
        if (!_guard.Owns(_ownsActionFamily))
        {
            return ServiceActionResult.Rejected(
                AutoScribeActionResultCodes.MutationPermitUnavailable);
        }
        if (!_guard.TryAdmitEpoch(_readLifecycleEpoch, action.CollectedAtEpoch, in action, out var refusal))
            return refusal;

        var submission = _gameAction.Submit(in action);
        _health.Observe(in submission);
        return _guard.Finish(submission.Outcome, in action);
    }
}
