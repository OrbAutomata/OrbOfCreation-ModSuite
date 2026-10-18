namespace OrbAutomata;

internal enum AutoHarvestEvidenceState
{
    Unknown,
    Rejected,
    Verified,
}

internal enum AutoHarvestActionSafetyState
{
    Unknown,
    Destructive,
    ResourceDrain,
    UnsafeCompletionEffects,
    NativePhaseCyclePreserving,
}

internal enum AutoHarvestRejectionReason
{
    None,
    IdentityUnverified,
    UnsupportedPair,
    NotSelected,
    PlotVisibilityUnknown,
    PlotNotVisible,
    ActionAvailabilityUnknown,
    ActionUnavailable,
    PrerequisitesUnknown,

    /// <summary>
    /// The game has not confirmed the action's prerequisites. Not a finding that they are unmet: the
    /// native latch this comes from is set when the game passes a check and never says whether it has
    /// run one, so its unset state is the absence of an answer. Named for what is known, because the
    /// health kind and the sentence a player reads are derived from it.
    /// </summary>
    PrerequisitesNotConfirmed,
    ReadinessUnknown,
    NotReady,
    PreservationUnknown,
    DestructiveAction,
    ResourceDrainPresent,
    UnsafeCompletionEffects,
    AlreadyQueuedOrRunning,
    NoActionSlot,
}

internal readonly struct AutoHarvestPairDecision
{
    public AutoHarvestPairDecision(bool shouldSubmit, AutoHarvestRejectionReason rejectionReason)
    {
        ShouldSubmit = shouldSubmit;
        RejectionReason = rejectionReason;
    }

    public bool ShouldSubmit { get; }
    public AutoHarvestRejectionReason RejectionReason { get; }
}
