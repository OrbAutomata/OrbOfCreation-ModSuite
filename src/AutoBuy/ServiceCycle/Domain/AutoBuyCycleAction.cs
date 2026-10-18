using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// What the planner believed about a candidate when it decided to buy it, carried to the boundary so
/// a native refusal can be read against the snapshot that produced it.
/// </summary>
/// <remarks>
/// <para>
/// A native refusal of a planned purchase is either expected snapshot staleness or a structural
/// contradiction. Distinguishing them needs both halves — what the plan believed and what the game
/// says — and only one of them survives the seam. The frame is gone by the time an action runs, so
/// the belief travels by value with the action, in the leaf shapes a published action may hold.
/// </para>
/// <para>
/// Only the binding resource is carried, not every cost row: it is the one that set
/// <see cref="CostRatio"/> for the exact group this action requests. A preferred one-level action
/// retains its original admission belief byte for byte; a multi-level or reduced action carries the
/// exact group comparison against the remaining batch ledger. The two counts beside it say how many
/// rows there were and how many of them were priced at all. Nought priced rows out of several is the
/// shape of the game's uncooked boot prices, where every structure reads as free.
/// </para>
/// </remarks>
internal readonly struct AutoBuyPlanBelief
{
    public AutoBuyPlanBelief(
        bool isAvailable,
        bool hasFiniteLevels,
        bool isMaxLevel,
        bool isMaxQueuedLevel,
        int currentLevel,
        int queuedLevels,
        int costResourceCount,
        int pricedResourceCount,
        double costRatio,
        Guid bindingResourceId,
        bool bindingIsBandwidth,
        BigDouble bindingCost,
        BigDouble bindingAvailable,
        BigDouble bindingReserveFloor)
    {
        IsAvailable = isAvailable;
        HasFiniteLevels = hasFiniteLevels;
        IsMaxLevel = isMaxLevel;
        IsMaxQueuedLevel = isMaxQueuedLevel;
        CurrentLevel = currentLevel;
        QueuedLevels = queuedLevels;
        CostResourceCount = costResourceCount;
        PricedResourceCount = pricedResourceCount;
        CostRatio = costRatio;
        BindingResourceId = bindingResourceId;
        BindingIsBandwidth = bindingIsBandwidth;
        BindingCost = bindingCost;
        BindingAvailable = bindingAvailable;
        BindingReserveFloor = bindingReserveFloor;
    }

    /// <summary>The candidate's published admission facts, which the live reads are compared against.</summary>
    public bool IsAvailable { get; }
    public bool HasFiniteLevels { get; }
    public bool IsMaxLevel { get; }
    public bool IsMaxQueuedLevel { get; }
    public int CurrentLevel { get; }
    public int QueuedLevels { get; }

    /// <summary>Distinct resources the published price named, and how many were priced above nought.</summary>
    public int CostResourceCount { get; }
    public int PricedResourceCount { get; }

    /// <summary>The chosen group's worst exact-cost-to-remaining ratio across those resources.</summary>
    public double CostRatio { get; }

    /// <summary>The resource that produced that ratio, and what the plan compared for it.</summary>
    public Guid BindingResourceId { get; }
    public bool BindingIsBandwidth { get; }
    public BigDouble BindingCost { get; }

    /// <summary>
    /// Spendable holdings for an ordinary resource, or room below the ceiling for a bandwidth one,
    /// used by the exact group comparison the action carries.
    /// </summary>
    public BigDouble BindingAvailable { get; }
    public BigDouble BindingReserveFloor { get; }
}

/// <summary>
/// One resource term that admitted a planned action against the worker's cumulative batch ledger.
/// It travels with the action so verified earlier spend can be reconciled before native dispatch.
/// </summary>
internal readonly struct AutoBuyPlannedSpend
{
    public AutoBuyPlannedSpend(
        Guid resourceId,
        BigDouble cost,
        BigDouble remainingBeforeSpend,
        BigDouble reserveFloor)
    {
        if (resourceId == Guid.Empty) throw new ArgumentException("A planned spend needs a resource UUID.", nameof(resourceId));
        ResourceId = resourceId;
        Cost = cost;
        RemainingBeforeSpend = remainingBeforeSpend;
        ReserveFloor = reserveFloor;
    }

    public Guid ResourceId { get; }
    public BigDouble Cost { get; }
    public BigDouble RemainingBeforeSpend { get; }
    public BigDouble ReserveFloor { get; }
}

