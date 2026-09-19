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
    /// which is the state every host starts in and the one a test puts back when it is done.
    /// </summary>
    internal static void ConfigureLog(Action<string>? log)
    {
        lock (Sync)
        {
            _log = log;
        }
    }

    /// <summary>
    /// Writes the whole exception to the suite log under a fresh reference and returns the sentence
    /// fragment naming that reference, or an empty string when there is nothing to record or
    /// nowhere to record it.
    /// </summary>
    internal static string Record(Exception? exception, string screen)
    {
        Action<string>? log;
        lock (Sync)
        {
            log = _log;
        }
        if (exception is null || log is null) return string.Empty;
        var reference = "MCP-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        log("Game action fault " + reference + " on " + screen + ": " + exception);
        return " Reference " + reference + " is in the suite log.";
    }
}
