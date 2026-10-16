using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpSemanticCaptureTests
{
    [Fact]
    public void TraceClosesOnlyAfterTheWorkerCycleReachesASettledBoundary()
    {
        using var evaluationEntered = new ManualResetEventSlim();
        using var evaluationRelease = new ManualResetEventSlim();
        var definition = new ExecutionServiceDefinition("trace.capture.settled")
        {
            ActionCount = 0,
            EvaluationEntered = evaluationEntered,
            EvaluationRelease = evaluationRelease,
        };
        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        var recorder = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(904),
            eventCapacity: 64,
            serviceCapacity: 1);
        using var pump = new SuiteFramePump(registry, recorder);
        TestWorldCollector.CollectedAtActivation(registry);
        var source = Assert.IsType<ServiceCycleSemanticTraceSource>(pump.SemanticTrace);

        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        Assert.True(evaluationEntered.Wait(ServiceCycleTestDeadline.Value));
        Assert.Equal(
            ServiceCycleSemanticTraceCloseResult.Pending,
            pump.TryCloseSemanticTraceAtSettledBoundary());

        evaluationRelease.Set();
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        Assert.Equal(1, pump.PumpFrame(2).ResponsesAcquired);
        Assert.Equal(
            ServiceCycleSemanticTraceCloseResult.Pending,
            pump.TryCloseSemanticTraceAtSettledBoundary());
        pump.PumpFrame(3);
        Assert.Equal(
            ServiceCycleSemanticTraceCloseResult.Closed,
            pump.TryCloseSemanticTraceAtSettledBoundary());
        var frozenCount = source.Count;

        pump.PumpFrame(4);

        Assert.Null(pump.SemanticTrace);
        Assert.Equal(frozenCount, source.Count);
        Assert.Equal(0UL, source.OverwrittenTotal);
    }

    [Fact]
    public void TraceDoesNotCloseWhileACapturedCycleAwaitsRequestPublication()
    {
        var definition = new ExecutionServiceDefinition("trace.capture.pending-publication")
        {
            ActionCount = 0,
        };
        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        var recorder = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(905),
            eventCapacity: 64,
            serviceCapacity: 1);
        using var pump = new SuiteFramePump(registry, recorder);
        TestWorldCollector.CollectedAtActivation(registry);
        using var contention = new HandoffGateContention(registration.Runner);
        definition.ShouldStartCallback = contention.Acquire;

        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);

        Assert.Equal(ServiceHandoffPhase.Empty, registration.Runner.HandoffPhaseHint);
        Assert.Equal(
            ServiceCycleSemanticTraceCloseResult.Pending,
            pump.TryCloseSemanticTraceAtSettledBoundary());
        Assert.NotNull(pump.SemanticTrace);
        contention.Release();
    }

    [Fact]
    public void LifecycleReplacementInvalidatesACapturedCycleThatCannotBePublished()
    {
        var definition = new ExecutionServiceDefinition("trace.capture.pending-lifecycle")
        {
            ActionCount = 0,
        };
        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        var recorder = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(908),
            eventCapacity: 64,
            serviceCapacity: 1);
        using var pump = new SuiteFramePump(registry, recorder);
        TestWorldCollector.CollectedAtActivation(registry);
        using var contention = new HandoffGateContention(registration.Runner);
        definition.ShouldStartCallback = contention.Acquire;

        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));

        Assert.Equal(
            ServiceCycleSemanticTraceCloseResult.Invalidated,
            pump.TryCloseSemanticTraceAtSettledBoundary());
        Assert.Null(pump.SemanticTrace);
        contention.Release();
    }

    [Fact]
    public void LifecycleReplacementInvalidatesAQueuedCycleTheWorkerHasNotStarted()
    {
        using var starter = new DeferredWorkerStarter();
        using var registry = new ServiceCycleRegistry(
            1,
            new ThreadSafeTestClock(100),
            measureWorkerAllocations: false,
            workerStarter: starter);
        using var registration = registry.Register(
            new ExecutionServiceDefinition("trace.capture.queued-lifecycle") { ActionCount = 0 },
            new LifecycleGeneration(1));
        registry.Seal();
        var recorder = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(909),
            eventCapacity: 64,
            serviceCapacity: 1);
        using var pump = new SuiteFramePump(registry, recorder);
        TestWorldCollector.CollectedAtActivation(registry);

        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        Assert.Equal(ServiceHandoffPhase.RequestReady, registration.Runner.HandoffPhaseHint);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));

        Assert.Equal(
            ServiceCycleSemanticTraceCloseResult.Invalidated,
            pump.TryCloseSemanticTraceAtSettledBoundary());
    }

    [Fact]
    public void LifecycleReplacementInsideCaptureInvalidatesTheUnpublishedCycle()
    {
        var definition = new SourceServiceDefinition("trace.capture.callback-lifecycle");
        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        using var registration = registry.RegisterSource(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        var recorder = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(910),
            eventCapacity: 64,
            serviceCapacity: 1);
        using var pump = new SuiteFramePump(registry, recorder);
        definition.CaptureCallback = () =>
            Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        ServiceRunnerTestWait.ForWorkerReady(registration);

        Assert.Equal(1, pump.PumpFrame(1).CapturesAttempted);

        Assert.Equal(
            ServiceCycleSemanticTraceCloseResult.Invalidated,
            pump.TryCloseSemanticTraceAtSettledBoundary());
    }

    [Fact]
    public void RemovingAServiceInvalidatesTheTraceEvenWhileItsWorkerIsExiting()
    {
        using var evaluationEntered = new ManualResetEventSlim();
        using var evaluationRelease = new ManualResetEventSlim();
        var definition = new ExecutionServiceDefinition("trace.capture.removed")
        {
            ActionCount = 0,
            EvaluationEntered = evaluationEntered,
            EvaluationRelease = evaluationRelease,
        };
        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        var recorder = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(906),
            eventCapacity: 64,
            serviceCapacity: 1);
        using var pump = new SuiteFramePump(registry, recorder);
        TestWorldCollector.CollectedAtActivation(registry);

        try
        {
            pump.PumpFrame(1);
            Assert.True(evaluationEntered.Wait(ServiceCycleTestDeadline.Value));
            registration.Dispose();

            Assert.Equal(
                ServiceCycleSemanticTraceCloseResult.Invalidated,
                pump.TryCloseSemanticTraceAtSettledBoundary());
            Assert.Null(pump.SemanticTrace);
        }
        finally
        {
            evaluationRelease.Set();
            registration.Dispose();
        }
    }

    private sealed class DeferredWorkerStarter : IServiceCycleWorkerStarter, IDisposable
    {
        private readonly List<Thread> _threads = new();

        public void Start(Thread thread) => _threads.Add(thread);

        public void Dispose()
        {
            foreach (var thread in _threads)
            {
                if ((thread.ThreadState & ThreadState.Unstarted) != 0) thread.Start();
            }
            foreach (var thread in _threads)
            {
                if (!thread.Join(ServiceCycleTestDeadline.Value))
                    throw new TimeoutException("A deferred test worker did not exit after shutdown.");
            }
        }
    }
}
