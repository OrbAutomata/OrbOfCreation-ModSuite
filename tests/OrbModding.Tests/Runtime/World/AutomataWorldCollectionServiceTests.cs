using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The collection service is the only producer of the snapshot every other service reads, so these
/// tests are about the loop rather than about the readings: capture fills a frame, the worker derives
/// and publishes, and a consumer sees a new generation.
/// </summary>
public sealed class AutomataWorldCollectionServiceTests
{
    private static readonly Guid Mana = Guid.NewGuid();
    private static readonly Guid Water = Guid.NewGuid();

    [Fact]
    public void CaptureThenEvaluatePublishesWhatWasCollected()
    {
        var capture = new FakeCapture((frame, _) =>
        {
            var mana = WorldSamples.Resource(Mana, 10d);
            frame.Resources.Append(in mana);
        });
        var publish = new FakePublish();

        RunOneCycle(capture, publish);

        var published = Assert.Single(publish.Published);
        Assert.Equal(1, published.Resources.Count);
        Assert.True(WorldLookup.TryFind(published.Resources, Mana, out var row));
        Assert.Equal(10d, row.Reading.Quantity.ToDouble());
    }

    [Fact]
    public void PublishedWorldCarriesTheCaptureTimestamp()
    {
        var capture = new FakeCapture((_, _) => { });
        var publish = new FakePublish();
        var definition = AutomataWorldCollectionService.Define(capture, publish, new AutomataWorldDemand());
        var worker = definition.CreateWorkerDefinition();
        var frame = new GameWorldCycleFrame();
        var state = worker.CreateState(new LifecycleGeneration(1));

        Cycle(
            definition,
            worker,
            frame,
            ref state,
            new MonotonicTimestamp(123456));

        Assert.Equal(new MonotonicTimestamp(123456), Assert.Single(publish.Published).CollectedAt);
    }

    /// <summary>
    /// The reason the frame is reused rather than rebuilt: a second cycle must not disturb the
    /// snapshot the first one published, because a worker somewhere is still reading it.
    /// </summary>
    [Fact]
    public void ASecondCycleDoesNotDisturbTheFirstSnapshot()
    {
        var quantity = 10d;
        var capture = new FakeCapture((frame, _) =>
        {
            var mana = WorldSamples.Resource(Mana, quantity);
            frame.Resources.Append(in mana);
        });
        var publish = new FakePublish();

        var definition = AutomataWorldCollectionService.Define(capture, publish, new AutomataWorldDemand());
        var worker = definition.CreateWorkerDefinition();
        var frame = new GameWorldCycleFrame();
        var state = worker.CreateState(new LifecycleGeneration(1));

        Cycle(definition, worker, frame, ref state);
        quantity = 999d;
        Cycle(definition, worker, frame, ref state);

        Assert.Equal(2, publish.Published.Count);
        Assert.True(WorldLookup.TryFind(publish.Published[0].Resources, Mana, out var first));
        Assert.True(WorldLookup.TryFind(publish.Published[1].Resources, Mana, out var second));
        Assert.Equal(10d, first.Reading.Quantity.ToDouble());
        Assert.Equal(999d, second.Reading.Quantity.ToDouble());
    }

    /// <summary>
    /// Buffers are reused, so a cycle that reads fewer entities than the last must publish the
    /// smaller set — not the previous cycle's leftovers padded onto it.
    /// </summary>
    [Fact]
    public void ACycleThatReadsFewerEntitiesPublishesFewer()
    {
        var count = 2;
        var capture = new FakeCapture((frame, _) =>
        {
            var mana = WorldSamples.Resource(Mana, 1d);
            frame.Resources.Append(in mana);
            if (count <= 1) return;
            var water = WorldSamples.Resource(Water, 2d);
            frame.Resources.Append(in water);
        });
        var publish = new FakePublish();

        var definition = AutomataWorldCollectionService.Define(capture, publish, new AutomataWorldDemand());
        var worker = definition.CreateWorkerDefinition();
        var frame = new GameWorldCycleFrame();
        var state = worker.CreateState(new LifecycleGeneration(1));

        Cycle(definition, worker, frame, ref state);
        count = 1;
        Cycle(definition, worker, frame, ref state);

        Assert.Equal(2, publish.Published[0].Resources.Count);
        Assert.Equal(1, publish.Published[1].Resources.Count);
    }

