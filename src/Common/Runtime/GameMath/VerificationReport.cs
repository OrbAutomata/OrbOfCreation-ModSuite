using System;
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
/// Every check that did not simply agree renders its own line — its verdict word and its counts —
/// and only a check that did not agree renders anything more. The checks that agreed and had nothing
/// else to say share one line naming every one of them and the count each agreed on. Agreeing checks
/// are not noise: a check that ran and agreed and a check that never ran are the same silence
/// otherwise, and a reader who cannot tell them apart cannot tell what the top-line verdict is a
/// verdict over. So none of them is dropped — only the sentence repeated around each of them.
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
        var agreeing = new List<string>();
        var foldedNotes = new List<string>();

        foreach (var finding in _findings)
        {
            if (finding.TryFold(out var folded))
            {
                agreeing.Add(folded);
                if (finding.Note.Length > 0) foldedNotes.Add(finding.Subject + " — " + finding.Note);
                continue;
            }
            lines.Add(finding.Headline());
            if (finding.Note.Length > 0) lines.Add(finding.Note);
            foreach (var detail in Collapse(finding.Detail)) lines.Add("  " + detail);
        }

        // The coverage roll-up sits directly under the verdict it is the evidence for, and the
        // checks that need a reader are what follows it. A folded check's note follows the roll-up
        // carrying the check's name, because the headline that used to carry it is gone.
        if (agreeing.Count > 0) lines.Insert(1, AgreementLine(agreeing));
        for (var index = 0; index < foldedNotes.Count; index++) lines.Insert(2 + index, foldedNotes[index]);

        lines.Add("window: " + window);
        return lines;
    }

    /// <summary>
    /// Every check that agreed and had nothing else to say, named with the count it agreed on.
    /// </summary>
    /// <remarks>
    /// Not a summary: no check name and no compared count is lost, which is what makes this
    /// compression rather than truncation. What goes is the <c>AGREE:</c>/<c>compared.</c> frame
    /// repeated once per check — 864 bytes of one live round's answer — under a first line that
    /// already states how many facts were compared and how many agreed.
    /// </remarks>
    private static string AgreementLine(List<string> agreeing) =>
        "AGREE (" + Count(agreeing.Count) + (agreeing.Count == 1 ? " check): " : " checks): ") +
        string.Join(", ", agreeing);

    /// <summary>
    /// One line per distinct finding, in the order they were found, each carrying how many rows it
    /// was found on.
    /// </summary>
    /// <remarks>
    /// A check writes one row per fact it walked, which is right — the fact is where the defect is.
    /// It is also how one real defect becomes a response nobody can read: a table whose rows are
    /// keyed per owner wrote the same sentence six times per owner, 458 of 610 lines identical to the
    /// line above them, and 97.5% of a 64 KB answer was one sentence template. Collapsing is not a
    /// budget trick — a reader learns nothing from the sixth copy that the count does not tell them,
    /// and every distinct finding, with every uuid in it, still gets its own line. Findings arrive
    /// grouped by the collector that raised them, so the collapsed list stays grouped that way too.
    /// </remarks>
    private static IReadOnlyList<string> Collapse(IReadOnlyList<string> detail)
    {
        if (detail.Count < 2) return detail;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>(detail.Count);
        foreach (var line in detail)
        {
            if (counts.TryGetValue(line, out var seen)) counts[line] = seen + 1;
            else
            {
                counts[line] = 1;
                order.Add(line);
            }
        }

        if (order.Count == detail.Count) return detail;

        var collapsed = new List<string>(order.Count);
        foreach (var line in order)
        {
            var seen = counts[line];
            collapsed.Add(seen == 1 ? line : $"{Count(seen)}× {line}");
        }

        return collapsed;
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
