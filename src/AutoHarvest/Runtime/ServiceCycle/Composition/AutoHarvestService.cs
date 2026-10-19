using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal static class AutoHarvestService
{
    internal static IAutomataServiceDefinition<
        AutoHarvestCycleState,
        AutoHarvestCycleAction> Define(IAutoHarvestCycleActionPort actions)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));

        var metadata = new AutomataServiceMetadata(
            AutoHarvestServicePolicies.ServiceId,
            AutoHarvestServicePolicies.DefaultWakePolicy,
            AutoHarvestServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<
            AutoHarvestCycleState,
            AutoHarvestCycleAction>(
                in metadata,
                createWorker: static () => new AutoHarvestWorkerDefinition(),
                shouldStart: ShouldStart,
                describeAction: static (in AutoHarvestCycleAction action) =>
                {
                    var target = AutoHarvestServicePolicies.RetryTarget(action.Pair);
                    return ServiceActionJournalAttribution.Native(target.Id, target.NativeType);
                },
                execute: actions.TryExecute);
    }

    private static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        AutoHarvestConfigurationPolicy.IsOperational(config)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.OnPublication);
}
