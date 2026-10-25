using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// The Auto Buy worker's per-lifecycle state: the lifecycle it was minted under, the last cycle's
/// decision metrics, and the scratch its evaluation fills.
/// </summary>
/// <remarks>
/// <para>
/// No control state. The worker is a per-cycle planner: it projects the pinned world and produces a
/// batch of purchase decisions purely as a function of that projection plus the pinned
/// configuration. There is no ranked-pass cursor, no group/batch counter, no retry/backoff
/// dictionary — pacing and fairness come from one-batch-per-cycle emission and fresh world
/// publications, so the worker never re-plans stale state.
/// </para>
/// <para>
/// The scratch carries nothing between cycles: every evaluation overwrites it before reading it. It
/// lives here because the row arrays underneath it are worth reusing and the state is the one thing
/// the worker owns for a whole lifecycle.
/// </para>
/// </remarks>
internal struct AutoBuyCycleState
{
    internal AutoBuyCycleFrame Scratch;

    private AutoBuyCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        Decision = default;
        Scratch = default;
    }

    public LifecycleGeneration Lifecycle { get; private set; }
    public AutoBuyDecisionMetrics Decision { get; private set; }

    public static AutoBuyCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);

    public void RecordDecision(AutoBuyDecisionMetrics decision) => Decision = decision;
}

/// <summary>
/// Why one candidate did not reach the plan, one member per term of the admission chain in the
/// order the evaluator tests them.
/// </summary>
/// <remarks>
/// Auto Buy reported 409 candidates and 0 eligible for four hundred consecutive cycles and said
/// nothing further, so every one of the ten terms that could have produced that zero had to be
/// eliminated by reading the code. One counter per term makes the same question a glance.
/// </remarks>
internal enum AutoBuyExclusion
{
    /// <summary>Not excluded — the candidate reached the ranked plan.</summary>
    None = 0,

    /// <summary>Its family is switched off: IncludeStructures or IncludeUpgrades.</summary>
    KindNotSelected = 1,

    /// <summary>The game's own IsAvailable() says its prerequisites are unmet.</summary>
    Unavailable = 2,

    /// <summary>The conditions on the level this purchase would reach are unmet or unevaluable.</summary>
    RequirementsUnmet = 3,

    /// <summary>Finite levels, and done or fully queued to the cap.</summary>
    Terminal = 4,

    /// <summary>Priced, and the holdings do not cover the price plus the reserve floor.</summary>
    Unaffordable = 5,

    /// <summary>
    /// Every cost row read as zero or negative, so no comparison was possible. Distinct from
    /// unaffordable on purpose: one is a fact about the player's holdings and the other is a fact
    /// about the snapshot.
    /// </summary>
    Unpriceable = 6,

    /// <summary>The exact owning view exists, but its progression prerequisites are locked.</summary>
    OwningViewUnavailable = 7,

    /// <summary>No candidate-to-list/view relation was authored or published.</summary>
    OwningViewRelationMissing = 8,

    /// <summary>The candidate-to-list/view graph could not be read completely.</summary>
    OwningViewRelationUnreadable = 9,

    /// <summary>The route contradicted exact candidate, category, list, or view identity.</summary>
    OwningViewRelationContradictory = 10,

    /// <summary>
    /// The list its reward is added to has no empty spot: every World > Aspects pedestal is taken.
    /// </summary>
    DestinationFull = 11,

    /// <summary>The game refused it lately and its retry is not due yet.</summary>
    BackingOff = 12,
}

