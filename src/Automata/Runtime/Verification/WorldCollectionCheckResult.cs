using System.Collections.Generic;
using OrbModding.Common.Runtime.GameMath;

namespace OrbAutomata;

/// <summary>
/// What the world-collection check found, and the conditions it found it under.
/// </summary>
/// <remarks>
/// The census and the timings are separated from the findings on purpose: they are the same on a
/// run that agreed and a run that did not, they move between two calls over an unchanged world, and
/// nothing in them is a verdict. They belong on the response's provenance line.
/// </remarks>
internal readonly struct WorldCollectionCheckResult
{
    internal WorldCollectionCheckResult(
        IReadOnlyList<VerificationFinding> findings,
        int entities,
        int categories,
        double collectMilliseconds,
        CacheDrift drift)
    {
        Findings = findings;
        Entities = entities;
        Categories = categories;
        CollectMilliseconds = collectMilliseconds;
        Drift = drift;
    }

    internal IReadOnlyList<VerificationFinding> Findings { get; }

    internal int Entities { get; }

    internal int Categories { get; }

    /// <summary>The warm pass — the steady-state cost every service cycle actually pays.</summary>
    internal double CollectMilliseconds { get; }

    internal CacheDrift Drift { get; }
}

/// <summary>
/// How far the game's own modifier memos had drifted from a fresh recompute when the comparison was
/// taken.
/// </summary>
/// <remarks>
/// Not an error in the snapshot — the game acts on its memo, so the suite reads the memo too. It is
/// recorded because it is a condition under which every compared number was read, and it is kept out
/// of the comparison body because it moves by thousands between two calls over an unchanged world.
/// </remarks>
internal readonly struct CacheDrift
{
    internal CacheDrift(int surveyed, int drifted, int dirty, int neverCalculated, string widest)
    {
        Surveyed = surveyed;
        Drifted = drifted;
        Dirty = dirty;
        NeverCalculated = neverCalculated;
        Widest = widest ?? string.Empty;
    }

    internal int Surveyed { get; }

    internal int Drifted { get; }

    internal int Dirty { get; }

    /// <summary>Memos still holding their unwritten zero, which is what the game will act on.</summary>
    internal int NeverCalculated { get; }

    /// <summary>The widest drift and the record it was on, in the screen's own notation.</summary>
    internal string Widest { get; }
}

/// <summary>The staleness survey's verdict, beside the drift that explains why it compares as it does.</summary>
internal readonly struct CacheStalenessSurvey
{
    internal CacheStalenessSurvey(VerificationFinding fold, CacheDrift drift)
    {
        Fold = fold;
        Drift = drift;
    }

    internal VerificationFinding Fold { get; }

    internal CacheDrift Drift { get; }
}
