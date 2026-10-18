using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal static class AutoHarvestPolicy
{
    public static AutoHarvestPairDecision EvaluatePair(
        AutoHarvestPair pair,
        bool selected,
        in AutoHarvestPairFacts facts)
    {
        var rejection = AutoHarvestRejectionReason.None;
        if (facts.Identity != AutoHarvestEvidenceState.Verified)
            rejection = AutoHarvestRejectionReason.IdentityUnverified;
        else if (pair is not AutoHarvestPair.FruitTree and not AutoHarvestPair.TreasureTree)
            rejection = AutoHarvestRejectionReason.UnsupportedPair;
        else if (!selected)
            rejection = AutoHarvestRejectionReason.NotSelected;
        else
            rejection = EvaluateFacts(
                facts.PlotVisibility,
                facts.ActionAvailability,
                facts.Prerequisites,
                facts.Readiness);
        return new AutoHarvestPairDecision(
            rejection == AutoHarvestRejectionReason.None,
            rejection);
    }

    /// <summary>
    /// The planned decision, taken again at the action boundary against the live action queue.
    /// </summary>
    /// <remarks>
    /// The pair facts and the safety verdict are the ones the action carries, so a plan already known
    /// to be inadmissible stops here. The mutation adapter separately re-reads the mutable native
    /// player-click gates after this policy judgment and before <c>AddInstance</c>. Safety and the two
    /// queue facts are only ever evaluated here. The queue describes a resource every service
    /// competes for and that this service's own actions consume, so an answer read while deciding is
    /// stale by the time it would be acted on. Safety is a structural audit of the build's authored
    /// content — what stands between the suite and a game whose assets changed under an assembly the
    /// hash gate still accepts — and it is drawn on the worker from collected facts. Neither means
    /// anything anywhere but here.
    /// </remarks>
    public static AutoHarvestPairDecision EvaluateSubmission(
        AutoHarvestPair pair,
        in AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety,
        in AutoHarvestSubmissionState queue)
    {
        var decision = EvaluatePair(pair, selected: true, facts);
        if (!decision.ShouldSubmit) return decision;

        var rejection = EvaluateSafety(safety);
        if (rejection != AutoHarvestRejectionReason.None)
            return new AutoHarvestPairDecision(false, rejection);

        if (AutoHarvestWorldFacts.IsQueuedOrRunning(queue))
            return new AutoHarvestPairDecision(false, AutoHarvestRejectionReason.AlreadyQueuedOrRunning);
        if (!AutoHarvestWorldFacts.HasActionSlot(queue))
            return new AutoHarvestPairDecision(false, AutoHarvestRejectionReason.NoActionSlot);
        return new AutoHarvestPairDecision(true, AutoHarvestRejectionReason.None);
    }

    private static AutoHarvestRejectionReason EvaluateFacts(
        AutoHarvestEvidenceState plotVisibility,
        AutoHarvestEvidenceState actionAvailability,
        PlotActionPrerequisiteEvidence prerequisites,
        AutoHarvestEvidenceState readiness)
    {
        var rejection = EvaluateEvidence(
            plotVisibility,
            AutoHarvestRejectionReason.PlotVisibilityUnknown,
            AutoHarvestRejectionReason.PlotNotVisible);
        if (rejection != AutoHarvestRejectionReason.None) return rejection;
        rejection = EvaluateEvidence(
            actionAvailability,
            AutoHarvestRejectionReason.ActionAvailabilityUnknown,
            AutoHarvestRejectionReason.ActionUnavailable);
        if (rejection != AutoHarvestRejectionReason.None) return rejection;
        rejection = EvaluatePrerequisites(prerequisites);
        if (rejection != AutoHarvestRejectionReason.None) return rejection;
        return EvaluateEvidence(
            readiness,
            AutoHarvestRejectionReason.ReadinessUnknown,
            AutoHarvestRejectionReason.NotReady);
    }

    private static AutoHarvestRejectionReason EvaluatePrerequisites(
        PlotActionPrerequisiteEvidence prerequisites) => prerequisites switch
    {
        PlotActionPrerequisiteEvidence.NativeLatchedTrue or
        PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation =>
            AutoHarvestRejectionReason.None,
        _ => AutoHarvestRejectionReason.PrerequisitesUnknown,
    };

    private static AutoHarvestRejectionReason EvaluateSafety(AutoHarvestActionSafetyState safety) => safety switch
    {
        AutoHarvestActionSafetyState.NativePhaseCyclePreserving => AutoHarvestRejectionReason.None,
        AutoHarvestActionSafetyState.Destructive => AutoHarvestRejectionReason.DestructiveAction,
        AutoHarvestActionSafetyState.ResourceDrain => AutoHarvestRejectionReason.ResourceDrainPresent,
        AutoHarvestActionSafetyState.UnsafeCompletionEffects => AutoHarvestRejectionReason.UnsafeCompletionEffects,
        _ => AutoHarvestRejectionReason.PreservationUnknown,
    };

    private static AutoHarvestRejectionReason EvaluateEvidence(
        AutoHarvestEvidenceState state,
        AutoHarvestRejectionReason unknownReason,
        AutoHarvestRejectionReason rejectedReason) => state switch
    {
        AutoHarvestEvidenceState.Verified => AutoHarvestRejectionReason.None,
        AutoHarvestEvidenceState.Rejected => rejectedReason,
        _ => unknownReason,
    };
}
