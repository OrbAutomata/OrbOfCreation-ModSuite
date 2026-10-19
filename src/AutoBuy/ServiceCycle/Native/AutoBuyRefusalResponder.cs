using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// What happens when the game refuses a purchase the worker planned. The action boundary reports the
/// refusal with the result it would return; the responder answers with the result the runner gets.
/// </summary>
internal interface IAutoBuyRefusalResponsePort
{
    ServiceActionResult Answer(in AutoBuyRefusalReport report, in ServiceActionResult refused);
}

/// <summary>
/// Lets a price refusal pass as an ordinary refusal, backs a candidate off when the game disagrees
/// with the world about it, and holds Auto Buy back for the lifecycle only when the refusal is one the
/// suite's model says cannot happen.
/// </summary>
/// <remarks>
/// <para>
/// Price moves after collection through drain and earlier queue-time spending, so a price-only
/// refusal is expected staleness: the action skips and the next fresh world is planned normally.
/// </para>
/// <para>
/// Availability is a gate the game opens and shuts on its own, most of all across a lifecycle reset,
/// so its refusal backs the candidate off quietly: the next world usually reads the gate shut and
/// ends the episode. Only a refusal again from a world collected after the first is announced.
/// A level refusal (at max level or max queued level while the world said below) backs the candidate
/// off loudly from the first refusal. Both retry with doubling waits held at one minute, because a
/// refusal ends the batch and a candidate that came due every second would hold back every purchase
/// ranked after it.
/// </para>
/// <para>
/// A refusal with every readable term passing is the invariant: the suite cannot say what the game
/// will do. It writes one diagnostic bundle, logs at error, and trips the lifecycle latch, which
/// holds the service back until the next lifecycle boundary probes the game again.
/// </para>
/// </remarks>
internal sealed class AutoBuyRefusalResponder : IAutoBuyRefusalResponsePort
{
    private readonly AutoBuyInvariantLatch _latch;
    private readonly IAutoBuyRefusalBundlePort _bundles;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _utcNow;

    public AutoBuyRefusalResponder(
        AutoBuyInvariantLatch latch,
        IAutoBuyRefusalBundlePort bundles,
        Action<string> log,
        Func<DateTime>? utcNow = null)
    {
        _latch = latch ?? throw new ArgumentNullException(nameof(latch));
        _bundles = bundles ?? throw new ArgumentNullException(nameof(bundles));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public ServiceActionResult Answer(in AutoBuyRefusalReport report, in ServiceActionResult refused)
    {
        switch (report.Diagnosis.Classification)
        {
            case AutoBuyRefusalClassification.AffordabilityChanged:
                return refused;
            case AutoBuyRefusalClassification.AvailabilityChanged:
                return refused.BackingOffTarget(AutoBuyServicePolicies.RefusalBackoffCap, quietFirstFailure: true);
            case AutoBuyRefusalClassification.StructuralMismatch:
                return refused.BackingOffTarget(AutoBuyServicePolicies.RefusalBackoffCap);
        }
        if (_latch.IsTripped) return refused;

        var now = _utcNow();
        var located = _bundles.TryWrite(AutoBuyRefusalBundle.Render(in report, now), now, out var path);
        var where = located ? path : "unavailable (the bundle could not be written or retained within budget)";
        var summary =
            $"Auto Buy planned a purchase the game would not take ({report.Candidate}): " +
            $"{report.Diagnosis.Describe()}. Diagnostic bundle: {where}. " +
            "Auto Buy holds off until the next load, reset or prestige checks the game again.";
        _latch.TryTrip(summary);
        _log(summary);
        return refused;
    }
}
