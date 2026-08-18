using System;

namespace OrbAutomata;

/// <summary>
/// One press of a discovery screen's Discover button, addressed by the row it would discover.
/// </summary>
/// <remarks>
/// The game's button carries no composition: <c>UIDiscoverablePage.HandleClick</c> reads
/// <c>currentRecipe</c> — the discoverable the page already resolved from the row the player
/// clicked — and calls <c>IDiscoverable.Discover()</c> on it. The selection the page keeps is a
/// scene-local mirror the discover path never reads back, so the target identity is the whole
/// intent.
/// </remarks>
internal readonly struct GenericDiscoveryAction
{
    internal GenericDiscoveryAction(
        Guid targetId,
        string expectedNativeType,
        long lifecycleEpoch)
    {
        if (targetId == Guid.Empty)
            throw new ArgumentException("A discoverable identity is required.", nameof(targetId));
        if (string.IsNullOrWhiteSpace(expectedNativeType))
            throw new ArgumentException("An exact native discoverable type is required.", nameof(expectedNativeType));
        TargetId = targetId;
        ExpectedNativeType = expectedNativeType;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal Guid TargetId { get; }
    internal string ExpectedNativeType { get; }
    internal long LifecycleEpoch { get; }
}
