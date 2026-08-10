using System;
using System.Collections.Generic;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>How one comparison between a suite-computed value and the game's own came out.</summary>
internal enum DifferentialOutcome
{
    /// <summary>Bit-for-bit identical. The expected result, since a port replicates the original's operation order.</summary>
    Exact = 0,

    /// <summary>
    /// Different, but within floating-point noise. Not a failure, yet worth counting separately:
    /// a port that only ever lands "close" has diverged in operation order somewhere, which is a
    /// warning that a larger divergence is possible on other inputs.
    /// </summary>
    Close = 1,

    /// <summary>A real disagreement. The port is wrong, or its inputs were read wrong.</summary>
    Mismatch = 2,

    /// <summary>One side produced a non-finite value the other did not. Always a failure.</summary>
    NotComparable = 3,
}

/// <summary>One recorded comparison, kept only when it is not <see cref="DifferentialOutcome.Exact"/>.</summary>
internal readonly struct DifferentialSample
{
    internal DifferentialSample(
        Guid entityId,
        string aspect,
        BigDouble ours,
        BigDouble theirs,
        DifferentialOutcome outcome)
    {
        EntityId = entityId;
        Aspect = aspect ?? string.Empty;
        Ours = ours;
        Theirs = theirs;
        Outcome = outcome;
    }

    internal Guid EntityId { get; }

    /// <summary>
    /// Which computation disagreed — a cost's resource identity, or the name of the step in a ported
    /// chain. This is what makes a failure actionable: a chain compared only at its end says the port
    /// is wrong, while comparing each step says which line of it is.
    /// </summary>
    internal string Aspect { get; }

    internal BigDouble Ours { get; }
    internal BigDouble Theirs { get; }
    internal DifferentialOutcome Outcome { get; }

    internal string Describe() =>
        $"{Outcome}: entity {EntityId} [{Aspect}] {VerificationValue.Sides(Ours, Theirs)}";
}

/// <summary>
/// Compares values the suite computes against the values the game computes, so ported math is checked
/// against the only oracle that actually matters.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests around the ported math prove it is self-consistent with values hand-derived from
/// the decompiled source. They cannot prove it agrees with the running game, because a misreading of
/// the original would be reproduced identically in both the port and the expected value. This is the
/// check that closes that gap.
/// </para>
/// <para>
/// It is deliberately bounded and on-demand: a run compares a fixed budget of entities and stops, so
/// verification never becomes a permanent tax on frame time. It is also the natural re-audit check
/// after a game patch — the same comparison that validates a port today is what proves a new build
/// still matches.
/// </para>
/// <para>
/// Exact and merely-close agreement are counted separately on purpose. Replicating the original's
/// operation order should produce bit-identical results; a drift into "close" means the order
/// diverged somewhere and is an early warning even while every comparison still passes.
/// </para>
/// </remarks>
internal sealed class DifferentialRun
{
    private readonly List<DifferentialSample> _failures = new();
    private readonly int _sampleLimit;

    internal DifferentialRun(string subject = "Game math", int sampleLimit = 32)
    {
        Subject = string.IsNullOrEmpty(subject) ? "Game math" : subject;
        _sampleLimit = sampleLimit < 0 ? 0 : sampleLimit;
    }

    /// <summary>What is being verified, used only to make the verdict line readable.</summary>
    internal string Subject { get; }

    internal int Compared { get; private set; }
    internal int ExactCount { get; private set; }
    internal int CloseCount { get; private set; }
    internal int MismatchCount { get; private set; }

    /// <summary>Whether every comparison agreed, exactly or within noise.</summary>
    internal bool Passed => MismatchCount == 0;

    /// <summary>Recorded disagreements, capped so a systematically broken port cannot exhaust memory.</summary>
    internal IReadOnlyList<DifferentialSample> Failures => _failures;

    internal DifferentialOutcome Compare(Guid entityId, string aspect, BigDouble ours, BigDouble theirs)
    {
        var outcome = Classify(ours, theirs);
        Compared++;

        switch (outcome)
        {
            case DifferentialOutcome.Exact:
                ExactCount++;
                break;
            case DifferentialOutcome.Close:
                CloseCount++;
                break;
            default:
                MismatchCount++;
                break;
        }

        // Only real disagreements are kept. A merely-close comparison agrees, and its row said so in
        // two byte-identical numbers behind two full UUIDs — twenty such rows a call, carrying
        // nothing a reader could act on. Its count survives on the finding.
        if (outcome is DifferentialOutcome.Mismatch or DifferentialOutcome.NotComparable &&
            _failures.Count < _sampleLimit)
        {
            _failures.Add(new DifferentialSample(entityId, aspect, ours, theirs, outcome));
        }

        return outcome;
    }

    /// <summary>
    /// Classifies one pair. Exactness is judged on the representation rather than on
    /// <c>ToDouble()</c>, because converting first would hide divergence beyond double precision —
    /// exactly where large magnitudes live.
    /// </summary>
    internal static DifferentialOutcome Classify(BigDouble ours, BigDouble theirs)
    {
        var oursBad = BigDouble.IsNaN(ours) || BigDouble.IsInfinity(ours);
        var theirsBad = BigDouble.IsNaN(theirs) || BigDouble.IsInfinity(theirs);

        // Both non-finite in the same way is agreement about an edge case, not a failure.
        if (oursBad || theirsBad)
        {
            if (oursBad != theirsBad) return DifferentialOutcome.NotComparable;
            if (BigDouble.IsNaN(ours) && BigDouble.IsNaN(theirs)) return DifferentialOutcome.Exact;
            return BigDouble.IsPositiveInfinity(ours) == BigDouble.IsPositiveInfinity(theirs)
                ? DifferentialOutcome.Exact
                : DifferentialOutcome.NotComparable;
        }

        if (ours.Mantissa == theirs.Mantissa && ours.Exponent == theirs.Exponent)
        {
            return DifferentialOutcome.Exact;
        }

        // Relative comparison, so tolerance means the same thing at 1e2 and 1e300.
        var difference = BigDouble.Abs(ours - theirs);
        var scale = BigDouble.Max(BigDouble.Abs(ours), BigDouble.Abs(theirs));
        if (scale.Mantissa == 0.0) return DifferentialOutcome.Exact;

        var relative = (difference / scale).ToDouble();
        return relative <= RelativeTolerance
            ? DifferentialOutcome.Close
            : DifferentialOutcome.Mismatch;
    }

    /// <summary>
    /// Tight enough that a genuine formula error cannot pass, loose enough that last-bit noise from
    /// a differing operation order does not raise a false alarm.
    /// </summary>
    private const double RelativeTolerance = 1e-12;

    /// <summary>This run's verdict, and the disagreements behind it.</summary>
    internal VerificationFinding Finding()
    {
        if (Compared == 0) return VerificationFinding.Inconclusive(Subject, "nothing was compared.");
        if (Passed) return VerificationFinding.Agree(Subject, Compared, CloseCount);

        var detail = new List<string>(_failures.Count + 1);
        foreach (var failure in _failures) detail.Add(failure.Describe());

        if (MismatchCount > _failures.Count)
        {
            detail.Add($"… {MismatchCount - _failures.Count} further disagreements not recorded.");
        }

        return VerificationFinding.Disagree(Subject, Compared, MismatchCount, detail, CloseCount);
    }
}
