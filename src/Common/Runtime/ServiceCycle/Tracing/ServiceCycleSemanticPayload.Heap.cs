namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public readonly partial struct ServiceCycleSemanticPayload
{
    /// <summary>
    /// What the managed heap did across one pump: the collection count and the used heap at both
    /// ends, what the frame's captures grew it by, and Unity's own allocation counter for the frame
    /// before, where the player has one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Profiling builds only. The span rides slots the wire already reserves, the way a collection
    /// span does: the collection counts are the code and the action count, the heap readings are the
    /// pump-duration slots, the capture tallies are the committed and untouched counts, and the
    /// frame counter is the fingerprint. Nothing reads those slots raw — the accessors below are the
    /// only readings.
    /// </para>
    /// <para>
    /// A census rides the pump-count, occurrence and duration slots on one sample per census
    /// interval, all three or none.
    /// </para>
    /// <para>
    /// The counts at both ends are what attribute a collection: one that completed between this
    /// pump's ends ran inside the suite's frame work, and one that completed between the previous
    /// pump's end and this one's start ran inside the game's.
    /// </para>
    /// </remarks>
    internal static ServiceCycleSemanticPayload PumpHeapFact(
        long frameIdentity,
        long timestampTicks,
        int collectionsAtStart,
        int collectionsAtEnd,
        long heapAtStart,
        long heapAtEnd,
        long captureHeapGrowth,
        int capturesMeasured,
        int capturesAcrossCollection,
        long? previousFrameAllocated,
        PumpHeapCensus? census = null) =>
        new(
            ServiceCycleSemanticFields.FrameIdentity | ServiceCycleSemanticFields.Timestamp |
            ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.ActionCount |
            ServiceCycleSemanticFields.PumpDurations | ServiceCycleSemanticFields.CommittedCount |
            ServiceCycleSemanticFields.UntouchedSuffixCount |
            (previousFrameAllocated is null
                ? ServiceCycleSemanticFields.None
                : ServiceCycleSemanticFields.Fingerprint) |
            (census is null ? ServiceCycleSemanticFields.None : CensusFields),
            service: 0,
            lifecycle: 0,
            configuration: 0,
            strategy: 0,
            capture: 0,
            cycle: 0,
            batch: 0,
            action: 0,
            statePublication: 0,
            timestampTicks: timestampTicks,
            durationTicks: census?.ReservedHeap ?? 0,
            deadlineTicks: 0,
            frameIdentity: frameIdentity,
            fingerprint: previousFrameAllocated is { } allocated ? (ulong)allocated : 0,
            code: collectionsAtStart,
            disposition: 0,
            actionIndex: 0,
            actionCount: collectionsAtEnd,
            committedCount: capturesMeasured,
            untouchedSuffixCount: capturesAcrossCollection,
            occurrenceCount: census?.LiveWorldSnapshots ?? 0,
            nativeCallsAttempted: 0,
            mutationAttempts: 0,
            mutationsCommitted: 0,
            responsesAcquired: census?.ScriptableObjects ?? 0,
            actionsAttempted: census?.GameObjects ?? 0,
            capturesAttempted: census?.MonoBehaviours ?? 0,
            emergencyBatchesRejected: 0,
            lifecycleTransitions: 0,
            responseDurationTicks: heapAtStart,
            actionDurationTicks: heapAtEnd,
            captureDurationTicks: captureHeapGrowth,
            totalDurationTicks: 0,
            nativeOutcome: 0);

    internal const ServiceCycleSemanticFields CensusFields =
        ServiceCycleSemanticFields.Duration | ServiceCycleSemanticFields.OccurrenceCount |
        ServiceCycleSemanticFields.PumpCounts;

    /// <summary>Collections the runtime had completed when the pump began.</summary>
    public int CollectionsAtPumpStart => Code;

    /// <summary>Collections the runtime had completed when the pump ended.</summary>
    public int CollectionsAtPumpEnd => ActionCount;

    /// <summary>Used managed heap, in bytes, when the pump began.</summary>
    public long HeapAtPumpStart => ResponseDurationTicks;

    /// <summary>Used managed heap, in bytes, when the pump ended.</summary>
    public long HeapAtPumpEnd => ActionDurationTicks;

    /// <summary>
    /// What this frame's captures grew the heap by, summed over the captures no collection
    /// interrupted. A capture a collection did interrupt is counted in
    /// <see cref="CapturesAcrossCollection"/> and contributes nothing here, because its growth is
    /// unknown rather than zero.
    /// </summary>
    public long CaptureHeapGrowth => CaptureDurationTicks;

    /// <summary>Captures whose growth <see cref="CaptureHeapGrowth"/> includes.</summary>
    public int CapturesMeasured => CommittedCount;

    /// <summary>Captures a collection completed inside, whose growth is unknown.</summary>
    public int CapturesAcrossCollection => UntouchedSuffixCount;

    /// <summary>
    /// Unity's "GC Allocated In Frame" for the frame before this pump's, or absent where the player
    /// does not provide the counter.
    /// </summary>
    public long? PreviousFrameAllocated =>
        (Fields & ServiceCycleSemanticFields.Fingerprint) != 0 ? (long)Fingerprint : null;

    /// <summary>What the heap held, on the samples that took a census.</summary>
    public PumpHeapCensus? HeapCensus =>
        (Fields & CensusFields) == CensusFields
            ? new PumpHeapCensus(
                DurationTicks,
                OccurrenceCount,
                ResponsesAcquired,
                ActionsAttempted,
                CapturesAttempted)
            : null;
}

/// <summary>
/// What the heap held at one census: the bytes reserved from the OS, the world snapshots still
/// reachable, and the live host objects that carry managed fields or that an instantiation creates.
/// </summary>
public readonly record struct PumpHeapCensus(
    long ReservedHeap,
    int LiveWorldSnapshots,
    int ScriptableObjects,
    int GameObjects,
    int MonoBehaviours);
