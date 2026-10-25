using OrbModding.TestSupport;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleTombstoneRetentionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedSealedSlotRetainsOnlyTombstoneMetadataEvenWhenReleaseThrows(bool throwOnRelease)
    {
        var proof = CreateDisposedGraph(throwOnRelease);

        ForceCollection(proof.PayloadReferences);

        Assert.All(proof.PayloadReferences, reference => Assert.False(reference.IsAlive));
        Assert.Equal(0, proof.Registry.Count);
        Assert.Equal(1, proof.Registry.OrdinalCount);
        Assert.Equal(proof.Ordinal, proof.Registration.Ordinal);
        Assert.Equal(proof.ServiceId, proof.Registry.GetServiceId(proof.Ordinal));
        var slot = proof.Registry.GetSlot(proof.Ordinal);
        Assert.IsType<ServiceCycleTombstone>(slot);
        Assert.True(slot.IsDisposed);
        Assert.Equal(proof.Ordinal, slot.Ordinal);
        Assert.True(slot.RegistrationToken > 0);
        Assert.False(proof.ReleaseFailed);
        // A tombstone consumed no configuration, and says so, even though the suite's publication is
        // very much alive: it belongs to the registry, not to any one registration.
        Assert.Equal(default, slot.LatestConfiguration);
        Assert.Equal(1UL, proof.Registry.Configuration.ReadLatest().Generation.Value);

        GC.KeepAlive(proof.Registry);
        GC.KeepAlive(proof.Registration);
        GC.KeepAlive(proof.Pump);
        proof.Pump.Dispose();
    }

    [Fact]
    public void OrdinalHighWaterAllowsScanningPastPreSealTombstone()
    {
        using var registry = new ServiceCycleRegistry(2);
        var first = registry.Register(
            new RetainedDefinition("test.tombstone.preseal.a", new RetainedPayload(1),
                new RetainedState(new RetainedPayload(3)), false),
            new LifecycleGeneration(1));
        using var second = registry.Register(
            new RetainedDefinition("test.tombstone.preseal.b", new RetainedPayload(5),
                new RetainedState(new RetainedPayload(7)), false),
            new LifecycleGeneration(1));

        first.Dispose();
        registry.Seal();

        Assert.Equal(1, registry.Count);
        Assert.Equal(2, registry.OrdinalCount);
        Assert.True(registry.GetSlot(0).IsDisposed);
        Assert.False(registry.GetSlot(1).IsDisposed);
        Assert.Equal(1, second.Ordinal);
        Assert.Equal("test.tombstone.preseal.b", registry.GetServiceId(1).Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TombstoneRetentionProof CreateDisposedGraph(bool throwOnRelease)
    {
        var definitionPayload = new RetainedPayload(1);
        var statePayload = new RetainedPayload(3);
        var state = new RetainedState(statePayload);
        var definition = new RetainedDefinition(
            throwOnRelease ? "test.tombstone.throw" : "test.tombstone.clean",
            definitionPayload,
            state,
            throwOnRelease);
        var payloadReferences = new[]
        {
            new WeakReference(definitionPayload),
            new WeakReference(statePayload),
            new WeakReference(state),
            new WeakReference(definition),
        };

        var clock = new ThreadSafeTestClock(100);
        var registry = new ServiceCycleRegistry(1, clock);
        var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        // One cycle first: worker state is minted lazily, and a state that was never created is a
        // state whose release cannot be observed to throw — which is half of what this proves.
        var runner = registration.Runner;
        var frame = TestWorldCollector.ActivationFrame + 1;
        ServiceRunnerTestWait.PumpToResponse(pump, runner, ref frame);
        ServiceRunnerTestWait.PumpToHandback(pump, runner, ref frame);
        var releaseFailed = false;
        try { registration.Dispose(); }
        catch (InvalidOperationException) { releaseFailed = true; }
        if (!definition.WaitForRelease())
            throw new TimeoutException("The signal-only runner shutdown did not complete test cleanup.");
        // Release signals from the callback's finally; exception logging and worker cleanup still
        // run after that signal. Collection can prove retention only after the thread has exited.
        if (!runner.WaitForWorkerExit(ServiceCycleTestDeadline.Value))
            throw new TimeoutException("The disposed worker did not finish releasing its state.");

        return new TombstoneRetentionProof(
            registry,
            pump,
            registration,
            payloadReferences,
            registration.Ordinal,
            new ServiceId(throwOnRelease ? "test.tombstone.throw" : "test.tombstone.clean"),
            releaseFailed);
    }

    private static void ForceCollection(WeakReference[] references)
    {
        for (var attempt = 0; attempt < 8 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    private sealed class TombstoneRetentionProof
    {
        internal TombstoneRetentionProof(
            ServiceCycleRegistry registry,
            SuiteFramePump pump,
            ServiceRegistration<RetainedState, RetainedAction> registration,
            WeakReference[] payloadReferences,
            int ordinal,
            ServiceId serviceId,
            bool releaseFailed)
        {
            Registry = registry;
            Pump = pump;
            Registration = registration;
            PayloadReferences = payloadReferences;
            Ordinal = ordinal;
            ServiceId = serviceId;
            ReleaseFailed = releaseFailed;
        }

        internal ServiceCycleRegistry Registry { get; }
        internal SuiteFramePump Pump { get; }
        internal ServiceRegistration<RetainedState, RetainedAction> Registration { get; }
        internal WeakReference[] PayloadReferences { get; }
        internal int Ordinal { get; }
        internal ServiceId ServiceId { get; }
        internal bool ReleaseFailed { get; }
    }

    private sealed class RetainedPayload
    {
        private readonly int _value;
        internal RetainedPayload(int value) => _value = value;
    }
    private sealed class RetainedState
    {
        private readonly RetainedPayload _payload;
        internal RetainedState(RetainedPayload payload) => _payload = payload;
    }
    private readonly struct RetainedAction { }

    private sealed class RetainedDefinition :
        IServiceCycleDefinition<RetainedState, RetainedAction>
    {
        private readonly ServiceId _serviceId;
        private readonly RetainedPayload _payload;
        private readonly RetainedWorkerResources _worker;
        private readonly ManualResetEventSlim _released = new(false);

        internal RetainedDefinition(
            string serviceId,
            RetainedPayload payload,
            RetainedState state,
            bool throwOnRelease)
        {
            _serviceId = new ServiceId(serviceId);
            _payload = payload;
            _worker = new RetainedWorkerResources(
                state,
                throwOnRelease,
                RetainedReleaseSignals.Register(_released));
        }

        public ServiceId ServiceId => _serviceId;
        public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
        public ServiceFaultRecoveryPolicy FaultRecoveryPolicy => new(
            MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(1)),
            MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
        public IServiceCycleWorkerDefinition<RetainedState, RetainedAction>
            CreateWorkerDefinition() => new WorkerDefinition(_worker);
        internal bool WaitForRelease() => _released.Wait(ServiceCycleTestDeadline.Value);
        public ServiceStartDecision ShouldStart(
            in SuiteRuntimeConfiguration config,
            in ServiceCycleStartContext context) =>
            ServiceStartDecision.Ready(CommonServiceDecisionCodes.Ready);
        public ServiceActionJournalAttribution DescribeAction(in RetainedAction action) =>
            ServiceActionJournalAttribution.Publication;
        public ServiceActionResult TryExecute(
            in RetainedAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);

        private sealed class WorkerDefinition :
            IServiceCycleWorkerDefinition<RetainedState, RetainedAction>
        {
            private readonly RetainedWorkerResources _resources;
            internal WorkerDefinition(RetainedWorkerResources resources) => _resources = resources;
            public RetainedState CreateState(LifecycleGeneration lifecycle) => _resources.State;
            public void ReleaseState(ref RetainedState state)
            {
                try
                {
                    if (_resources.ThrowOnRelease)
                        throw new InvalidOperationException("release failed without clearing state");
                }
                finally { RetainedReleaseSignals.Signal(_resources.ReleaseSignalId); }
            }

            public WakePolicy Evaluate(
                in SuiteRuntimeConfiguration config,
                GameWorldState world,
                SuiteStrategy strategy,
                in ServiceCycleContext context,
                ref RetainedState state,
                ServiceActionWriter<RetainedAction> actions) => WakePolicy.Immediate;
            public void ProjectState(
                in RetainedState state,
                in ServiceProjectionContext context,
                ServiceStateProjectionBuilder output) { }
        }

        private sealed class RetainedWorkerResources
        {
            internal RetainedWorkerResources(RetainedState state, bool throwOnRelease, int releaseSignalId)
            {
                State = state;
                ThrowOnRelease = throwOnRelease;
                ReleaseSignalId = releaseSignalId;
            }
            internal RetainedState State { get; }
            internal bool ThrowOnRelease { get; }
            internal int ReleaseSignalId { get; }
        }
    }

    private static class RetainedReleaseSignals
    {
        private static readonly ConcurrentDictionary<int, ManualResetEventSlim> Signals = new();
        private static int _nextId;
        internal static int Register(ManualResetEventSlim signal)
        {
            var id = Interlocked.Increment(ref _nextId);
            if (!Signals.TryAdd(id, signal)) throw new InvalidOperationException("Duplicate test signal id.");
            return id;
        }
        internal static void Signal(int id)
        {
            if (Signals.TryRemove(id, out var signal)) signal.Set();
        }
    }
}
