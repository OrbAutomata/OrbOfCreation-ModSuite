using System;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Turns the tick-fault observer's records into what a player and a bug report need: one log
/// entry per faulting element, and — once one element fails every tick — one stuck-game notice
/// per lifecycle. Runs on the suite's own update, never in the tick.
/// </summary>
internal sealed class StuckGameMonitor
{
    internal const string StuckSentence =
        "The game itself hit an error and stopped advancing. Return to the main menu and reload your save.";

    private readonly Func<object, string> _describe;
    private readonly Action<string> _logError;
    private bool _stuck;

    internal StuckGameMonitor(Func<object, string> describe, Action<string> logError)
    {
        _describe = describe ?? throw new ArgumentNullException(nameof(describe));
        _logError = logError ?? throw new ArgumentNullException(nameof(logError));
    }

    /// <summary>The player-facing notice, or null while the game is advancing.</summary>
    internal string? Notice { get; private set; }

    internal void Update()
    {
        GameTickFaultObserver.Drain(_describe, LogFault);
        if (_stuck) return;
        var repeating = GameTickFaultObserver.Repeating;
        if (repeating is null) return;

        _stuck = true;
        Notice = StuckSentence;
        _logError(
            "The game has stopped advancing: " + repeating.Headline +
            " Every tick fails at the same element, so development, managers, and menus that follow it are frozen until the save is reloaded.");
    }

    /// <summary>A lifecycle transition ends the occurrence: native references and the notice go.</summary>
    internal void ResetLifecycle()
    {
        GameTickFaultObserver.Reset();
        _stuck = false;
        Notice = null;
    }

    private void LogFault(GameTickFaultReport report) =>
        _logError(report.Headline + " Later repeats are counted, not logged.\n" + report.FirstTrace);
}