/// <summary>
/// One planned purchase for a specific candidate. Unlike Auto Harvest's family-only action, an Auto
/// Buy target must be a specific candidate, so the action carries the stable UUID plus the exact
/// family (Structure/Upgrade) and a <see cref="Count"/> of levels to request. The action adapter
/// re-resolves and revalidates this identity natively, then submits a single native purchase call
/// for <see cref="Count"/> levels; buying fewer than requested (but at least one) is a success.
/// </summary>
internal readonly struct AutoBuyCycleAction
{
    public AutoBuyCycleAction(
        AutoBuyCandidateKind kind,
        Guid uuid,
        long collectedAtEpoch,
        int count = 1)
        : this(kind, uuid, collectedAtEpoch, count, default)
    {
    }

    public AutoBuyCycleAction(
        AutoBuyCandidateKind kind,
        Guid uuid,
        long collectedAtEpoch,
        int count,
        AutoBuyPlanBelief belief,
        Guid owningListId = default,
        Guid owningViewId = default,
        PublicationTable<AutoBuyPlannedSpend>? plannedSpend = null)
    {
        if (kind is not (AutoBuyCandidateKind.Structure or AutoBuyCandidateKind.Upgrade))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (uuid == Guid.Empty)
            throw new ArgumentException("An Auto Buy action requires a non-empty candidate UUID.", nameof(uuid));
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), "An Auto Buy action must request at least one level.");
        Kind = kind;
        Uuid = uuid;
        CollectedAtEpoch = collectedAtEpoch;
        Count = count;
        Belief = belief;
        OwningListId = owningListId;
        OwningViewId = owningViewId;
        PlannedSpend = plannedSpend ?? PublicationTable<AutoBuyPlannedSpend>.Empty;
    }

    public AutoBuyCandidateKind Kind { get; }
    public Guid Uuid { get; }

    /// <summary>What the planner believed about this candidate when it chose it.</summary>
    public AutoBuyPlanBelief Belief { get; }

    /// <summary>
    /// The lifecycle epoch the world this purchase was planned from was collected under.
    /// </summary>
    /// <remarks>
    /// Carried by value with the action rather than looked up at the boundary, because by then the
    /// snapshot it names is no longer reachable — an ordinary service sees no world on the main
    /// thread. The adapter compares it against a live reading of the game's own epoch; a plan made
    /// against another run of the game is refused there, penalty-free. An unstamped world carries
    /// zero, which no live reading ever matches, so a purchase planned against a world nobody
    /// collected cannot be submitted.
    /// </remarks>
    public long CollectedAtEpoch { get; }

    /// <summary>The deterministic authored list/view route that admitted this candidate.</summary>
    public Guid OwningListId { get; }
    public Guid OwningViewId { get; }

    /// <summary>The exact resource terms whose reconciled margin decides whether this action dispatches.</summary>
    public PublicationTable<AutoBuyPlannedSpend> PlannedSpend { get; }

    /// <summary>
    /// How many levels this action should request for its candidate — the live bulk or multiplier
    /// count when a bulk grouping mode is on, otherwise 1. It is one action but <em>not</em> one
    /// queue slot: the game stacks one queue entry per committed level. An upgrade takes them in a
    /// single native call under a pinned multiplier; a structure takes one level per call and so
    /// takes several. Committing at least one level (even if fewer than <see cref="Count"/>) is a
    /// success. Defaults to 1 so a plain single-level purchase is unchanged.
    ///
    /// This is a request, not a guarantee. The action adapter clamps it to the live queue room above
    /// the operator's reserve before submitting, because the game's own purchase loop does not consult
    /// the queue room at all.
    /// </summary>
    public int Count { get; }
}
