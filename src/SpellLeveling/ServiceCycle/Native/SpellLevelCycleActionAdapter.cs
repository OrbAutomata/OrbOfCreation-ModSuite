using System;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The native execution boundary for Spell Leveling. The pure worker plans at most one mastery-level
/// purchase; this adapter revalidates that decision against the live game on the main thread and
/// submits it.
/// </summary>
/// <remarks>
/// Configuration is re-checked here so a feature switched off between planning and execution
/// rejects without a penalty. The shared GameAction owns main-thread, lifecycle and action-family
/// admission before reading the live game.
/// <para>
/// After that the GameAction owns the verdict. Spell Leveling's planner sees less than Auto Buy's does by
/// design (W59), so a refusal here is the ordinary case rather than the alarming one, and the result
/// codes distinguish the refusal that means "not unlocked yet" from the one that means "cannot afford
/// it right now" — the first is what the feature status reads as <c>Locked</c>.
/// </para>
/// </remarks>
internal sealed class SpellLevelCycleActionAdapter : ISpellLevelCycleActionPort
{
    private readonly SpellLevelGameAction _levels;
    private readonly Action<AutoSpellLevelCapability>? _observeCapability;
    private readonly Action<SpellLevelSubmission>? _observeBoundaryStatus;
    private readonly AutomationCycleGuard<SpellLevelCycleAction> _guard;

    public SpellLevelCycleActionAdapter(
        SpellLevelGameAction levels,
        Action<string> warn,
        Action<AutoSpellLevelCapability>? observeCapability = null,
        Action<SpellLevelSubmission>? observeBoundaryStatus = null)
    {
        _levels = levels ?? throw new ArgumentNullException(nameof(levels));
        _observeCapability = observeCapability;
        _observeBoundaryStatus = observeBoundaryStatus;
        _guard = new AutomationCycleGuard<SpellLevelCycleAction>(
            "Spell Leveling",
            static action => "Spell Leveling failed to buy a mastery level for " +
                (action.Kind == SpellLevelActionKind.All
                    ? "every ready spell"
                    : EntityIdentityFormatter.Format(action.Uuid)),
            warn ?? throw new ArgumentNullException(nameof(warn)));
    }

    public ServiceActionResult TryExecute(
        in SpellLevelCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => TryExecuteCore(in action, in config, in context, requireAutomationPolicy: true);

#if SERVICE_CYCLE_PROFILE
    internal SpellLevelSubmission LastSubmission { get; private set; }

    internal ServiceActionResult TryExecuteGameMcp(
        in SpellLevelCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context)
        => TryExecuteCore(in action, in config, in context, requireAutomationPolicy: false);
#endif

    private ServiceActionResult TryExecuteCore(
        in SpellLevelCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context,
        bool requireAutomationPolicy)
    {
#if SERVICE_CYCLE_PROFILE
        LastSubmission = default;
#endif
        if (requireAutomationPolicy && !SpellLevelConfigurationPolicy.IsOperational(config))
            return ServiceActionResult.Rejected(CommonActionResultCodes.ServiceDisabled);

        SpellLevelSubmission submission;
        try
        {
            submission = _levels.Submit(in action);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return _guard.Fault(ex, in action).ToResult();
        }

#if SERVICE_CYCLE_PROFILE
        LastSubmission = submission;
#endif
        ObserveCapability(in action, in submission);
        _observeBoundaryStatus?.Invoke(submission);
        var result = _guard.Finish(submission.Outcome, in action);
        return submission.PaidWithoutLevel ? result.BackingOffAfterPayment() : result;
    }

    /// <summary>
    /// Tells the feature status what the boundary just learned about the capability, which is the only
    /// place <see cref="AutoSpellLevelCapability.Locked"/> can be observed at all.
    /// </summary>
    private void ObserveCapability(
        in SpellLevelCycleAction action,
        in SpellLevelSubmission submission)
    {
        if (_observeCapability is null) return;
        if (submission.Outcome.Code == SpellLevelActionResultCodes.ProgressionLocked)
        {
            // SubmitAll has already revalidated the committed batch upgrade. A prerequisite
            // refusal says the live set cannot move, not that the upgrade ceased to exist.
            if (action.Kind == SpellLevelActionKind.Single)
                _observeCapability(AutoSpellLevelCapability.Locked);
        }
        else if (submission.Outcome.Code == SpellLevelActionResultCodes.LevelAllLocked)
        {
            _observeCapability(AutoSpellLevelCapability.Single);
        }
    }
}
