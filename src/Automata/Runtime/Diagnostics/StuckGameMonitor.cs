using System;
using System.Collections.Generic;
using System.Text;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Turns the tick-fault observer's records into what a player and a bug report need: one log
/// entry per faulting element, a stuck-game notice while one element or view fails every run, and one
/// automatic bug report per lifecycle. Runs on the suite's own update, never in the tick.
/// </summary>
internal sealed class StuckGameMonitor
{
    internal const string AdvanceSentence =
        "The game itself hit an error and stopped advancing. Return to the main menu and reload your save.";

    internal const string MenuSentence =
        "The game itself hit an error and its menus stopped switching. Return to the main menu and reload your save.";

    internal const string ReportPending = " A bug report is being saved.";

    private readonly Func<object, string> _describe;
    private readonly Action<string> _logInfo;
    private readonly Action<string> _logError;
    private readonly Action<string, Action<string>, Action<string>> _requestBundle;
    private long _occurrence;
    private GameTickFaultSite? _stuck;
    private string? _report;

    internal StuckGameMonitor(
        Func<object, string> describe,
        Action<string> logInfo,
        Action<string> logError,
        Action<string, Action<string>, Action<string>> requestBundle)
    {
        _describe = describe ?? throw new ArgumentNullException(nameof(describe));
        _logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
        _logError = logError ?? throw new ArgumentNullException(nameof(logError));
        _requestBundle = requestBundle ?? throw new ArgumentNullException(nameof(requestBundle));
    }

    /// <summary>The player-facing notice, or null while the game is advancing.</summary>
    internal string? Notice { get; private set; }

    /// <summary>
    /// Shows the notice while an element is stuck and clears it once that element runs clean again.
    /// The bug report is written for the first stuck stretch of a lifecycle; a later one names the
    /// same report.
    /// </summary>
    internal void Update()
    {
        GameTickFaultObserver.Drain(_describe, LogFault);
        var repeating = GameTickFaultObserver.Repeating;
        if (repeating is null)
        {
            if (_stuck is not { } recovered) return;
            _stuck = null;
            Notice = null;
            _logInfo(
                (recovered == GameTickFaultSite.Element ? "The game is advancing again" : "The game's menus are switching again") +
                ": the failing element completed " + GameTickFaultObserver.RecoveryThreshold + " runs in a row.");
            return;
        }
        if (_stuck is not null) return;

        _stuck = repeating.Site;
        _logError(repeating.Site == GameTickFaultSite.Element
            ? "The game has stopped advancing: " + repeating.Headline +
              " Every tick fails at the same element, so development and managers that follow it are frozen until the save is reloaded."
            : "The game's menus have stopped switching: " + repeating.Headline +
              " Every frame fails at the same view, so views after it no longer show or hide until the save is reloaded; the game's tick still advances.");
        if (_report is not null)
        {
            Notice = Sentence(repeating.Site) + _report;
            return;
        }
        var occurrence = _occurrence;
        _report = ReportPending;
        Notice = Sentence(repeating.Site) + _report;
        _requestBundle(
            DescribeFaults(GameTickFaultObserver.Snapshot()),
            path => Settle(occurrence, " A bug report was saved to " + path + "; send it to the mod authors."),
            reason => Settle(occurrence, " The bug report could not be saved (" + reason + "); send BepInEx/LogOutput.log to the mod authors."));
    }

    /// <summary>A lifecycle transition ends the occurrence: native references and the notice go.</summary>
    internal void ResetLifecycle()
    {
        GameTickFaultObserver.Reset();
        _occurrence++;
        _stuck = null;
        _report = null;
        Notice = null;
    }

    internal static string Sentence(GameTickFaultSite site) =>
        site == GameTickFaultSite.Element ? AdvanceSentence : MenuSentence;

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

    private void Settle(long occurrence, string report)
    {
        if (occurrence != _occurrence) return;
        _report = report;
        if (_stuck is { } site) Notice = Sentence(site) + report;
    }
}
