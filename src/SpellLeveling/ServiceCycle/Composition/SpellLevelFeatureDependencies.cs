using System;
using System.Threading;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// What the game currently lets Spell Leveling do, shared between the action boundary that learns it
/// and the toggle-button tooltip that shows it.
/// </summary>
/// <remarks>
/// The worker can derive <see cref="AutoSpellLevelCapability.Single"/> and
/// <see cref="AutoSpellLevelCapability.All"/> from the snapshot but never
/// <see cref="AutoSpellLevelCapability.Locked"/>, because that answer is the leveling prerequisite and
/// prerequisites are a boundary fact (W59). So the capability is main-thread state, seeded by a probe
/// once per lifecycle and corrected only when the boundary learns a progression or committed-upgrade
/// fact.
/// <para>
/// It resets to <see cref="AutoSpellLevelCapability.Locked"/> on a lifecycle boundary rather than
/// keeping the last generation's answer. Nothing is retained across a boundary, and a tooltip that
/// claims the previous save's progression is worse than one that admits it does not know yet.
/// </para>
/// </remarks>
internal sealed class SpellLevelCapabilityState
{
    private int _current = (int)AutoSpellLevelCapability.Locked;

    public AutoSpellLevelCapability Current => (AutoSpellLevelCapability)Volatile.Read(ref _current);

    public void Observe(AutoSpellLevelCapability capability) =>
        Volatile.Write(ref _current, (int)capability);

    public void Reset() => Observe(AutoSpellLevelCapability.Locked);
}

/// <summary>
/// The last ordinary affordability refusal learned at the action boundary. It is separate from
/// capability: being broke says what the feature is waiting for, not what the committed progression
/// upgrade lets it do.
/// </summary>
internal sealed class SpellLevelBoundaryStatusState
{
    private string? _waitingReason;

    public string? WaitingReason => Volatile.Read(ref _waitingReason);

    public void Observe(SpellLevelSubmission submission) =>
        Volatile.Write(
            ref _waitingReason,
            submission.Outcome.Code == SpellLevelActionResultCodes.LevelNotAffordable
                ? submission.Outcome.Reason
                : null);

    public void Reset() => Volatile.Write(ref _waitingReason, null);
}

/// <summary>
/// Feature-scoped dependencies for the Spell Leveling ServiceCycle contribution. Only the seams the
/// feature owns live here — the live native lifecycle epoch, its action-family lease, the capability
/// the UI reads, and its feature-status output.
/// </summary>
internal sealed class SpellLevelFeatureDependencies
{
    public SpellLevelFeatureDependencies(
        Func<long> readLifecycleEpoch,
        Func<bool> ownsActionFamily,
        SpellLevelCapabilityState capability,
        AutomataFeatureStatusReporter featureStatus)
    {
        ReadLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        OwnsActionFamily = ownsActionFamily ?? throw new ArgumentNullException(nameof(ownsActionFamily));
        Capability = capability ?? throw new ArgumentNullException(nameof(capability));
        FeatureStatus = featureStatus ?? throw new ArgumentNullException(nameof(featureStatus));
    }

    public Func<long> ReadLifecycleEpoch { get; }
    public Func<bool> OwnsActionFamily { get; }
    public SpellLevelCapabilityState Capability { get; }
    public AutomataFeatureStatusReporter FeatureStatus { get; }
}
