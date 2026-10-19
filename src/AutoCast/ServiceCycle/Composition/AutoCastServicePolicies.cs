using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoCastServicePolicies
{
    private static readonly MonotonicDuration InitialFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration MaximumFaultBackoff =
        MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(10));

    public static ServiceId ServiceId => new("orbautomata.auto-cast");

    public static WakePolicy DefaultWakePolicy => WakePolicy.OnPublication;

    // A fault backs that spell's action of that kind off on the retry gate from these waits. A cast
    // spends mana that regenerates, so it keeps the default doubling rather than a paid press's
    // minute; the ten-second ceiling keeps a fault that holds from re-probing every second.
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);

    internal static ServiceRetryTarget RetryTarget(AutoCastActionKind kind, Guid spellId) =>
        new(spellId, ServiceActionNativeTypeId.SpellRecipeSO, (byte)kind);

    /// <summary>A retry target in a sentence: which spell, and what the service was doing with it.</summary>
    internal static string Describe(ServiceRetryTarget target) =>
        (AutoCastActionKind)target.ActionKind switch
        {
            AutoCastActionKind.ReleaseCharge => "releasing the charge on ",
            AutoCastActionKind.ToggleOff => "turning off ",
            _ => "casting ",
        } + EntityIdentityFormatter.Format(target.Id);
}
