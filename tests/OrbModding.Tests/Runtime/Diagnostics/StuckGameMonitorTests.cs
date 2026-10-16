using System;
using System.Collections.Generic;
using System.Diagnostics;
using OrbAutomata;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.Runtime.Diagnostics;

[Collection(GameTickFaultObserverCollection.Name)]
public sealed class StuckGameMonitorTests : IDisposable
{
    private readonly List<string> _errors = new();

    public StuckGameMonitorTests()
    {
        GameTickFaultObserver.Reset();
        GameTickFaultObserver.UseClockForTests(() => 0);
    }

    public void Dispose()
    {
        GameTickFaultObserver.Reset();
        GameTickFaultObserver.UseClockForTests(Stopwatch.GetTimestamp);
    }

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
    }

    [Fact]
    public void AnElementFailingEveryTickShowsOneNotice()
    {
        var monitor = Monitor();
        var element = new ThrowingElement();
        Throw(element, GameTickFaultObserver.RepeatThreshold);

        monitor.Update();
        Throw(element, GameTickFaultObserver.RepeatThreshold);
        monitor.Update();

        Assert.Equal(StuckGameMonitor.StuckSentence, monitor.Notice);
        Assert.Single(_errors, error => error.StartsWith("The game has stopped advancing:", StringComparison.Ordinal));
    }

    [Fact]
    public void ALifecycleChangeClearsTheNotice()
    {
        var monitor = Stuck();

        monitor.ResetLifecycle();

        Assert.Null(monitor.Notice);
        Assert.Null(GameTickFaultObserver.Repeating);
    }

    [Fact]
    public void TheNextLifecycleThatSticksShowsTheNoticeAgain()
    {
        var monitor = Stuck();
        monitor.ResetLifecycle();

        Throw(new ThrowingElement(), GameTickFaultObserver.RepeatThreshold);
        monitor.Update();

        Assert.Equal(StuckGameMonitor.StuckSentence, monitor.Notice);
    }

    private StuckGameMonitor Stuck()
    {
        var monitor = Monitor();
        Throw(new ThrowingElement(), GameTickFaultObserver.RepeatThreshold);
        monitor.Update();
        return monitor;
    }

    private StuckGameMonitor Monitor() => new(_ => "Faulting element", _errors.Add);

    private static void Throw(ThrowingElement element, int times)
    {
        for (var index = 0; index < times; index++)
            Assert.Throws<NullReferenceException>(() => GameTickFaultObserver.IncrementElement(element, 0.02f));
    }

    private sealed class ThrowingElement : IIncrement
    {
        public void Increment(float deltaTime) => throw new NullReferenceException("native");
    }
}
