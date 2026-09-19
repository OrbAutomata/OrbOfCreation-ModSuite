using System;
using System.Globalization;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// One earlier committed action in the same native batch, retained only when it may overlap the
/// refused candidate's live resources or when its resources could not be read.
/// </summary>
internal readonly struct AutoBuyEarlierPurchase
{
    private readonly AutoBuyLiveCostRow[]? _costs;

    public AutoBuyEarlierPurchase(
        AutoBuyCandidateKind kind,
        Guid uuid,
        int actionIndex,
        int committedLevels,
        in AutoBuyLiveCostSnapshot costs)
    {
        Kind = kind;
        Uuid = uuid;
        ActionIndex = actionIndex;
        CommittedLevels = committedLevels;
        CostStatus = costs.Status;
        _costs = costs.Rows.ToArray();
    }

    public AutoBuyCandidateKind Kind { get; }
    public Guid Uuid { get; }
    public int ActionIndex { get; }
    public int CommittedLevels { get; }
    public AutoBuyLiveCostReadStatus CostStatus { get; }
    public ReadOnlySpan<AutoBuyLiveCostRow> Costs => _costs ?? Array.Empty<AutoBuyLiveCostRow>();
    public bool HasCompleteCosts => CostStatus == AutoBuyLiveCostReadStatus.Complete;
}

/// <summary>
/// Everything known about one purchase the worker planned and the game refused: who it was for, what
/// the plan believed, what the game says now, and which readings both halves were true for.
/// </summary>
/// <remarks>
/// The two halves are the point. A refusal on its own says only that the game disagreed; a belief on
/// its own says only what the plan thought. Put side by side they say where the planner is wrong,
/// which is the whole reason the suite stops after one.
/// </remarks>
internal readonly struct AutoBuyRefusalReport
{
    private readonly AutoBuyEarlierPurchase[]? _earlierPurchases;

    public AutoBuyRefusalReport(
        AutoBuyCandidateKind kind,
        Guid uuid,
        int requestedLevels,
        in AutoBuyPlanBelief belief,
        in AutoBuyAdmissionDiagnosis diagnosis,
        ulong worldGeneration,
        long collectedAtEpoch,
        ulong configGeneration,
        ulong lifecycleGeneration,
        ulong cycleId)
        : this(
            kind,
            uuid,
            requestedLevels,
            in belief,
            in diagnosis,
            worldGeneration,
            collectedAtEpoch,
            configGeneration,
            lifecycleGeneration,
            cycleId,
            batchId: 0,
            actionIndex: 0,
            worldCollectedAt: default,
            admissionAttemptedAt: default,
            latestWorldGenerationReadable: false,
            latestWorldGeneration: 0,
            earlierPurchases: Array.Empty<AutoBuyEarlierPurchase>())
    {
    }

    public AutoBuyRefusalReport(
        AutoBuyCandidateKind kind,
        Guid uuid,
        int requestedLevels,
        in AutoBuyPlanBelief belief,
        in AutoBuyAdmissionDiagnosis diagnosis,
        ulong worldGeneration,
        long collectedAtEpoch,
        ulong configGeneration,
        ulong lifecycleGeneration,
        ulong cycleId,
        ulong batchId,
        int actionIndex,
        MonotonicTimestamp worldCollectedAt,
        MonotonicTimestamp admissionAttemptedAt,
        bool latestWorldGenerationReadable,
        ulong latestWorldGeneration,
        AutoBuyEarlierPurchase[] earlierPurchases)
    {
        Kind = kind;
        Uuid = uuid;
        RequestedLevels = requestedLevels;
        Belief = belief;
        Diagnosis = diagnosis;
        WorldGeneration = worldGeneration;
        CollectedAtEpoch = collectedAtEpoch;
        ConfigGeneration = configGeneration;
        LifecycleGeneration = lifecycleGeneration;
        CycleId = cycleId;
        BatchId = batchId;
        ActionIndex = actionIndex;
        WorldCollectedAt = worldCollectedAt;
        AdmissionAttemptedAt = admissionAttemptedAt;
        LatestWorldGenerationReadable = latestWorldGenerationReadable;
        LatestWorldGeneration = latestWorldGeneration;
        _earlierPurchases = earlierPurchases ??
            throw new ArgumentNullException(nameof(earlierPurchases));
    }

    public AutoBuyCandidateKind Kind { get; }
    public Guid Uuid { get; }
    public int RequestedLevels { get; }
    public AutoBuyPlanBelief Belief { get; }
    public AutoBuyAdmissionDiagnosis Diagnosis { get; }
    public ulong WorldGeneration { get; }
    public long CollectedAtEpoch { get; }
    public ulong ConfigGeneration { get; }
    public ulong LifecycleGeneration { get; }
    public ulong CycleId { get; }
    public ulong BatchId { get; }
    public int ActionIndex { get; }
    public MonotonicTimestamp WorldCollectedAt { get; }
    public MonotonicTimestamp AdmissionAttemptedAt { get; }
    public bool LatestWorldGenerationReadable { get; }
    public ulong LatestWorldGeneration { get; }
    public ReadOnlySpan<AutoBuyEarlierPurchase> EarlierPurchases =>
        _earlierPurchases ?? Array.Empty<AutoBuyEarlierPurchase>();

    /// <summary>The candidate as it appears in a log line: "Structure 99a0da45-...".</summary>
    public string Candidate => Kind + " " + EntityIdentityFormatter.Format(Uuid);
}

