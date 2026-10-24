using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

/// <summary>Bounded, single-owner export scope that preserves loss evidence across partial drains.</summary>
public sealed class ServiceCycleTraceCapture
{
    private readonly ServiceCycleSemanticEvent[] _events;
    private readonly int _ownerThreadId;
    private readonly ServiceCycleTraceSessionId _session;
    private readonly int _serviceCapacity;
    private ServiceCycleTraceCursor _cursor;
    private ServiceCycleTraceDropRange _dropped;
    private int _count;

    public ServiceCycleTraceCapture(ServiceCycleTraceSessionId session, int capacity)
        : this(session, capacity, 1) { }

    public ServiceCycleTraceCapture(
        ServiceCycleTraceSessionId session,
        int capacity,
        int serviceCapacity)
    {
        if (!session.IsValid) throw new ArgumentException("A valid trace session is required.", nameof(session));
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        _session = session;
        _serviceCapacity = serviceCapacity;
        _events = new ServiceCycleSemanticEvent[capacity];
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public int Count { get { EnsureOwner(); return _count; } }
    public bool IsComplete { get { EnsureOwner(); return !_dropped.IsPresent; } }
    public ServiceCycleTraceDropRange Dropped { get { EnsureOwner(); return _dropped; } }

    public ServiceCycleEventDrain Pull(ServiceCycleEventRing ring, int maximumEvents)
    {
        EnsureOwner();
        if (ring is null) throw new ArgumentNullException(nameof(ring));
        if (maximumEvents <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEvents));
        if (ring.Session != _session) throw new ArgumentException("The ring belongs to another trace session.", nameof(ring));
        var available = Math.Min(maximumEvents, _events.Length - _count);
        if (available == 0)
        {
            var probe = ring.DrainSince(_cursor, Span<ServiceCycleSemanticEvent>.Empty);
            if (!probe.Dropped.IsPresent)
                throw new InvalidOperationException("The bounded trace capture is full.");
            ResetForLoss(probe.Dropped, 0);
            available = Math.Min(maximumEvents, _events.Length);
        }
        var drain = ring.DrainSince(_cursor, _events.AsSpan(_count, available));
        if (drain.Dropped.IsPresent)
            ResetForLoss(drain.Dropped, drain.Copied);
        _count += drain.Copied;
        _cursor = drain.Cursor;
        return drain;
    }

    private void ResetForLoss(ServiceCycleTraceDropRange loss, int newlyCopied)
    {
        // Any observed loss follows the current cursor. Root the cumulative loss at one so the
        // remaining suffix is always a canonical, portable incomplete document.
        if (newlyCopied != 0)
            _events.AsSpan(_count, newlyCopied).CopyTo(_events);
        _count = 0;
        _dropped = new ServiceCycleTraceDropRange(_session, 1, loss.LastSequence);
    }

    public int GetEncodedLength()
    {
        EnsureOwner();
        return ServiceCycleTraceCodec.GetEncodedLength(_count);
    }

    public int Encode(Span<byte> destination)
    {
        EnsureOwner();
        return ServiceCycleTraceCodec.Encode(
            _session, _dropped, _serviceCapacity, _events.AsSpan(0, _count), destination);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The trace capture is single-owner.");
    }
}
