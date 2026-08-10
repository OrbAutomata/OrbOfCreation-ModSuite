using System;
using System.Collections.Generic;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// What one check found: its verdict, how much it compared, and — only when they exist — the
/// disagreements themselves.
/// </summary>
/// <remarks>
/// <para>
/// A check reports a finding rather than a line, because the response cannot lead with its verdict
/// while its checks are writing their own prose into the middle of it. Rendering is one decision
/// made once, in <see cref="VerificationReport"/>.
/// </para>
/// <para>
/// The disagreements are the fact and the agreement is the count. A row saying two numbers are equal
/// carries no information and cost this response twenty of them per call, each one two full UUIDs
/// wide; the count line accounts for every one of those rows, so dropping them hides nothing.
/// </para>
/// </remarks>
internal readonly struct VerificationFinding
{
    private static readonly string[] Nothing = Array.Empty<string>();

    private VerificationFinding(
        string subject,
        VerificationVerdict verdict,
        int compared,
        int differed,
        int withinTolerance,
        string reason,
        IReadOnlyList<string>? detail)
    {
        Subject = string.IsNullOrEmpty(subject) ? "Game math" : subject;
        Verdict = verdict;
        Compared = compared;
        Differed = differed;
        WithinTolerance = withinTolerance;
        Reason = reason ?? string.Empty;
        Detail = detail ?? Nothing;
    }

    internal string Subject { get; }

    internal VerificationVerdict Verdict { get; }

    /// <summary>Facts this check reached a verdict on.</summary>
    internal int Compared { get; }

    /// <summary>Facts the two sides genuinely disagreed about.</summary>
    internal int Differed { get; }

    /// <summary>
    /// Facts that agreed only within floating-point tolerance rather than bit for bit. Not a
    /// failure, and counted anyway: a port that only ever lands close has diverged in operation
    /// order somewhere, and that is an early warning worth keeping even once the rows are gone.
    /// </summary>
    internal int WithinTolerance { get; }

    internal int Agreed => Compared - Differed;

    /// <summary>Why a check could not finish, in the player's words. Empty when it did.</summary>
    internal string Reason { get; }

    /// <summary>The disagreements, rendered only when this finding is not an agreement.</summary>
    internal IReadOnlyList<string> Detail { get; }

    internal static VerificationFinding Agree(string subject, int compared, int withinTolerance = 0) =>
        compared == 0
            ? Inconclusive(subject, "nothing was comparable.")
            : new VerificationFinding(
                subject, VerificationVerdict.Agree, compared, 0, withinTolerance, string.Empty, null);

    internal static VerificationFinding Disagree(
        string subject,
        int compared,
        int differed,
        IReadOnlyList<string>? detail = null,
        int withinTolerance = 0) =>
        new(subject, VerificationVerdict.Disagree, compared, differed, withinTolerance, string.Empty, detail);

    internal static VerificationFinding Disagree(
        string subject,
        int compared,
        int differed,
        string detail) =>
        Disagree(subject, compared, differed, new[] { detail });

    /// <summary>Everything readable agreed, and something in scope was not readable.</summary>
    internal static VerificationFinding Incomplete(
        string subject,
        int compared,
        string reason,
        IReadOnlyList<string>? detail = null,
        int withinTolerance = 0) =>
        new(subject, VerificationVerdict.Incomplete, compared, 0, withinTolerance, reason, detail);

    internal static VerificationFinding Inconclusive(string subject, string reason) =>
        new(subject, VerificationVerdict.Inconclusive, 0, 0, 0, reason, null);

    /// <summary>The finding's own line, in the one verdict vocabulary.</summary>
    internal string Headline() => Verdict switch
    {
        VerificationVerdict.Inconclusive => $"{Subject} INCONCLUSIVE: {Reason}",
        VerificationVerdict.Incomplete =>
            $"{Subject} INCOMPLETE: {Compared} compared, all agree — {Reason}",
        VerificationVerdict.Disagree =>
            $"{Subject} DISAGREE: {Compared} compared, {Agreed} agree, {Differed} differ.",
        _ => $"{Subject} AGREE: {Compared} compared.",
    };
}
