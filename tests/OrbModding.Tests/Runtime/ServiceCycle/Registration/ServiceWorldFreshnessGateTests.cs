using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

/// <summary>
/// A service must not start a cycle while the live world was collected before that service went live
/// or before its last game-facing action attempt — otherwise it decides against a world it does not
/// appear in, or re-decides from facts its own action boundary already invalidated.
/// </summary>
/// <remarks>
/// These live here rather than in any consuming feature on purpose. The rule belongs to consuming a
/// shared snapshot, not to Auto Buy, and nothing opts into it: the gate is born armed, and after that
/// every attempted game-facing action raises its floor. Every composition here publishes worlds the
/// way production's collection service does, because that is the only world a service is ever gated
/// against — the first reading included.
/// </remarks>
public sealed class ServiceWorldFreshnessGateTests
{
    /// <summary>
    /// The generation a test publishes so a mutating service may start at all. Any generation newer
    /// than the seed publication does, and every frame these tests pump is later than this one.
    /// </summary>
    private const long ActivationWorld = TestWorldCollector.ActivationFrame;

    /// <summary>
    /// The gate is closed the moment a mutating service goes live, so even its first cycle waits for
    /// a reading collected after it existed.
    /// </summary>
    /// <remarks>
    /// The world a service is handed at activation is the seed publication: an empty world, or one
    /// the game has not finished pricing. Deciding against it is deciding about a game with nothing
    /// in it in which nothing costs anything — in a real save that submitted around 180 purchases
    /// Auto Buy believed were free, before the first honest collection ever landed.
    /// </remarks>
    [Fact]
    public void AMutatingServiceDoesNotStartBeforeAWorldCollectedAfterItWentLive()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.birth") { ActionCount = 1 };
        using var registration = registry.Register(
            service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        for (var frame = 10L; frame < 20; frame++) pump.PumpFrame(frame);
        Assert.Equal(0, service.StartCount);

        TestWorldCollector.CollectedAtActivation(registry);
        PumpUntil(pump, registration, () => service.StartCount > 0, 20);
    }

