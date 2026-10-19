using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Assembles the typed Auto Buy service from its action port. Per-cycle debugging value flows through
/// the always-on decision journal via the worker's state projection.
/// </summary>
internal static class AutoBuyService
{
    internal static IAutomataServiceDefinition<
        AutoBuyCycleState,
        AutoBuyCycleAction> Define(IAutoBuyCycleActionPort actions, AutoBuyInvariantLatch latch)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (latch is null) throw new ArgumentNullException(nameof(latch));

        var metadata = new AutomataServiceMetadata(
            AutoBuyServicePolicies.ServiceId,
            AutoBuyServicePolicies.DefaultWakePolicy,
            AutoBuyServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<
            AutoBuyCycleState,
            AutoBuyCycleAction>(
                in metadata,
                createWorker: static () => new AutoBuyWorkerDefinition(),
                shouldStart: ShouldStart,
                describeAction: static (in AutoBuyCycleAction action) =>
                    action.OwningListId != Guid.Empty && action.OwningViewId != Guid.Empty
                        ? ServiceActionJournalAttribution.Routed(
                            action.Uuid,
                            AutoBuyServicePolicies.NativeType(action.Kind),
                            action.OwningListId,
                            action.OwningViewId)
                        : new ServiceActionJournalAttribution(
                            action.Uuid,
                            AutoBuyServicePolicies.NativeType(action.Kind),
                            Guid.Empty,
                            Guid.Empty,
                            ServiceActionRouteStatus.Contradictory),
                execute: actions.TryExecute);

        // Only Auto Buy's own policy. Whether the shared world is fresh enough to act on is not a
        // feature question: the runtime gates a service bound to a world generation source before
        // this is ever called.
        ServiceStartDecision ShouldStart(
            in SuiteRuntimeConfiguration config,
            in ServiceCycleStartContext context) =>
            AutoBuyConfigurationPolicy.IsOperational(config) && !latch.IsTripped
                ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
                : ServiceStartDecision.Wait(
                    CommonServiceDecisionCodes.NotReady,
                    WakePolicy.OnPublication);
    }
}
