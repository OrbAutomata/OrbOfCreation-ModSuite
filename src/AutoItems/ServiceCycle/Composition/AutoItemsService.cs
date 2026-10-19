using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsService
{
    internal static IAutomataServiceDefinition<AutoItemsCycleState, AutoItemsCycleAction> Define(
        IAutoItemsCycleActionPort actions)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        var metadata = new AutomataServiceMetadata(
            AutoItemsServicePolicies.ServiceId,
            AutoItemsServicePolicies.DefaultWakePolicy,
            AutoItemsServicePolicies.FaultRecoveryPolicy);
        return AutomataService.Define<AutoItemsCycleState, AutoItemsCycleAction>(
            in metadata,
            static () => new AutoItemsWorkerDefinition(),
            ShouldStart,
            static (in AutoItemsCycleAction action) =>
                ServiceActionJournalAttribution.Native(
                    action.ItemId,
                    AutoItemsServicePolicies.NativeType),
            actions.TryExecute);
    }

    private static ServiceStartDecision ShouldStart(
        in SuiteRuntimeConfiguration config,
        in ServiceCycleStartContext context) =>
        AutoItemsConfigurationPolicy.IsOperational(config)
            ? ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready)
            : ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.OnPublication);
}

internal static class AutoItemsServicePolicies
{
    internal static ServiceId ServiceId => new("orbautomata.auto-items");
    internal static WakePolicy DefaultWakePolicy => WakePolicy.OnPublication;
    internal static ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250)),
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));
    internal const ServiceActionNativeTypeId NativeType = ServiceActionNativeTypeId.ConsumableSO;

    internal static ServiceRetryTarget RetryTarget(Guid itemId) => new(itemId, NativeType);
}
