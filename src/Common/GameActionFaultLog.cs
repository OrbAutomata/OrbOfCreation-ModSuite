using System;

namespace OrbModding.Common;

/// <summary>
/// Where a native exception's own text goes now that no GameAction wire sentence carries it.
/// </summary>
/// <remarks>
/// <para>
/// That text names .NET types and members from inside the suite and the game. A caller can act on
/// none of it, which is why <see cref="GameActionAnswer"/> stopped shipping it — and it is exactly
/// what whoever fixes the defect needs. So it goes to the suite log under a generated reference,
/// and the answer ends with that reference the way the MCP router's internal-error answer does.
/// </para>
/// <para>
/// The delegate is what keeps this assembly from depending on the plugin: OrbAutomata hands its own
/// logger in at plugin start. A host that never does — a test, a tool — records nothing and leaves
/// the sentence exactly as it was. The text never falls back onto the wire.
/// </para>
/// </remarks>
internal static class GameActionFaultLog
{
    private static readonly object Sync = new();
    private static Action<string>? _log;

    /// <summary>
    /// Names where the suite writes. <see langword="null"/> says this host has nowhere to write,
    /// which is the state every host starts in and the one a test puts back when it is done. The
    /// committed-after-throw references name entries in the previous log, so they go.
    /// </summary>
    internal static void ConfigureLog(Action<string>? log)
    {
        lock (Sync)
        {
            _log = log;
#if SERVICE_CYCLE_PROFILE
            CommittedAfterThrow.Clear();
#endif
        }
    }

    /// <summary>
    /// Writes the whole exception to the suite log under a fresh reference and returns the sentence
    /// fragment naming that reference, or an empty string when there is nothing to record or
    /// nowhere to record it. It suits a one-off answer; an answer whose repeats something compares
    /// keeps the reference apart through <see cref="Reason"/>. Its result never goes into a
    /// GameAction reason, which would take it whole as the sentence and see every attempt as new.
    /// </summary>
    internal static string Record(Exception? exception, string screen) =>
        Sentence(RecordReference(exception, screen));

    /// <summary>
    /// <paramref name="sentence"/>, with the reference <paramref name="exception"/> was written under
    /// kept apart from it.
    /// </summary>
    internal static GameActionReason Reason(string sentence, Exception? exception, string screen) =>
        new(sentence, RecordReference(exception, screen));

    /// <summary>
    /// Writes the exception and answers the bare reference, such as <c>MCP-1A2B3C4D</c>.
    /// <paramref name="putBack"/> is what undoing the press threw after <paramref name="exception"/>,
    /// written into the same entry after the cause.
    /// </summary>
    internal static string RecordReference(Exception? exception, string screen, Exception? putBack = null) =>
        RecordReference(exception, screen, "Putting the press back", putBack);

    /// <summary>
    /// The same, for a cause followed by what <paramref name="afterwards"/> then threw, both in one
    /// entry. Either may be missing; with neither there is nothing to record.
    /// </summary>
    internal static string RecordReference(
        Exception? exception,
        string screen,
        string afterwards,
        Exception? afterwardsThrew)
    {
        var detail = exception?.ToString() ?? string.Empty;
        if (afterwardsThrew is not null)
            detail += (detail.Length == 0 ? string.Empty : Environment.NewLine) +
                afterwards + " then threw: " + afterwardsThrew;
        return Write(detail, screen);
    }

    /// <summary>
    /// The bare reference for a maintainer's fact that never was an exception, such as which member
    /// of which type would not bind.
    /// </summary>
    internal static string RecordDetail(string detail, string screen) => Write(detail, screen);

    /// <summary>The sentence fragment naming <paramref name="reference"/>, or empty for none.</summary>
    internal static string Sentence(string reference) =>
        reference.Length == 0 ? string.Empty : " Reference " + reference + " is in the suite log.";

    private static string Write(string detail, string screen)
    {
        Action<string>? log;
        lock (Sync)
        {
            log = _log;
        }
        if (detail.Length == 0 || log is null) return string.Empty;
        var reference = "MCP-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        log("Game action fault " + reference + " on " + screen + ": " + detail);
        return reference;
    }
}
