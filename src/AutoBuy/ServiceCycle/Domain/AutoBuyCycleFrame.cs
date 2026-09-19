using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>The owning-view term the planner carries for one candidate.</summary>
internal enum AutoBuyOwningViewStatus
{
    Available = 0,
    Unavailable,
    RelationMissing,
    RelationUnreadable,
    RelationContradictory,
}

/// <summary>
/// One global row for an Auto Buy cycle frame: the once-per-frame facts the worker needs for
/// grouping and lifecycle gating.
/// </summary>
/// <remarks>
/// Three things used to be here and are not any more. The action queue's capacity and free room
/// describe a resource every service competes for and that this service's own actions consume, so an
/// answer carried through a decision is stale before it is used (W39). Which action families this
/// instance owns is a lease another plugin can take mid-cycle, and is checked before submitting
/// (W46). The emergency signal was the configuration's own <c>EmergencyDisable</c> read a second way,
/// and the operational check beside it already asked.
/// </remarks>
internal readonly struct AutoBuyGlobalRow
{
    public AutoBuyGlobalRow(
        int bulkDevelopment,
        long collectedAtEpoch)
        : this(bulkDevelopment, collectedAtEpoch, default)
    {
    }

    public AutoBuyGlobalRow(
        int bulkDevelopment,
        long collectedAtEpoch,
        MonotonicTimestamp collectedAt)
    {
        BulkDevelopment = bulkDevelopment;
        CollectedAtEpoch = collectedAtEpoch;
        CollectedAt = collectedAt;
    }

    public int BulkDevelopment { get; }

    /// <summary>
    /// The lifecycle epoch the world this frame was projected from was collected under.
    /// </summary>
    /// <remarks>
    /// This was the runner's own lifecycle generation, which answers a slightly different question and
    /// which nothing read. A runner's generation is frozen when the runner is built and advances only
    /// when the host is told to replace it; the snapshot's epoch is stamped when the game was actually
    /// read, so it is the one that describes the plan. A plan is about the world it was made from, so
    /// that is the epoch it carries to the boundary — where it is compared against a live reading,
    /// which is the comparison that closes the game-reload race.
    /// </remarks>
    public long CollectedAtEpoch { get; }

    /// <summary>When the world readings were captured, on the runtime's monotonic clock.</summary>
    public MonotonicTimestamp CollectedAt { get; }
}

/// <summary>
/// One candidate row. Identity is a stable UUID plus the exact native type name
/// (Structure/Upgrade); levels and availability come from the world snapshot. Cost rows live in the
/// frame's shared cost array, referenced here by
/// <see cref="CostRowStart"/>/<see cref="CostRowCount"/>.
/// </summary>
/// <remarks>
/// The game's <c>CanPurchase()</c> verdict used to be here. Its exact fold is type-specific:
/// structures ask only per-level requirements and action-load admission, while upgrades also ask
/// max-queued level, affordability, own availability, and queued-level requirements. Every term is
/// live, so the purchase adapter asks the fold immediately before mutating. See W39.
/// </remarks>
internal readonly struct AutoBuyCandidateRow
{
    public AutoBuyCandidateRow(
        AutoBuyCandidateKind kind,
        Guid uuid,
        bool isAvailable,
        int currentLevel,
        int queuedLevels,
        bool hasFiniteLevels,
        bool isMaxLevel,
        bool isMaxQueuedLevel,
        bool meetsNextLevelRequirements,
        int costRowStart,
        int costRowCount,
        Guid owningListId = default,
        Guid owningViewId = default)
        : this(
            kind,
            uuid,
            AutoBuyOwningViewStatus.Available,
            isAvailable,
            currentLevel,
            queuedLevels,
            hasFiniteLevels,
            isMaxLevel,
            isMaxQueuedLevel,
            meetsNextLevelRequirements,
            costRowStart,
            costRowCount,
            owningListId,
            owningViewId)
    {
    }

    public AutoBuyCandidateRow(
        AutoBuyCandidateKind kind,
        Guid uuid,
        AutoBuyOwningViewStatus owningView,
        bool isAvailable,
        int currentLevel,
        int queuedLevels,
        bool hasFiniteLevels,
        bool isMaxLevel,
        bool isMaxQueuedLevel,
        bool meetsNextLevelRequirements,
        int costRowStart,
        int costRowCount,
        Guid owningListId = default,
        Guid owningViewId = default)
    {
        Kind = kind;
        Uuid = uuid;
        OwningView = owningView;
        OwningListId = owningListId;
        OwningViewId = owningViewId;
        IsAvailable = isAvailable;
        CurrentLevel = currentLevel;
        QueuedLevels = queuedLevels;
        HasFiniteLevels = hasFiniteLevels;
        IsMaxLevel = isMaxLevel;
        IsMaxQueuedLevel = isMaxQueuedLevel;
        MeetsNextLevelRequirements = meetsNextLevelRequirements;
        CostRowStart = costRowStart;
        CostRowCount = costRowCount;
    }

    public AutoBuyCandidateKind Kind { get; }
    public Guid Uuid { get; }

    /// <summary>
    /// The candidate's authored view/list route set intersected with the views' published
    /// availability. Every non-available member maps one-to-one to a planner exclusion reason.
    /// </summary>
    public AutoBuyOwningViewStatus OwningView { get; }
    public Guid OwningListId { get; }
    public Guid OwningViewId { get; }
    public bool IsAvailable { get; }
    public int CurrentLevel { get; }
    public int QueuedLevels { get; }
    public bool HasFiniteLevels { get; }
    public bool IsMaxLevel { get; }
    public bool IsMaxQueuedLevel { get; }

    /// <summary>
    /// Whether every condition gating the level this candidate would buy next holds, evaluated from
    /// the published requirement rows.
    /// </summary>
    /// <remarks>
    /// The one admission term the game answers only when asked about a specific level. It is a
    /// projected fact rather than a live one for the same reason the rest are: the container's
    /// contents cannot change while the game runs, and the entities it compares against are rows in
    /// the same snapshot. False also covers a condition the suite cannot evaluate — the snapshot never
    /// distinguishes "does not hold" from "cannot be read" here, because a purchase must be refused
    /// either way.
    /// </remarks>
    public bool MeetsNextLevelRequirements { get; }

    public int CostRowStart { get; }
    public int CostRowCount { get; }
}