    /// <summary>
    /// A snapshot is published every cycle even when nothing changed. Consumers gate on the
    /// generation, and suppressing an unchanged publication would make a stalled collector
    /// indistinguishable from a still world.
    /// </summary>
    [Fact]
    public void AnUnchangedWorldIsStillPublished()
    {
        var capture = new FakeCapture((frame, _) =>
        {
            var mana = WorldSamples.Resource(Mana, 10d);
            frame.Resources.Append(in mana);
        });
        var publish = new FakePublish();

        var definition = AutomataWorldCollectionService.Define(capture, publish, new AutomataWorldDemand());
        var worker = definition.CreateWorkerDefinition();
        var frame = new GameWorldCycleFrame();
        var state = worker.CreateState(new LifecycleGeneration(1));

        Cycle(definition, worker, frame, ref state);
        Cycle(definition, worker, frame, ref state);
        Cycle(definition, worker, frame, ref state);

        Assert.Equal(3, publish.Published.Count);
        Assert.Equal(new WorldGeneration(3), state.LastPublished);
    }

    /// <summary>
    /// A build that resolves nothing must not spin. Capture reports itself unavailable and asks to be
    /// retried later rather than publishing an empty world that reads like an empty save.
    /// </summary>
    [Fact]
    public void ACollectorThatResolvedNothingReportsUnavailableInsteadOfPublishingEmpty()
    {
        var capture = new FakeCapture((_, _) => { }) { IsAvailable = false };
        var publish = new FakePublish();
        var definition = AutomataWorldCollectionService.Define(capture, publish, new AutomataWorldDemand());
        var frame = new GameWorldCycleFrame();
        var config = new SuiteRuntimeConfiguration();

        var result = definition.Capture(frame, in config, CaptureContext());

        Assert.Equal(ServiceCaptureDisposition.Unavailable, result.Disposition);
        Assert.Empty(publish.Published);
    }

