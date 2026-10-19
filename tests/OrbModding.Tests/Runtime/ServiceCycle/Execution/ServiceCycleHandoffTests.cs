using System;
using System.Threading;
using System.Threading.Tasks;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceCycleHandoffTests
{
    [Fact]
    public async Task OfflineWorkerReadyWaitObservesTheInitialParkWithoutPolling()
    {
        var handoff = new ServiceCycleHandoff();
        Assert.False(handoff.WaitForWorkerReady(TimeSpan.Zero));

        var worker = Task.Run(() =>
        {
            Assert.Equal(ServiceWorkerWorkKind.Stop, handoff.WaitForWorkerWork(out _, out _, out _));
            handoff.PrepareWorkerExit();
        });

        Assert.True(handoff.WaitForWorkerReady(ServiceCycleTestDeadline.Value));
        handoff.SignalStop();
        await worker.WaitAsync(ServiceCycleTestDeadline.Value);
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public async Task OfflineWaitObservesResponsePublicationWithoutPollingOrLostWakeups()
    {
        var handoff = new ServiceCycleHandoff();
        var configuration = new ConfigurationPublication(new ConfigGeneration(1), TestSuiteConfiguration.Default);
        var identity = new ServiceCycleIdentity(
            new ServiceId("test.handoff.offline-wait"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(1));
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp(10));
        var batch = new BatchId(1);
        var world = EmptyWorld();
        var strategy = NeutralStrategy();

        Assert.True(handoff.TryPublishRequestNonBlocking(configuration, world, strategy, in context, batch, out var sequence));
        Assert.Equal(ServiceWorkerWorkKind.Evaluate, handoff.WaitForWorkerWork(out _, out _, out _));
        var wait = Task.Run(() => handoff.WaitForResponseReady(TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => handoff.OfflineResponseWaiterCount == 1, ServiceCycleTestDeadline.Value));

        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(11));
        var response = ServiceWorkerResponse.Failure(
            sequence,
            identity,
            batch,
            new MonotonicTimestamp(10),
            new MonotonicTimestamp(11),
            fault,
            new MonotonicTimestamp(12),
            default);
        Assert.True(handoff.TryPublishResponse(sequence, in response));
        Assert.True(await wait);
        Assert.Equal(1, handoff.OfflineResponseWakePulseCount);
        Assert.True(handoff.TryAcquireResponseNonBlocking(out _));
        Assert.False(handoff.WaitForResponseReady(TimeSpan.FromMilliseconds(1)));
        Assert.True(handoff.TryCompleteMainOwnershipNonBlocking());
        handoff.DisposeNeverStarted();
    }

    [Fact]
    public async Task SettledWaitObservesTheWorkerSettledAfterResponsePublication()
    {
        var handoff = new ServiceCycleHandoff();
        var configuration = new ConfigurationPublication(new ConfigGeneration(1), TestSuiteConfiguration.Default);
        var identity = new ServiceCycleIdentity(
            new ServiceId("test.handoff.settled-wait"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(1));
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp(10));
        var batch = new BatchId(1);
        var world = EmptyWorld();
        var strategy = NeutralStrategy();
        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(11));

        Assert.True(handoff.TryPublishRequestNonBlocking(configuration, world, strategy, in context, batch, out var sequence));
        using var responsePublished = new ManualResetEventSlim();
        using var allowWorkerToSettle = new ManualResetEventSlim();
        var worker = Task.Run(() =>
        {
            Assert.Equal(ServiceWorkerWorkKind.Evaluate, handoff.WaitForWorkerWork(out _, out _, out _));
            var response = ServiceWorkerResponse.Failure(
                sequence,
                identity,
                batch,
                new MonotonicTimestamp(10),
                new MonotonicTimestamp(11),
                fault,
                new MonotonicTimestamp(12),
                default);
            Assert.True(handoff.TryPublishResponse(sequence, in response));
            responsePublished.Set();
            Assert.True(allowWorkerToSettle.Wait(ServiceCycleTestDeadline.Value));
            Assert.Equal(ServiceWorkerWorkKind.Stop, handoff.WaitForWorkerWork(out _, out _, out _));
            handoff.PrepareWorkerExit();
        });

        Assert.True(responsePublished.Wait(ServiceCycleTestDeadline.Value));
        Assert.False(handoff.WaitForResponseReadyAndWorkerSettled(identity, TimeSpan.Zero));
        var wait = Task.Run(() =>
            handoff.WaitForResponseReadyAndWorkerSettled(identity, ServiceCycleTestDeadline.Value));
        Assert.True(SpinWait.SpinUntil(() => handoff.OfflineResponseWaiterCount == 1, ServiceCycleTestDeadline.Value));
        allowWorkerToSettle.Set();

        Assert.True(await wait);
        Assert.True(handoff.OfflineResponseWakePulseCount >= 1);
        Assert.True(SpinWait.SpinUntil(
            () => handoff.TryAcquireResponseNonBlocking(out _),
            ServiceCycleTestDeadline.Value));
        Assert.True(SpinWait.SpinUntil(
            handoff.TryCompleteMainOwnershipNonBlocking,
            ServiceCycleTestDeadline.Value));
        handoff.SignalStop();
        await worker.WaitAsync(ServiceCycleTestDeadline.Value);
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public async Task OfflineWaitStopsPromptlyAndRejectsUnboundedTimeout()
    {
        var handoff = new ServiceCycleHandoff();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            handoff.WaitForResponseReady(TimeSpan.FromMilliseconds((double)int.MaxValue + 1)));
        Assert.False(handoff.WaitForResponseReady(TimeSpan.Zero));

        var wait = Task.Run(() => handoff.WaitForResponseReady(TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => handoff.OfflineResponseWaiterCount == 1, ServiceCycleTestDeadline.Value));
        handoff.SignalStop();
        Assert.False(await wait);
        Assert.Equal(1, handoff.OfflineResponseWakePulseCount);

        Assert.Equal(ServiceWorkerWorkKind.Stop, handoff.WaitForWorkerWork(out _, out _, out _));
        handoff.PrepareWorkerExit();
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public async Task OfflineWaitWakesWhenWorkerExitMakesResponseImpossible()
    {
        var handoff = new ServiceCycleHandoff();
        var wait = Task.Run(() => handoff.WaitForResponseReady(TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => handoff.OfflineResponseWaiterCount == 1, ServiceCycleTestDeadline.Value));

        handoff.PrepareWorkerExit();

        Assert.False(await wait);
        Assert.Equal(1, handoff.OfflineResponseWakePulseCount);
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public void StopSignalAfterWorkerWakeDisposalIsIdempotent()
    {
        var handoff = new ServiceCycleHandoff();
        handoff.PrepareWorkerExit();
        Assert.True(handoff.WorkerWakeDisposed);

        handoff.SignalStop();

        Assert.True(handoff.Snapshot.StopRequested);
        Assert.True(handoff.TryAcknowledgeWorkerExited());
    }

    [Fact]
    public void StaleAndDuplicateResponsesCannotCrossTheHalfDuplexBoundary()
    {
        var handoff = new ServiceCycleHandoff();
        var configuration = new ConfigurationPublication(new ConfigGeneration(1), TestSuiteConfiguration.Default);
        var identity = new ServiceCycleIdentity(
            new ServiceId("test.handoff.identity"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(1));
        var context = new ServiceCycleContext(identity, default, new MonotonicTimestamp(10));
        var batch = new BatchId(1);
        var world = EmptyWorld();
        var strategy = NeutralStrategy();

        Assert.True(handoff.TryPublishRequestNonBlocking(configuration, world, strategy, in context, batch, out var sequence));
        Assert.Equal(ServiceWorkerWorkKind.Evaluate, handoff.WaitForWorkerWork(out _, out _, out _));
        var fault = new ServiceFault(
            ServiceFaultCategory.Evaluation,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(11));
        var stale = ServiceWorkerResponse.Failure(
            sequence + 1,
            identity,
            batch,
            new MonotonicTimestamp(10),
            new MonotonicTimestamp(11),
            fault,
            new MonotonicTimestamp(12),
            default);
        Assert.False(handoff.TryPublishResponse(sequence, in stale));

        var response = ServiceWorkerResponse.Failure(
            sequence,
            identity,
            batch,
            new MonotonicTimestamp(10),
            new MonotonicTimestamp(11),
            fault,
            new MonotonicTimestamp(12),
            default);
        Assert.True(handoff.TryPublishResponse(sequence, in response));
        Assert.False(handoff.TryPublishResponse(sequence, in response));
        Assert.True(handoff.TryAcquireResponseNonBlocking(out _));
        Assert.False(handoff.TryAcquireResponseNonBlocking(out _));
        Assert.True(handoff.TryCompleteMainOwnershipNonBlocking());
        Assert.Equal(ServiceHandoffPhase.Empty, handoff.Snapshot.Phase);
        handoff.DisposeNeverStarted();
        Assert.True(handoff.WorkerWakeDisposed);
    }

    /// <summary>The world publication a request carries when the test is not about the world.</summary>
    private static WorldPublication<GameWorldState> EmptyWorld()
    {
        using var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        return publisher.ReadLatest();
    }

    private static StrategyPublication NeutralStrategy()
    {
        using var publisher = new ServiceStrategyPublisher(TestSuiteStrategy.Neutral);
        return publisher.ReadLatest();
    }
}
