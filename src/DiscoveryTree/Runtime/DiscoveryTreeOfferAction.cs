using System;

namespace OrbAutomata;

internal enum DiscoveryTreeOfferActionKind
{
    Initiate = 0,
    Select = 1,
    Confirm = 2,
    Reroll = 3,
}

/// <summary>
/// Save-independent intent for one synchronous re-drive of the Discovery Tree data pipeline.
/// It carries stable identities and the lifecycle epoch that observed them, never native objects.
/// </summary>
internal readonly struct DiscoveryTreeOfferAction
{
    internal DiscoveryTreeOfferAction(
        DiscoveryTreeOfferActionKind kind,
        Guid treeId,
        Guid offerId,
        long lifecycleEpoch)
    {
        if (treeId == Guid.Empty) throw new ArgumentException("A Discovery Tree identity is required.", nameof(treeId));
        if (kind is DiscoveryTreeOfferActionKind.Select or DiscoveryTreeOfferActionKind.Confirm &&
            offerId == Guid.Empty)
            throw new ArgumentException("Select and confirm require an offered identity.", nameof(offerId));
        Kind = kind;
        TreeId = treeId;
        OfferId = offerId;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal DiscoveryTreeOfferActionKind Kind { get; }
    internal Guid TreeId { get; }
    internal Guid OfferId { get; }
    internal long LifecycleEpoch { get; }
}