/// <summary>How many candidates each exclusion term accounted for in one cycle.</summary>
/// <remarks>
/// A fixed-width value rather than a dictionary, because it rides in a struct the worker hands back
/// once per cycle and the framework's validator forbids a worker definition holding collections.
/// </remarks>
internal readonly struct AutoBuyExclusionHistogram
{
    internal AutoBuyExclusionHistogram(
        int kindNotSelected,
        int unavailable,
        int requirementsUnmet,
        int terminal,
        int unaffordable,
        int unpriceable,
        int owningViewUnavailable,
        int owningViewRelationMissing,
        int owningViewRelationUnreadable,
        int owningViewRelationContradictory,
        int destinationFull,
        int backingOff)
    {
        KindNotSelected = kindNotSelected;
        Unavailable = unavailable;
        RequirementsUnmet = requirementsUnmet;
        Terminal = terminal;
        Unaffordable = unaffordable;
        Unpriceable = unpriceable;
        OwningViewUnavailable = owningViewUnavailable;
        OwningViewRelationMissing = owningViewRelationMissing;
        OwningViewRelationUnreadable = owningViewRelationUnreadable;
        OwningViewRelationContradictory = owningViewRelationContradictory;
        DestinationFull = destinationFull;
        BackingOff = backingOff;
    }

    public int KindNotSelected { get; }
    public int Unavailable { get; }
    public int RequirementsUnmet { get; }
    public int Terminal { get; }
    public int Unaffordable { get; }
    public int Unpriceable { get; }
    public int OwningViewUnavailable { get; }
    public int OwningViewRelationMissing { get; }
    public int OwningViewRelationUnreadable { get; }
    public int OwningViewRelationContradictory { get; }
    public int DestinationFull { get; }
    public int BackingOff { get; }

    public int Total => KindNotSelected + Unavailable +
        RequirementsUnmet + Terminal + Unaffordable + Unpriceable +
        OwningViewUnavailable + OwningViewRelationMissing +
        OwningViewRelationUnreadable +
        OwningViewRelationContradictory + DestinationFull + BackingOff;

    public int For(AutoBuyExclusion exclusion) => exclusion switch
    {
        AutoBuyExclusion.KindNotSelected => KindNotSelected,
        AutoBuyExclusion.Unavailable => Unavailable,
        AutoBuyExclusion.RequirementsUnmet => RequirementsUnmet,
        AutoBuyExclusion.Terminal => Terminal,
        AutoBuyExclusion.Unaffordable => Unaffordable,
        AutoBuyExclusion.Unpriceable => Unpriceable,
        AutoBuyExclusion.OwningViewUnavailable => OwningViewUnavailable,
        AutoBuyExclusion.OwningViewRelationMissing => OwningViewRelationMissing,
        AutoBuyExclusion.OwningViewRelationUnreadable => OwningViewRelationUnreadable,
        AutoBuyExclusion.OwningViewRelationContradictory => OwningViewRelationContradictory,
        AutoBuyExclusion.DestinationFull => DestinationFull,
        AutoBuyExclusion.BackingOff => BackingOff,
        _ => 0,
    };
}

internal readonly struct AutoBuyDecisionMetrics
{
    internal AutoBuyDecisionMetrics(
        int capturedStructures,
        int capturedUpgrades,
        int eligibleCandidates,
        int plannedActions,
        int requestedLevels,
        in AutoBuyExclusionHistogram exclusions = default)
    {
        if (capturedStructures < 0) throw new System.ArgumentOutOfRangeException(nameof(capturedStructures));
        if (capturedUpgrades < 0) throw new System.ArgumentOutOfRangeException(nameof(capturedUpgrades));
        if (eligibleCandidates < 0) throw new System.ArgumentOutOfRangeException(nameof(eligibleCandidates));
        if (plannedActions < 0) throw new System.ArgumentOutOfRangeException(nameof(plannedActions));
        if (requestedLevels < 0) throw new System.ArgumentOutOfRangeException(nameof(requestedLevels));
        CapturedStructures = capturedStructures;
        CapturedUpgrades = capturedUpgrades;
        EligibleCandidates = eligibleCandidates;
        PlannedActions = plannedActions;
        RequestedLevels = requestedLevels;
        Exclusions = exclusions;
    }

    public int CapturedStructures { get; }
    public int CapturedUpgrades { get; }
    public int CapturedCandidates => checked(CapturedStructures + CapturedUpgrades);
    public int EligibleCandidates { get; }
    public int PlannedActions { get; }
    public int RequestedLevels { get; }

    /// <summary>Why the candidates that did not reach the plan did not reach it.</summary>
    public AutoBuyExclusionHistogram Exclusions { get; }
}
