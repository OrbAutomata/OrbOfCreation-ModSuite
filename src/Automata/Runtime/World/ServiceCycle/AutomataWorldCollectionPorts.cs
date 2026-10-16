using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.WorldCollection;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The Unity-thread half of collection, behind an interface so the service can be driven without a
/// game.
/// </summary>
/// <remarks>
/// The collector is the only implementation that matters, but it needs a loaded Unity player loop and
/// a live assembly to construct, and the service's own logic — when to collect, what to do with a
/// partial pass, what to publish — is worth testing without either.
/// </remarks>
internal interface IAutomataWorldCapturePort
{
    /// <summary>Whether any category resolved on this build. False means collection cannot run at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Reads every category into <paramref name="frame"/>. Unity thread only.</summary>
    WorldCollectionReport Collect(GameWorldCycleFrame frame);
}

/// <summary>Collects through the real <see cref="GameWorldCollector"/>.</summary>
internal sealed class AutomataWorldCapturePort : IAutomataWorldCapturePort
{
    private readonly GameWorldCollector _collector;
    private readonly Func<long> _readFrameIdentity;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Action<WorldCollectionReport>? _announce;
    private readonly WorldCollectionSpanRegistry? _spans;
    private string _announced = string.Empty;
    private int _announcedSampled;

    /// <param name="readFrameIdentity">
    /// The same frame counter the host pumps with. Read here rather than threaded through the cycle
    /// contexts because capture already runs on the Unity thread, where the counter is meaningful,
    /// and both callers resolve it from one delegate so they cannot drift.
    /// </param>
    /// <param name="readLifecycleEpoch">
    /// The same native lifecycle counter the host replaces its runners on. Read at the moment the game
    /// is read, for the same reason the frame counter is: an epoch resolved later would name the run
    /// the derivation finished under rather than the one the readings came from.
    /// </param>
    /// <param name="spans">
    /// Where a recording session collects this pass's per-category numbers. Nothing is measured for
    /// it — the report already carries what each category cost — and nothing is recorded unless a
    /// session is running.
    /// </param>
    internal AutomataWorldCapturePort(
        GameWorldCollector collector,
        Func<long> readFrameIdentity,
        Func<long> readLifecycleEpoch,
        Action<WorldCollectionReport>? announce = null,
        WorldCollectionSpanRegistry? spans = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _readFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _announce = announce;
        _spans = spans;
    }

    /// <summary>
    /// True while any category can be read. A build that renamed one member still yields every other
    /// category, so a partial collector is worth running; a collector that resolved nothing is not.
    /// </summary>
    public bool IsAvailable => _collector.IsAnyCategoryAvailable;

    public WorldCollectionReport Collect(GameWorldCycleFrame frame)
    {
        frame.CollectedAtFrame = _readFrameIdentity();
        frame.CollectedAtEpoch = _readLifecycleEpoch();
        frame.EntityIdentities = EntityIdentityCatalog.Shared.Capture(frame.CollectedAtEpoch);
        var report = _collector.Collect(frame);
        // Every pass, where the announce speaks only when the answer moves: a session is measuring a
        // distribution and a distribution needs the quiet passes too.
        _spans?.Observe(report, frame.CollectedAtFrame, frame.CollectedAtEpoch, frame.CollectedAt);
        Announce(in report);
        return report;
    }

    /// <summary>
    /// Says what the pass managed — once, and again only when the answer changes.
    /// </summary>
    /// <remarks>
    /// Collection runs four times a second, so announcing every pass would bury the log, and
    /// announcing none of them leaves a build that renamed one member indistinguishable from a quiet
    /// game: the projection carries a count of unavailable categories and no member name anywhere.
    /// The population belongs in that comparison. Keying a healthy pass on the literal word
    /// "complete" made the announce say nothing while the world it describes went from 6,683
    /// entities to 4,051 across a prestige — the one line in the log that names the population was
    /// silent about the only population change of the session.
    /// </remarks>
    private void Announce(in WorldCollectionReport report)
    {
        if (_announce is null) return;
        var key = report.IsComplete ? "complete" : report.Describe();
        var sampled = report.TotalSampled;
        if (string.Equals(key, _announced, StringComparison.Ordinal) && !HasMoved(sampled)) return;
        _announced = key;
        _announcedSampled = sampled;
        _announce(report);
    }

    /// <summary>
    /// Whether the sampled population has moved far enough from the last announced one to be worth
    /// a line: at least a tenth of it.
    /// </summary>
    /// <remarks>
    /// Measured against what was last announced rather than bucketed against fixed boundaries.
    /// Buckets flap — a population resting on a boundary re-announces every pass — while a band
    /// around the last spoken number lets ordinary play drift quietly and accumulate until the drift
    /// is itself the news. A prestige (−39%) or a save reload clears it immediately; crafting one
    /// item does not.
    /// </remarks>
    private bool HasMoved(int sampled)
    {
        var moved = Math.Abs((long)sampled - _announcedSampled);
        return moved != 0 && moved * 10 >= _announcedSampled;
    }
}
