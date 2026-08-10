namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

// Values are stable within a build, not across builds: the only reader is
// tools/OrbModding.ServiceCycleTrace, compiled from this tree, and full-trace artifacts are
// disposable per docs/north-star.md. Renumber freely when it buys clarity; the format version in
// ServiceCycleSemanticEventV7Codec is the compatibility gate.
public enum ServiceCycleSemanticEventKind
{
    ConfigurationPublished = 1,
    StrategyPublished = 2,
    LifecycleRequested = 3,
    LifecycleActivated = 4,
    LifecycleRetired = 5,
    EmergencyEntered = 6,
    EmergencyCleared = 7,
    CycleQueued = 8,
    CycleStarted = 9,
    CycleCompleted = 10,
    CycleOrphaned = 11,
    CycleFaulted = 12,
    CaptureStarted = 13,
    CaptureCompleted = 14,
    CaptureUnavailable = 15,
    CaptureFaulted = 16,
    EvaluationStarted = 17,
    EvaluationCompleted = 18,
    EvaluationFaulted = 19,
    StatePublished = 20,
    BatchPublished = 21,
    ActionAttempted = 22,
    ActionCommitted = 23,
    ActionRejected = 24,
    ActionFaulted = 25,
    BatchCompleted = 26,
    BatchAborted = 27,
    BatchOrphaned = 28,
    RetryScheduled = 29,
    FaultObserved = 30,
    FaultRecovered = 31,
    PumpCompleted = 32,
    EvaluationDeferred = 33,
    LifecycleConstructionDeferred = 34,
    ProjectionFaulted = 35,
    StartAttempted = 36,
    StartDeferred = 37,
    StartFaulted = 38,
    StartReady = 39,
    ActionSkipped = 40,

    /// <summary>
    /// One world-collection category, as one pass read it. Appended rather than folded into the
    /// capture facts: a capture is one fact per service cycle, and collection is the one capture whose
    /// cost is a distribution over sixty-odd readers rather than a single number.
    /// </summary>
    WorldCategoryCollected = 41,
}

[System.Flags]
public enum ServiceCycleSemanticFields : ulong
{
    None = 0,
    Service = 1UL << 0,
    Lifecycle = 1UL << 1,
    Configuration = 1UL << 2,
    Strategy = 1UL << 3,
    Capture = 1UL << 4,
    Cycle = 1UL << 5,
    Batch = 1UL << 6,
    Action = 1UL << 7,
    StatePublication = 1UL << 8,
    Timestamp = 1UL << 9,
    Duration = 1UL << 10,
    Deadline = 1UL << 11,
    FrameIdentity = 1UL << 12,
    Fingerprint = 1UL << 13,
    Code = 1UL << 14,
    Disposition = 1UL << 15,
    ActionIndex = 1UL << 16,
    ActionCount = 1UL << 17,
    CommittedCount = 1UL << 18,
    UntouchedSuffixCount = 1UL << 19,
    OccurrenceCount = 1UL << 20,
    NativeCallTotals = 1UL << 21,
    PumpCounts = 1UL << 22,
    PumpDurations = 1UL << 23,
    NativeMutationOutcome = 1UL << 24,

    // Bit 25 was PublishedCount. It is permanently burned: never reuse or renumber this wire bit.

    /// <summary>
    /// Which reading of the game a cycle ran against. The fourth pinned generation, and the last one
    /// the wire was missing: without it a decision can be read but not answered for, because nothing
    /// says which collection it acted on.
    /// </summary>
    World = 1UL << 26,
}
