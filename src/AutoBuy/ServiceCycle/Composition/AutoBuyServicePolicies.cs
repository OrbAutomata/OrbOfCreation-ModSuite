using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoBuyServicePolicies
{
    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    public static ServiceId ServiceId => new("orbautomata.auto-buy");
    public static WakePolicy DefaultWakePolicy => WakePolicy.OnPublication;
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);

    /// <summary>
    /// The longest wait a refused candidate backs off to. A refusal ends the batch, so a candidate
    /// that came due every second would hold back every purchase ranked after it.
    /// </summary>
    public static MonotonicDuration RefusalBackoffCap { get; } =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1));

    public static ServiceRetryTarget RetryTarget(AutoBuyCandidateKind kind, Guid uuid) =>
        new(uuid, NativeType(kind));

    public static ServiceActionNativeTypeId NativeType(AutoBuyCandidateKind kind) =>
        kind == AutoBuyCandidateKind.Structure
            ? ServiceActionNativeTypeId.StructureSO
            : ServiceActionNativeTypeId.UpgradeSO;
}
