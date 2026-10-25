using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoScribeCycleActionAdapter : IAutoScribeCycleActionPort
{
    private readonly AutoScribeOneShotCraftGameAction _gameAction;
    private readonly Func<bool> _ownsActionFamily;
    private readonly AutoScribeActionHealth _health;
    private readonly AutomationCycleGuard<AutoScribeCycleAction> _guard;

    internal AutoScribeCycleActionAdapter(
        AutoScribeOneShotCraftGameAction gameAction,
        Func<bool> ownsActionFamily,
        AutoScribeActionHealth health,
        Action<string> warn)
    {
        _gameAction = gameAction ?? throw new ArgumentNullException(nameof(gameAction));
        _ownsActionFamily = ownsActionFamily ??
            throw new ArgumentNullException(nameof(ownsActionFamily));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _guard = new AutomationCycleGuard<AutoScribeCycleAction>(
            "Auto Scribe",
            static action => $"Auto Scribe failed to craft {EntityIdentityFormatter.Format(action.RecipeId)}",
            warn ?? throw new ArgumentNullException(nameof(warn)));
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
        var submission = _gameAction.Submit(in action);
        // A live read can fail for one attempt while the binding set remains usable. Return and
        // log that fault, but let the next attempt retry without retaining a contract failure.
        if (submission.Outcome.Code != AutoScribeActionResultCodes.ContractUnavailable ||
            !_gameAction.BindingsAvailable)
            _health.Observe(in submission);
        var result = _guard.Finish(submission.Outcome, in action);
        return AutoScribeServicePolicies.PaidWithoutWork(result.Code)
            ? result.BackingOffAfterPayment()
            : result;
    }
}