/// <summary>
/// Renders a refusal as the text a person reads. Pure and culture-invariant, so what the bundle says
/// can be asserted without a filesystem and reads the same on every machine it is sent from.
/// </summary>
internal static class AutoBuyRefusalBundle
{
    /// <summary>The file name a bundle written at <paramref name="utcNow"/> takes.</summary>
    internal static string FileName(DateTime utcNow) =>
        "autobuy-refusal-" +
        utcNow.ToUniversalTime().ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) +
        ".txt";

    /// <summary>Every bundle file name starts with this; retention sweeps on it.</summary>
    internal const string FileNamePrefix = "autobuy-refusal-";

    internal static string Render(in AutoBuyRefusalReport report, DateTime utcNow)
    {
        var text = new StringBuilder();
        text.Append("Auto Buy planned a purchase the game refused.").AppendLine();
        text.Append("Written ")
            .Append(utcNow.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture))
            .AppendLine();
        text.AppendLine();

        text.Append("Candidate: ").Append(report.Candidate).AppendLine();
        text.Append("Levels requested: ").Append(Number(report.RequestedLevels)).AppendLine();
        text.Append("Verdict: ").Append(report.Diagnosis.Describe()).AppendLine();
        text.Append("Classification: ").Append(report.Diagnosis.Classification).AppendLine();
        text.AppendLine();

        text.AppendLine("Live diagnostic terms, read one at a time after the type-specific CanPurchase() refused:");
        AppendTerm(text, "IsAvailable()", report.Diagnosis.IsAvailable);
        AppendTerm(text, "IsMaxLevel()", report.Diagnosis.IsMaxLevel);
        AppendTerm(text, "IsMaxQueuedLevel()", report.Diagnosis.IsMaxQueuedLevel);
        AppendTerm(text, "GetPurchaseCost().HasEnough()", report.Diagnosis.HasEnough);
        text.AppendLine();

        text.AppendLine("Live cost rows at refusal:");
        var liveCosts = report.Diagnosis.LiveCosts;
        if (!liveCosts.IsComplete)
        {
            text.Append("  unavailable: ").Append(liveCosts.Status).AppendLine();
        }
        else if (liveCosts.Rows.Length == 0)
        {
            text.AppendLine("  none");
        }
        else
        {
            var rows = liveCosts.Rows;
            for (var index = 0; index < rows.Length; index++)
            {
                ref readonly var row = ref rows[index];
                text.Append("  [").Append(Number(index + 1)).Append("] Resource: ")
                    .Append(EntityIdentityFormatter.Format(row.ResourceId)).AppendLine();
                text.Append("      IsBandwidth: ").Append(Flag(row.IsBandwidth)).AppendLine();
                text.Append("      Cost: ").Append(Magnitude(row.Cost)).AppendLine();
                text.Append("      Available: ").Append(Magnitude(row.Available))
                    .Append(row.IsBandwidth ? " (live room below the ceiling)" : " (live TrueQuantity)")
                    .AppendLine();
            }
        }
        text.AppendLine();

        text.AppendLine("Earlier committed purchases in this batch touching these resources:");
        var earlier = report.EarlierPurchases;
        if (earlier.Length == 0)
        {
            text.AppendLine("  none observed");
        }
        else
        {
            for (var index = 0; index < earlier.Length; index++)
            {
                ref readonly var purchase = ref earlier[index];
                text.Append("  Action ").Append(Number(purchase.ActionIndex))
                    .Append(": ").Append(purchase.Kind).Append(' ')
                    .Append(EntityIdentityFormatter.Format(purchase.Uuid))
                    .Append(", committed ").Append(Number(purchase.CommittedLevels))
                    .Append(" level(s)");
                if (!purchase.HasCompleteCosts)
                {
                    text.Append(", resource evidence unavailable: ")
                        .Append(purchase.CostStatus).AppendLine();
                    continue;
                }

                text.Append(", first-level resource(s): ");
                var costs = purchase.Costs;
                for (var rowIndex = 0; rowIndex < costs.Length; rowIndex++)
                {
                    if (rowIndex > 0) text.Append(", ");
                    text.Append(EntityIdentityFormatter.Format(costs[rowIndex].ResourceId));
                }
                text.AppendLine();
            }
        }
        text.AppendLine();

        var belief = report.Belief;
        text.AppendLine("What the plan believed, from the world it was made from:");
        text.Append("  IsAvailable: ").Append(Flag(belief.IsAvailable)).AppendLine();
        text.Append("  HasFiniteLevels: ").Append(Flag(belief.HasFiniteLevels)).AppendLine();
        text.Append("  IsMaxLevel: ").Append(Flag(belief.IsMaxLevel)).AppendLine();
        text.Append("  IsMaxQueuedLevel: ").Append(Flag(belief.IsMaxQueuedLevel)).AppendLine();
        text.Append("  CurrentLevel: ").Append(Number(belief.CurrentLevel)).AppendLine();
        text.Append("  QueuedLevels: ").Append(Number(belief.QueuedLevels)).AppendLine();
        text.Append("  Cost rows: ").Append(Number(belief.CostResourceCount))
            .Append(" resource(s), ").Append(Number(belief.PricedResourceCount))
            .Append(" priced above nought").AppendLine();
        text.Append("  CostRatio: ")
            .Append(belief.CostRatio.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        text.AppendLine();

        text.AppendLine("The resource that set that ratio:");
        if (belief.BindingResourceId == Guid.Empty)
        {
            text.AppendLine("  none — every published cost row for this candidate priced at nought");
        }
        else
        {
            text.Append("  Resource: ")
                .Append(EntityIdentityFormatter.Format(belief.BindingResourceId)).AppendLine();
            text.Append("  IsBandwidth: ").Append(Flag(belief.BindingIsBandwidth)).AppendLine();
            text.Append("  Cost: ").Append(Magnitude(belief.BindingCost)).AppendLine();
            text.Append("  Available: ").Append(Magnitude(belief.BindingAvailable))
                .Append(belief.BindingIsBandwidth
                    ? " (planned spendable room below the ceiling)"
                    : " (planned spendable TrueQuantity)")
                .AppendLine();
            text.Append("  Reserve floor applied: ").Append(Magnitude(belief.BindingReserveFloor)).AppendLine();
        }
        text.AppendLine();

        text.AppendLine("Readings this decision was pinned to:");
        text.Append("  World generation: ").Append(Number(report.WorldGeneration)).AppendLine();
        text.Append("  World collected at epoch: ").Append(Number(report.CollectedAtEpoch)).AppendLine();
        text.Append("  Config generation: ").Append(Number(report.ConfigGeneration)).AppendLine();
        text.Append("  Lifecycle generation: ").Append(Number(report.LifecycleGeneration)).AppendLine();
        text.Append("  Cycle: ").Append(Number(report.CycleId)).AppendLine();
        text.Append("  Batch: ").Append(Number(report.BatchId)).AppendLine();
        text.Append("  Action index: ").Append(Number(report.ActionIndex)).AppendLine();
        text.Append("  World collected monotonic ticks: ")
            .Append(Number(report.WorldCollectedAt.Ticks)).AppendLine();
        text.Append("  Admission attempted monotonic ticks: ")
            .Append(Number(report.AdmissionAttemptedAt.Ticks)).AppendLine();
        if (report.AdmissionAttemptedAt >= report.WorldCollectedAt)
        {
            var elapsedTicks = report.AdmissionAttemptedAt.Ticks - report.WorldCollectedAt.Ticks;
            text.Append("  Collection-to-admission elapsed milliseconds: ")
                .Append(TimeSpan.FromTicks(elapsedTicks).TotalMilliseconds
                    .ToString("R", CultureInfo.InvariantCulture))
                .AppendLine();
        }
        else
        {
            text.AppendLine("  Collection-to-admission elapsed milliseconds: unavailable");
        }
        if (report.LatestWorldGenerationReadable)
        {
            text.Append("  Latest world generation at admission: ")
                .Append(Number(report.LatestWorldGeneration)).AppendLine();
            if (report.LatestWorldGeneration >= report.WorldGeneration)
            {
                text.Append("  World generations elapsed: ")
                    .Append(Number(report.LatestWorldGeneration - report.WorldGeneration))
                    .AppendLine();
            }
            else
            {
                text.AppendLine(
                    "  World generations elapsed: invalid (latest generation is older than the pinned world)");
            }
        }
        else
        {
            text.AppendLine("  Latest world generation at admission: unavailable");
            text.AppendLine("  World generations elapsed: unavailable");
        }
        return text.ToString();
    }

    private static void AppendTerm(StringBuilder text, string name, AutoBuyAdmissionTerm term)
    {
        text.Append("  ").Append(name).Append(": ");
        text.Append(term switch
        {
            AutoBuyAdmissionTerm.Passed => "passed",
            AutoBuyAdmissionTerm.Refused => "REFUSED",
            _ => "could not be read",
        });
        text.AppendLine();
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A magnitude written as the game holds it. The game's own formatting abbreviates and rounds,
    /// and a diagnosis of a number the plan got wrong is exactly where rounding must not happen.
    /// </summary>
    private static string Magnitude(BigDouble value) =>
        value.Mantissa.ToString("R", CultureInfo.InvariantCulture) + "e" +
        value.Exponent.ToString(CultureInfo.InvariantCulture);
}
