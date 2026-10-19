using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoHarvestServicePolicies
{
    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));

    // A harvest add spends nothing when it is made, so a faulted pair waits a second rather than a
    // paid press's minute. A second outlasts the service's own first wait, so the other pair takes
    // its turn before the faulted one is tried again.
    private static readonly MonotonicDuration PairFirstWait =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    private static readonly MonotonicDuration PairWaitCap =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10));

    public static ServiceId ServiceId => new("orbautomata.auto-harvest");
    public static WakePolicy DefaultWakePolicy => WakePolicy.OnPublication;
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);

    internal static ServiceRetryTarget RetryTarget(AutoHarvestPair pair) =>
        new(AutoHarvestPairAuthoring.For(pair).ActionId, ServiceActionNativeTypeId.PlotNodeActionSO);

    /// <summary>The same result, backing its pair off on the pair's own waits when it faulted.</summary>
    internal static ServiceActionResult BackingOffThePair(in ServiceActionResult result) =>
        result.Disposition == ServiceActionDisposition.Faulted
            ? result.BackingOffTarget(PairWaitCap, initialBackoff: PairFirstWait)
            : result;

    /// <summary>A retry target in a sentence: which plot the service was collecting.</summary>
    internal static string Describe(ServiceRetryTarget target)
    {
        foreach (var pair in new[] { AutoHarvestPair.FruitTree, AutoHarvestPair.TreasureTree })
        {
            var authoring = AutoHarvestPairAuthoring.For(pair);
            if (authoring.ActionId == target.Id)
                return "collecting " + EntityIdentityFormatter.Format(authoring.PlotId);
        }
        return EntityIdentityFormatter.Format(target.Id);
    }
}
