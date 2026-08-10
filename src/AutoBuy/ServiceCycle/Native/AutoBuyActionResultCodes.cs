using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Why an Auto Buy action ended the way it did. Codes are append-only.</summary>
internal static class AutoBuyActionResultCodes
{
    /// <summary>
    /// Another plugin holds the action-family lease for this candidate's kind.
    /// </summary>
    /// <remarks>
    /// A rejection rather than a fault: standing down for a plugin that owns the family is the
    /// arbitration working, not a failure, and the lease can come back at any time.
    /// </remarks>
    public static ServiceActionResultCode ActionFamilyUnavailable => new(2048);

    public static ServiceActionResultCode OwningViewUnavailable => new(2049);
    public static ServiceActionResultCode OwningViewRelationMissing => new(2050);

    /// <summary>
    /// The captured relation for this candidate says the game's own owning-view chain could not be
    /// read when the topology was captured.
    /// </summary>
    /// <remarks>
    /// This used to be five different facts wearing one number. A caller reading "2051" could not
    /// tell an unbound suite from an uncaptured lifecycle from a candidate whose chain the game
    /// refused, and neither could the operator reading the log — the twenty-seven-minute outage cost
    /// a code trace to diagnose because the one refusal it emitted said nothing more than this.
    /// </remarks>
    public static ServiceActionResultCode OwningViewRelationUnreadable => new(2051);
    public static ServiceActionResultCode OwningViewRelationContradictory => new(2053);
    public static ServiceActionResultCode StructureUnavailable => new(2054);
    public static ServiceActionResultCode DestinationCapacityFull => new(2055);
    public static ServiceActionResultCode DestinationCapacityContractUnavailable => new(2056);
    public static ServiceActionResultCode DestinationCapacityIdentityMismatch => new(2057);

    /// <summary>
    /// Verified spend earlier in this Auto Buy batch invalidated the remaining planned resource
    /// margin. The action is skipped before native submission and the next publication replans it.
    /// </summary>
    public static ServiceActionResultCode BatchSpendDrift => new(2058);

    /// <summary>The suite never bound the owning-view topology contract on this build.</summary>
    public static ServiceActionResultCode OwningViewTopologyUnbound => new(2059);

    /// <summary>
    /// The topology is bound but holds nothing this purchase's lifecycle can be admitted against.
    /// </summary>
    public static ServiceActionResultCode OwningViewTopologyUncaptured => new(2060);

    /// <summary>The captured relation carries a status this build does not model.</summary>
    public static ServiceActionResultCode OwningViewRelationStatusUnmodeled => new(2061);

    /// <summary>
    /// The relation resolved, but the game refused to answer whether the owning screen is available
    /// on this fresh action-boundary read.
    /// </summary>
    public static ServiceActionResultCode OwningViewAvailabilityUnreadable => new(2062);

    /// <summary>
    /// An explicit request asked for more levels than the live action queue holds above the reserve.
    /// Only a caller-named amount reaches this: a planned batch is clamped to the room instead,
    /// because a plan that takes what fits is the planner working.
    /// </summary>
    public static ServiceActionResultCode QueueRoomBelowRequest => new(2063);
}
