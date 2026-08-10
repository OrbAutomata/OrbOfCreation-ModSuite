namespace OrbAutomata;

using System;

/// <summary>What one live admission term said, or that it could not be asked at all.</summary>
internal enum AutoBuyAdmissionTerm
{
    Unread = 0,

    /// <summary>The term is satisfied — on its own it would let the purchase through.</summary>
    Passed,

    /// <summary>The term is the reason, or one of the reasons, the game said no.</summary>
    Refused,
}

/// <summary>Policy class for a native refusal, derived from the individual live terms.</summary>
internal enum AutoBuyRefusalClassification
{
    /// <summary>
    /// Only affordability moved after the snapshot. This is expected staleness: skip and re-plan.
    /// </summary>
    AffordabilityChanged = 0,

    /// <summary>
    /// Only the candidate's own availability moved after the snapshot. Also expected staleness:
    /// skip this candidate and re-plan.
    /// </summary>
    /// <remarks>
    /// The world reads the same <c>IsAvailable()</c> the boundary re-asks, several times a second,
    /// and the doctrine treats availability as a mutable native fact for exactly that reason — the
    /// boundary revalidates it because it moves. A lifecycle reset moves a great many of them at
    /// once: after a prestige an entity the player has not re-earned is legitimately shut again,
    /// and a plan made a frame earlier is simply out of date. Filing that as a contradiction stood
    /// the whole service down over one candidate the game was right about.
    /// </remarks>
    AvailabilityChanged,

    /// <summary>A live structural boolean contradicts the snapshot the planner admitted.</summary>
    StructuralMismatch,

    /// <summary>
    /// Every readable term passed (or affordability was unreadable) although the native fold refused.
    /// The world model claims this cannot happen, so it remains an invariant violation.
    /// </summary>
    ImpossibleMismatch,
}

/// <summary>Why live per-resource terms could not be captured after admission refused.</summary>
internal enum AutoBuyLiveCostReadStatus
{
    Unread = 0,
    Complete,
    PurchaseCostUnavailable,
    EntryListUnavailable,
    EntryContractUnavailable,
    ResourceContractUnavailable,
    IdentityContractUnavailable,
    InvalidResourceIdentity,
}

/// <summary>One row from the live native cost list, detached from all game objects.</summary>
internal readonly struct AutoBuyLiveCostRow
{
    public AutoBuyLiveCostRow(
        Guid resourceId,
        bool isBandwidth,
        BigDouble cost,
        BigDouble available)
    {
        ResourceId = resourceId;
        IsBandwidth = isBandwidth;
        Cost = cost;
        Available = available;
    }

    public Guid ResourceId { get; }
    public bool IsBandwidth { get; }
    public BigDouble Cost { get; }
    public BigDouble Available { get; }
}

/// <summary>
/// All rows in one live native cost list, or an explicit reason the complete list was unavailable.
/// </summary>
internal readonly struct AutoBuyLiveCostSnapshot
{
    private readonly AutoBuyLiveCostRow[]? _rows;

    private AutoBuyLiveCostSnapshot(
        AutoBuyLiveCostReadStatus status,
        AutoBuyLiveCostRow[]? rows)
    {
        Status = status;
        _rows = rows;
    }

    public AutoBuyLiveCostReadStatus Status { get; }
    public bool IsComplete => Status == AutoBuyLiveCostReadStatus.Complete;
    public ReadOnlySpan<AutoBuyLiveCostRow> Rows => _rows ?? Array.Empty<AutoBuyLiveCostRow>();

    public static AutoBuyLiveCostSnapshot Complete(AutoBuyLiveCostRow[] rows) =>
        new(AutoBuyLiveCostReadStatus.Complete, rows ?? throw new ArgumentNullException(nameof(rows)));

    public static AutoBuyLiveCostSnapshot Unavailable(AutoBuyLiveCostReadStatus status)
    {
        if (status == AutoBuyLiveCostReadStatus.Complete)
            throw new ArgumentOutOfRangeException(nameof(status));
        return new AutoBuyLiveCostSnapshot(status, null);
    }
}

