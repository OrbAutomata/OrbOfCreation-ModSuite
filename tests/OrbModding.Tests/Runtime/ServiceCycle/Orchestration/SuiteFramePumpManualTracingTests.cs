using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpManualTracingTests
{
    [Fact]
    public void ManualTraceAttachesAndDetachesAtSettledBoundariesWithoutAHostTrace()
    {
        using var registry = Registry("trace.manual.only");
        using var pump = new SuiteFramePump(registry);
        var observer = new RecordingObserver();
        var recorder = Recorder(new ServiceCycleTraceSessionId(201), observer);

        Assert.True(pump.TryAttachManualSemanticTrace(recorder, out var attached));
        Assert.NotNull(attached);
        Assert.Null(pump.SemanticTrace);
        var roots = observer.Events.Count;

        var report = pump.PumpFrame(1);

        Assert.True(report.Accepted);
        Assert.True(observer.Events.Count > roots);
        Assert.Contains(observer.Events, item => item.Kind == ServiceCycleSemanticEventKind.PumpCompleted);
        Assert.True(pump.TryDetachManualSemanticTrace(attached!));
        var detachedCount = observer.Events.Count;
        pump.PumpFrame(2);
        Assert.Equal(detachedCount, observer.Events.Count);
    }

    [Fact]
    public void ManualTraceWaitsForEmergencyClearInsteadOfInventingAnEntryTransition()
    {
        using var registry = Registry("trace.manual.emergency-boundary");
        using var pump = new SuiteFramePump(registry);
        var observer = new RecordingObserver();
        var recorder = Recorder(new ServiceCycleTraceSessionId(401), observer);

        pump.SetEmergencyStop(true);
        Assert.False(pump.TryAttachManualSemanticTrace(recorder, out var pending));
        Assert.Null(pending);
        Assert.Empty(observer.Events);

        pump.SetEmergencyStop(false);
        Assert.True(pump.TryAttachManualSemanticTrace(recorder, out var attached));
        Assert.NotNull(attached);
        Assert.DoesNotContain(observer.Events, item =>
            item.Kind is ServiceCycleSemanticEventKind.EmergencyEntered or
                ServiceCycleSemanticEventKind.EmergencyCleared);
        Assert.True(pump.TryDetachManualSemanticTrace(attached!));
    }

    [Fact]
    public void ManualObserverFailureFaultsOnlyItsTrace()
    {
        using var registry = Registry("trace.manual.observer-fault");
        using var pump = new SuiteFramePump(registry);
        var recorder = Recorder(new ServiceCycleTraceSessionId(501), new ThrowingObserver());

        Assert.True(pump.TryAttachManualSemanticTrace(recorder, out var attached));
        Assert.True(attached!.IsFaulted);

        var report = pump.PumpFrame(1);
        Assert.True(report.Accepted);
        Assert.True(pump.TryDetachManualSemanticTrace(attached));
    }

    private static ServiceCycleRegistry Registry(string serviceId)
    {
        var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        var definition = new ExecutionServiceDefinition(serviceId)
        {
            StartDecision = ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(new MonotonicDuration(1))),
        };
        registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        return registry;
    }

    private static ServiceCycleSemanticRecorder Recorder(
        ServiceCycleTraceSessionId session,
        IServiceCycleSemanticEventObserver observer) => new(
            session,
            eventCapacity: 1,
            serviceCapacity: 1,
            enabled: true,
            observer: observer);

    private sealed class RecordingObserver : IServiceCycleSemanticEventObserver
    {
        internal List<ServiceCycleSemanticEvent> Events { get; } = new();
        public void Observe(in ServiceCycleSemanticEvent item) => Events.Add(item);
    }

    private sealed class ThrowingObserver : IServiceCycleSemanticEventObserver
    {
        public void Observe(in ServiceCycleSemanticEvent item) =>
            throw new InvalidOperationException("Injected observation failure.");
    }
}
