#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif
using OrbModding.TestSupport;
using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class AutomataServiceCycleHostTests : IDisposable
{
    private readonly System.Collections.Generic.List<string> _faults = new();

    public AutomataServiceCycleHostTests() => GameActionFaultLog.ConfigureLog(_faults.Add);
    public void Dispose() => GameActionFaultLog.ConfigureLog(null);

    [Fact]
    public void OneHostPumpsDifferentlyTypedServicesAndOwnsSuiteLifecycle()
    {
        var frame = 1L;
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(
            2,
            new LifecycleGeneration(7),
            clock);
        var execution = new ExecutionServiceDefinition("host.execution");
        // A second service with a different state and action, reading the same suite
        // configuration: there is one publication, so there is one snapshot type.
        var synthetic = new TypeSafetyDefinition<SyntheticState, SyntheticAction>(
            new SyntheticState(), "host.synthetic");
        using var executionRegistration = registry.Register(
            execution,
            new LifecycleGeneration(7));
        using var syntheticRegistration = registry.Register(
            synthetic,
            new LifecycleGeneration(7));
        using var host = new AutomataServiceCycleHost(
            registry,
            () => frame,
            pumpTiming: null,
            semanticTrace: null
#if SERVICE_CYCLE_PROFILE
            , profileProbe: new ServiceCycleProfileProbe()
#endif
            );
        TestWorldCollector.CollectedAtActivation(registry);

        var first = host.Tick();

        Assert.True(first.Accepted);
        Assert.Equal(2, first.CyclesStarted);
        Assert.Equal(1, execution.StartCount);
        Assert.Equal(2, host.Pump.ServiceCapacity);
        Assert.False(host.Tick().Accepted);
        Assert.Equal(1, execution.StartCount);
        Assert.True(host.TryReplaceLifecycle(8));
        Assert.Equal(new LifecycleGeneration(8), host.CurrentLifecycle);
        Assert.False(host.TryReplaceLifecycle(8));
    }

    [Fact]
    public void SemanticRecorderSizedToTheRegistryOrdinalCountInitializesTheMultiServiceHost()
    {
        var frame = 1L;
        using var registry = new ServiceCycleRegistry(
            2,
            new LifecycleGeneration(3),
            new ThreadSafeTestClock(100));
        using var executionRegistration = registry.Register(
            new ExecutionServiceDefinition("host.semantic.execution"),
            new LifecycleGeneration(3));
        using var syntheticRegistration = registry.Register(
            new TypeSafetyDefinition<SyntheticState, SyntheticAction>(
                new SyntheticState(), "host.semantic.synthetic"),
            new LifecycleGeneration(3));
        Assert.Equal(2, registry.OrdinalCount);
        var semantic = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(9),
            eventCapacity: 32,
            serviceCapacity: registry.OrdinalCount);

        using var host = new AutomataServiceCycleHost(
            registry,
            () => frame,
            pumpTiming: null,
            semanticTrace: semantic
#if SERVICE_CYCLE_PROFILE
            , profileProbe: new ServiceCycleProfileProbe()
#endif
            );

        Assert.Equal(2, host.Pump.ServiceCapacity);
        Assert.True(host.Tick().Accepted);
    }

    [Fact]
    public void SemanticRecorderUndersizedForTheRegistryFailsHostInitialization()
    {
        using var registry = new ServiceCycleRegistry(
            2,
            new LifecycleGeneration(3),
            new ThreadSafeTestClock(100));
        using var executionRegistration = registry.Register(
            new ExecutionServiceDefinition("host.undersized.execution"),
            new LifecycleGeneration(3));
        using var syntheticRegistration = registry.Register(
            new TypeSafetyDefinition<SyntheticState, SyntheticAction>(
                new SyntheticState(), "host.undersized.synthetic"),
            new LifecycleGeneration(3));
        var undersized = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(9),
            eventCapacity: 32,
            serviceCapacity: 1);

        var error = Assert.Throws<ArgumentException>(() => new AutomataServiceCycleHost(
            registry,
            () => 1L,
            pumpTiming: null,
            semanticTrace: undersized
#if SERVICE_CYCLE_PROFILE
            , profileProbe: new ServiceCycleProfileProbe()
#endif
            ));
        Assert.Equal("semanticRecorder", error.ParamName);
    }

    /// <summary>
    /// A game read that throws faults the whole capture, and the log names what threw and where. The
    /// capture fault alone told the status line that the world could not be read, never why. A streak
    /// of the same fault is logged once, so a read that keeps throwing does not flood the player log.
    /// </summary>
    [Fact]
    public void ACaptureThatThrowsLogsWhatThrewOncePerFaultStreak()
    {
        var frame = 1L;
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        var definition = new SourceServiceDefinition("host.capture-fault")
        {
            CaptureCallback = ReadAMissingGameObject,
        };
        using var registration = registry.RegisterSource(definition, new LifecycleGeneration(1));
        var messages = new System.Collections.Generic.List<string>();
        using var host = new AutomataServiceCycleHost(
            registry,
            () => frame,
            pumpTiming: null,
            semanticTrace: null,
            serviceFailureLog: messages.Add
#if SERVICE_CYCLE_PROFILE
            , profileProbe: new ServiceCycleProfileProbe()
#endif
            );
        TestWorldCollector.CollectedAtActivation(registry);

        TickUntil(host, clock, ref frame, () => definition.CaptureCount == 2);

        var logged = Assert.Single(messages);
        Assert.StartsWith(
            $"ServiceCycle capture for service {definition.ServiceId.Value} threw, so it published " +
            "nothing and retries with backoff: The world capture failed. Reference MCP-",
            logged);
        Assert.DoesNotContain("NullReferenceException", logged);
        Assert.Equal(2, _faults.Count);
        Assert.All(_faults, entry =>
        {
            Assert.Contains("System.NullReferenceException", entry);
            Assert.Contains(nameof(ReadAMissingGameObject), entry);
        });
        Assert.Contains(_faults[0].Split(' ')[3], logged);

        definition.CaptureCallback = null;
        TickUntil(host, clock, ref frame, () => definition.CaptureCount == 3);
        definition.CaptureCallback = ReadAMissingGameObject;
        TickUntil(host, clock, ref frame, () => definition.CaptureCount == 4);

        Assert.Equal(2, messages.Count);
        Assert.Equal(3, _faults.Count);
        Assert.Contains(_faults[2].Split(' ')[3], messages[1]);
        Assert.NotEqual(messages[0], messages[1]);
    }

    private static void TickUntil(
        AutomataServiceCycleHost host,
        ThreadSafeTestClock clock,
        ref long frame,
        Func<bool> reached)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            host.Tick();
            if (reached()) return;
            if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                throw new TimeoutException("The service never reached the expected capture.");
            clock.Advance(new MonotonicDuration(1_000));
            frame++;
            System.Threading.Thread.Yield();
        }
    }

    [Fact]
    public void AStartDecisionThatThrowsLogsWhatThrew()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        var definition = new SourceServiceDefinition("host.start-fault")
        {
            ShouldStartCallback = static () => throw new InvalidOperationException("start exploded"),
        };
        using var registration = registry.RegisterSource(definition, new LifecycleGeneration(1));
        var messages = new System.Collections.Generic.List<string>();
        using var host = new AutomataServiceCycleHost(
            registry,
            () => 1,
            pumpTiming: null,
            semanticTrace: null,
            serviceFailureLog: messages.Add
#if SERVICE_CYCLE_PROFILE
            , profileProbe: new ServiceCycleProfileProbe()
#endif
            );
        TestWorldCollector.CollectedAtActivation(registry);

        host.Tick();

        Assert.StartsWith(
            $"ServiceCycle start decision for service {definition.ServiceId.Value} threw, so no " +
            "cycle opened and it retries with backoff: The service could not decide whether to start. Reference MCP-",
            Assert.Single(messages));
        var fault = Assert.Single(_faults);
        Assert.Contains("System.InvalidOperationException: start exploded", fault);
        Assert.Contains(fault.Split(' ')[3], messages[0]);
        Assert.DoesNotContain("start exploded", messages[0]);
    }

    /// <summary>
    /// Running out of memory is not a capture fault the suite can back off from. It leaves the capture
    /// boundary the way it leaves the action boundary.
    /// </summary>
    [Fact]
    public void AProcessFatalCaptureFailureIsNotContained()
    {
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1),
            new ThreadSafeTestClock(100));
        using var registration = registry.RegisterSource(
            new SourceServiceDefinition("host.capture-fatal")
            {
                CaptureCallback = static () => throw new OutOfMemoryException(),
            },
            new LifecycleGeneration(1));
        using var host = new AutomataServiceCycleHost(
            registry,
            () => 1,
            pumpTiming: null,
            semanticTrace: null
#if SERVICE_CYCLE_PROFILE
            , profileProbe: new ServiceCycleProfileProbe()
#endif
            );
        TestWorldCollector.CollectedAtActivation(registry);

        Assert.Throws<OutOfMemoryException>(() => host.Tick());
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ReadAMissingGameObject()
    {
        object? missing = null;
        _ = missing!.GetHashCode();
    }

    [Fact]
    public void ClaimedPumpShutdownRunsExactlyOnce()
    {
        var calls = 0;
        using var registry = new ServiceCycleRegistry(
            1,
            new LifecycleGeneration(1),
            new ThreadSafeTestClock(100));
        using var registration = registry.Register(
            new SyntheticServiceDefinition("host.shutdown"),
            new LifecycleGeneration(1));
        var host = new AutomataServiceCycleHost(
            registry,
            () => 1,
            pumpTiming: null,
            semanticTrace: null
#if SERVICE_CYCLE_PROFILE
            , profileProbe: new ServiceCycleProfileProbe()
#endif
            );
        host.ClaimPumpShutdown(() =>
        {
            calls++;
            host.Pump.Dispose();
            return true;
        });

        host.Dispose();
        host.Dispose();

        Assert.Equal(1, calls);
    }
}