/// <summary>
/// The game's <c>CanPurchase()</c> taken apart, read term by term immediately after it refused a
/// purchase the worker had planned.
/// </summary>
/// <remarks>
/// <para>
/// <c>CanPurchase()</c> answers with a single bool, and its folded terms differ by type. A refusal
/// says nothing about which included condition was the one that bit. A planner that disagrees with
/// the game has a bug in it, and "the game said no" is not a bug report — so the terms the game
/// exposes individually are asked individually, on the cold path only, after the fold has already
/// refused. Owning-view and structure availability are mandatory gates before this path and are not
/// inferred to be part of either native fold.
/// </para>
/// <para>
/// Some of what <c>CanPurchase()</c> folds in has no parameterless reader: the per-level
/// prerequisites are checked against a level the caller supplies. When every applicable readable
/// term passes and the fold still refused, that is what is left, and the diagnosis says so by
/// elimination rather than pretending to have read it. Since W58 the planner models those conditions itself and refuses to
/// plan a candidate whose next level is gated, so reaching this clause now means the suite's model
/// and the game disagree — which is the strongest signal this boundary can give and is worth saying
/// in those words.
/// </para>
/// </remarks>
internal readonly struct AutoBuyAdmissionDiagnosis
{
    /// <summary>What the boundary says when every term it could read was satisfied.</summary>
    internal const string RefusedByAnUnreadableTerm =
        "refused by an unreadable admission term (per-level prerequisites by elimination, " +
        "which the planner modelled as met)";

    /// <summary>What the boundary says when not one term could be read.</summary>
    internal const string RefusedWithoutDiagnosis = "native admission refused";

    public AutoBuyAdmissionDiagnosis(
        AutoBuyAdmissionTerm isAvailable,
        AutoBuyAdmissionTerm isMaxLevel,
        AutoBuyAdmissionTerm isMaxQueuedLevel,
        AutoBuyAdmissionTerm hasEnough)
        : this(
            isAvailable,
            isMaxLevel,
            isMaxQueuedLevel,
            hasEnough,
            AutoBuyLiveCostSnapshot.Unavailable(
                AutoBuyLiveCostReadStatus.PurchaseCostUnavailable))
    {
    }

    public AutoBuyAdmissionDiagnosis(
        AutoBuyAdmissionTerm isAvailable,
        AutoBuyAdmissionTerm isMaxLevel,
        AutoBuyAdmissionTerm isMaxQueuedLevel,
        AutoBuyAdmissionTerm hasEnough,
        in AutoBuyLiveCostSnapshot liveCosts)
    {
        IsAvailable = isAvailable;
        IsMaxLevel = isMaxLevel;
        IsMaxQueuedLevel = isMaxQueuedLevel;
        HasEnough = hasEnough;
        LiveCosts = liveCosts;
    }

    /// <summary>The candidate is unlocked and visible. <c>Refused</c> means it is not.</summary>
    public AutoBuyAdmissionTerm IsAvailable { get; }

    /// <summary>
    /// The candidate has levels left. <c>Refused</c> means it is finished; absent on a kind with no
    /// bounded level at all, which is why it is read rather than derived.
    /// </summary>
    public AutoBuyAdmissionTerm IsMaxLevel { get; }

    /// <summary>Room remains once what is already queued is counted.</summary>
    public AutoBuyAdmissionTerm IsMaxQueuedLevel { get; }

    /// <summary>The game's own verdict on the price, from the cost list it builds for this level.</summary>
    public AutoBuyAdmissionTerm HasEnough { get; }

    /// <summary>Every live price row and its live spendable amount, when the cost list was readable.</summary>
    public AutoBuyLiveCostSnapshot LiveCosts { get; }

    /// <summary>Whether any term at all could be read.</summary>
    public bool WasAsked =>
        IsAvailable != AutoBuyAdmissionTerm.Unread ||
        IsMaxLevel != AutoBuyAdmissionTerm.Unread ||
        IsMaxQueuedLevel != AutoBuyAdmissionTerm.Unread ||
        HasEnough != AutoBuyAdmissionTerm.Unread;

    /// <summary>The first term that refused, or an empty string when none of the readable ones did.</summary>
    public string RefusingTerm
    {
        get
        {
            if (IsAvailable == AutoBuyAdmissionTerm.Refused) return "IsAvailable()";
            if (IsMaxLevel == AutoBuyAdmissionTerm.Refused) return "IsMaxLevel()";
            if (IsMaxQueuedLevel == AutoBuyAdmissionTerm.Refused) return "IsMaxQueuedLevel()";
            if (HasEnough == AutoBuyAdmissionTerm.Refused) return "GetPurchaseCost().HasEnough()";
            return string.Empty;
        }
    }

    /// <summary>
    /// The policy boundary for this refusal. Structural contradictions take precedence when several
    /// native terms refuse at once; the two facts the boundary exists to revalidate — the price and
    /// the candidate's own availability — are expected staleness only when one of them is the sole
    /// readable contradiction.
    /// </summary>
    public AutoBuyRefusalClassification Classification
    {
        get
        {
            if (IsMaxLevel == AutoBuyAdmissionTerm.Refused ||
                IsMaxQueuedLevel == AutoBuyAdmissionTerm.Refused)
            {
                return AutoBuyRefusalClassification.StructuralMismatch;
            }

            // Availability alone. The level terms above are the candidate's own progress, which
            // only the suite moves and so cannot disagree behind its back; availability is a gate
            // the game opens and shuts on its own, most of all across a lifecycle reset.
            if (IsAvailable == AutoBuyAdmissionTerm.Refused)
                return AutoBuyRefusalClassification.AvailabilityChanged;

            return HasEnough == AutoBuyAdmissionTerm.Refused
                ? AutoBuyRefusalClassification.AffordabilityChanged
                : AutoBuyRefusalClassification.ImpossibleMismatch;
        }
    }

    /// <summary>One clause naming why the game refused, for the log line and the bundle alike.</summary>
    public string Describe()
    {
        if (!WasAsked) return RefusedWithoutDiagnosis;
        var refusing = RefusingTerm;
        return refusing.Length == 0 ? RefusedByAnUnreadableTerm : "refused by " + refusing;
    }
}
