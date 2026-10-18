using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;

/// <summary>
/// Owner-thread, allocation-free action timeline over the same assembled observations consumed
/// by the decision journal. Every registered service remains present even before it acts.
/// </summary>
internal sealed class ServiceActionTimelineProjection :
    IDecisionJournalObservationSink,
    IServiceActionTimelineSource
{
    internal const int TimelineBucketCount = 30;
    private const long TicksPerMinute = TimeSpan.TicksPerMinute;
    private readonly ServiceActionTimelineService[] _services;
    private readonly long[] _timelineMinuteKeys;
    private readonly long[] _timelineCommitted;
    private readonly long[] _timelineSkipped;
    private readonly long[] _timelineRejected;
    private readonly long[] _timelineFaulted;
    private ulong _timelineLifecycle;
    private long _timelineCurrentMinute = -1;
    private long _timelineRevision;

    private ServiceActionTimelineProjection(ServiceActionTimelineService[] services)
    {
        if (services is null || services.Length == 0)
            throw new ArgumentException("At least one registered service is required.", nameof(services));
        _services = services;
        _timelineMinuteKeys = new long[TimelineBucketCount];
        Array.Fill(_timelineMinuteKeys, long.MinValue);
        _timelineCommitted = new long[checked(TimelineBucketCount * services.Length)];
        _timelineSkipped = new long[_timelineCommitted.Length];
        _timelineRejected = new long[_timelineCommitted.Length];
        _timelineFaulted = new long[_timelineCommitted.Length];
    }

    internal static ServiceActionTimelineProjection Create(ServiceCycleRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        var count = registry.OrdinalCount;
        if (count <= 0)
            throw new InvalidOperationException("The action timeline requires registered services.");
        var services = new ServiceActionTimelineService[count];
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var slot = registry.GetSlot(ordinal);
            services[ordinal] = new ServiceActionTimelineService(
                slot.ServiceId,
                slot.ActionDispatchPolicy.Shape);
        }
        var projection = new ServiceActionTimelineProjection(services);
        projection._timelineLifecycle = registry.CurrentLifecycle.Value;
        return projection;
    }

    public int TimelineServiceCount => _services.Length;
    public int TimelineBucketCapacity => TimelineBucketCount;
    public int TimelineCellCapacity => checked(_services.Length * TimelineBucketCount);
    public long TimelineRevision => _timelineRevision;
    public bool IsFaulted => false;

    public void ObserveAction(in DecisionJournalActionObservation observation) { }

    public ServiceActionTimelineCopyResult CopyTimelineTo(
        Span<ServiceActionTimelineCellSnapshot> destination)
    {
        var available = TimelineCellCapacity;
        var written = Math.Min(available, destination.Length);
        if (_timelineCurrentMinute < 0)
        {
            return new ServiceActionTimelineCopyResult(
                0,
                0,
                0,
                0,
                _timelineRevision);
        }

        var firstMinute = _timelineCurrentMinute - TimelineBucketCount + 1;
        for (var cell = 0; cell < written; cell++)
        {
            var bucket = cell / _services.Length;
            var serviceIndex = cell % _services.Length;
            var minute = firstMinute + bucket;
            var slot = Slot(minute);
            var retained = _timelineMinuteKeys[slot] == minute;
            var service = _services[serviceIndex];
            destination[cell] = new ServiceActionTimelineCellSnapshot(
                minute,
                service.Service,
                service.Shape,
                retained ? _timelineCommitted[TimelineIndex(slot, serviceIndex)] : 0,
                retained ? _timelineSkipped[TimelineIndex(slot, serviceIndex)] : 0,
                retained ? _timelineRejected[TimelineIndex(slot, serviceIndex)] : 0,
                retained ? _timelineFaulted[TimelineIndex(slot, serviceIndex)] : 0);
        }
        return new ServiceActionTimelineCopyResult(
            _services.Length,
            TimelineBucketCount,
            available,
            written,
            _timelineRevision);
    }

    public void Observe(in DecisionJournalObservation observation) =>
        ObserveTimeline(ServiceIndex(observation.Service), in observation);

    public void ObserveTransition(in DecisionJournalRecord transition)
    {
        DecisionJournalRecordValidation.Validate(in transition);
        if (transition.Kind == DecisionJournalRecordKind.LifecycleChanged)
            ResetTimeline(transition.Lifecycle);
    }

    public void BreakServiceSpan(
        ServiceCycleTraceServiceId service,
        MonotonicTimestamp observedAt)
    {
        _ = ServiceIndex(service);
        _ = observedAt;
    }

    public void Advance(MonotonicTimestamp now) => AdvanceTimeline(now);
    public void Flush(MonotonicTimestamp now) => AdvanceTimeline(now);
    public void Stop(MonotonicTimestamp now) => AdvanceTimeline(now);

    private void ObserveTimeline(
        int serviceIndex,
        in DecisionJournalObservation observation)
    {
        if (observation.Lifecycle != _timelineLifecycle)
            ResetTimeline(observation.Lifecycle);
        var committed = observation.Terminal.IsPresent
            ? observation.Terminal.CommittedCount
            : 0;
        var skipped = observation.Terminal.IsPresent
            ? observation.Terminal.SkippedCount
            : 0;
        var rejected = observation.Terminal.IsPresent &&
            observation.Terminal.Disposition == BatchTerminalDisposition.Rejected ? 1 : 0;
        var faulted = (observation.Terminal.IsPresent &&
                observation.Terminal.Disposition == BatchTerminalDisposition.Faulted) ||
            observation.Fault.IsValid ? 1 : 0;
        if (committed <= 0 && skipped <= 0 && rejected <= 0 && faulted <= 0) return;

        var minute = Minute(observation.LastObservedAt);
        if (minute < _timelineCurrentMinute) return;
        AdvanceTimeline(observation.LastObservedAt);
        var slot = Slot(minute);
        EnsureTimelineSlot(slot, minute);
        var index = TimelineIndex(slot, serviceIndex);
        var visibleChange = false;
        if (committed > 0)
        {
            _timelineCommitted[index] = SaturatingAdd(_timelineCommitted[index], committed);
            visibleChange = true;
        }
        if (skipped > 0)
            _timelineSkipped[index] = SaturatingAdd(_timelineSkipped[index], skipped);
        if (rejected > 0)
            _timelineRejected[index] = SaturatingAdd(_timelineRejected[index], rejected);
        if (faulted > 0)
        {
            if (_timelineFaulted[index] == 0) visibleChange = true;
            _timelineFaulted[index] = SaturatingAdd(_timelineFaulted[index], faulted);
        }
        if (visibleChange) _timelineRevision = checked(_timelineRevision + 1);
    }

    private void AdvanceTimeline(MonotonicTimestamp observedAt)
    {
        var minute = Minute(observedAt);
        if (minute <= _timelineCurrentMinute) return;
        var hadCurrentMinute = _timelineCurrentMinute >= 0;
        _timelineCurrentMinute = minute;
        if (hadCurrentMinute) _timelineRevision = checked(_timelineRevision + 1);
    }

    private void ResetTimeline(ulong lifecycle)
    {
        if (lifecycle == 0 || lifecycle == _timelineLifecycle) return;
        _timelineLifecycle = lifecycle;
        Array.Fill(_timelineMinuteKeys, long.MinValue);
        Array.Clear(_timelineCommitted, 0, _timelineCommitted.Length);
        Array.Clear(_timelineSkipped, 0, _timelineSkipped.Length);
        Array.Clear(_timelineRejected, 0, _timelineRejected.Length);
        Array.Clear(_timelineFaulted, 0, _timelineFaulted.Length);
        _timelineCurrentMinute = -1;
        _timelineRevision = checked(_timelineRevision + 1);
    }

    private void EnsureTimelineSlot(int slot, long minute)
    {
        if (_timelineMinuteKeys[slot] == minute) return;
        _timelineMinuteKeys[slot] = minute;
        var start = checked(slot * _services.Length);
        Array.Clear(_timelineCommitted, start, _services.Length);
        Array.Clear(_timelineSkipped, start, _services.Length);
        Array.Clear(_timelineRejected, start, _services.Length);
        Array.Clear(_timelineFaulted, start, _services.Length);
    }

    private int TimelineIndex(int slot, int serviceIndex) =>
        checked(slot * _services.Length + serviceIndex);

    private static long Minute(MonotonicTimestamp timestamp) => timestamp.Ticks / TicksPerMinute;

    private static int Slot(long minute)
    {
        var slot = minute % TimelineBucketCount;
        return checked((int)(slot < 0 ? slot + TimelineBucketCount : slot));
    }

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;

    private int ServiceIndex(ServiceCycleTraceServiceId service)
    {
        if (!service.IsValid || service.Value > (ulong)_services.Length)
            throw new ArgumentOutOfRangeException(nameof(service));
        return checked((int)service.Value - 1);
    }

    private readonly struct ServiceActionTimelineService
    {
        internal ServiceActionTimelineService(ServiceId service, ServiceShape shape)
        {
            if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
            if (shape is not (ServiceShape.Source or ServiceShape.Ordinary))
                throw new ArgumentOutOfRangeException(nameof(shape));
            Service = service;
            Shape = shape;
        }

        internal ServiceId Service { get; }
        internal ServiceShape Shape { get; }
    }
}
