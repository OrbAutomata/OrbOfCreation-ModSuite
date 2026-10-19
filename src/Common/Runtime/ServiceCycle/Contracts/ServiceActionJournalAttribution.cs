using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

/// <summary>The compact, stable wire identity of an exact native action target.</summary>
/// <remarks>
/// This is an append-only journal roster. Existing numeric values are never reused or renumbered.
/// </remarks>
public enum ServiceActionNativeTypeId : ushort
{
    NotApplicable = 0,
    StructureSO = 1,
    UpgradeSO = 2,
    PlotNodeActionSO = 3,
    SpellRecipeSO = 4,
    AlchemyRecipeSO = 5,
    ConsumableSO = 6,
    CraftingRecipeSO = 7,
    EquipmentSO = 8,
}

/// <summary>Whether list/view routing applies to an action target and, when it does, was resolved.</summary>
public enum ServiceActionRouteStatus : byte
{
    NotApplicable = 1,
    Resolved = 2,
    Contradictory = 3,
    AttributionFailed = 4,
}

/// <summary>
/// The exact identity written beside one action outcome in the always-on decision journal.
/// </summary>
public readonly struct ServiceActionJournalAttribution
{
    public ServiceActionJournalAttribution(
        Guid candidateId,
        ServiceActionNativeTypeId nativeType,
        Guid listId,
        Guid viewId,
        ServiceActionRouteStatus routeStatus,
        byte actionKind = 0)
    {
        if (nativeType is < ServiceActionNativeTypeId.NotApplicable or > ServiceActionNativeTypeId.EquipmentSO)
            throw new ArgumentOutOfRangeException(nameof(nativeType));
        if (routeStatus is < ServiceActionRouteStatus.NotApplicable or > ServiceActionRouteStatus.AttributionFailed)
            throw new ArgumentOutOfRangeException(nameof(routeStatus));
        if ((nativeType == ServiceActionNativeTypeId.NotApplicable) != (candidateId == Guid.Empty))
            throw new ArgumentException(
                "A native candidate requires both its UUID and exact native type.", nameof(nativeType));
        if (routeStatus == ServiceActionRouteStatus.Resolved)
        {
            if (listId == Guid.Empty || viewId == Guid.Empty)
                throw new ArgumentException("A resolved route requires exact list and view UUIDs.");
        }
        else if (listId != Guid.Empty || viewId != Guid.Empty)
        {
            throw new ArgumentException("Only a resolved route can carry list or view UUIDs.");
        }
        if (candidateId == Guid.Empty && routeStatus is not (
                ServiceActionRouteStatus.NotApplicable or ServiceActionRouteStatus.AttributionFailed))
            throw new ArgumentException(
                "A non-native action cannot carry a native route state.", nameof(routeStatus));
        if (candidateId != Guid.Empty && routeStatus == ServiceActionRouteStatus.AttributionFailed)
            throw new ArgumentException(
                "An attribution failure cannot claim a native candidate.", nameof(routeStatus));
        if (candidateId == Guid.Empty && actionKind != 0)
            throw new ArgumentException("Only a native candidate carries an action kind.", nameof(actionKind));
        CandidateId = candidateId;
        NativeType = nativeType;
        ListId = listId;
        ViewId = viewId;
        RouteStatus = routeStatus;
        ActionKind = actionKind;
    }

    public Guid CandidateId { get; }
    public ServiceActionNativeTypeId NativeType { get; }
    public Guid ListId { get; }
    public Guid ViewId { get; }
    public ServiceActionRouteStatus RouteStatus { get; }

    /// <summary>
    /// Which of the service's actions on the candidate this is, when one candidate takes several
    /// that fail apart; zero for a service whose candidate takes one.
    /// </summary>
    public byte ActionKind { get; }

    public bool IsValid =>
        NativeType is >= ServiceActionNativeTypeId.NotApplicable and <= ServiceActionNativeTypeId.EquipmentSO &&
        RouteStatus is >= ServiceActionRouteStatus.NotApplicable and <= ServiceActionRouteStatus.AttributionFailed &&
        (NativeType == ServiceActionNativeTypeId.NotApplicable) == (CandidateId == Guid.Empty) &&
        (RouteStatus == ServiceActionRouteStatus.Resolved
            ? ListId != Guid.Empty && ViewId != Guid.Empty
            : ListId == Guid.Empty && ViewId == Guid.Empty) &&
        (CandidateId != Guid.Empty || RouteStatus is
            ServiceActionRouteStatus.NotApplicable or ServiceActionRouteStatus.AttributionFailed) &&
        (CandidateId == Guid.Empty || RouteStatus != ServiceActionRouteStatus.AttributionFailed) &&
        (CandidateId != Guid.Empty || ActionKind == 0);

    public static ServiceActionJournalAttribution Native(
        Guid candidateId,
        ServiceActionNativeTypeId nativeType,
        byte actionKind = 0) =>
        new(candidateId, nativeType, Guid.Empty, Guid.Empty, ServiceActionRouteStatus.NotApplicable, actionKind);

    public static ServiceActionJournalAttribution Routed(
        Guid candidateId,
        ServiceActionNativeTypeId nativeType,
        Guid listId,
        Guid viewId) =>
        new(candidateId, nativeType, listId, viewId, ServiceActionRouteStatus.Resolved);

    public static ServiceActionJournalAttribution Publication =>
        new(Guid.Empty, ServiceActionNativeTypeId.NotApplicable, Guid.Empty, Guid.Empty,
            ServiceActionRouteStatus.NotApplicable);

    internal static ServiceActionJournalAttribution Failed =>
        new(Guid.Empty, ServiceActionNativeTypeId.NotApplicable, Guid.Empty, Guid.Empty,
            ServiceActionRouteStatus.AttributionFailed);
}