    /// <summary>
    /// Collection is infrastructure: it has no switch of its own, because a consumer cannot tell an
    /// empty world from a collector that was turned off. The pace only spaces readings apart, so a
    /// lifecycle that has read nothing yet is ready even at the slowest one.
    /// </summary>
    [Fact]
    public void ALifecycleThatHasReadNothingIsReadyToStartAtAnyPace()
    {
        var definition = AutomataWorldCollectionService.Define(
            new FakeCapture((_, _) => { }), new FakePublish(), new AutomataWorldDemand());
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Pace = AutomationPace.CpuSaver },
        };

        Assert.True(definition.ShouldStart(in config, default).ShouldStart);
    }

    /// <summary>
    /// The worker hands the snapshot over as one action rather than publishing it, so the live
    /// generation only ever changes on the main thread. An action carrying no generation, or a worker
    /// that published behind the pump's back, would both put a moving world under a service that is
    /// mid-decision.
    /// </summary>
    [Fact]
    public void TheWorkerEmitsOnePublishActionCarryingTheCollectedGeneration()
    {
        var capture = new FakeCapture((frame, _) =>
        {
            var mana = WorldSamples.Resource(Mana, 1d);
            frame.Resources.Append(in mana);
        });
        var publish = new FakePublish();
        var definition = AutomataWorldCollectionService.Define(capture, publish, new AutomataWorldDemand());
        var worker = definition.CreateWorkerDefinition();
        var frame = new GameWorldCycleFrame();
        var state = worker.CreateState(new LifecycleGeneration(1));
        var config = new SuiteRuntimeConfiguration();
        var store = new ReusableActionStore<AutomataWorldCollectionAction>();
        store.BeginWrite();

        definition.Capture(frame, in config, CaptureContext());
        worker.Evaluate(
            frame, in config, default, ref state,
            new ServiceActionWriter<AutomataWorldCollectionAction>(store));

        Assert.Equal(1, store.Count);
        Assert.Equal(new WorldGeneration((ulong)frame.CollectedAtFrame), store.GetCurrent().Generation);

        // Nothing is live until the main thread dispatches it.
        Assert.Empty(publish.Published);
    }

    /// <summary>
    /// Executing the action is what makes a snapshot live, and it reports itself as a publication
    /// rather than as a native mutation it never performed.
    /// </summary>
    [Fact]
    public void DispatchingThatActionPublishesUnderTheCollectedGeneration()
    {
        var capture = new FakeCapture((frame, _) =>
        {
            var mana = WorldSamples.Resource(Mana, 1d);
            frame.Resources.Append(in mana);
        });
        var publish = new FakePublish();

        RunOneCycle(capture, publish);

        var generation = Assert.Single(publish.Generations);
        Assert.Equal(new WorldGeneration(1), generation);
        Assert.Single(publish.Published);
    }

    [Fact]
    public void TheProjectionReportsWhatTheLastPassManaged()
    {
        var capture = new FakeCapture((frame, _) =>
        {
            var mana = WorldSamples.Resource(Mana, 1d);
            frame.Resources.Append(in mana);
        })
        {
            Report = new WorldCollectionReport(
                new WorldCategoryReport("resources", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                WorldCategoryReport.Missing("rituals", "the RitualSO type was not found on this build")),
        };
        var publish = new FakePublish();

        var state = RunOneCycle(capture, publish);

        Assert.Equal(1, state.LastEntities);
        Assert.False(state.LastPassComplete);
        Assert.Equal(1, state.LastCategoriesUnavailable);
    }

    private static AutomataWorldCollectionState RunOneCycle(
        IAutomataWorldCapturePort capture,
        IServiceWorldPublicationSink<GameWorldState> publish)
    {
        var definition = AutomataWorldCollectionService.Define(capture, publish, new AutomataWorldDemand());
        var worker = definition.CreateWorkerDefinition();
        var frame = new GameWorldCycleFrame();
        var state = worker.CreateState(new LifecycleGeneration(1));
        Cycle(definition, worker, frame, ref state);
        return state;
    }

    private static void Cycle(
        IServiceCycleSourceDefinition<
            AutomataWorldCollectionState,
            AutomataWorldCollectionAction> definition,
        IServiceCycleSourceWorkerDefinition<
            AutomataWorldCollectionState,
            AutomataWorldCollectionAction> worker,
        GameWorldCycleFrame frame,
        ref AutomataWorldCollectionState state,
        MonotonicTimestamp capturedAt = default)
    {
        var config = new SuiteRuntimeConfiguration();
        var captured = definition.Capture(frame, in config, CaptureContext(capturedAt));
        Assert.Equal(ServiceCaptureDisposition.Captured, captured.Disposition);
        var store = new ReusableActionStore<AutomataWorldCollectionAction>();
        store.BeginWrite();
        worker.Evaluate(
            frame, in config, default, ref state,
            new ServiceActionWriter<AutomataWorldCollectionAction>(store));

        // Dispatch what the worker emitted. Publication is a main-thread action now, so a cycle that
        // stopped at Evaluate would publish nothing and prove nothing.
        while (!store.IsComplete)
        {
            ref readonly var action = ref store.GetCurrent();
            var result = definition.TryExecute(in action, in config, ActionContext());
            Assert.True(result.IsValid);
            Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
            Assert.Equal(ServiceActionEffect.Publication, result.Effect);
            store.AdvanceCurrentAndClear();
        }
    }

    private static ServiceActionContext ActionContext() =>
        new(
            new ServiceCycleIdentity(
                AutomataWorldCollectionPolicies.ServiceId,
                new LifecycleGeneration(1),
                new ConfigGeneration(1),
                new StrategyGeneration(1),
                new WorldGeneration(1),
                new CycleId(1)),
            new BatchId(1),
            new ActionId(1),
            0,
            default);

    private static ServiceCaptureContext CaptureContext(
        MonotonicTimestamp capturedAt = default) =>
        new(
            AutomataWorldCollectionPolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            StrategyGeneration.Initial,
            new CaptureSequence(1),
            new CycleId(1),
            GameWorldStateDefaults.Empty,
            capturedAt);

    private sealed class FakeCapture : IAutomataWorldCapturePort
    {
        private readonly Action<GameWorldCycleFrame, int> _fill;
        private int _passes;
        private long _collectedAtFrame;

        internal FakeCapture(Action<GameWorldCycleFrame, int> fill) => _fill = fill;

        public bool IsAvailable { get; init; } = true;

        internal WorldCollectionReport Report { get; init; } =
            new(new WorldCategoryReport("resources", WorldCategoryOutcome.Collected, 1, 0, string.Empty));

        public WorldCollectionReport Collect(GameWorldCycleFrame frame)
        {
            frame.Resources.Reset();
            frame.CollectedAtFrame = ++_collectedAtFrame;
            _fill(frame, _passes++);
            frame.Report = Report;
            return Report;
        }
    }

    private sealed class FakePublish : IServiceWorldPublicationSink<GameWorldState>
    {
        internal List<GameWorldState> Published { get; } = new();
        internal List<WorldGeneration> Generations { get; } = new();

        public WorldGeneration Publish(GameWorldState world, WorldGeneration generation)
        {
            Published.Add(world);
            Generations.Add(generation);
            return generation;
        }
    }
}
