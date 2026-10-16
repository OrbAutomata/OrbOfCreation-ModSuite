using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.Runtime.Diagnostics;

[Collection(GameTickFaultObserverCollection.Name)]
public sealed class StuckGameMonitorTests : IDisposable
{
    private readonly List<string> _errors = new();
    private readonly List<string> _infos = new();
    private readonly List<string> _bundles = new();
    private Action<string>? _written;
    private Action<string>? _failed;

    public StuckGameMonitorTests() => GameTickFaultObserver.Reset();

    public void Dispose() => GameTickFaultObserver.Reset();

    [Fact]
    public void AnOccasionalFaultIsLoggedOnceAndLeavesThePlayerAlone()
    {
        var monitor = Monitor();
        Throw(new ThrowingElement(), 3);

        monitor.Update();
        monitor.Update();

        var logged = Assert.Single(_errors);
        Assert.StartsWith("The game's own tick threw System.NullReferenceException from Faulting element (3x this lifecycle).", logged);
        Assert.Contains("Later repeats are counted, not logged.", logged);
        Assert.Null(monitor.Notice);
        Assert.Empty(_bundles);
    }

    [Fact]
    public void AnElementFailingEveryTickShowsOneNoticeAndRequestsOneReport()
    {
        var monitor = Monitor();
        var element = new ThrowingElement();
        Throw(element, GameTickFaultObserver.RepeatThreshold);

        monitor.Update();
        Throw(element, GameTickFaultObserver.RepeatThreshold);
        monitor.Update();

        Assert.Equal(StuckGameMonitor.StuckSentence + " A bug report is being saved.", monitor.Notice);
        var faults = Assert.Single(_bundles);
        Assert.Contains("(50x this lifecycle)", faults);
        Assert.Contains(nameof(ThrowingElement.Increment), faults);
        Assert.Contains(_errors, error => error.StartsWith("The game has stopped advancing:", StringComparison.Ordinal));
    }

    [Fact]
    public void TheNoticeNamesTheSavedReport()
    {
        var monitor = Stuck();

        _written!("/game/BepInEx/config/OrbOfCreation-ModSuite/diagnostics/report.zip");

        Assert.Equal(
            "The game itself hit an error and stopped advancing. Return to the main menu and reload your save. " +
            "A bug report was saved to /game/BepInEx/config/OrbOfCreation-ModSuite/diagnostics/report.zip; " +
            "send it to the mod authors.",
            monitor.Notice);
    }

    [Fact]
    public void AReportThatCouldNotBeSavedPointsAtTheLogInstead()
    {
        var monitor = Stuck();

        _failed!("disk full");

        Assert.Equal(
            StuckGameMonitor.StuckSentence +
            " The bug report could not be saved (disk full); send BepInEx/LogOutput.log to the mod authors.",
            monitor.Notice);
    }

    [Fact]
    public void ALifecycleChangeClearsTheNoticeAndALateReportDoesNotBringItBack()
    {
        var monitor = Stuck();

        monitor.ResetLifecycle();
        _written!("late.zip");

        Assert.Null(monitor.Notice);
        Assert.Null(GameTickFaultObserver.Repeating);
    }

    [Fact]
    public void TheNextLifecycleThatSticksIsANewOccurrence()
    {
        var monitor = Stuck();
        monitor.ResetLifecycle();

        Throw(new ThrowingElement(), GameTickFaultObserver.RepeatThreshold);
        monitor.Update();

        Assert.Equal(2, _bundles.Count);
        Assert.NotNull(monitor.Notice);
    }

    [Fact]
    public void TheNoticeClearsOnceTheElementRunsCleanForTheRecoveryStretch()
    {
        var element = new ThrowingElement();
        var monitor = Stuck(element);

        Complete(element, GameTickFaultObserver.RecoveryThreshold - 1);
        monitor.Update();
        Assert.NotNull(monitor.Notice);
        Complete(element, 1);
        monitor.Update();

        Assert.Null(monitor.Notice);
        Assert.StartsWith("The game is advancing again", Assert.Single(_infos));
    }

    [Fact]
    public void StickingAgainInTheSameLifecycleNamesTheSameReport()
    {
        var element = new ThrowingElement();
        var monitor = Stuck(element);
        _written!("report.zip");
        Complete(element, GameTickFaultObserver.RecoveryThreshold);
        monitor.Update();

        Throw(element, GameTickFaultObserver.RepeatThreshold);
        monitor.Update();

        Assert.Single(_bundles);
        Assert.Equal(
            StuckGameMonitor.StuckSentence + " A bug report was saved to report.zip; send it to the mod authors.",
            monitor.Notice);
    }

    private StuckGameMonitor Stuck(ThrowingElement? element = null)
    {
        var monitor = Monitor();
        Throw(element ?? new ThrowingElement(), GameTickFaultObserver.RepeatThreshold);
        monitor.Update();
        return monitor;
    }

    private static void Complete(ThrowingElement element, int times)
    {
        element.Throws = false;
        for (var index = 0; index < times; index++) GameTickFaultObserver.IncrementElement(element, 0.02f);
        element.Throws = true;
    }

    private StuckGameMonitor Monitor() =>
        new(
            _ => "Faulting element",
            _infos.Add,
            _errors.Add,
            (faults, written, failed) =>
            {
                _bundles.Add(faults);
                _written = written;
                _failed = failed;
            });

    private static void Throw(ThrowingElement element, int times)
    {
        for (var index = 0; index < times; index++)
            Assert.Throws<NullReferenceException>(() => GameTickFaultObserver.IncrementElement(element, 0.02f));
    }

    private sealed class ThrowingElement : IIncrement
    {
        internal bool Throws { get; set; } = true;

        public void Increment(float deltaTime)
        {
            if (Throws) throw new NullReferenceException("native");
        }
    }
}
