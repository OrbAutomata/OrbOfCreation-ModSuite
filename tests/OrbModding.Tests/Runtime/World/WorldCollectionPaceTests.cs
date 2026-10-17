using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The player's pace slows idle reading of the game, and nothing that waits on a reading.
/// </summary>
/// <remarks>
/// Everything runs through the real collection definition on the real pump, frame by frame on a
/// test clock, because the pace is a promise about when captures happen relative to everything else
/// on the pump — publication, the world gate, configuration and lifecycle — and only the pump keeps
/// all of those.
/// </remarks>
public sealed class WorldCollectionPaceTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan FastTick = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void AtCpuSaverAnIdleSuiteReadsTheGameEveryThirtySeconds()
    {
        using var suite = new PacedSuite(AutomationPace.CpuSaver);

        suite.RunFor(TimeSpan.FromSeconds(95));

        Assert.Equal(4, suite.Captures.Count);
        foreach (var gap in suite.Gaps())
            Assert.InRange(gap, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30) + FastTick + Frame);
    }

    [Fact]
    public void AtFastestTheIdleCadenceIsTheQuarterSecondItAlwaysWas()
    {
        using var suite = new PacedSuite(AutomationPace.Fastest);

        suite.RunFor(TimeSpan.FromSeconds(5));

        Assert.InRange(suite.Captures.Count, 14, 20);
        foreach (var gap in suite.Gaps())
            Assert.InRange(gap, FastTick, FastTick + Frame + Frame);
    }

    /// <summary>
    /// An MCP press waits up to a second for a world showing it, so a waiter is read for at the
    /// fastest cadence for as long as it is open, and the pace resumes once it closes.
    /// </summary>
    [Fact]
    public void AWaiterIsReadForWithinAQuarterSecondAtCpuSaver()
    {
        using var suite = new PacedSuite(AutomationPace.CpuSaver);
        suite.RunFor(TimeSpan.FromSeconds(2));
        Assert.Single(suite.Captures);

        suite.Demand.Begin();
        var opened = suite.Clock.Now;
        suite.RunFor(TimeSpan.FromSeconds(1));
        suite.Demand.End();

        Assert.True(suite.Captures.Count >= 4);
        Assert.InRange(
            Elapsed(opened, suite.Captures[1]),
            TimeSpan.Zero,
            FastTick + Frame);

        var afterClosing = suite.Captures.Count;
        suite.RunFor(TimeSpan.FromSeconds(25));
        Assert.Equal(afterClosing, suite.Captures.Count);
    }

    /// <summary>
    /// An automation's own action does not pull the next reading forward: the world gate holds it
    /// until the pace's next reading, and then it acts again rather than stalling.
    /// </summary>
    [Fact]
    public void AnAutomationAtCpuSaverActsOnceForEachReading()
    {
        var service = new ExecutionServiceDefinition("pace.automation")
        {
            ActionCount = 1,
            DefaultWakePolicy = WakePolicy.OnPublication,
        };
        using var suite = new PacedSuite(AutomationPace.CpuSaver, service);

        suite.RunFor(TimeSpan.FromSeconds(2));
        Assert.Single(suite.Captures);
        Assert.Equal(1, service.ActionExecutionCount);

        suite.RunFor(TimeSpan.FromSeconds(27));
        Assert.Single(suite.Captures);
        Assert.Equal(1, service.ActionExecutionCount);

        suite.RunFor(TimeSpan.FromSeconds(3));
        Assert.Equal(2, suite.Captures.Count);
        Assert.Equal(2, service.ActionExecutionCount);
    }

    [Fact]
    public void SpeedingUpAppliesAtOnceRatherThanAfterTheOldInterval()
    {
        using var suite = new PacedSuite(AutomationPace.CpuSaver);
        suite.RunFor(TimeSpan.FromSeconds(2));
        Assert.Single(suite.Captures);

        suite.Publish(AutomationPace.Fastest);
        var switched = suite.Clock.Now;
        suite.RunFor(TimeSpan.FromSeconds(1));

        Assert.True(suite.Captures.Count >= 4);
        Assert.InRange(Elapsed(switched, suite.Captures[1]), TimeSpan.Zero, Frame + Frame);
    }

    [Fact]
    public void SlowingDownAppliesFromTheLastReading()
    {
        using var suite = new PacedSuite(AutomationPace.Fastest);
        suite.RunFor(TimeSpan.FromSeconds(2));
        var last = suite.Captures[suite.Captures.Count - 1];

        suite.Publish(AutomationPace.Relaxed);
        suite.RunFor(TimeSpan.FromSeconds(6));

        var next = suite.Captures.FindIndex(capture => capture.Ticks > last.Ticks);
        Assert.Equal(suite.Captures.Count - 1, next);
        Assert.InRange(
            Elapsed(last, suite.Captures[next]),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5) + FastTick + Frame + Frame);
    }

    /// <summary>
    /// A load, reset, scene change or NG+ replaces every runner, and a new lifecycle has read
    /// nothing yet, so it reads at once whatever the pace.
    /// </summary>
    [Fact]
    public void ANewLifecycleReadsAtOnceAtCpuSaver()
    {
        using var suite = new PacedSuite(AutomationPace.CpuSaver);
        suite.RunFor(TimeSpan.FromSeconds(2));
        Assert.Single(suite.Captures);

        suite.Pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        var replaced = suite.Clock.Now;
        suite.RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(2, suite.Captures.Count);
        Assert.InRange(Elapsed(replaced, suite.Captures[1]), TimeSpan.Zero, FastTick + Frame);
    }

    [Fact]
    public void EachPaceNamesItsInterval()
    {
        AssertInterval(AutomationPace.Fastest, 250);
        AssertInterval(AutomationPace.Fast, 500);
        AssertInterval(AutomationPace.Balanced, 1000);
        AssertInterval(AutomationPace.Relaxed, 5000);
        AssertInterval(AutomationPace.CpuSaver, 30000);

        static void AssertInterval(AutomationPace pace, int milliseconds) =>
            Assert.Equal(
                MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)),
                AutomataWorldCollectionPolicies.Interval(pace));
    }

    private static TimeSpan Elapsed(MonotonicTimestamp from, MonotonicTimestamp to) =>
        TimeSpan.FromTicks(to.Ticks - from.Ticks);

    private sealed class PacedSuite : IDisposable
    {
        private readonly ServiceRegistration<
            AutomataWorldCollectionState,
            AutomataWorldCollectionAction> _collection;
        private readonly ServiceRegistration<ExecutionState, ExecutionAction>? _service;
        private long _frame = 10;

        internal PacedSuite(AutomationPace pace, ExecutionServiceDefinition? service = null)
        {
            Clock = new ThreadSafeTestClock(100);
            Registry = new ServiceCycleRegistry(service is null ? 1 : 2, Clock);
            Publish(pace);
            _collection = Registry.RegisterSource(
                AutomataWorldCollectionService.Define(
                    new ClockedCapture(this),
                    Registry.WorldPublication,
                    Demand),
                new LifecycleGeneration(1));
            if (service is not null)
                _service = Registry.Register(service, new LifecycleGeneration(1));
            Registry.Seal();
            Pump = new SuiteFramePump(Registry);
        }

        internal ThreadSafeTestClock Clock { get; }
        internal ServiceCycleRegistry Registry { get; }
        internal SuiteFramePump Pump { get; }
        internal AutomataWorldDemand Demand { get; } = new();
        internal List<MonotonicTimestamp> Captures { get; } = new();
        internal long CurrentFrame => _frame;

        internal void Publish(AutomationPace pace) =>
            Registry.Configuration.Publish(new SuiteRuntimeConfiguration
            {
                General = new SuiteGeneralConfiguration { Enabled = true, Pace = pace },
            });

        internal void RunFor(TimeSpan duration)
        {
            for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += Frame)
            {
                Settle(_collection);
                if (_service is not null) Settle(_service);
                Clock.Advance(MonotonicDuration.FromTimeSpan(Frame));
                Pump.PumpFrame(++_frame);
            }
        }

        internal IEnumerable<TimeSpan> Gaps()
        {
            for (var index = 1; index < Captures.Count; index++)
                yield return Elapsed(Captures[index - 1], Captures[index]);
        }

        public void Dispose()
        {
            Pump.Dispose();
            _service?.Dispose();
            _collection.Dispose();
            Registry.Dispose();
        }

        /// <summary>
        /// Lets the worker finish what it was handed before the next frame, as real frame time
        /// would; pumping test-clock frames faster than a busy worker returns would charge the
        /// worker's scheduling to the pace.
        /// </summary>
        private static void Settle<TState, TAction>(
            ServiceRegistration<TState, TAction> registration)
        {
            ServiceRunnerTestWait.ForHandoff(
                registration.Runner,
                handoff => handoff.Phase is not (ServiceHandoffPhase.RequestReady or ServiceHandoffPhase.Evaluating) &&
                    !handoff.CleanupPending,
                "an idle worker");
            ServiceRunnerTestWait.ForWorkerReady(registration);
        }
    }

    private sealed class ClockedCapture : IAutomataWorldCapturePort
    {
        private static readonly WorldCollectionReport Report =
            new(new WorldCategoryReport("resources", WorldCategoryOutcome.Collected, 0, 0, string.Empty));

        private readonly PacedSuite _suite;

        internal ClockedCapture(PacedSuite suite) => _suite = suite;

        public bool IsAvailable => true;

        public WorldCollectionReport Collect(GameWorldCycleFrame frame)
        {
            _suite.Captures.Add(_suite.Clock.Now);
            frame.CollectedAtFrame = _suite.CurrentFrame;
            frame.Report = Report;
            return Report;
        }
    }
}
