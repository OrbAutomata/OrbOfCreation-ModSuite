using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct AutoHarvestFeatureStatus
{
    public AutoHarvestFeatureStatus(
        FeatureStatusState state,
        FeatureStatusReasonCode reason,
        string summary)
    {
        State = state;
        Reason = reason;
        Summary = summary;
    }

    public FeatureStatusState State { get; }
    public FeatureStatusReasonCode Reason { get; }
    public string Summary { get; }
}

internal static class AutoHarvestFeatureStatusProjector
{
    /// <summary>
    /// What the running feature reports from its pair evidence.
    /// </summary>
    /// <remarks>
    /// A zero selected count is ignored by the runtime-status boundary; saved configuration owns
    /// whether the feature is disabled.
    /// </remarks>
    public static AutoHarvestFeatureStatus Project(
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure,
        ServiceRetryEntry? backingOff = null)
    {
        var selectedCount = CountSelected(fruit) + CountSelected(treasure);
        if (selectedCount == 0)
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.ConfigurationDisabled,
                FeatureStatusReasonCode.ConfigurationDisabled,
                "Auto Harvest is disabled by configuration.");
        }

        if (TryProjectFeatureScoped(fruit, treasure, out var featureScoped))
            return featureScoped;

        if (backingOff is { } retry)
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.NativeMutationFailed,
                $"Auto Harvest is backing off {AutoHarvestServicePolicies.Describe(retry.Target)} after " +
                $"{retry.ConsecutiveFailures} failed attempt(s) and retries it when due: {retry.Reason}");
        }

        var hasUnavailablePair = IsPairUnavailable(fruit) || IsPairUnavailable(treasure);
        var hasCapableSibling = IsCapable(fruit) || IsCapable(treasure);
        if (hasUnavailablePair && hasCapableSibling)
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.Degraded,
                FeatureStatusReasonCode.PartialCapabilityUnavailable,
                "One selected harvest pair is unavailable while another remains usable.");
        }

        if (IsEligible(fruit) || IsEligible(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.Operational,
                FeatureStatusReasonCode.None,
                string.Empty);
        }

        if (IsNativeBusy(fruit) || IsNativeBusy(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.NativeBusy,
                "An unlocked selected harvest action is waiting for native readiness.");
        }

        // Before the locked fall-through, because a plot that is merely bare is not a plot the player
        // has yet to unlock, and telling them to go and progress is telling them to do nothing.
        if (IsActionNotOffered(fruit) || IsActionNotOffered(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.NativeBusy,
                "A selected harvest plot is not offering its harvest action right now.");
        }

        if (IsQueueBlocked(fruit) || IsQueueBlocked(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.TemporarilyBlocked,
                FeatureStatusReasonCode.QueueFull,
                "The native plot-action list has no free action entry.");
        }

        if (IsPairContractUnavailable(fruit) || IsPairContractUnavailable(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.ContractUnavailable,
                "A selected harvest pair failed its audited native contract.");
        }

        // After the failure branch, because a broken sibling is the more urgent thing to say,
        // and before the registry one, because "the game has not evaluated this yet" is the more
        // specific reading of the same waiting. Both are NotReady; neither is Locked.
        if (IsPrerequisitesNotConfirmed(fruit) || IsPrerequisitesNotConfirmed(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "The game has not confirmed a selected harvest action's prerequisites yet.");
        }

        if (IsRegistryNotReady(fruit) || IsRegistryNotReady(treasure) ||
            IsNotObserved(fruit) || IsNotObserved(treasure))
        {
            return new AutoHarvestFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.RegistryNotReady,
                "Auto Harvest is waiting for authoritative native evidence.");
        }

        return new AutoHarvestFeatureStatus(
            FeatureStatusState.Locked,
            FeatureStatusReasonCode.ProgressionLocked,
            "The selected harvest content is not currently unlocked and available.");
    }

    private static bool TryProjectFeatureScoped(
        in AutoHarvestPairHealth fruit,
        in AutoHarvestPairHealth treasure,
        out AutoHarvestFeatureStatus health)
    {
        var contract = IsFeatureScoped(fruit, AutoHarvestPairHealthKind.ContractUnavailable) ||
                       IsFeatureScoped(treasure, AutoHarvestPairHealthKind.ContractUnavailable);
        if (contract)
        {
            health = new AutoHarvestFeatureStatus(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.ContractUnavailable,
                "The native Auto Harvest contract is unavailable.");
            return true;
        }

        var registry = IsFeatureScoped(fruit, AutoHarvestPairHealthKind.RegistryNotReady) ||
                       IsFeatureScoped(treasure, AutoHarvestPairHealthKind.RegistryNotReady);
        if (registry)
        {
            health = new AutoHarvestFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.RegistryNotReady,
                "Auto Harvest is waiting for its audited native registry entries.");
            return true;
        }

        health = default;
        return false;
    }

    private static int CountSelected(in AutoHarvestPairHealth value) => value.Selected ? 1 : 0;
    private static bool IsFeatureScoped(in AutoHarvestPairHealth value, AutoHarvestPairHealthKind kind) =>
        value.Selected && value.FeatureScoped && value.Kind == kind;
    private static bool IsPairUnavailable(in AutoHarvestPairHealth value) =>
        value.Selected && !value.FeatureScoped &&
        (value.Kind == AutoHarvestPairHealthKind.ContractUnavailable ||
         value.Kind == AutoHarvestPairHealthKind.RegistryNotReady);
    /// <summary>
    /// Whether this pair is a working one, whatever it happens to be doing this instant.
    /// </summary>
    /// <remarks>
    /// A pair whose plot is not offering its action belongs here beside the busy and queue-blocked
    /// ones: nothing is wrong with it and it will act again on its own. It also has to be here for
    /// the degraded branch to keep working — a failed sibling beside a merely waiting pair is what
    /// "partially unavailable" means, and without this the failure would be reported as the waiting.
    /// </remarks>
    private static bool IsCapable(in AutoHarvestPairHealth value) =>
        value.Selected &&
        (value.Kind == AutoHarvestPairHealthKind.Eligible ||
         value.Kind == AutoHarvestPairHealthKind.NativeBusy ||
         value.Kind == AutoHarvestPairHealthKind.ActionNotOffered ||
         value.Kind == AutoHarvestPairHealthKind.QueueBlocked);
    private static bool IsEligible(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.Eligible;
    private static bool IsNativeBusy(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.NativeBusy;
    private static bool IsActionNotOffered(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.ActionNotOffered;
    private static bool IsQueueBlocked(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.QueueBlocked;
    private static bool IsPairContractUnavailable(in AutoHarvestPairHealth value) =>
        value.Selected && !value.FeatureScoped && value.Kind == AutoHarvestPairHealthKind.ContractUnavailable;
    private static bool IsRegistryNotReady(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.RegistryNotReady;
    /// <summary>
    /// Deliberately in neither <see cref="IsCapable"/> nor <see cref="IsPairUnavailable"/>: a pair
    /// whose prerequisites the game has not evaluated is not known to be working and not known to be
    /// broken, and counting it as either would put a guess in the degraded summary.
    /// </summary>
    private static bool IsPrerequisitesNotConfirmed(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.PrerequisitesNotConfirmed;
    private static bool IsNotObserved(in AutoHarvestPairHealth value) =>
        value.Selected && value.Kind == AutoHarvestPairHealthKind.NotObserved;
}
