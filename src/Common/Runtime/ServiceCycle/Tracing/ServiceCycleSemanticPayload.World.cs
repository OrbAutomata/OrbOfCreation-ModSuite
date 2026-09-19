namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public readonly partial struct ServiceCycleSemanticPayload
{
    /// <summary>
    /// One category of one world-collection pass: which category, what the pass spent on it, how many
    /// entities it produced, and how many categories the pass reported in total.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The record is the same 288 bytes every other event is, so the span rides slots the wire already
    /// reserves rather than growing the format: the category identity is the code, the sampled count is
    /// the action count, and the pass width is the occurrence count. Nothing reads those slots raw —
    /// <see cref="WorldCategory"/>, <see cref="WorldCategorySampled"/> and
    /// <see cref="WorldPassCategories"/> are the only readings, so a span is never mistaken for an
    /// action fact.
    /// </para>
    /// <para>
    /// The pass width is on every span rather than on a header record of its own, because it is what a
    /// reader reconciles a pass against: a pass that says sixty-two and shows sixty is a pass whose
    /// spans were lost, and one that never says so cannot be checked at all.
    /// </para>
    /// </remarks>
    internal static ServiceCycleSemanticPayload WorldCategoryFact(
        int category,
        int sampled,
        int passCategories,
        ulong lifecycle,
        long frameIdentity,
        long timestampTicks,
        long durationTicks) =>
        new(
            ServiceCycleSemanticFields.Code | ServiceCycleSemanticFields.Timestamp |
            ServiceCycleSemanticFields.Duration | ServiceCycleSemanticFields.ActionCount |
            ServiceCycleSemanticFields.OccurrenceCount |
            (lifecycle == 0 ? ServiceCycleSemanticFields.None : ServiceCycleSemanticFields.Lifecycle) |
            FrameField(frameIdentity),
            service: 0,
            lifecycle: lifecycle,
            configuration: 0,
            strategy: 0,
            capture: 0,
            cycle: 0,
            batch: 0,
            action: 0,
            statePublication: 0,
            timestampTicks: timestampTicks,
            durationTicks: durationTicks,
            deadlineTicks: 0,
            frameIdentity: FrameValue(frameIdentity),
            fingerprint: 0,
            code: category,
            disposition: 0,
            actionIndex: 0,
            actionCount: sampled,
            committedCount: 0,
            untouchedSuffixCount: 0,
            occurrenceCount: passCategories,
            nativeCallsAttempted: 0,
            mutationAttempts: 0,
            mutationsCommitted: 0,
            responsesAcquired: 0,
            actionsAttempted: 0,
            capturesAttempted: 0,
            emergencyBatchesRejected: 0,
            lifecycleTransitions: 0,
            responseDurationTicks: 0,
            actionDurationTicks: 0,
            captureDurationTicks: 0,
            totalDurationTicks: 0,
            nativeOutcome: 0);

    /// <summary>
    /// Which collection category a <see cref="ServiceCycleSemanticEventKind.WorldCategoryCollected"/>
    /// span describes. The number is the collector's traversal position plus one; the session roster
    /// says what it is called.
    /// </summary>
    public int WorldCategory => Code;

    /// <summary>Entities this category turned into rows on this pass.</summary>
    public int WorldCategorySampled => ActionCount;

    /// <summary>
    /// How many categories the pass this span belongs to reported. Every span of one pass carries the
    /// same number, so a reader can say a pass is whole without trusting that it is.
    /// </summary>
    public int WorldPassCategories => OccurrenceCount;
}
