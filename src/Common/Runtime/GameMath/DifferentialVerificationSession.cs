using System;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// A bounded, on-demand verification pass: start it, feed it entities for a few ticks, get one
/// verdict.
/// </summary>
/// <remarks>
/// <para>
/// Verification exists to be run deliberately — after a game update, or when ported math changes —
/// not continuously. Bounding it by both ticks and entities keeps a run from becoming a frame-time
/// tax and guarantees it terminates even if the entity source is larger than expected or never runs
/// dry.
/// </para>
/// <para>
/// Entities that could not be read are tracked separately from entities that disagreed, and both
/// block a pass. The distinction matters when reading the verdict: disagreement means the ported
/// math is wrong, whereas unverifiable means the game's shape is not what the contract expects —
/// different causes, different fixes. Counting unreadable entities as success would be the one bug
/// that makes the whole verification worthless.
/// </para>
/// </remarks>
internal sealed class DifferentialVerificationSession
{
    private readonly int _tickBudget;
    private readonly int _entityBudget;
    private bool _currentEntityWasExpectedSkip;

    internal DifferentialVerificationSession(
        string subject = "Game math",
        int tickBudget = 5,
        int entityBudget = 400,
        int sampleLimit = 32)
    {
        _tickBudget = tickBudget < 1 ? 1 : tickBudget;
        _entityBudget = entityBudget < 1 ? 1 : entityBudget;
        Subject = string.IsNullOrEmpty(subject) ? "Game math" : subject;
        Run = new DifferentialRun(Subject, sampleLimit);
    }

    internal string Subject { get; }

    internal DifferentialRun Run { get; }

    internal bool IsRunning { get; private set; }
    internal int TicksElapsed { get; private set; }
    internal int EntitiesVerified { get; private set; }
    internal int Unverifiable { get; private set; }
    internal int ExpectedSkips { get; private set; }

    /// <summary>The first reason an entity could not be read, kept for the verdict line.</summary>
    internal string FirstUnverifiableReason { get; private set; } = string.Empty;

    /// <summary>Ticks spent evaluating the ported math.</summary>
    internal long OurElapsedTicks { get; private set; }

    /// <summary>Ticks spent asking the game for the same answer.</summary>
    internal long TheirElapsedTicks { get; private set; }

    /// <summary>
    /// Records how long each side took for one entity. This is the measurement the whole approach
    /// rests on: owning the math is only worth its risk if evaluating it ourselves is materially
    /// cheaper than asking the game to recompute it.
    /// </summary>
    internal void RecordTiming(long ourTicks, long theirTicks)
    {
        OurElapsedTicks += ourTicks;
        TheirElapsedTicks += theirTicks;
    }

    private static string FormatMilliseconds(long ticks) =>
        (ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("0.###");

    internal void Start()
    {
        IsRunning = true;
        TicksElapsed = 0;
    }

    /// <summary>
    /// Whether this tick should still verify. False once either budget is spent, at which point the
    /// caller should <see cref="Complete"/>.
    /// </summary>
    internal bool WantsMoreWork() =>
        IsRunning && TicksElapsed < _tickBudget &&
        EntitiesVerified + Unverifiable + ExpectedSkips < _entityBudget;

    /// <summary>Whether another entity fits within the entity budget this tick.</summary>
    internal bool HasEntityBudget() =>
        EntitiesVerified + Unverifiable + ExpectedSkips < _entityBudget;

    internal void RecordVerified()
    {
        if (_currentEntityWasExpectedSkip)
        {
            _currentEntityWasExpectedSkip = false;
            return;
        }
        EntitiesVerified++;
    }

    /// <summary>
    /// Classifies the current entity as an expected skip. The caller's ordinary successful-entity
    /// bookkeeping is consumed rather than also counting this entity as verified.
    /// </summary>
    internal void RecordExpectedSkip()
    {
        ExpectedSkips++;
        _currentEntityWasExpectedSkip = true;
    }

    internal void RecordUnverifiable(string reason)
    {
        Unverifiable++;
        if (FirstUnverifiableReason.Length == 0 && !string.IsNullOrEmpty(reason))
        {
            FirstUnverifiableReason = reason;
        }
    }

    internal void EndTick() => TicksElapsed++;

    /// <summary>
    /// Stops the session and produces the one line a player should see, plus recorded
    /// disagreements. Never reports success for a run that verified nothing.
    /// </summary>
    internal string Complete()
    {
        IsRunning = false;

        if (EntitiesVerified == 0)
        {
            var reason = FirstUnverifiableReason.Length == 0
                ? ExpectedSkips > 0
                    ? $"{ExpectedSkips} entities were expected skips."
                    : "no entities were available to check."
                : FirstUnverifiableReason;
            return $"{Subject} verification INCONCLUSIVE: nothing could be verified — {reason}";
        }

        var summary = Run.Summarize();
        var detail = $" [{EntitiesVerified} entities";
        if (Unverifiable > 0) detail += $", {Unverifiable} unreadable";
        if (ExpectedSkips > 0) detail += $", {ExpectedSkips} expected skips";

        // Stated only when the run actually spanned frames. A single-frame run always spent exactly
        // one tick, so reporting it says nothing and invites the reader to ask what the others were.
        if (TicksElapsed > 1) detail += $", {TicksElapsed} ticks";
        detail += "]";

        if (OurElapsedTicks > 0 || TheirElapsedTicks > 0)
        {
            // Reported as a ratio as well as absolutes, because the absolute numbers include the
            // verifier's own reflection overhead on our side and so understate the real margin.
            var ratio = OurElapsedTicks > 0
                ? (TheirElapsedTicks / (double)OurElapsedTicks).ToString("0.##") + "x"
                : "n/a";
            detail += $" ours={FormatMilliseconds(OurElapsedTicks)}ms " +
                $"theirs={FormatMilliseconds(TheirElapsedTicks)}ms ({ratio})";
        }

        if (Unverifiable > 0 && Run.Passed)
        {
            // Everything readable agreed, but some entities could not be read at all. That is not a
            // clean pass, and saying so plainly avoids a false sense of coverage.
            return $"{Subject} verification INCOMPLETE: {Run.Compared} comparisons agreed, " +
                $"but {Unverifiable} entities could not be read — {FirstUnverifiableReason}{detail}";
        }

        return summary + detail;
    }
}
