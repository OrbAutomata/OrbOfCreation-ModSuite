using System;
using System.Collections.Generic;
using System.Text;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Turns the tick-fault observer's records into what a player and a bug report need: one log
/// entry per faulting element, and — once one element fails every tick — one stuck-game notice
/// and one automatic bug report per lifecycle. Runs on the suite's own update, never in the tick.
/// </summary>
internal sealed class StuckGameMonitor
{
    internal const string StuckSentence =
        "The game itself hit an error and stopped advancing. Return to the main menu and reload your save.";

    private readonly Func<object, string> _describe;
    private readonly Action<string> _logError;
    private readonly Action<string, Action<string>, Action<string>> _requestBundle;
    private long _occurrence;
    private bool _stuck;

    internal StuckGameMonitor(
        Func<object, string> describe,
        Action<string> logError,
        Action<string, Action<string>, Action<string>> requestBundle)
    {
        _describe = describe ?? throw new ArgumentNullException(nameof(describe));
        _logError = logError ?? throw new ArgumentNullException(nameof(logError));
        _requestBundle = requestBundle ?? throw new ArgumentNullException(nameof(requestBundle));
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
        var occurrence = _occurrence;
        Notice = StuckSentence + " A bug report is being saved.";
        _logError(
            "The game has stopped advancing: " + repeating.Headline +
            " Every tick fails at the same element, so development, managers, and menus that follow it are frozen until the save is reloaded.");
        _requestBundle(
            DescribeFaults(GameTickFaultObserver.Snapshot()),
            path => Settle(occurrence, StuckSentence + " A bug report was saved to " + path + "; send it to the mod authors."),
            reason => Settle(occurrence, StuckSentence + " The bug report could not be saved (" + reason + "); send BepInEx/LogOutput.log to the mod authors."));
    }

    /// <summary>A lifecycle transition ends the occurrence: native references and the notice go.</summary>
    internal void ResetLifecycle()
    {
        GameTickFaultObserver.Reset();
        _occurrence++;
        _stuck = false;
        Notice = null;
    }

    internal static string DescribeFaults(IReadOnlyList<GameTickFaultReport> faults)
    {
        var text = new StringBuilder();
        foreach (var fault in faults)
        {
            text.AppendLine(fault.Headline);
            text.AppendLine(fault.FirstTrace);
            text.AppendLine();
        }
        return text.ToString();
    }

    private void LogFault(GameTickFaultReport report) =>
        _logError(report.Headline + " Later repeats are counted, not logged.\n" + report.FirstTrace);

    private void Settle(long occurrence, string notice)
    {
        if (occurrence == _occurrence && _stuck) Notice = notice;
    }
}