    /// <summary>
    /// A Source is exempt, because it is the collector: a gate closed on a generation only it can
    /// publish would stop the whole suite on its first frame, every service waiting for a world
    /// nobody is left to read.
    /// </summary>
    [Fact]
    public void ASourceShapedServiceStartsOnTheSeedWorldBecauseNothingElseWouldPublishOne()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.collector")
        {
            ActionCount = 1,
            PublishesGeneration = new WorldGeneration(2),
        };
        using var registration = registry.Register(
            service,
            new LifecycleGeneration(1),
            ServiceActionDispatchPolicy.Bounded(1, ServiceActionDispatchClass.Publication));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        PumpUntil(pump, registration, () => service.StartCount > 0);
    }

    /// <summary>
    /// A replacement runner is armed on the world it went live on, not on the one its predecessor
    /// started against: a fresh runner is as new to the world as a fresh service is.
    /// </summary>
    [Fact]
    public void AReplacementRunnerIsArmedOnTheWorldItWentLiveOn()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.rearmed") { ActionCount = 0 };
        using var registration = registry.Register(
            service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        TestWorldCollector.CollectedAtActivation(registry);
        var frame = PumpUntil(pump, registration, () => service.StartCount > 0);
        frame = SettleZeroActionCycle(pump, registration, frame + 1);

        // The world moves well past the first runner's arming, and only then is the runner replaced:
        // the replacement takes that newer world as its own floor.
        TestWorldCollector.CollectedAt(registry, frame + 20);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        frame = PumpUntil(
            pump,
            registration,
            () => registration.LifecycleSnapshot.ActiveLifecycle.Value == 2,
            frame + 1);

        var startsUnderTheReplacement = service.StartCount;
        for (var i = 0; i < 5; i++) pump.PumpFrame(frame + i + 1);
        Assert.Equal(startsUnderTheReplacement, service.StartCount);

        TestWorldCollector.CollectedAt(registry, frame + 21);
        PumpUntil(
            pump,
            registration,
            () => service.StartCount > startsUnderTheReplacement,
            frame + 7);
    }

    /// <summary>
    /// A service on a long timed wait still meets a new lifecycle promptly: the replacement runner has
    /// no wait, so the first world collected under it starts a cycle.
    /// </summary>
    [Fact]
    public void AServiceOnATimedWaitStartsOnTheFirstWorldOfANewLifecycle()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.timed")
        {
            ActionCount = 0,
            EvaluationWake = WakePolicy.AfterDecision(new MonotonicDuration(1_000_000)),
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        TestWorldCollector.CollectedAtActivation(registry);
        var frame = PumpUntil(pump, registration, () => service.StartCount > 0);
        frame = SettleZeroActionCycle(pump, registration, frame + 1);
        var startsBefore = service.StartCount;
        TestWorldCollector.CollectedAt(registry, frame + 1);
        for (var i = 0; i < 5; i++) pump.PumpFrame(frame + i + 1);
        Assert.Equal(startsBefore, service.StartCount);

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        frame = PumpUntil(
            pump,
            registration,
            () => registration.LifecycleSnapshot.ActiveLifecycle.Value == 2,
            frame + 6);
        TestWorldCollector.CollectedAt(registry, frame + 1);
        PumpUntil(pump, registration, () => service.StartCount > startsBefore, frame + 2);
    }

    /// <summary>
    /// The race in one test: the action lands on a later frame than the snapshot that arrives after
    /// it, so that snapshot cannot contain the action and must not be acted on.
    /// </summary>
    [Fact]
    public void ASnapshotCollectedBeforeTheActionDoesNotLetTheServiceStartAgain()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.stale") { ActionCount = 1 };
        using var registration = registry.Register(
            service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = PumpUntilActionExecuted(pump, registry, registration, service);
        var capturesAfterAction = service.StartCount;

        // Collected before the action committed: newly published, but describing an older world.
        TestWorldCollector.CollectedAt(registry, frame - 1);
        for (var i = 0; i < 5; i++) pump.PumpFrame(frame + i + 1);

        Assert.Equal(capturesAfterAction, service.StartCount);
    }

    /// <summary>
    /// The gate re-arms: once a stale snapshot has held the service back, the next collection that
    /// does contain the action lets it start again. Inherited from the replay-registration fact that
    /// died with the replay layer — the sequence was only ever exercised on that path.
    /// </summary>
    [Fact]
    public void ASnapshotCollectedAfterTheActionLetsItStartAgain()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.fresh") { ActionCount = 1 };
        using var registration = registry.Register(
            service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = PumpUntilActionExecuted(pump, registry, registration, service);
        var capturesAfterAction = service.StartCount;

        TestWorldCollector.CollectedAt(registry, frame - 1);
        for (var i = 0; i < 5; i++) pump.PumpFrame(frame + i + 1);
        Assert.Equal(capturesAfterAction, service.StartCount);

        TestWorldCollector.CollectedAt(registry, frame + 1);
        PumpUntil(pump, registration, () => service.StartCount > capturesAfterAction, frame + 7);
    }

    /// <summary>
    /// The gate needs no declaration: a service that has committed a native mutation is held by the
    /// next stale reading whether or not it ever said it consumes the world.
    /// </summary>
    /// <remarks>
    /// This replaces the fact that a service which never opted in was never gated. There is no
    /// opting in any more, and the case it protected — a composition with no world publisher — was
    /// only ever a test fixture: the game always has a collector.
    /// </remarks>
    [Fact]
    public void AMutatingServiceIsGatedWithoutDeclaringAnything()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.undeclared") { ActionCount = 1 };
        using var registration = registry.Register(
            service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = PumpUntilActionExecuted(pump, registry, registration, service);
        var capturesAfterAction = service.StartCount;

        for (var i = 0; i < 5; i++) pump.PumpFrame(frame + i + 1);
        Assert.Equal(capturesAfterAction, service.StartCount);

        TestWorldCollector.CollectedAt(registry, frame + 1);
        PumpUntil(pump, registration, () => service.StartCount > capturesAfterAction, frame + 7);
    }

    /// <summary>
    /// A Source-shaped service must not gate itself behind its own output. It changed no game state,
    /// so there is nothing for a later snapshot to be missing — and it is gated like everything else,
    /// so only the shape of what it commits keeps it running.
    /// </summary>
    [Fact]
    public void ASourceShapedServiceIsNeverGatedByItsOwnPublication()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.publisher")
        {
            ActionCount = 1,
            PublishesGeneration = new WorldGeneration(2),
        };
        using var registration = registry.Register(
            service,
            new LifecycleGeneration(1),
            ServiceActionDispatchPolicy.Bounded(1, ServiceActionDispatchClass.Publication));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        Assert.Equal(ServiceShape.Source, registry.GetSlot(0).ActionDispatchPolicy.Shape);

        var frame = PumpUntilActionExecuted(pump, registry, registration, service);
        var capturesAfterAction = service.StartCount;

        TestWorldCollector.CollectedAt(registry, frame - 1);
        for (var i = 0; i < 5; i++) pump.PumpFrame(frame + i + 1);

        Assert.True(service.StartCount > capturesAfterAction);
    }

    /// <summary>
    /// A failed publication attempt cannot close the collector behind a reading only it can produce.
    /// The exemption belongs to the Source shape, not to one successful publication disposition.
    /// </summary>
    [Fact]
    public void ASourceShapedServiceIsNeverGatedByARejectedPublicationAttempt()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.publisher-rejected")
        {
            ActionCount = 1,
            RejectAtIndex = 0,
        };
        using var registration = registry.Register(
            service,
            new LifecycleGeneration(1),
            ServiceActionDispatchPolicy.Bounded(1, ServiceActionDispatchClass.Publication));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = PumpUntil(pump, registration, () => service.ActionExecutionCount > 0);
        var startsAfterAttempt = service.StartCount;

        PumpUntil(pump, registration, () => service.StartCount > startsAfterAttempt, frame + 1);
    }

    /// <summary>
    /// The gate belongs to the slot, not to the runner sitting in it: replacing a service's
    /// lifecycle does not forgive what that service already did to the game.
    /// </summary>
    /// <remarks>
    /// The fresh runner has no memory of the action, but the world is still missing it. Starting it
    /// straight away would decide against exactly the stale reading the gate exists to refuse —
    /// the duplicate-action bug wearing a new runner.
    /// </remarks>
    [Fact]
    public void AReplacedRunnerInheritsItsSlotsLastActionFrame()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.replaced") { ActionCount = 1 };
        using var registration = registry.Register(
            service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var actionFrame = PumpUntilActionExecuted(pump, registry, registration, service);
        var startsAfterAction = service.StartCount;

        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        Assert.Equal((ulong)2, registration.LifecycleSnapshot.DesiredLifecycle.Value);

        for (var i = 0; i < 5; i++) pump.PumpFrame(actionFrame + i + 1);
        Assert.Equal(startsAfterAction, service.StartCount);

        TestWorldCollector.CollectedAt(registry, actionFrame + 1);
        PumpUntil(pump, registration, () => service.StartCount > startsAfterAction, actionFrame + 7);
    }

    /// <summary>
    /// The gate is per slot: a service parked behind its own stuck retirees cannot park a sibling
    /// the collector is still reading for.
    /// </summary>
    /// <remarks>
    /// The sibling changes the game before either replacement, which is what arms its own gate, so
    /// this is the claim a stuck-retiree test can honestly make — and it holds because a reading
    /// arrives every frame, not because a thread happened to be scheduled in a lucky order.
    /// </remarks>
    [Fact]
    public void AStuckSiblingDoesNotHoldAnotherSlotsGate()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var stuckDefinition = new LifecycleServiceDefinition("gate.stuck-sibling");
        var liveDefinition = new LifecycleServiceDefinition("gate.live-sibling");
        using var firstGate = stuckDefinition.BlockEvaluation(1);
        using var secondGate = stuckDefinition.BlockEvaluation(2);
        using var stuck = registry.Register(
            stuckDefinition, new LifecycleGeneration(1));
        using var live = registry.Register(
            liveDefinition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = 2L;
        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => firstGate.Entered.IsSet, clock, registry);
        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => liveDefinition.ExecutionCount(1) != 0, clock, registry);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(2));
        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => secondGate.Entered.IsSet, clock, registry);
        pump.RequestLifecycleReplacement(new LifecycleGeneration(3));
        Assert.Equal(2, stuck.LifecycleSnapshot.LivePositionCount);

        ServiceCyclePumpTestWait.PumpUntil(
            pump, ref frame, () => liveDefinition.ExecutionCount(3) != 0, clock, registry);
        Assert.Equal(2, stuck.LifecycleSnapshot.LivePositionCount);
        firstGate.Release.Set();
        secondGate.Release.Set();
    }

    /// <summary>
    /// A held frame is counted, because a held service and an idle one otherwise leave identical
    /// evidence — and a collector that stopped publishing holds every mutating service at once.
    /// </summary>
    [Fact]
    public void HeldFramesAreCountedAndStopBeingCountedOnceAReadingArrives()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.counted") { ActionCount = 1 };
        using var registration = registry.Register(
            service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var actionFrame = PumpUntilActionExecuted(pump, registry, registration, service);
        var firstHeld = PumpUntilHeld(pump, actionFrame + 1);
        for (var i = 1; i <= 5; i++)
            Assert.Equal(1, pump.PumpFrame(firstHeld + i).WorldGateDeferrals);
        Assert.Equal(6, ServiceCycleDiagnostics.ReadPump(pump).WorldGateDeferrals);
        Assert.Equal(1, service.ActionExecutionCount);

        TestWorldCollector.CollectedAt(registry, firstHeld + 5);
        Assert.Equal(0, pump.PumpFrame(firstHeld + 6).WorldGateDeferrals);
        Assert.Equal(6, ServiceCycleDiagnostics.ReadPump(pump).WorldGateDeferrals);
    }

    /// <summary>
    /// The held service says which frame held it and what it is waiting past, so a stall can be read
    /// off the slot rather than inferred from an absence of work.
    /// </summary>
    [Fact]
    public void AHeldServiceRecordsTheFrameAndTheActionItIsWaitingPast()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.fact") { ActionCount = 1 };
        using var registration = registry.Register(
            service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var actionFrame = PumpUntilActionExecuted(pump, registry, registration, service);
        Assert.False(registry.GetSlot(0).LifecycleSnapshot.LatestWorldGateDeferral.IsPresent);

        var heldFrame = PumpUntilHeld(pump, actionFrame + 1);

        var deferral = registry.GetSlot(0).LifecycleSnapshot.LatestWorldGateDeferral;
        Assert.True(deferral.IsPresent);
        Assert.Equal(heldFrame, deferral.FrameIdentity);
        Assert.Equal(actionFrame, deferral.LastActionFrame);
        Assert.Equal((ulong)ActivationWorld, deferral.World.Value);
    }

    /// <summary>
    /// A pre-native skip says the action's snapshot was stale even though no game mutation happened,
    /// so the same snapshot must not be planned again.
    /// </summary>
    [Fact]
    public void APreNativeSkipWaitsForAWorldCollectedAfterTheRefusal()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.pre-native-skip")
        {
            ActionCount = 1,
            SkipWithoutNativeAtIndex = 0,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var actionFrame = PumpUntilActionExecuted(pump, registry, registration, service);
        var executions = service.ActionExecutionCount;
        PumpUntilHeld(pump, actionFrame + 1);
        Assert.Equal(executions, service.ActionExecutionCount);

        TestWorldCollector.CollectedAt(registry, actionFrame + 1);
        PumpUntil(
            pump,
            registration,
            () => service.ActionExecutionCount > executions,
            actionFrame + 2);
    }

    /// <summary>
    /// A native rejection proves that live reality disagreed with the pinned facts which produced
    /// the action, so immediate wake cannot plan against those facts again.
    /// </summary>
    [Fact]
    public void ARejectedActionWaitsForAWorldCollectedAfterTheAttempt()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.rejected")
        {
            ActionCount = 1,
            RejectAtIndex = 0,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var actionFrame = PumpUntilActionExecuted(pump, registry, registration, service);
        var executions = service.ActionExecutionCount;
        PumpUntilHeld(pump, actionFrame + 1);
        Assert.Equal(executions, service.ActionExecutionCount);

        TestWorldCollector.CollectedAt(registry, actionFrame + 1);
        PumpUntil(
            pump,
            registration,
            () => service.ActionExecutionCount > executions,
            actionFrame + 2);
    }

    /// <summary>
    /// Fault backoff remains the failure policy, but expiring it is not permission to reuse the
    /// world whose action boundary faulted.
    /// </summary>
    [Fact]
    public void AFaultedActionWaitsForAWorldCollectedAfterTheAttempt()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.faulted")
        {
            ActionCount = 1,
            FaultAtIndex = 0,
        };
        using var registration = registry.Register(service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var actionFrame = PumpUntilActionExecuted(pump, registry, registration, service);
        var executions = service.ActionExecutionCount;
        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        PumpUntilHeld(pump, actionFrame + 1);
        Assert.Equal(executions, service.ActionExecutionCount);

        TestWorldCollector.CollectedAt(registry, actionFrame + 1);
        PumpUntil(
            pump,
            registration,
            () => service.ActionExecutionCount > executions,
            actionFrame + 2);
    }

    /// <summary>
    /// The epoch a snapshot was collected under is no part of this gate, in either direction: a
    /// snapshot from another run of the game releases a held service if its frame is fresh enough, and
    /// one from this run holds it if its frame is not.
    /// </summary>
    /// <remarks>
    /// The gate answers "has the world been re-read since my action", which is a frame comparison and
    /// only ever a frame comparison. An epoch answers a different question, and a consumer that needs
    /// it asks it at its own boundary — folding it in here would turn one rule into two that can
    /// disagree, and would make a save load look like a stall rather than a replacement. This pins
    /// that; it is the cheap guard against a future writer making the gate epoch-aware.
    /// </remarks>
    [Fact]
    public void TheEpochASnapshotWasCollectedUnderIsNoPartOfTheGate()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var service = new ExecutionServiceDefinition("gate.epoch") { ActionCount = 1 };
        using var registration = registry.Register(
            service, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        var frame = PumpUntilActionExecuted(pump, registry, registration, service);
        var capturesAfterAction = service.StartCount;

        // Collected under this very lifecycle, but before the action: still held, because the epoch
        // agreeing says nothing about whether the reading contains the action.
        var sameEpoch = new GameWorldState { CollectedAtEpoch = 1 };
        TestWorldCollector.CollectedAt(registry, frame - 1, sameEpoch);
        for (var i = 0; i < 5; i++) pump.PumpFrame(frame + i + 1);
        Assert.Equal(capturesAfterAction, service.StartCount);

        // Collected under a lifecycle the service was never pinned to, and after the action: released
        // anyway, because the frame is the only thing this gate reads.
        var otherEpoch = new GameWorldState { CollectedAtEpoch = 99 };
        TestWorldCollector.CollectedAt(registry, frame + 1, otherEpoch);
        PumpUntil(pump, registration, () => service.StartCount > capturesAfterAction, frame + 7);
    }

    /// <summary>
    /// The shape is read off where the service's turn falls rather than declared beside it, so a
    /// Publication dispatch class and a Source shape cannot come apart.
    /// </summary>
    [Fact]
    public void TheShapeIsDerivedFromWhereTheServicesTurnFalls()
    {
        Assert.Equal(ServiceShape.Ordinary, ServiceActionDispatchPolicy.Single.Shape);
        Assert.Equal(ServiceShape.Ordinary, ServiceActionDispatchPolicy.Bounded(4).Shape);
        Assert.Equal(
            ServiceShape.Ordinary,
            ServiceActionDispatchPolicy.Bounded(4, ServiceActionDispatchClass.GameMutation).Shape);
        Assert.Equal(
            ServiceShape.Source,
            ServiceActionDispatchPolicy.Bounded(1, ServiceActionDispatchClass.Publication).Shape);
    }

    /// <summary>
    /// Pumps to the first frame the gate reports holding the service back.
    /// </summary>
    /// <remarks>
    /// Not the frame after the action: a slot that has already had its turn is not asked to start
    /// again that frame, and the frames right after an action still have that batch's response to
    /// collect. The gate is only consulted once the slot has nothing else left to do.
    /// </remarks>
    private static long PumpUntilHeld(SuiteFramePump pump, long from)
    {
        for (var frame = from; frame < from + 20; frame++)
            if (pump.PumpFrame(frame).WorldGateDeferrals != 0) return frame;
        throw new TimeoutException("the gate never reported holding the service");
    }

    /// <summary>
    /// Says what the collector would have said, then pumps until the service has changed the game.
    /// </summary>
    /// <remarks>
    /// The reading comes first because the gate is born armed: without one the service never starts
    /// a cycle, never acts, and there is nothing left to gate. Its generation is
    /// <see cref="ActivationWorld"/>, older than every frame these tests go on to pump, so the frames
    /// a test collects at afterwards are still the newer publications the gate compares against.
    /// </remarks>
    private static long PumpUntilActionExecuted(
        SuiteFramePump pump,
        ServiceCycleRegistry registry,
        ServiceRegistration<ExecutionState, ExecutionAction> registration,
        ExecutionServiceDefinition service)
    {
        TestWorldCollector.CollectedAtActivation(registry);
        return PumpUntil(pump, registration, () => service.ActionExecutionCount > 0);
    }

    /// <summary>
    /// Returns the exact frame identity the condition became true on — for an action, the frame the
    /// gate will compare a later snapshot against. Whenever a frame starts worker work, the fixture
    /// waits on the handoff's response-publication monitor before pumping again. The twenty-frame
    /// ceiling therefore bounds the pump state machine, not how soon the host schedules a worker.
    /// </summary>
    private static long PumpUntil(
        SuiteFramePump pump,
        ServiceRegistration<ExecutionState, ExecutionAction> registration,
        Func<bool> condition,
        long from = 10L)
    {
        ServiceRunnerTestWait.ForWorkerReady(registration);
        for (var frame = from; frame < from + 20; frame++)
        {
            var report = pump.PumpFrame(frame);
            if (report.CyclesStarted != 0)
                ServiceRunnerTestWait.ForResponse(registration);
            if (condition()) return frame;
            if (registration.Runner.Snapshot.Handoff.CleanupPending)
                ServiceRunnerTestWait.ForCleanup(registration.Runner);
        }

        var handoff = registration.Runner.Snapshot.Handoff;
        throw new TimeoutException(
            $"The pump did not reach the expected condition in twenty state transitions; " +
            $"handoff {handoff.Phase}, cleanup pending {handoff.CleanupPending}.");
    }

    private static long SettleZeroActionCycle(
        SuiteFramePump pump,
        ServiceRegistration<ExecutionState, ExecutionAction> registration,
        long frame)
    {
        Assert.Equal(1, pump.PumpFrame(frame).ResponsesAcquired);
        Assert.Equal(ServiceHandoffPhase.MainOwnedBatch, registration.Runner.HandoffPhaseHint);
        pump.PumpFrame(++frame);
        Assert.Equal(ServiceHandoffPhase.Empty, registration.Runner.HandoffPhaseHint);
        return frame;
    }
}
