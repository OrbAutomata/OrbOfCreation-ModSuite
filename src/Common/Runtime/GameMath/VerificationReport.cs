using System.Collections.Generic;
using System.Globalization;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// Every check's finding, answered as one response: the verdict word, then what disagreed, then
/// where the numbers came from.
/// </summary>
/// <remarks>
/// <para>
/// The verdict is the first word of the first line. The check exists to answer one question, and it
/// used to close on a duration instead — a reader had to count verdict words down ninety lines to
/// learn whether the suite and the game agreed, and the difference between a clean run and a
/// degraded one was one word buried sixty lines in.
/// </para>
/// <para>
/// Every check renders one line — its verdict word and its counts — and only a check that did not
/// agree renders anything more. Agreeing lines are not noise: a check that ran and agreed and a check
/// that never ran are the same silence otherwise, and a reader who cannot tell them apart cannot tell
/// what the top-line verdict is a verdict over. The body stays one line per check either way.
/// </para>
/// <para>
/// Nothing that changes between two identical calls appears in the comparison body. Frame numbers,
/// elapsed milliseconds and cache-warmth measurements all live on the provenance line, where a
/// reader diffing two runs reads them as the conditions of the run rather than as findings that
/// moved.
/// </para>
/// </remarks>
internal sealed class VerificationReport
{
    private readonly List<VerificationFinding> _findings = new();

    internal void Add(in VerificationFinding finding) => _findings.Add(finding);

    internal IReadOnlyList<VerificationFinding> Findings => _findings;

    internal int Compared
    {
        get
        {
            var total = 0;
            foreach (var finding in _findings) total += finding.Compared;
            return total;
        }
    }

    internal int Differed
    {
        get
        {
            var total = 0;
            foreach (var finding in _findings) total += finding.Differed;
            return total;
        }
    }

    internal int WithinTolerance
    {
        get
        {
            var total = 0;
            foreach (var finding in _findings) total += finding.WithinTolerance;
            return total;
        }
    }

    internal int Agreed => Compared - Differed;

    /// <summary>Checks that reached no comparison at all.</summary>
    internal int Silent
    {
        get
        {
            var total = 0;
            foreach (var finding in _findings)
            {
                if (finding.Verdict == VerificationVerdict.Inconclusive) total++;
            }

            return total;
        }
    }

    /// <summary>
    /// The whole run's answer. A single genuine disagreement outranks everything, because it is the
    /// finding the reader has to act on; short coverage outranks agreement, because a pass over a
    /// subset is not a pass.
    /// </summary>
    internal VerificationVerdict Verdict
    {
        get
        {
            if (Differed > 0) return VerificationVerdict.Disagree;
            if (Compared == 0) return VerificationVerdict.Inconclusive;

            foreach (var finding in _findings)
            {
                if (finding.Verdict != VerificationVerdict.Agree) return VerificationVerdict.Incomplete;
            }

            return VerificationVerdict.Agree;
        }
    }

    /// <summary>The whole response, verdict first and provenance last.</summary>
    internal IReadOnlyList<string> Render(string window)
    {
        var lines = new List<string> { Verdict.Word() + " — " + CountLine() };

        foreach (var finding in _findings)
        {
            lines.Add(finding.Headline());
            if (finding.Note.Length > 0) lines.Add(finding.Note);
            foreach (var detail in finding.Detail) lines.Add("  " + detail);
        }

        lines.Add("window: " + window);
        return lines;
    }

    private string CountLine()
    {
        var compared = Count(Compared);
        var line = Compared == 1
            ? $"{compared} fact compared, {Count(Agreed)} agree, {Count(Differed)} differ."
            : $"{compared} facts compared, {Count(Agreed)} agree, {Count(Differed)} differ.";

        if (WithinTolerance > 0)
            line += $" {Count(WithinTolerance)} agree only within tolerance.";

        if (Silent > 0)
            line += Silent == 1
                ? " 1 check compared nothing."
                : $" {Count(Silent)} checks compared nothing.";

        return line;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
