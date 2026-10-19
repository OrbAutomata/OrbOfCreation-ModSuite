using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class SpellLevelServicePolicies
{
    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10));

    public static ServiceId ServiceId => new("orbautomata.spell-level");

    public static WakePolicy DefaultWakePolicy => WakePolicy.OnPublication;

    // A fault backs its spell off on the retry gate from these waits; a level the game charged for
    // without the level rising waits a minute instead, doubling to half an hour. The ten-second
    // ceiling keeps a contract mismatch, which holds until the next lifecycle, from re-probing every
    // second.
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);

    internal static ServiceRetryTarget RetryTarget(Guid spellId) =>
        new(spellId, ServiceActionNativeTypeId.SpellRecipeSO);
}
