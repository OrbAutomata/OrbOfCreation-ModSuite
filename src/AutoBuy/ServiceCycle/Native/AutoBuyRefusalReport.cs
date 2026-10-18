using System;
using System.Globalization;
using System.Text;
using OrbModding.Common;

namespace OrbAutomata;

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
