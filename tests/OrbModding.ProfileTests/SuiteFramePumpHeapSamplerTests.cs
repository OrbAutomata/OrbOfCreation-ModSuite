using System.Linq;
using System.Threading;
using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class SuiteFramePumpHeapSamplerTests
{
    private const long Megabyte = 1 << 20;

    /// <summary>
    /// A heap reading that does not follow what is allocated would turn every frame into a clean zero,
    /// so the capture holds no samples and the log names the reason instead.
    /// </summary>
    [Fact]
    public void CountersWhoseHeapDoesNotMoveWithAnAllocationAreRefused()
    {
        var sampler = new SuiteFramePumpHeapSampler();

        Assert.False(sampler.TryAttach(new FakeHeap(), out var unavailable));
        Assert.Equal("the used-heap reading did not grow with a 1 MiB allocation", unavailable);
        Assert.True(sampler.TryAttach(new FakeHeap { GrowthPerRead = Megabyte }, out unavailable));
        Assert.Empty(unavailable);
    }

    [Fact]
    public void AWitnessThatACollectionInterruptsTwiceIsNoAnswer()
    {
        var sampler = new SuiteFramePumpHeapSampler();

        Assert.False(sampler.TryAttach(new FakeHeap { GrowthPerRead = Megabyte, CollectionsPerRead = 1 }, out var unavailable));
        Assert.Equal("a collection interrupted both witness allocations", unavailable);
    }

    /// <summary>
    /// A collection frees the game's garbage and the suite's alike, so a capture it ran inside is
    /// unknown, never zero, and so is one whose heap fell under an incremental sweep.
    /// </summary>
    [Fact]
    public void ACaptureACollectionRanInsideIsCountedAsUnknown()
    {
        var heap = new FakeHeap { GrowthPerRead = Megabyte };
        var sampler = new SuiteFramePumpHeapSampler();
        Assert.True(sampler.TryAttach(heap, out _));
        heap.GrowthPerRead = 0;

        sampler.BeginPump(tracing: true);
        var measured = sampler.BeginCapture();
        heap.Heap += 4_096;
        sampler.EndCapture(in measured);
        var collected = sampler.BeginCapture();
        heap.Collections++;
        sampler.EndCapture(in collected);
        var swept = sampler.BeginCapture();
        heap.Heap -= Megabyte;
        sampler.EndCapture(in swept);
        Assert.True(sampler.TryEndPump(7, out var sample));

        Assert.Equal(7, sample.FrameIdentity);
        Assert.Equal(1, sample.CollectionsAtEnd - sample.CollectionsAtStart);
        Assert.Equal(4_096, sample.CaptureGrowth);
        Assert.Equal(1, sample.CapturesMeasured);
        Assert.Equal(2, sample.CapturesAcrossCollection);
        Assert.Null(sample.PreviousFrameAllocated);
    }

    [Fact]
    public void NothingIsReadWhileNoTraceIsOpen()
    {
        var heap = new FakeHeap { GrowthPerRead = Megabyte };
        var sampler = new SuiteFramePumpHeapSampler();
        Assert.True(sampler.TryAttach(heap, out _));
        var reads = heap.Reads;

        sampler.BeginPump(tracing: false);
        var capture = sampler.BeginCapture();
        sampler.EndCapture(in capture);

        Assert.False(sampler.TryEndPump(1, out _));
        Assert.Equal(reads, heap.Reads);
    }

    [Fact]
    public void AnAcceptedPumpWritesItsHeapSampleBehindItsCompletion()
    {
        using var registry = new ServiceCycleRegistry(1, new TickingClock());
        using var registration = registry.Register(new IdleService(), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, new ServiceCycleSemanticRecorder(new ServiceCycleTraceSessionId(700), 64, 1));
        var heap = new FakeHeap { GrowthPerRead = Megabyte, Allocated = 2_048 };
        Assert.True(pump.TryAttachHeapCounters(heap, out _));

        pump.PumpFrame(3);

        var events = Drain(pump);
        var completed = events.Single(item => item.Kind == ServiceCycleSemanticEventKind.PumpCompleted);
        var sampled = events.Single(item => item.Kind == ServiceCycleSemanticEventKind.PumpHeapSampled);
        Assert.True(completed.Id.Sequence < sampled.Id.Sequence);
        Assert.Equal(3, sampled.Payload.FrameIdentity);
        Assert.True(sampled.Payload.HeapAtPumpEnd > sampled.Payload.HeapAtPumpStart);
        Assert.Equal(2_048, sampled.Payload.PreviousFrameAllocated);
    }

    [Fact]
    public void APumpWithoutCountersWritesNoHeapSample()
    {
        using var registry = new ServiceCycleRegistry(1, new TickingClock());
        using var registration = registry.Register(new IdleService(), new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry, new ServiceCycleSemanticRecorder(new ServiceCycleTraceSessionId(700), 64, 1));

        pump.PumpFrame(3);

        var events = Drain(pump);
        Assert.Contains(events, item => item.Kind == ServiceCycleSemanticEventKind.PumpCompleted);
        Assert.DoesNotContain(events, item => item.Kind == ServiceCycleSemanticEventKind.PumpHeapSampled);
    }

    private static ServiceCycleSemanticEvent[] Drain(SuiteFramePump pump)
    {
        var source = Assert.IsType<ServiceCycleSemanticTraceSource>(pump.SemanticTrace);
        var events = new ServiceCycleSemanticEvent[source.Capacity];
        var drain = source.DrainSince(default, events);
        return events[..drain.Copied];
    }

    private sealed class FakeHeap : ISuiteFramePumpHeapCounters
    {
        internal long Heap = 100 * Megabyte;
        internal long GrowthPerRead;
        internal int Collections = 10;
        internal int CollectionsPerRead;
        internal long? Allocated;
        internal int Reads;

        public int ReadCollectionCount()
        {
            Reads++;
            return Collections += CollectionsPerRead;
        }

        public long ReadUsedHeapBytes()
        {
            Reads++;
            return Heap += GrowthPerRead;
        }

        public bool TryReadPreviousFrameAllocated(out long bytes)
        {
            bytes = Allocated ?? 0;
            return Allocated is not null;
        }
    }

    private sealed class IdleService : IServiceCycleDefinition<int, int>
    {
        public ServiceId ServiceId { get; } = new("profile.heap-sampler.idle");
        public WakePolicy DefaultWakePolicy => WakePolicy.Immediate;
        public ServiceFaultRecoveryPolicy FaultRecoveryPolicy { get; } =
            new(new MonotonicDuration(10), new MonotonicDuration(80));

        public ServiceStartDecision ShouldStart(
            in SuiteRuntimeConfiguration config,
            in ServiceCycleStartContext context) =>
            ServiceStartDecision.Wait(CommonServiceDecisionCodes.NotReady, WakePolicy.AfterDecision(new MonotonicDuration(1_000)));

        public ServiceActionJournalAttribution DescribeAction(in int action) => throw new NotSupportedException();

        public ServiceActionResult TryExecute(
            in int action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context) =>
            throw new NotSupportedException();

        public IServiceCycleWorkerDefinition<int, int> CreateWorkerDefinition() => new IdleWorker();
    }

    private sealed class IdleWorker : IServiceCycleWorkerDefinition<int, int>
    {
        public int CreateState(LifecycleGeneration lifecycle) => 0;

        public void ReleaseState(ref int state) { }

        public void ProjectState(in int state, in ServiceProjectionContext context, ServiceStateProjectionBuilder output) { }

        public WakePolicy Evaluate(
            in SuiteRuntimeConfiguration config,
            GameWorldState world,
            SuiteStrategy strategy,
            in ServiceCycleContext context,
            ref int state,
            ServiceActionWriter<int> actions) =>
            throw new NotSupportedException();
    }

    private sealed class TickingClock : IMonotonicClock
    {
        private long _ticks = 100;

        public MonotonicTimestamp Now => new(Interlocked.Increment(ref _ticks));
    }
}