/// <summary>
/// One captured resource row: the verbatim native quantity/quality/capacity/availability
/// facts (per AB-SC-007 <c>GetTrueQuantity</c>/<c>GetAttributeCostMod</c>/<c>IsAvailable</c>
/// convenience results) keyed by stable resource UUID. Magnitudes are the game's own
/// <see cref="BigDouble"/> so no conversion or allocation happens at capture.
/// </summary>
internal readonly struct AutoBuyResourceRow
{
    public AutoBuyResourceRow(
        Guid resourceId,
        bool isBandwidth,
        BigDouble storedQuantity,
        BigDouble trueQuantity,
        BigDouble spendable,
        BigDouble quality,
        BigDouble effectiveAttributeCost,
        bool hasCapacity,
        BigDouble capacity,
        bool isAvailable)
    {
        ResourceId = resourceId;
        IsBandwidth = isBandwidth;
        StoredQuantity = storedQuantity;
        TrueQuantity = trueQuantity;
        Spendable = spendable;
        Quality = quality;
        EffectiveAttributeCost = effectiveAttributeCost;
        HasCapacity = hasCapacity;
        Capacity = capacity;
        IsAvailable = isAvailable;
    }

    public Guid ResourceId { get; }
    public bool IsBandwidth { get; }
    public BigDouble StoredQuantity { get; }
    public BigDouble TrueQuantity { get; }
    public BigDouble Quality { get; }
    public BigDouble EffectiveAttributeCost { get; }
    public bool HasCapacity { get; }
    public BigDouble Capacity { get; }

    public bool IsAvailable { get; }

    /// <summary>
    /// What a purchase drawing on this resource may spend in the native-cost coordinate, selected
    /// once by <c>WorldResourceCoordinate</c> before this feature frame is built.
    /// </summary>
    public BigDouble Spendable { get; }
}

/// <summary>
/// One cost row: a single resource cost for a candidate, projected from the published
/// <c>WorldPurchaseCost</c> table rather than from a native call. Projection stays math-free — it
/// emits one row per published cost entry; the worker groups rows sharing a
/// <see cref="ResourceRowIndex"/> and applies the stricter-than-native duplicate-resource rule when
/// it computes affordability.
/// <see cref="ResourceRowIndex"/> refers into the frame's resource array so the worker never
/// re-reads native quantities.
/// </summary>
/// <remarks>
/// A row says what a candidate owes and which resource it owes it to, and nothing about the
/// resource itself. It carried a copy of the resource's bandwidth flag that nothing ever read, while
/// the answer that decides affordability — what a bandwidth cost is actually paid out of — lives on
/// the resource row. Two copies of one fact is one copy too many when only one of them is consulted.
/// </remarks>
internal readonly struct AutoBuyCostRow : IExactCostRow<int>
{
    public AutoBuyCostRow(int resourceRowIndex, BigDouble cost)
        : this(resourceRowIndex, cost, exactGroupedLevels: 1, cost)
    {
    }

    public AutoBuyCostRow(
        int resourceRowIndex,
        BigDouble cost,
        int exactGroupedLevels,
        BigDouble exactGroupedCost)
    {
        ResourceRowIndex = resourceRowIndex;
        Cost = cost;
        ExactGroupedLevels = exactGroupedLevels;
        ExactGroupedCost = exactGroupedCost;
    }

    public int ResourceRowIndex { get; }
    public BigDouble Cost { get; }
    public int ExactGroupedLevels { get; }
    public BigDouble ExactGroupedCost { get; }

    int IExactCostRow<int>.CostResourceKey => ResourceRowIndex;
    BigDouble IExactCostRow<int>.EffectiveExactAmount => Cost;
    int IExactCostRow<int>.ExactGroupedLevels => ExactGroupedLevels;
    BigDouble IExactCostRow<int>.ExactGroupedAmount => ExactGroupedCost;
}

