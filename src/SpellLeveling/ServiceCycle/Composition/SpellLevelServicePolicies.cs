using System;
using OrbModding.Common;
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

    // A fault backs its target off on the retry gate from these waits: the spell for a single level,
    // the level-all upgrade for the batch. A press the game charged for without a level rising waits
    // a minute instead, doubling to half an hour. The ten-second ceiling keeps a contract mismatch,
    // which holds until the next lifecycle, from re-probing every second.
    public static ServiceFaultRecoveryPolicy FaultRecoveryPolicy =>
        new(InitialFaultBackoff, MaximumFaultBackoff);

    internal static ServiceRetryTarget RetryTarget(Guid spellId) =>
        new(spellId, ServiceActionNativeTypeId.SpellRecipeSO);

    internal static ServiceRetryTarget LevelAllRetryTarget =>
        new(KnownEntities.UnlockLevelAllSpells.Uuid, ServiceActionNativeTypeId.UpgradeSO);

    internal static ServiceRetryTarget RetryTarget(in SpellLevelCycleAction action) =>
        action.Kind == SpellLevelActionKind.All ? LevelAllRetryTarget : RetryTarget(action.Uuid);
}
