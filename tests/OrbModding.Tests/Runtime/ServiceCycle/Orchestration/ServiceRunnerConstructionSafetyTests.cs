using System;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCyclePumpTestWait;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class ServiceRunnerConstructionSafetyTests
{
    [Fact]
    public void ReplacementConstructionFailureBacksOffAndCoalescesNewestGeneration()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.backoff");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        definition.FailNextWorkerFactories(2);

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(110, registration.LifecycleSnapshot.ConstructionRetryDue.Ticks);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(3)));
        pump.PumpFrame(1);
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);

        clock.Advance(new MonotonicDuration(10));
        pump.PumpFrame(2);
        Assert.Equal(2, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(130, registration.LifecycleSnapshot.ConstructionRetryDue.Ticks);
        clock.Advance(new MonotonicDuration(20));
        pump.PumpFrame(3);
        Assert.Equal((ulong)3, registration.Runner.Lifecycle.Value);
        Assert.Equal(3, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.False(registration.LifecycleSnapshot.ConstructionFault.IsValid);
    }

    [Fact]
    public void PumpEpochAllowsOnlyOneConstructionAttemptWhenFactoryAdvancesPastRetryDue()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.epoch-backoff");
        using var firstGate = definition.BlockEvaluation(1);
        using var secondGate = definition.BlockEvaluation(2);
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 2L;
        PumpUntil(pump, ref frame, () => firstGate.Entered.IsSet, collector: registry);
        var first = registration.Runner;
        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        PumpUntil(pump, ref frame, () => secondGate.Entered.IsSet, collector: registry);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(3));

        firstGate.Release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => first.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));
        var advanced = false;
        definition.WorkerDefinitionFactoryCallback = () =>
        {
            if (advanced) return;
            advanced = true;
            clock.Advance(new MonotonicDuration(10));
        };
        definition.FailNextWorkerFactories(1);
        var attemptsBeforeFrame = registration.LifecycleSnapshot.ConstructionAttemptCount;

        var failedFrame = pump.PumpFrame(frame++);
        Assert.Equal(1, failedFrame.LifecyclePositionTransitions);
        Assert.Equal(attemptsBeforeFrame + 1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(110, registration.LifecycleSnapshot.ConstructionRetryDue.Ticks);
        Assert.Equal(110, clock.Now.Ticks);
        Assert.Equal(3, definition.WorkerDefinitionCreateCount);
        Assert.Throws<InvalidOperationException>(() => _ = registration.Runner);

        var retryFrame = pump.PumpFrame(frame++);
        Assert.Equal(1, retryFrame.LifecyclePositionTransitions);
        Assert.Equal(attemptsBeforeFrame + 2, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal((ulong)3, registration.Runner.Lifecycle.Value);
        Assert.Equal(4, definition.WorkerDefinitionCreateCount);
        secondGate.Release.Set();
    }

    [Fact]
    public void WorkerConstructionFailureIsAtomicAndRetriesAtDueTime()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.worker-failure");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        definition.FailNextWorkerFactories(1);

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.True(registration.LifecycleSnapshot.ConstructionFault.IsValid);
        Assert.Equal(2, definition.WorkerDefinitionCreateCount);
        clock.Advance(new MonotonicDuration(9));
        pump.PumpFrame(1);
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        clock.Advance(new MonotonicDuration(1));
        pump.PumpFrame(2);
        Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
        Assert.Equal(3, definition.WorkerDefinitionCreateCount);
    }

    [Fact]
    public void ThreadStartConstructionFailureIsAtomicAndRetriesAtDueTime()
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new FailSecondThreadStart();
        using var registry = new ServiceCycleRegistry(
            1, clock, measureWorkerAllocations: false, workerStarter: starter);
        var definition = new LifecycleServiceDefinition("lifecycle.thread-start-failure");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        Assert.Equal(2, starter.AttemptCount);
        Assert.True(registration.LifecycleSnapshot.ConstructionFault.IsValid);
        Assert.Equal(110, registration.LifecycleSnapshot.ConstructionRetryDue.Ticks);
        clock.Advance(new MonotonicDuration(10));
        pump.PumpFrame(1);
        Assert.Equal(3, starter.AttemptCount);
        Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
    }

    [Fact]
    public void ReplacementAllocatesFreshRunnerResourceGraphAndLifecycleState()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new LifecycleServiceDefinition("lifecycle.resources");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 2L;
        PumpUntil(pump, ref frame, () => definition.StateSerial(1) != 0, collector: registry);
        var oldRunner = registration.Runner;
        var old = oldRunner.ResourceIdentity;

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        var current = registration.Runner;
        var fresh = current.ResourceIdentity;
        Assert.NotSame(old.WorkerDefinition, fresh.WorkerDefinition);
        Assert.NotSame(old.ActionStore, fresh.ActionStore);
        Assert.NotSame(old.Handoff, fresh.Handoff);
        Assert.NotSame(old.Worker, fresh.Worker);
        Assert.NotSame(old.MainState, fresh.MainState);
        Assert.NotSame(old.StartCoordinator, fresh.StartCoordinator);
        Assert.NotSame(old.BatchCompletion, fresh.BatchCompletion);
        PumpUntil(pump, ref frame, () => definition.StateSerial(2) != 0, collector: registry);
        Assert.NotEqual(definition.StateSerial(1), definition.StateSerial(2));
    }

    [Fact]
    public void WorkerDefinitionAliasIsRejectedBeforeThreadStart()
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new CountingThreadStarter();
        using var registry = new ServiceCycleRegistry(1, clock, false, starter);
        var definition = new LifecycleServiceDefinition("lifecycle.worker-alias");
        using var gate = definition.BlockEvaluation(1);
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        PumpUntil(pump, ref frame, () => gate.Entered.IsSet);
        definition.ReuseWorkerDefinition = true;

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(2, definition.WorkerDefinitionCreateCount);
        Assert.Equal(1, starter.AttemptCount);
        pump.PumpFrame(frame++);
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        clock.Advance(new MonotonicDuration(10));
        pump.PumpFrame(frame++);
        Assert.Equal(2, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(3, definition.WorkerDefinitionCreateCount);
        Assert.Equal(1, starter.AttemptCount);
        gate.Release.Set();
    }

    [Fact]
    public void RegistryRejectsCrossServiceWorkerDefinitionAliases()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(3, clock);
        var ownerDefinition = new LifecycleServiceDefinition("lifecycle.cross-owner");
        using var owner = registry.Register(
            ownerDefinition, new LifecycleGeneration(1));

        var workerAlias = new LifecycleServiceDefinition("lifecycle.cross-worker")
        {
            SharedWorkerDefinition =
                (IServiceCycleWorkerDefinition<LifecycleState, LifecycleAction>)
                owner.Runner.ResourceIdentity.WorkerDefinition,
        };
        Assert.Throws<ServiceRunnerResourceAliasingException>(() => registry.Register(
            workerAlias, new LifecycleGeneration(1)));
        Assert.Equal(1, registry.Count);
        Assert.Equal((ulong)1, owner.Runner.Lifecycle.Value);
    }

    [Fact]
    public void CallbackCanObserveConcurrentStateFactoryFailFastWithoutDeadlock()
    {
        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        var ledger = (ServiceResourceClaimLedger)typeof(ServiceCycleRegistry).GetField(
            "_resourceClaims",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
        var definition = new LifecycleServiceDefinition("lifecycle.worker-callback-state-claim");
        using var completed = new ManualResetEventSlim(false);
        var stateAdmission = default(ServiceResourceClaimResult);
        definition.WorkerDefinitionFactoryCallback = () =>
        {
            var claimant = new Thread(() =>
            {
                stateAdmission = ledger.TryBeginFactory(ServiceResourceRole.State, out _);
                completed.Set();
            }) { IsBackground = true };
            claimant.Start();
            Assert.True(completed.Wait(ServiceCycleTestDeadline.Value));
        };

        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));

        Assert.True(completed.IsSet);
        Assert.Equal(ServiceResourceClaimResult.Contended, stateAdmission);
        Assert.Equal(1, definition.WorkerDefinitionCreateCount);
        Assert.Equal(1, ledger.LiveClaimCount);
    }

    [Fact]
    public void UniqueReservedWorkerDefinitionRollsBackExactlyOnceAfterLaterConstructionFailure()
    {
        var starter = new FailSecondThreadStart();
        using var registry = new ServiceCycleRegistry(
            2,
            new ThreadSafeTestClock(100),
            false,
            starter);
        using var owner = registry.Register(
            new LifecycleServiceDefinition("lifecycle.worker-rollback-owner"),
            new LifecycleGeneration(1));
        var ledger = (ServiceResourceClaimLedger)typeof(ServiceCycleRegistry).GetField(
            "_resourceClaims",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
        var ownerClaims = ledger.LiveClaimCount;
        var definition = new LifecycleServiceDefinition("lifecycle.worker-rollback-contender");

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition,
            new LifecycleGeneration(1)));
        Assert.Equal(1, definition.WorkerDefinitionCreateCount);
        // The claim the failed attempt reserved is gone, and only that one: the owner still holds
        // its own. A rollback that released more than it took would be invisible in a count of
        // registrations and fatal on the next construction.
        Assert.Equal(ownerClaims, ledger.LiveClaimCount);
        Assert.Equal(1, registry.Count);
        Assert.Equal(2, starter.AttemptCount);

        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        Assert.Equal(2, definition.WorkerDefinitionCreateCount);
        Assert.Equal(3, starter.AttemptCount);
        Assert.Equal((ulong)1, registration.Runner.Lifecycle.Value);
    }

    [Fact]
    public void CapacityPreventsWorkerDefinitionFactoryCall()
    {
        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        var ledger = (ServiceResourceClaimLedger)typeof(ServiceCycleRegistry).GetField(
            "_resourceClaims",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
        for (var index = 0; index < ledger.Capacity; index++)
        {
            Assert.Equal(
                ServiceResourceClaimResult.Claimed,
                ledger.TryBeginFactory(ServiceResourceRole.State, out var claim));
            Assert.Equal(
                ServiceResourceClaimResult.Claimed,
                ledger.FinalizeFactory(claim, new object()));
            ledger.EndFactory(claim);
        }
        var definition = new LifecycleServiceDefinition("lifecycle.worker-capacity");

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition,
            new LifecycleGeneration(1)));
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, definition.WorkerDefinitionCreateCount);
        Assert.Equal(ledger.Capacity, ledger.LiveClaimCount);
    }

    [Fact]
    public void WorkerDefinitionFactoryExceptionReleasesReservedSlotForRetry()
    {
        using var registry = new ServiceCycleRegistry(1, new ThreadSafeTestClock(100));
        var definition = new LifecycleServiceDefinition("lifecycle.worker-reservation-callback");
        definition.WorkerDefinitionFactoryCallback = () =>
            throw new InvalidOperationException("synthetic worker callback failure");

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition,
            new LifecycleGeneration(1)));
        Assert.Equal(1, definition.WorkerDefinitionCreateCount);
        Assert.Equal(0, registry.Count);

        definition.WorkerDefinitionFactoryCallback = null;
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        Assert.Equal(2, definition.WorkerDefinitionCreateCount);
        Assert.Equal((ulong)1, registration.Runner.Lifecycle.Value);
    }

    [Fact]
    public void ConstructionFactoryCannotReenterLifecycleReconciliation()
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new CountingThreadStarter();
        using var registry = new ServiceCycleRegistry(1, clock, false, starter);
        var definition = new LifecycleServiceDefinition("lifecycle.factory-reentrant");
        using var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        Exception? rejected = null;
        definition.WorkerDefinitionFactoryCallback = () =>
        {
            try { pump.RequestLifecycleReplacement(new LifecycleGeneration(3)); }
            catch (Exception ex) { rejected = ex; throw; }
        };

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        Assert.IsType<InvalidOperationException>(rejected);
        Assert.Equal((ulong)2, registration.LifecycleSnapshot.DesiredLifecycle.Value);
        Assert.Equal(1, registration.LifecycleSnapshot.ConstructionAttemptCount);
        Assert.Equal(1, starter.AttemptCount);
        Assert.Throws<InvalidOperationException>(() => _ = registration.Runner);

        definition.WorkerDefinitionFactoryCallback = null;
        clock.Advance(new MonotonicDuration(10));
        pump.PumpFrame(1);
        Assert.Equal(2, starter.AttemptCount);
        Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
    }

    [Theory]
    [InlineData(InitialConstructionMutation.Register)]
    [InlineData(InitialConstructionMutation.Seal)]
    [InlineData(InitialConstructionMutation.Dispose)]
    [InlineData(InitialConstructionMutation.Release)]
    [InlineData(InitialConstructionMutation.ReconcileLifecycle)]
    public void InitialFactoryCannotMutateCompositionAndFailedRegistrationIsRetryable(
        InitialConstructionMutation mutation)
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new CountingThreadStarter();
        using var registry = new ServiceCycleRegistry(2, clock, false, starter);
        ServiceRegistration<LifecycleState, LifecycleAction>? owner = null;
        if (mutation == InitialConstructionMutation.Release)
        {
            owner = registry.Register(
                new LifecycleServiceDefinition("lifecycle.initial-reentrant.release-owner"),
                new LifecycleGeneration(1));
        }
        var baselineCount = owner is null ? 0 : 1;
        var definition = new LifecycleServiceDefinition(
            $"lifecycle.initial-reentrant.{mutation}");
        Exception? rejected = null;
        var nestedDefinition = new LifecycleServiceDefinition(
            $"lifecycle.initial-reentrant.nested.{mutation}");
        Action callback = () =>
        {
            try
            {
                switch (mutation)
                {
                    case InitialConstructionMutation.Register:
                        registry.Register(
                            nestedDefinition,
                            new LifecycleGeneration(1));
                        break;
                    case InitialConstructionMutation.Seal:
                        registry.Seal();
                        break;
                    case InitialConstructionMutation.Dispose:
                        registry.Dispose();
                        break;
                    case InitialConstructionMutation.Release:
                        owner!.Dispose();
                        break;
                    case InitialConstructionMutation.ReconcileLifecycle:
                        registry.ReconcileLifecycle(clock.Now);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
            }
            catch (Exception ex)
            {
                rejected = ex;
                throw;
            }
        };
        definition.WorkerDefinitionFactoryCallback = callback;

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition,
            new LifecycleGeneration(1)));

        Assert.IsType<InvalidOperationException>(rejected);
        Assert.Equal(baselineCount, registry.Count);
        Assert.Equal(baselineCount, registry.OrdinalCount);
        Assert.False(registry.IsSealed);
        Assert.Equal(baselineCount, starter.AttemptCount);
        Assert.Equal(1, definition.WorkerDefinitionCreateCount);

        definition.WorkerDefinitionFactoryCallback = null;
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        Assert.Equal(baselineCount + 1, registry.Count);
        Assert.Equal(baselineCount + 1, registry.OrdinalCount);
        Assert.Equal(baselineCount + 1, starter.AttemptCount);
        Assert.Equal((ulong)1, registration.Runner.Lifecycle.Value);
        owner?.Dispose();
    }

    [Theory]
    [InlineData("registration")]
    [InlineData("registry")]
    [InlineData("pump")]
    [InlineData("register")]
    [InlineData("seal")]
    public void ConstructionCallbacksCannotMutateComposition(string mutation)
    {
        var clock = new ThreadSafeTestClock(100);
        var starter = new CountingThreadStarter();
        var registry = new ServiceCycleRegistry(2, clock, false, starter);
        var definition = new LifecycleServiceDefinition(
            $"lifecycle.composition-guard.{mutation}");
        var registration = registry.Register(
            definition, new LifecycleGeneration(1));
        registry.Seal();
        var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        Exception? rejected = null;
        Action callback = () =>
        {
            try
            {
                switch (mutation)
                {
                    case "registration":
                        registration.Dispose();
                        break;
                    case "registry":
                        registry.Dispose();
                        break;
                    case "pump":
                        pump.Dispose();
                        break;
                    case "register":
                        registry.Register(
                            new LifecycleServiceDefinition("lifecycle.illegal-register"),
                            new LifecycleGeneration(2));
                        break;
                    case "seal":
                        registry.Seal();
                        break;
                    default:
                        throw new InvalidOperationException("Unknown composition mutation fixture.");
                }
            }
            catch (Exception ex)
            {
                rejected = ex;
            }
        };
        definition.WorkerDefinitionFactoryCallback = callback;

        try
        {
            Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
            Assert.IsType<InvalidOperationException>(rejected);
            Assert.False(pump.IsDisposed);
            Assert.Equal(1, registry.Count);
            Assert.Equal(2, starter.AttemptCount);
            Assert.Equal((ulong)2, registration.Runner.Lifecycle.Value);
            Assert.Equal(2, registration.LifecycleSnapshot.LivePositionCount);

            definition.WorkerDefinitionFactoryCallback = null;
            registration.Dispose();
            Assert.Equal(0, registry.Count);
            pump.Dispose();
            Assert.True(pump.IsDisposed);
        }
        finally
        {
            definition.WorkerDefinitionFactoryCallback = null;
            try { registration.Dispose(); }
            catch { }
            try { pump.Dispose(); }
            catch { }
            try { registry.Dispose(); }
            catch { }
        }
    }

    public enum InitialConstructionMutation
    {
        Register = 1,
        Seal = 2,
        Dispose = 3,
        Release = 4,
        ReconcileLifecycle = 5,
    }

    private sealed class FailSecondThreadStart : IServiceCycleWorkerStarter
    {
        internal int AttemptCount { get; private set; }

        public void Start(Thread thread)
        {
            AttemptCount++;
            if (AttemptCount == 2)
                throw new InvalidOperationException("synthetic replacement thread-start failure");
            thread.Start();
        }
    }

}
