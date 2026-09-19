using System;

namespace OrbAutomata;

/// <summary>
/// What happens when the game refuses a purchase the worker planned. The action boundary reports the
/// refusal; the responder decides what the suite does about it.
/// </summary>
internal interface IAutoBuyRefusalResponsePort
{
    void ObserveRefusal(in AutoBuyRefusalReport report);
}

/// <summary>
/// Logs expected affordability staleness without a synchronous capture, and stands Auto Buy down
/// with one bounded diagnostic only for structural or otherwise impossible disagreements.
/// </summary>
/// <remarks>
/// <para>
/// A live structural contradiction is not a race the planner should ride out: retrying one produced
/// 1,988 identical refusals in a prior session, so those still stop after one full diagnostic.
/// Affordability is different. Resource quantities can move after collection through drain and
/// earlier queue-time spending, so a price-only disagreement is expected snapshot staleness: the
/// action skips, the service stays active, and the next fresh world is planned normally.
/// </para>
/// <para>
/// Availability is the same kind of fact and was treated as the opposite one. The game opens and
/// shuts that gate on its own, and a lifecycle reset shuts a great many at once — so 27 seconds
/// after a prestige a candidate the player had not re-earned was legitimately refused, and the
/// suite switched the whole feature off over it for the following 26 minutes. The planner already
/// gates on the world's own reading of that gate, so the re-gate needs no new machinery: skip the
/// candidate, stay enabled, and the next capture — which reads the same member the boundary just
/// asked — will not plan it again until the game says yes.
/// </para>
/// <para>
/// What that must not become is a retry loop. The two readings can only disagree across a frame,
/// so a second refusal on the same candidate planned from a world collected <em>after</em> the
/// first is no longer staleness: the suite's reading of that entity and the game's genuinely
/// differ, which is the structural contradiction the stand-down exists for, and it stands down
/// then with the full diagnostic.
/// </para>
/// <para>
/// Standing down means turning Auto Buy's own setting off, through the same write path the toggle
/// button uses. That is deliberate: the Mod Config screen then shows it off, and turning it back on
/// is the one-click thing an operator already knows how to do. Nothing here re-enables it, and there
/// is no separate quarantine state that could disagree with the setting.
/// </para>
/// </remarks>
internal sealed class AutoBuyRefusalResponder : IAutoBuyRefusalResponsePort
{
    private readonly Func<bool> _isActive;
    private readonly Action<string> _standDown;
    private readonly IAutoBuyRefusalBundlePort _bundles;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _utcNow;
    private Guid _availabilityCandidate;
    private ulong _availabilityWorldGeneration;

    public AutoBuyRefusalResponder(
        Func<bool> isActive,
        Action<string> standDown,
        IAutoBuyRefusalBundlePort bundles,
        Action<string> log,
        Func<DateTime>? utcNow = null)
    {
        _isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
        _standDown = standDown ?? throw new ArgumentNullException(nameof(standDown));
        _bundles = bundles ?? throw new ArgumentNullException(nameof(bundles));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void ObserveRefusal(in AutoBuyRefusalReport report)
    {
        // Already off: there is no active service policy to recover or stand down, and another
        // callback would only duplicate an earlier diagnosis. Re-enabling opts into handling a new
        // refusal again.
        if (!_isActive()) return;

        if (report.Diagnosis.Classification ==
            AutoBuyRefusalClassification.AffordabilityChanged)
        {
            var affordabilitySummary =
                $"Auto Buy skipped a purchase whose live resources had moved since planning " +
                $"({report.Candidate}): {report.Diagnosis.Describe()}. " +
                "Auto Buy remains enabled and will re-plan from the next world collection.";
            _log(affordabilitySummary);
            return;
        }

        if (report.Diagnosis.Classification ==
                AutoBuyRefusalClassification.AvailabilityChanged &&
            !RepeatedOnAFreshWorld(in report))
        {
            _availabilityCandidate = report.Uuid;
            _availabilityWorldGeneration = report.WorldGeneration;
            _log(
                $"Auto Buy skipped a purchase the game had shut since planning " +
                $"({report.Candidate}): {report.Diagnosis.Describe()}. " +
                "Auto Buy remains enabled and will re-plan from the next world collection, " +
                "which reads the same gate.");
            return;
        }

        var now = _utcNow();
        var located = _bundles.TryWrite(AutoBuyRefusalBundle.Render(in report, now), now, out var path);
        var where = located ? path : "unavailable (the bundle could not be written or retained within budget)";

        var summary =
            $"Auto Buy planned a purchase the game would not take ({report.Candidate}): " +
            $"{report.Diagnosis.Describe()}. Diagnostic bundle: {where}. " +
            "Auto Buy disabled itself; re-enable in Mod Config after reviewing.";

        _standDown(summary);
        _log(summary);
    }

    /// <summary>
    /// Whether this candidate already refused on availability, and was then planned again from a
    /// world collected after that refusal.
    /// </summary>
    /// <remarks>
    /// The generation comparison is what separates staleness from disagreement. A plan carried over
    /// from the same world says nothing new — the capture never had a chance to see the shut gate.
    /// A plan made from a later world did have that chance, read the gate open, and the game shut
    /// it anyway, which is the suite being wrong about an entity rather than merely late.
    /// </remarks>
    private bool RepeatedOnAFreshWorld(in AutoBuyRefusalReport report) =>
        _availabilityCandidate == report.Uuid &&
        report.WorldGeneration > _availabilityWorldGeneration;
}
