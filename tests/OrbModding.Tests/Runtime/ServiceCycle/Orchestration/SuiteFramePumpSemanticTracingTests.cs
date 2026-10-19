using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpSemanticTracingTests
{
    [Fact]
    public void AdvancingClockProducesAForwardCausalGraphAndEncodableSnapshot()
    {
        var clock = new IncrementingTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.advancing-clock") { ActionCount = 0 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;

        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

        var source = Assert.IsType<ServiceCycleSemanticTraceSource>(pump.SemanticTrace);
        var events = new ServiceCycleSemanticEvent[source.Capacity];
        var drain = source.DrainSince(default, events);
        Assert.True(drain.IsComplete);
        var resident = events[..drain.Copied];
        var document = new ServiceCycleTraceDocument(
            ServiceCycleTraceCodec.SchemaVersion,
            source.Session,
            drain.Dropped,
            resident);
        var graph = ServiceCycleTraceGraphValidator.Validate(document);
        Assert.True(graph.IsValid, $"Causal graph failed with {graph.Issue} at {graph.EventIndex}.");
        var encoded = new byte[ServiceCycleTraceCodec.GetEncodedLength(resident.Length)];
        Assert.Equal(encoded.Length, ServiceCycleTraceCodec.Encode(
            source.Session, drain.Dropped, resident, encoded));

        var queued = Assert.Single(resident, value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);
        var started = Assert.Single(resident, value => value.Kind == ServiceCycleSemanticEventKind.CycleStarted);
        var projected = Assert.Single(resident, value => value.Kind == ServiceCycleSemanticEventKind.StatePublished);
        var completed = Assert.Single(resident, value => value.Kind == ServiceCycleSemanticEventKind.EvaluationCompleted);
        Assert.True(queued.Payload.TimestampTicks <= started.Payload.TimestampTicks);
        Assert.True(projected.Payload.TimestampTicks <= completed.Payload.TimestampTicks);
    }

    [Fact]
    public void SuccessfulCyclePublishesExactCommittedFactsInRuntimeOrder()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.success") { ActionCount = 1 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        var recorder = Recorder(1);
        using var pump = new SuiteFramePump(registry, recorder);
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);

        var initial = Drain(pump, ref cursor);
        Assert.Equal(
            new[]
            {
                ServiceCycleSemanticEventKind.ConfigurationPublished,
                ServiceCycleSemanticEventKind.StrategyPublished,
                ServiceCycleSemanticEventKind.LifecycleActivated,
            },
            initial.Select(value => value.Kind));
        // The suite publishes one configuration record and one strategy bulletin, so those two name
        // no service; only the lifecycle activation is this service's own.
        Assert.Equal(0UL, initial[0].Payload.Service);
        Assert.Equal(0UL, initial[1].Payload.Service);
        Assert.Equal(1UL, initial[2].Payload.Service);
        Assert.Equal(1UL, initial[0].Payload.Configuration);
        Assert.Equal(1UL, initial[1].Payload.Strategy);
        Assert.Equal(1UL, initial[2].Payload.Lifecycle);

        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        ServiceCyclePumpTestWait.UntilAction(pump, ref frame);

        var events = Drain(pump, ref cursor);
        AssertOrderedSubsequence(
            events,
            ServiceCycleSemanticEventKind.StartAttempted,
            ServiceCycleSemanticEventKind.StartReady,
            ServiceCycleSemanticEventKind.CycleQueued,
            ServiceCycleSemanticEventKind.CycleStarted,
            ServiceCycleSemanticEventKind.EvaluationStarted,
            ServiceCycleSemanticEventKind.StatePublished,
            ServiceCycleSemanticEventKind.EvaluationCompleted,
            ServiceCycleSemanticEventKind.BatchPublished,
            ServiceCycleSemanticEventKind.ActionAttempted,
            ServiceCycleSemanticEventKind.ActionCommitted,
            ServiceCycleSemanticEventKind.BatchCompleted,
            ServiceCycleSemanticEventKind.CycleCompleted);

        var serviceEvents = events.Where(value => value.Payload.Service == 1).ToArray();
        var cycle = serviceEvents.First(value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued).Payload;
        Assert.All(
            serviceEvents.Where(value =>
                value.Kind is >= ServiceCycleSemanticEventKind.CycleQueued and <= ServiceCycleSemanticEventKind.BatchCompleted),
            value =>
            {
                Assert.Equal(cycle.Lifecycle, value.Payload.Lifecycle);
                Assert.Equal(cycle.Configuration, value.Payload.Configuration);
                Assert.Equal(cycle.Cycle, value.Payload.Cycle);
            });
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.ActionAttempted);
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.ActionCommitted);
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.BatchCompleted);
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.CycleCompleted);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void SkippedActionIsTracedAndDoesNotTerminateTheBatch(
        bool beforeNative,
        long expectedMutationAttempts)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.skipped")
        {
            ActionCount = 2,
            SkipAtIndex = beforeNative ? -1 : 0,
            SkipWithoutNativeAtIndex = beforeNative ? 0 : -1,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);
        Drain(pump, ref cursor);

        Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);
        Assert.False(registration.Runner.Snapshot.PreviousReceipt.IsPresent);
        Assert.Equal(1, pump.PumpFrame(frame).ActionsAttempted);

        var events = Drain(pump, ref cursor);
        AssertOrderedSubsequence(
            events,
            ServiceCycleSemanticEventKind.ActionAttempted,
            ServiceCycleSemanticEventKind.ActionSkipped,
            ServiceCycleSemanticEventKind.ActionAttempted,
            ServiceCycleSemanticEventKind.ActionCommitted,
            ServiceCycleSemanticEventKind.BatchCompleted,
            ServiceCycleSemanticEventKind.CycleCompleted);
        var skipped = Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.ActionSkipped);
        Assert.Equal(expectedMutationAttempts, skipped.Payload.MutationAttempts);
        Assert.Equal(0, skipped.Payload.MutationsCommitted);
        Assert.Equal(1, registration.Runner.Snapshot.PreviousReceipt.SkippedCount);
    }

    [Fact]
    public void LatestConfigurationAndStrategyPublicationsAreObservedWithoutStartingAServiceCycle()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.publications")
        {
            StartDecision = ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(new MonotonicDuration(1))),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));
        registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(2));
        pump.PumpFrame(1);

        var events = Drain(pump, ref cursor);
        var configuration = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.ConfigurationPublished);
        var strategyEvent = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.StrategyPublished);
        Assert.Equal(2UL, configuration.Payload.Configuration);
        Assert.Equal(2UL, strategyEvent.Payload.Strategy);
        Assert.DoesNotContain(events, value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);
    }

    /// <summary>
    /// A publication is one event however many services are registered.
    /// </summary>
    /// <remarks>
    /// The suite has one configuration record and one strategy bulletin. Emitting per registered
    /// service restated the same generation N times and implied a per-service publication the
    /// runtime has never had.
    /// </remarks>
    [Fact]
    public void OnePublicationIsOneEventWhateverTheServiceCount()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(3, clock);
        using var first = registry.Register(
            new ExecutionServiceDefinition("trace.runtime.publications.a"),
            new LifecycleGeneration(1));
        using var second = registry.Register(
            new ExecutionServiceDefinition("trace.runtime.publications.b"),
            new LifecycleGeneration(1));
        using var third = registry.Register(
            new ExecutionServiceDefinition("trace.runtime.publications.c"),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(3));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        var bound = Drain(pump, ref cursor);

        Assert.Single(
            bound,
            value => value.Kind == ServiceCycleSemanticEventKind.ConfigurationPublished);
        Assert.Single(
            bound,
            value => value.Kind == ServiceCycleSemanticEventKind.StrategyPublished);

        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));
        registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(2));
        pump.PumpFrame(1);

        var events = Drain(pump, ref cursor);
        var configuration = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.ConfigurationPublished);
        var strategy = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.StrategyPublished);
        Assert.Equal(2UL, configuration.Payload.Configuration);
        Assert.Equal(2UL, strategy.Payload.Strategy);
        Assert.Equal(0UL, configuration.Payload.Service);
        Assert.Equal(0UL, strategy.Payload.Service);
    }

    [Fact]
    public void ConfigurationPinnedAfterFrameStartIsPublishedBeforeTheCycleThatPinnedIt()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var publisher = new ExecutionServiceDefinition("trace.runtime.config-publisher");
        var target = new ExecutionServiceDefinition("trace.runtime.config-target");
        using var publisherRegistration = registry.Register(
            publisher,
            new LifecycleGeneration(1));
        using var targetRegistration = registry.Register(
            target,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(2));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);
        publisher.ShouldStartCallback = () =>
            registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));

        pump.PumpFrame(1);

        var events = Drain(pump, ref cursor);
        var configuration = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.ConfigurationPublished &&
                value.Payload.Configuration == 2);
        var queued = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued &&
                value.Payload.Service == 2);
        // One publication for the suite rather than one per service, so the record it produces is
        // not the target cycle's causal parent; what it must still be is earlier than that cycle.
        Assert.Equal(0UL, configuration.Payload.Service);
        Assert.True(
            configuration.Id.Sequence < queued.Id.Sequence,
            "The publication must be recorded before the cycle that pinned it.");
        Assert.Equal(2UL, queued.Payload.Configuration);
    }

    /// <summary>
    /// The generation a cycle carries is the bound publisher's, not a number the service chose.
    /// </summary>
    /// <remarks>
    /// Every service used to return a hardcoded one from its capture, which happened to be right only
    /// because nothing published a bulletin yet. See W49.
    /// </remarks>
    [Fact]
    public void ACycleIsStampedWithTheSuitesLatestStrategy()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.strategy-stamped");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);
        registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(2));
        registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(3));

        pump.PumpFrame(1);

        var cycle = Assert.Single(
            Drain(pump, ref cursor),
            value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);
        Assert.Equal(3UL, cycle.Payload.Strategy);
    }

    /// <summary>
    /// A suite with no strategist runs against the neutral bulletin, and says so.
    /// </summary>
    /// <remarks>
    /// There is nothing to bind and nothing to leave unbound: the registry constructs the
    /// publication on the neutral bulletin, so generation one is what every cycle is stamped with
    /// until something publishes.
    /// </remarks>
    [Fact]
    public void ASuiteWithNoStrategistIsStampedWithTheNeutralGeneration()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var registration = registry.Register(
            new ExecutionServiceDefinition("trace.runtime.strategy-unbound"),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        pump.PumpFrame(1);

        var cycle = Assert.Single(
            Drain(pump, ref cursor),
            value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);
        Assert.Equal(StrategyGeneration.Initial.Value, cycle.Payload.Strategy);
    }

    /// <summary>
    /// A bulletin published while a capture is running belongs to the next cycle, not this one.
    /// </summary>
    /// <remarks>
    /// The runtime pins the strategy generation before it calls <c>Capture</c>, so the number the
    /// cycle is stamped with is the one that was live when the cycle opened. Stamping it afterwards
    /// would let a service publish during its own capture and have the runtime believe the cycle had
    /// consulted a bulletin it never saw. See W49.
    /// </remarks>
    [Fact]
    public void AStrategyPublishedDuringCaptureIsNotTheGenerationTheCycleIsStampedWith()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SourceServiceDefinition("trace.runtime.strategy-during-capture");
        using var registration = registry.RegisterSource(
            definition,
            new LifecycleGeneration(1));
        using var contention = new HandoffGateContention(registration.Runner);
        definition.CaptureCallback = () =>
        {
            registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(2));
            contention.Acquire();
        };
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);

        var events = Drain(pump, ref cursor);
        var started = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.CaptureStarted);
        var completed = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.CaptureCompleted);
        Assert.Equal(started.Id, completed.Parent);
        Assert.Equal(1L, started.Payload.FrameIdentity);
        Assert.Equal(1L, completed.Payload.FrameIdentity);
        Assert.Contains(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.StrategyPublished &&
                value.Payload.Strategy == 2);
        Assert.Equal(1UL, completed.Payload.Strategy);
        Assert.DoesNotContain(events, value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);

        contention.Release();
        definition.CaptureCallback = null;
    }

    [Fact]
    public void DeferredOldContextDoesNotRegressOrDuplicatePublicationHighWater()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.deferred-old-context");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        using var contention = new HandoffGateContention(registration.Runner);
        definition.ShouldStartCallback = contention.Acquire;
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        Drain(pump, ref cursor);
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));
        registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(2));

        pump.PumpFrame(2);
        var advanced = Drain(pump, ref cursor);
        Assert.Single(
            advanced,
            value => value.Kind == ServiceCycleSemanticEventKind.ConfigurationPublished &&
                value.Payload.Configuration == 2);
        Assert.Single(
            advanced,
            value => value.Kind == ServiceCycleSemanticEventKind.StrategyPublished &&
                value.Payload.Strategy == 2);
        Assert.DoesNotContain(advanced, value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);

        contention.Release();
        definition.ShouldStartCallback = null;
        pump.PumpFrame(3);

        var queued = Drain(pump, ref cursor);
        var cycle = Assert.Single(
            queued,
            value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);
        Assert.Equal(1UL, cycle.Payload.Configuration);
        Assert.Equal(1UL, cycle.Payload.Strategy);
        Assert.DoesNotContain(
            queued,
            value => value.Kind is ServiceCycleSemanticEventKind.ConfigurationPublished or
                ServiceCycleSemanticEventKind.StrategyPublished);
    }

    [Fact]
    public void MidEvaluationSidebandFactsCannotBecomeBackwardCycleParents()
    {
        var clock = new ThreadSafeTestClock(100);
        using var evaluationEntered = new System.Threading.ManualResetEventSlim(false);
        using var evaluationRelease = new System.Threading.ManualResetEventSlim(false);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.sideband-causality")
        {
            ActionCount = 1,
            EvaluationEntered = evaluationEntered,
            EvaluationRelease = evaluationRelease,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        Assert.True(evaluationEntered.Wait(ServiceCycleTestDeadline.Value));
        clock.AdvanceTo(new MonotonicTimestamp(200));
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));
        registry.StrategyPublication.Publish(TestSuiteStrategy.WithSetting(2));
        pump.PumpFrame(frame++);
        pump.SetEmergencyStop(true);
        clock.AdvanceTo(new MonotonicTimestamp(201));
        pump.SetEmergencyStop(false);
        evaluationRelease.Set();
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

        var events = Drain(pump, ref cursor);
        var queued = Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);
        var started = Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.CycleStarted);
        var configuration = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.ConfigurationPublished);
        var strategyEvent = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.StrategyPublished);
        var emergency = Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.EmergencyEntered);
        var actionRejected = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.ActionRejected);
        var batchAborted = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.BatchAborted);
        Assert.Equal(queued.Id, started.Parent);
        Assert.Equal(0UL, configuration.Payload.Service);
        Assert.Equal(configuration.Id, strategyEvent.Parent);
        Assert.Equal(emergency.Id, actionRejected.Parent);
        Assert.Equal(emergency.Id, batchAborted.Parent);

        var byId = events.ToDictionary(value => value.Id);
        foreach (var item in events)
        {
            if (!item.HasParent || !byId.TryGetValue(item.Parent, out var parent)) continue;
            Assert.True(
                parent.Payload.TimestampTicks <= item.Payload.TimestampTicks,
                $"Backward edge: {parent.Kind}@{parent.Payload.TimestampTicks} -> " +
                $"{item.Kind}@{item.Payload.TimestampTicks}.");
        }
    }

    [Fact]
    public void ZeroActionResponseTerminatesBatchAndCycleExactlyOnce()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.zero") { ActionCount = 0 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

        var events = Drain(pump, ref cursor);
        AssertOrderedSubsequence(
            events,
            ServiceCycleSemanticEventKind.CycleStarted,
            ServiceCycleSemanticEventKind.EvaluationStarted,
            ServiceCycleSemanticEventKind.StatePublished,
            ServiceCycleSemanticEventKind.EvaluationCompleted,
            ServiceCycleSemanticEventKind.BatchPublished,
            ServiceCycleSemanticEventKind.BatchCompleted,
            ServiceCycleSemanticEventKind.CycleCompleted);
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.BatchCompleted);
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.CycleCompleted);
        Assert.DoesNotContain(events, value => value.Kind == ServiceCycleSemanticEventKind.ActionAttempted);
    }

    [Fact]
    public void EmergencyEntryIsTheExactParentOfOneSyntheticBatchRejectionTerminal()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.emergency") { ActionCount = 3 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);

        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);
        Drain(pump, ref cursor);
        pump.SetEmergencyStop(true);
        pump.PumpFrame(frame);

        var events = Drain(pump, ref cursor);
        var entered = Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.EmergencyEntered);
        var terminal = Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.BatchAborted);
        Assert.Equal(entered.Id, terminal.Parent);
        Assert.Equal(CommonActionResultCodes.EmergencyStop.Value, terminal.Payload.Code);
        Assert.Equal(2, terminal.Payload.UntouchedSuffixCount);
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.ActionRejected);
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.CycleCompleted);
        Assert.Equal(0, definition.ActionExecutionCount);
    }

    [Fact]
    public void DuplicateFrameProducesOneRejectedPumpFactAndNoServiceFacts()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.duplicate")
        {
            StartDecision = ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(new MonotonicDuration(1))),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        Assert.True(pump.PumpFrame(7).Accepted);
        Drain(pump, ref cursor);
        Assert.False(pump.PumpFrame(7).Accepted);

        var events = Drain(pump, ref cursor);
        var rejected = Assert.Single(events);
        Assert.Equal(ServiceCycleSemanticEventKind.PumpCompleted, rejected.Kind);
        Assert.False(rejected.Payload.PumpAccepted);
        Assert.Equal(7, rejected.Payload.FrameIdentity);
        Assert.Equal(0UL, rejected.Payload.Service);
    }

    [Fact]
    public void DeferredHandoffQueuesTheOriginalDecisionWithoutRepeatingCaptureFacts()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SourceServiceDefinition("trace.runtime.deferred");
        using var registration = registry.RegisterSource(
            definition,
            new LifecycleGeneration(1));
        using var contention = new HandoffGateContention(registration.Runner);
        definition.CaptureCallback = contention.Acquire;
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        Assert.Equal(1, pump.PumpFrame(1).CapturesAttempted);
        var deferred = Drain(pump, ref cursor);
        Assert.Single(deferred, value => value.Kind == ServiceCycleSemanticEventKind.CaptureStarted);
        Assert.Single(deferred, value => value.Kind == ServiceCycleSemanticEventKind.CaptureCompleted);
        Assert.DoesNotContain(deferred, value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);

        contention.Release();
        definition.CaptureCallback = null;
        pump.PumpFrame(2);

        var queued = Drain(pump, ref cursor);
        var cycleQueued = Assert.Single(
            queued,
            value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);
        Assert.Equal(CommonServiceDecisionCodes.Ready.Value, cycleQueued.Payload.Code);
        Assert.DoesNotContain(queued, value => value.Kind == ServiceCycleSemanticEventKind.CaptureStarted);
        Assert.DoesNotContain(queued, value => value.Kind == ServiceCycleSemanticEventKind.CaptureCompleted);
        Assert.False(Assert.IsType<ServiceCycleSemanticTraceSource>(pump.SemanticTrace).EmissionFaulted);
    }

    [Fact]
    public void ReentrantControlFactsFollowTheAttemptFactThatTriggeredTheirCallback()
    {
        AssertStartAttemptPrecedesLifecycleRequest();
        AssertActionPrecedesEmergencyEntry();
    }

    [Fact]
    public void ActionFaultPublishesTerminalAndFaultFactsInExactOrder()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.action-fault")
        {
            ActionCount = 2,
            FaultAtIndex = 0,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);
        Drain(pump, ref cursor);

        Assert.Equal(1, pump.PumpFrame(frame).ActionsAttempted);

        var events = Drain(pump, ref cursor);
        var faultKinds = events
            .Where(value => value.Kind is
                ServiceCycleSemanticEventKind.ActionAttempted or
                ServiceCycleSemanticEventKind.ActionFaulted or
                ServiceCycleSemanticEventKind.BatchAborted or
                ServiceCycleSemanticEventKind.CycleFaulted or
                ServiceCycleSemanticEventKind.FaultObserved or
                ServiceCycleSemanticEventKind.RetryScheduled)
            .Select(value => value.Kind)
            .ToArray();
        Assert.Equal(
            new[]
            {
                ServiceCycleSemanticEventKind.ActionAttempted,
                ServiceCycleSemanticEventKind.ActionFaulted,
                ServiceCycleSemanticEventKind.BatchAborted,
                ServiceCycleSemanticEventKind.CycleFaulted,
                ServiceCycleSemanticEventKind.FaultObserved,
                ServiceCycleSemanticEventKind.RetryScheduled,
            },
            faultKinds);
    }

    [Fact]
    public void ActionFaultRecoversOnlyAfterANonFaultedNativeAttempt()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.action-recovery")
        {
            ActionCount = 1,
            FaultAtIndex = 0,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);
        Drain(pump, ref cursor);

        Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);
        var failed = Drain(pump, ref cursor);
        var observed = Assert.Single(
            failed,
            value => value.Kind == ServiceCycleSemanticEventKind.FaultObserved);

        definition.FaultAtIndex = -1;
        definition.ActionCount = 0;
        var faultedHandoff = registration.Runner.ProbeHandoff();
        Assert.Equal(ServiceHandoffPhase.MainOwnedBatch, faultedHandoff.Phase);
        Assert.Equal(0, pump.PumpFrame(frame++).CyclesStarted);
        var cleaned = ServiceRunnerTestWait.ForHandoff(
            registration.Runner,
            value => value.Phase == ServiceHandoffPhase.Empty &&
                !value.CleanupPending &&
                value.CleanupAcknowledgementCount > faultedHandoff.CleanupAcknowledgementCount &&
                value.WorkerWaitCount > faultedHandoff.WorkerWaitCount,
            "the action-fault cleanup handback");
        TestWorldCollector.CollectedAt(registry, frame);
        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        Assert.Equal(1, pump.PumpFrame(frame++).CyclesStarted);
        ServiceRunnerTestWait.ForHandoff(
            registration.Runner,
            value => value.Phase == ServiceHandoffPhase.ResponseReady &&
                value.WorkerWaitCount > cleaned.WorkerWaitCount,
            "the zero-action response and worker wait");
        Assert.Equal(1, pump.PumpFrame(frame++).ResponsesAcquired);

        var zeroAction = Drain(pump, ref cursor);
        Assert.DoesNotContain(
            zeroAction,
            value => value.Kind == ServiceCycleSemanticEventKind.FaultRecovered);
        Assert.Equal(
            ServiceFaultCategory.ActionExecution,
            registration.Runner.Snapshot.Fault.Category);

        definition.ActionCount = 1;
        TestWorldCollector.CollectedAt(registry, frame);
        Assert.Equal(0, pump.PumpFrame(frame++).CyclesStarted);
        var returned = registration.Runner.ProbeHandoff();
        Assert.Equal(ServiceHandoffPhase.Empty, returned.Phase);
        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        Assert.Equal(1, pump.PumpFrame(frame++).CyclesStarted);
        ServiceRunnerTestWait.ForHandoff(
            registration.Runner,
            value => value.Phase == ServiceHandoffPhase.ResponseReady &&
                value.WorkerWaitCount > returned.WorkerWaitCount,
            "the recovery response and worker wait");
        Assert.Equal(1, pump.PumpFrame(frame++).ResponsesAcquired);
        Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);

        var recovered = Assert.Single(
            Drain(pump, ref cursor),
            value => value.Kind == ServiceCycleSemanticEventKind.FaultRecovered);
        Assert.Equal(observed.Payload.Code, recovered.Payload.Code);
        Assert.Equal(observed.Payload.Disposition, recovered.Payload.Disposition);
        Assert.False(registration.Runner.Snapshot.Fault.IsValid);
    }

    [Fact]
    public void EmergencyRejectedResponseDoesNotPretendAnActionFaultRecovered()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.action-emergency-recovery")
        {
            ActionCount = 1,
            FaultAtIndex = 0,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);
        Drain(pump, ref cursor);
        Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);
        Drain(pump, ref cursor);

        definition.FaultAtIndex = -1;
        TestWorldCollector.CollectedAt(registry, frame);
        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        pump.SetEmergencyStop(true);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

        var rejected = Drain(pump, ref cursor);
        Assert.DoesNotContain(
            rejected,
            value => value.Kind == ServiceCycleSemanticEventKind.FaultRecovered);
        Assert.Equal(
            ServiceFaultCategory.ActionExecution,
            registration.Runner.Snapshot.Fault.Category);
        Assert.Equal(1, definition.ActionExecutionCount);
    }

    [Fact]
    public void BoundedActionTurnPublishesEveryActionUnderOnePumpFrame()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.action-slice")
        {
            ActionCount = 3,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1),
            ServiceActionDispatchPolicy.Bounded(16));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);
        Drain(pump, ref cursor);

        var report = pump.PumpFrame(frame);
        Assert.Equal(3, report.ActionsAttempted);

        var events = Drain(pump, ref cursor);
        var attempts = events
            .Where(value => value.Kind == ServiceCycleSemanticEventKind.ActionAttempted)
            .ToArray();
        var commits = events
            .Where(value => value.Kind == ServiceCycleSemanticEventKind.ActionCommitted)
            .ToArray();
        Assert.Equal(3, attempts.Length);
        Assert.Equal(3, commits.Length);
        Assert.Equal(new ulong[] { 1, 2, 3 }, attempts.Select(value => value.Payload.Action));
        Assert.All(attempts, value => Assert.NotEqual(0UL, value.Payload.World));
        Assert.All(attempts, value => Assert.Equal(frame, value.Payload.FrameIdentity));
        Assert.All(commits, value => Assert.Equal(frame, value.Payload.FrameIdentity));
        Assert.Contains(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.PumpCompleted &&
                value.Payload.FrameIdentity == frame &&
                value.Payload.ActionsAttempted == 3 &&
                value.Payload.CyclesStarted == report.CyclesStarted &&
                value.Payload.WorldGateDeferrals == report.WorldGateDeferrals);
    }

    [Fact]
    public void UnavailableCapturePublishesAttemptAndUnavailableWithoutQueueing()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SourceServiceDefinition("trace.runtime.capture-unavailable")
        {
            CaptureResult = ServiceCaptureResult.Unavailable(
                CommonServiceDecisionCodes.CaptureUnavailable,
                WakePolicy.AfterDecision(new MonotonicDuration(10))),
        };
        using var registration = registry.RegisterSource(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);

        var events = Drain(pump, ref cursor);
        AssertOrderedSubsequence(
            events,
            ServiceCycleSemanticEventKind.CaptureStarted,
            ServiceCycleSemanticEventKind.CaptureUnavailable);
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.CaptureStarted);
        Assert.Single(events, value => value.Kind == ServiceCycleSemanticEventKind.CaptureUnavailable);
        Assert.DoesNotContain(events, value => value.Kind == ServiceCycleSemanticEventKind.CycleQueued);
    }

    [Fact]
    public void EvaluationFaultPublishesRetryAndLaterRecovery()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.evaluation-recovery")
        {
            ActionCount = 0,
        };
        definition.FailNextEvaluations(1);
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

        var failed = Drain(pump, ref cursor);
        AssertOrderedSubsequence(
            failed,
            ServiceCycleSemanticEventKind.EvaluationStarted,
            ServiceCycleSemanticEventKind.EvaluationFaulted,
            ServiceCycleSemanticEventKind.CycleFaulted,
            ServiceCycleSemanticEventKind.FaultObserved,
            ServiceCycleSemanticEventKind.RetryScheduled);
        var observed = Assert.Single(failed, value => value.Kind == ServiceCycleSemanticEventKind.FaultObserved);

        pump.PumpFrame(frame++);
        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

        var recovered = Drain(pump, ref cursor);
        var recovery = Assert.Single(
            recovered,
            value => value.Kind == ServiceCycleSemanticEventKind.FaultRecovered);
        Assert.Equal(observed.Payload.Code, recovery.Payload.Code);
        Assert.Equal(observed.Payload.Disposition, recovery.Payload.Disposition);
    }

    [Fact]
    public void ProjectionFaultPreservesCompletedEvaluationEvidenceWithoutPublishingStateOrBatch()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var returnedWake = WakePolicy.AfterDecision(new MonotonicDuration(17));
        var definition = new ExecutionServiceDefinition("trace.runtime.projection-fault")
        {
            ActionCount = 2,
            EvaluationWake = returnedWake,
        };
        definition.FailNextProjections(1);
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

        var failed = Drain(pump, ref cursor);
        AssertOrderedSubsequence(
            failed,
            ServiceCycleSemanticEventKind.EvaluationStarted,
            ServiceCycleSemanticEventKind.EvaluationCompleted,
            ServiceCycleSemanticEventKind.ProjectionFaulted,
            ServiceCycleSemanticEventKind.CycleFaulted,
            ServiceCycleSemanticEventKind.FaultObserved,
            ServiceCycleSemanticEventKind.RetryScheduled);
        var evaluated = Assert.Single(
            failed,
            value => value.Kind == ServiceCycleSemanticEventKind.EvaluationCompleted);
        var projectionFault = Assert.Single(
            failed,
            value => value.Kind == ServiceCycleSemanticEventKind.ProjectionFaulted);
        Assert.Equal(2, evaluated.Payload.ActionCount);
        Assert.Equal(2, projectionFault.Payload.ActionCount);
        Assert.True(evaluated.Payload.TryGetReturnedWake(out var evaluatedWake));
        Assert.True(projectionFault.Payload.TryGetReturnedWake(out var faultWake));
        Assert.Equal(returnedWake, evaluatedWake);
        Assert.Equal(returnedWake, faultWake);
        Assert.DoesNotContain(failed, value => value.Kind == ServiceCycleSemanticEventKind.StatePublished);
        Assert.DoesNotContain(failed, value => value.Kind == ServiceCycleSemanticEventKind.BatchPublished);
    }

    [Fact]
    public void StateFactoryContentionPublishesDeferredDeadlineAndCompletesCycle()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.state-contention") { ActionCount = 0 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var ledger = ServiceCycleRegistryTestAccess.ResourceClaims(registry);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.WorkerDefinition, out var blocker));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);

        try
        {
            var frame = 1L;
            ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
            ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
            ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
            ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

            var events = Drain(pump, ref cursor);
            AssertOrderedSubsequence(
                events,
                ServiceCycleSemanticEventKind.CycleStarted,
                ServiceCycleSemanticEventKind.EvaluationDeferred,
                ServiceCycleSemanticEventKind.CycleCompleted);
            var deferred = Assert.Single(
                events,
                value => value.Kind == ServiceCycleSemanticEventKind.EvaluationDeferred);
            Assert.Equal(CommonServiceDecisionCodes.TransientContention.Value, deferred.Payload.Code);
            Assert.True(deferred.Payload.DeadlineTicks > deferred.Payload.TimestampTicks);
            Assert.DoesNotContain(events, value => value.Kind == ServiceCycleSemanticEventKind.EvaluationStarted);
            Assert.DoesNotContain(events, value => value.Kind == ServiceCycleSemanticEventKind.FaultObserved);
        }
        finally
        {
            ledger.EndFactory(blocker);
        }
    }

    [Fact]
    public void LifecycleConstructionContentionPublishesOneNonFaultDeferralBeforeActivation()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("trace.runtime.lifecycle-contention");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);
        var ledger = ServiceCycleRegistryTestAccess.ResourceClaims(registry);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.State, out var blocker));

        try
        {
            Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
            var frame = 1L;
            while (registration.LifecycleSnapshot.ConstructionContentionCount == 0)
                pump.PumpFrame(frame++);

            var deferredEvents = Drain(pump, ref cursor);
            var requested = Assert.Single(
                deferredEvents,
                value => value.Kind == ServiceCycleSemanticEventKind.LifecycleRequested);
            var retired = Assert.Single(
                deferredEvents,
                value => value.Kind == ServiceCycleSemanticEventKind.LifecycleRetired);
            var deferred = Assert.Single(
                deferredEvents,
                value => value.Kind == ServiceCycleSemanticEventKind.LifecycleConstructionDeferred);
            Assert.Equal(requested.Id, retired.Parent);
            Assert.Equal(retired.Id, deferred.Parent);
            Assert.Equal(CommonServiceDecisionCodes.TransientContention.Value, deferred.Payload.Code);
            Assert.Equal(100, deferred.Payload.TimestampTicks);
            Assert.Equal(
                100 + TimeSpan.FromMilliseconds(16).Ticks,
                deferred.Payload.DeadlineTicks);
            Assert.DoesNotContain(
                deferredEvents,
                value => value.Kind is ServiceCycleSemanticEventKind.FaultObserved or
                    ServiceCycleSemanticEventKind.RetryScheduled or
                    ServiceCycleSemanticEventKind.FaultRecovered);

            pump.PumpFrame(frame++);
            Assert.DoesNotContain(
                Drain(pump, ref cursor),
                value => value.Kind == ServiceCycleSemanticEventKind.LifecycleConstructionDeferred);
            ledger.EndFactory(blocker);
            blocker = null!;
            clock.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(16)));
            while (registration.LifecycleSnapshot.Position0.Lifecycle != new LifecycleGeneration(2) &&
                   registration.LifecycleSnapshot.Position1.Lifecycle != new LifecycleGeneration(2))
                pump.PumpFrame(frame++);

            var activatedEvents = Drain(pump, ref cursor);
            var activated = Assert.Single(
                activatedEvents,
                value => value.Kind == ServiceCycleSemanticEventKind.LifecycleActivated &&
                    value.Payload.Lifecycle == 2);
            Assert.Equal(deferred.Id, activated.Parent);
            Assert.DoesNotContain(
                activatedEvents,
                value => value.Kind is ServiceCycleSemanticEventKind.FaultObserved or
                    ServiceCycleSemanticEventKind.RetryScheduled or
                    ServiceCycleSemanticEventKind.FaultRecovered);
        }
        finally
        {
            if (blocker is not null) ledger.EndFactory(blocker);
        }
    }

    [Fact]
    public void RetainedCompletedReceiptPrecedesLaterLifecycleRetirement()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("trace.runtime.lifecycle-retained-receipt")
        {
            ActionCount = 0,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);
        var frame = 1L;

        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        clock.Advance(new MonotonicDuration(100));

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));

        var events = Drain(pump, ref cursor);
        AssertOrderedSubsequence(
            events,
            ServiceCycleSemanticEventKind.BatchCompleted,
            ServiceCycleSemanticEventKind.CycleCompleted,
            ServiceCycleSemanticEventKind.LifecycleRetired);
        var cycleCompleted = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.CycleCompleted);
        var retired = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.LifecycleRetired);
        Assert.Equal(cycleCompleted.Id, retired.Parent);
        Assert.True(cycleCompleted.Payload.TimestampTicks < retired.Payload.TimestampTicks);
    }

    [Fact]
    public void ConstructionFaultRecoveryKeepsTheFaultedLifecycleAfterNewerCoalescing()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("trace.runtime.lifecycle-fault-coalescing");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        Drain(pump, ref cursor);
        definition.FailNextWorkerFactories(1);

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        pump.PumpFrame(1);
        Assert.True(registration.LifecycleSnapshot.ConstructionFault.IsValid);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(3)));
        clock.AdvanceTo(registration.LifecycleSnapshot.ConstructionRetryDue);
        pump.PumpFrame(2);

        var events = Drain(pump, ref cursor);
        var observed = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.FaultObserved);
        var retry = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.RetryScheduled);
        var recovered = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.FaultRecovered);
        var activated = Assert.Single(
            events,
            value => value.Kind == ServiceCycleSemanticEventKind.LifecycleActivated &&
                value.Payload.Lifecycle == 3);
        Assert.Equal(2UL, observed.Payload.Lifecycle);
        Assert.Equal(2UL, retry.Payload.Lifecycle);
        Assert.Equal(2UL, recovered.Payload.Lifecycle);
        Assert.Equal(3UL, activated.Payload.Lifecycle);
    }

    [Fact]
    public void RecorderValidationDoesNotClaimPumpAndDisabledRecorderIsNoOp()
    {
        var clock = new ThreadSafeTestClock(100);
        using (var registry = new ServiceCycleRegistry(1, clock))
        {
            using var registration = registry.Register(
                new ExecutionServiceDefinition("trace.runtime.recorder-mismatch"),
                new LifecycleGeneration(1));
            registry.Seal();

            Assert.Throws<ArgumentException>(() =>
                new SuiteFramePump(registry, Recorder(2)));
            using var validPump = new SuiteFramePump(registry, Recorder(1));
            TestWorldCollector.CollectedAtActivation(registry);
            Assert.NotNull(validPump.SemanticTrace);
        }

        using (var registry = new ServiceCycleRegistry(1, clock))
        {
            using var registration = registry.Register(
                new ExecutionServiceDefinition("trace.runtime.recorder-disabled"),
                new LifecycleGeneration(1));
            registry.Seal();
            var disabled = new ServiceCycleSemanticRecorder(
                new ServiceCycleTraceSessionId(701),
                8,
                4,
                enabled: false);
            using var pump = new SuiteFramePump(registry, disabled);
            TestWorldCollector.CollectedAtActivation(registry);

            Assert.Null(pump.SemanticTrace);
            Assert.True(pump.PumpFrame(1).Accepted);
            Assert.Equal(0, disabled.Count);
        }
    }

    private static ServiceCycleSemanticRecorder Recorder(int services) =>
        new(new ServiceCycleTraceSessionId(700), 256, services);

    private static ServiceCycleSemanticEvent[] Drain(
        SuiteFramePump pump,
        ref ServiceCycleTraceCursor cursor)
    {
        var source = Assert.IsType<ServiceCycleSemanticTraceSource>(pump.SemanticTrace);
        var output = new ServiceCycleSemanticEvent[source.Capacity];
        var drain = source.DrainSince(cursor, output);
        Assert.True(drain.IsComplete);
        Assert.False(drain.HasMore);
        cursor = drain.Cursor;
        return output[..drain.Copied];
    }

    private static void AssertOrderedSubsequence(
        IReadOnlyList<ServiceCycleSemanticEvent> events,
        params ServiceCycleSemanticEventKind[] expected)
    {
        var cursor = -1;
        foreach (var kind in expected)
        {
            var match = -1;
            for (var index = cursor + 1; index < events.Count; index++)
            {
                if (events[index].Kind != kind) continue;
                match = index;
                break;
            }
            Assert.True(match >= 0, $"Missing {kind} after index {cursor}. Actual: {string.Join(", ", events.Select(value => value.Kind))}");
            cursor = match;
        }
    }

    private static void AssertStartAttemptPrecedesLifecycleRequest()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.reentrant-start") { ActionCount = 0 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        SuiteFramePump? pump = null;
        using (pump = new SuiteFramePump(registry, Recorder(1)))
        {
            TestWorldCollector.CollectedAtActivation(registry);
            var cursor = default(ServiceCycleTraceCursor);
            Drain(pump, ref cursor);
            definition.ShouldStartCallback = () =>
                pump.RequestLifecycleReplacement(new LifecycleGeneration(2));

            pump.PumpFrame(1);

            var events = Drain(pump, ref cursor);
            AssertBefore(
                events,
                ServiceCycleSemanticEventKind.StartAttempted,
                ServiceCycleSemanticEventKind.LifecycleRequested);
        }
    }

    private static void AssertActionPrecedesEmergencyEntry()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("trace.runtime.reentrant-action") { ActionCount = 2 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, Recorder(1));
        TestWorldCollector.CollectedAtActivation(registry);
        var cursor = default(ServiceCycleTraceCursor);
        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);
        Drain(pump, ref cursor);
        definition.ActionCallback = () => pump.SetEmergencyStop(true);

        pump.PumpFrame(frame);

        var events = Drain(pump, ref cursor);
        AssertBefore(
            events,
            ServiceCycleSemanticEventKind.ActionAttempted,
            ServiceCycleSemanticEventKind.EmergencyEntered);
    }

    private static void AssertBefore(
        IReadOnlyList<ServiceCycleSemanticEvent> events,
        ServiceCycleSemanticEventKind first,
        ServiceCycleSemanticEventKind second)
    {
        var firstIndex = events.ToList().FindIndex(value => value.Kind == first);
        var secondIndex = events.ToList().FindIndex(value => value.Kind == second);
        Assert.True(firstIndex >= 0, $"Missing {first}.");
        Assert.True(secondIndex >= 0, $"Missing {second}.");
        Assert.True(firstIndex < secondIndex, $"Expected {first} before {second}.");
    }
}