/// <summary>
/// The immutable per-cycle snapshot the Auto Buy worker evaluates. Rows live in reusable arrays the
/// frame itself owns; the frame exposes only bounded read-only spans, and counts bound the live region
/// of each reused array.
/// </summary>
/// <remarks>
/// <para>
/// The frame carries its own storage because the projection runs on the worker, and a worker
/// definition may hold no arrays of its own — <c>ServiceCycleWorkerDefinitionValidator</c> forbids it,
/// which is what stops a worker retaining anything that could reach the game. The runtime already
/// keeps one frame per service and hands it back every cycle, so lending the arrays out and taking
/// them back is the same reuse the reader used to get from its own fields, without the feature owning
/// mutable storage. See W50.
/// </para>
/// </remarks>
internal readonly struct AutoBuyCycleFrame
{
    private readonly AutoBuyCandidateRow[]? _candidates;
    private readonly AutoBuyResourceRow[]? _resources;
    private readonly AutoBuyCostRow[]? _costs;

    internal AutoBuyCycleFrame(
        in AutoBuyGlobalRow global,
        AutoBuyCandidateRow[] candidates,
        int candidateCount,
        int structureCount,
        int upgradeCount,
        AutoBuyResourceRow[] resources,
        int resourceCount,
        AutoBuyCostRow[] costs,
        int costCount)
    {
        if (candidates is null) throw new ArgumentNullException(nameof(candidates));
        if (resources is null) throw new ArgumentNullException(nameof(resources));
        if (costs is null) throw new ArgumentNullException(nameof(costs));
        if ((uint)candidateCount > (uint)candidates.Length)
            throw new ArgumentOutOfRangeException(nameof(candidateCount));
        if (structureCount < 0 || upgradeCount < 0 ||
            checked(structureCount + upgradeCount) != candidateCount)
            throw new ArgumentOutOfRangeException(nameof(structureCount));
        if ((uint)resourceCount > (uint)resources.Length)
            throw new ArgumentOutOfRangeException(nameof(resourceCount));
        if ((uint)costCount > (uint)costs.Length)
            throw new ArgumentOutOfRangeException(nameof(costCount));

        Global = global;
        _candidates = candidates;
        CandidateCount = candidateCount;
        StructureCount = structureCount;
        UpgradeCount = upgradeCount;
        _resources = resources;
        ResourceCount = resourceCount;
        _costs = costs;
        CostCount = costCount;
    }

    public AutoBuyGlobalRow Global { get; }
    public int CandidateCount { get; }
    public int StructureCount { get; }
    public int UpgradeCount { get; }
    public int ResourceCount { get; }
    public int CostCount { get; }

    // Row spans are exposed only within the OrbAutomata assembly: the frame is read synchronously
    // by the internal worker and native adapters, never published across a boundary. Keeping the
    // memory-view accessors internal (rather than public) keeps them off the service-cycle
    // publication surface the structural validator audits, while the private arrays stay hidden.
    internal ReadOnlySpan<AutoBuyCandidateRow> Candidates =>
        _candidates is null ? default : _candidates.AsSpan(0, CandidateCount);

    internal ReadOnlySpan<AutoBuyResourceRow> Resources =>
        _resources is null ? default : _resources.AsSpan(0, ResourceCount);

    internal ReadOnlySpan<AutoBuyCostRow> Costs =>
        _costs is null ? default : _costs.AsSpan(0, CostCount);

    internal AutoBuyCandidateRow[]? LendCandidates() => _candidates;
    internal AutoBuyResourceRow[]? LendResources() => _resources;
    internal AutoBuyCostRow[]? LendCosts() => _costs;
}
