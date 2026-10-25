using OrbModding.TestSupport;
using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

/// <summary>
/// Pumping a frame is how a test waits for the worker, and it must yield while it does.
/// </summary>
/// <remarks>
/// The pump does its work on the calling thread, so a bare <c>while (pump.PumpFrame(f++).X == 0) { }</c>
/// holds a core at full tilt against the very worker it is waiting for. Measured on a loaded machine,
/// waiting for the capture after a faulted batch took 86 to 1908 frames that way and exactly 2 with a
/// yield. Every one of those frames emits semantic trace events, so a spinning wait overruns the
/// fixture's event ring and the next drain reports events dropped — which is a real assertion about
/// what was emitted, failing for a reason that has nothing to do with what the test is about.
/// </remarks>
internal static class ServiceCyclePumpTestWait
{
    /// <summary>
    /// Pumps until a cycle starts, yielding between frames, and returns that frame's report.
    /// </summary>
    internal static SuiteFramePumpReport UntilStart(SuiteFramePump pump, ref long frame) =>
        Until(pump, ref frame, report => report.CyclesStarted != 0, "a started cycle");

    /// <summary>
    /// Pumps until a worker response is acquired, yielding between frames, and returns that frame's report.
    /// </summary>
    internal static SuiteFramePumpReport UntilResponse(SuiteFramePump pump, ref long frame) =>
        Until(pump, ref frame, report => report.ResponsesAcquired != 0, "an acquired response");

    /// <summary>Pumps until an action is attempted, yielding between frames.</summary>
    internal static void UntilAction(SuiteFramePump pump, ref long frame) =>
        Until(pump, ref frame, report => report.ActionsAttempted != 0, "an attempted action");

    /// <summary>
    /// Pumps until a frame starts its turn order at <paramref name="ordinal"/>, yielding between frames.
    /// </summary>
    internal static void UntilStartingOrdinal(SuiteFramePump pump, ref long frame, int ordinal) =>
        Until(
            pump,
            ref frame,
            report => report.StartingOrdinal == ordinal,
            $"a frame starting at ordinal {ordinal}");

    private static SuiteFramePumpReport Until(
        SuiteFramePump pump,
        ref long frame,
        Func<SuiteFramePumpReport, bool> reached,
        string expectation)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var report = pump.PumpFrame(frame++);
            if (reached(report)) return report;
            if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                throw new TimeoutException($"The pump never reported {expectation}.");
            Thread.Yield();
        }
    }

    internal static void PrepareBatch(
        SuiteFramePump pump,
        ServiceRegistration<LifecycleState, LifecycleAction> registration,
        ref long frame)
    {
        WaitForResponse(pump, registration, ref frame);
        PumpUntil(pump, ref frame, () => registration.Runner.Snapshot.ActionCount != 0);
    }

    /// <summary>
    /// Pumps until the fixture publishes a response, optionally reading the world every frame the way
    /// production's collection service does. <paramref name="collector"/> carries the same meaning as
    /// on <see cref="PumpUntil"/>.
    /// </summary>
    internal static void WaitForResponse(
        SuiteFramePump pump,
        ServiceRegistration<LifecycleState, LifecycleAction> registration,
        ref long frame,
        ServiceCycleRegistry? collector = null)
    {
        var timeout = ServiceCycleTestDeadline.Value;
        var deadline = Stopwatch.StartNew();
        while (registration.Runner.HandoffPhaseHint != ServiceHandoffPhase.ResponseReady)
        {
            if (registration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty)
            {
                var identity = frame++;
                pump.PumpFrame(identity);
                if (collector is not null) TestWorldCollector.CollectedAt(collector, identity);
            }
            else
                Thread.Yield();
            if (deadline.Elapsed > timeout)
                throw new TimeoutException("The lifecycle fixture did not publish its response.");
        }
        var remaining = timeout - deadline.Elapsed;
        if (remaining <= TimeSpan.Zero || !registration.WaitForResponseReady(remaining))
            throw new TimeoutException("The lifecycle fixture response did not settle.");
    }

    /// <summary>
    /// Pumps until <paramref name="condition"/> holds, optionally reading the world every frame the
    /// way production's collection service does.
    /// </summary>
    /// <remarks>
    /// Supply <paramref name="collector"/> whenever the wait is for a service that changes the game
    /// to run again. Such a service does not start another cycle until a reading stamped after its
    /// last action arrives, so a wait with no collector pumps against a world frozen at the seed
    /// generation and never ends. Callers that pass one start their frame counter at 2, because the
    /// seed publication already holds generation 1.
    /// </remarks>
    internal static void PumpUntil(
        SuiteFramePump pump,
        ref long frame,
        Func<bool> condition,
        ThreadSafeTestClock? progressClock = null,
        ServiceCycleRegistry? collector = null)
    {
        var progressStep = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(16));
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            var identity = frame++;
            pump.PumpFrame(identity);
            if (collector is not null) TestWorldCollector.CollectedAt(collector, identity);
            progressClock?.Advance(progressStep);
            if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                throw new TimeoutException("The lifecycle fixture did not reach its expected phase.");
            Thread.Yield();
        }
    }
}
