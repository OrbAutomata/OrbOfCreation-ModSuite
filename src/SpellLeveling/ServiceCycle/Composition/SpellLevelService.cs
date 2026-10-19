using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Assembles the typed Spell Leveling service from its action port. Per-cycle debugging value flows
/// through the always-on decision journal via the worker's state projection.
/// </summary>
internal static class SpellLevelService
{
    internal static IAutomataServiceDefinition<
        SpellLevelCycleState,
        SpellLevelCycleAction> Define(ISpellLevelCycleActionPort actions)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));

        var metadata = new AutomataServiceMetadata(
            SpellLevelServicePolicies.ServiceId,
            SpellLevelServicePolicies.DefaultWakePolicy,
            SpellLevelServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<
            SpellLevelCycleState,
            SpellLevelCycleAction>(
                in metadata,
                createWorker: static () => new SpellLevelWorkerDefinition(),
                shouldStart: ShouldStart,
                describeAction: static (in SpellLevelCycleAction action) =>
                {
                    var target = SpellLevelServicePolicies.RetryTarget(in action);
                    return ServiceActionJournalAttribution.Native(target.Id, target.NativeType);
                },
                execute: actions.TryExecute);

        // Only Spell Leveling's own policy, which is Auto Buy's: the feature has no settings of its
        // own. Whether the shared world is fresh enough to act on is not a feature question — the
        // runtime gates a service bound to a world generation source before this is ever called.
        static ServiceStartDecision ShouldStart(
            in SuiteRuntimeConfiguration config,
            in ServiceCycleStartContext context) =>
            SpellLevelConfigurationPolicy.IsOperational(config)
                ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
                : ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.OnPublication);
    }
}
