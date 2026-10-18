using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

public readonly struct ServiceCycleTraceDropRange : IEquatable<ServiceCycleTraceDropRange>
{
    internal ServiceCycleTraceDropRange(
        ServiceCycleTraceSessionId session,
        ulong firstSequence,
        ulong lastSequence)
    {
        if (!session.IsValid) throw new ArgumentException("A valid trace session is required.", nameof(session));
        if (firstSequence == 0 || lastSequence < firstSequence ||
            lastSequence > ServiceCycleTraceEventId.MaximumSequence)
            throw new ArgumentOutOfRangeException(nameof(firstSequence));
        Session = session;
        FirstSequence = firstSequence;
        LastSequence = lastSequence;
    }

    private ServiceCycleTraceDropRange(
        ServiceCycleTraceSessionId session,
        ulong firstSequence,
        ulong lastSequence,
        bool uncheckedBoundary)
    {
        Session = session;
        FirstSequence = firstSequence;
        LastSequence = lastSequence;
    }

    internal static ServiceCycleTraceDropRange UncheckedForValidationTests(
        ServiceCycleTraceSessionId session,
        ulong firstSequence,
        ulong lastSequence) => new(session, firstSequence, lastSequence, true);

    public ServiceCycleTraceSessionId Session { get; }
    public ulong FirstSequence { get; }
    public ulong LastSequence { get; }
    public bool IsPresent => Session.IsValid;
    public ulong Count => IsPresent ? checked(LastSequence - FirstSequence + 1) : 0;
    public bool Equals(ServiceCycleTraceDropRange other) =>
        Session == other.Session && FirstSequence == other.FirstSequence && LastSequence == other.LastSequence;
    public override bool Equals(object? obj) => obj is ServiceCycleTraceDropRange other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Session, FirstSequence, LastSequence);
}

public readonly struct ServiceCycleEventDrain
{
    internal ServiceCycleEventDrain(
        ServiceCycleTraceSessionId session,
        int copied,
        ServiceCycleTraceCursor cursor,
        ServiceCycleTraceDropRange dropped,
        ulong overwrittenTotal,
        bool hasMore)
    {
        Session = session;
        Copied = copied;
        Cursor = cursor;
        Dropped = dropped;
        OverwrittenTotal = overwrittenTotal;
        HasMore = hasMore;
    }

    public ServiceCycleTraceSessionId Session { get; }
    public int Copied { get; }
    public ServiceCycleTraceCursor Cursor { get; }
    public ServiceCycleTraceDropRange Dropped { get; }
    public ulong OverwrittenTotal { get; }
    public bool HasMore { get; }
    public bool IsComplete => !Dropped.IsPresent;
}

/// <summary>Single-owner, fixed-capacity ring for semantic events.</summary>
public sealed class ServiceCycleEventRing
{
    private readonly ServiceCycleSemanticEvent[] _events;
    private readonly int _ownerThreadId;
    private readonly ServiceCycleTraceSessionId _session;
    private readonly IServiceCycleSemanticEventObserver? _observer;
    private ulong _nextSequence = 1;
    private int _nextIndex;
    private int _count;

    public ServiceCycleEventRing(ServiceCycleTraceSessionId session, int capacity)
        : this(session, capacity, null) { }

    internal ServiceCycleEventRing(
        ServiceCycleTraceSessionId session,
        int capacity,
        IServiceCycleSemanticEventObserver? observer)
    {
        if (!session.IsValid) throw new ArgumentException("A valid trace session is required.", nameof(session));
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _session = session;
        _observer = observer;
        _events = new ServiceCycleSemanticEvent[capacity];
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public ServiceCycleTraceSessionId Session { get { EnsureOwner(); return _session; } }
    public int Capacity { get { EnsureOwner(); return _events.Length; } }
    public int Count { get { EnsureOwner(); return _count; } }
    public ulong OverwrittenTotal { get { EnsureOwner(); return OverwrittenTotalUnchecked; } }
    public ServiceCycleTraceDropRange OverwrittenRange { get { EnsureOwner(); return OverwrittenTotalUnchecked == 0
        ? default
        : new ServiceCycleTraceDropRange(_session, 1, OverwrittenTotalUnchecked); } }
    public ServiceCycleTraceCursor Cursor { get { EnsureOwner(); return new(_session, _nextSequence - 1); } }

    internal ServiceCycleTraceEventId Append(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload,
        ServiceCycleTraceEventId parent = default)
    {
        EnsureOwner();
        if (parent.IsValid && (parent.Session != _session || parent.Sequence >= _nextSequence))
            throw new ArgumentException("A causal parent must be an earlier event from this trace session.", nameof(parent));

        var id = new ServiceCycleTraceEventId(_session, _nextSequence);
        var semanticEvent = new ServiceCycleSemanticEvent(id, parent, kind, in payload);
        _events[_nextIndex] = semanticEvent;
        _nextIndex++;
        if (_nextIndex == _events.Length) _nextIndex = 0;
        if (_count != _events.Length)
        {
            _count++;
        }

        _nextSequence++;
        _observer?.Observe(in semanticEvent);
        return id;
    }

    public ServiceCycleEventDrain DrainSince(
        ServiceCycleTraceCursor after,
        Span<ServiceCycleSemanticEvent> destination)
    {
        EnsureOwner();
        if (after.IsValid && after.Session != _session)
            throw new ArgumentException("The cursor belongs to another trace session.", nameof(after));

        var latest = _nextSequence - 1;
        if (after.IsValid && after.Sequence > latest)
            throw new ArgumentOutOfRangeException(nameof(after), "The cursor is ahead of this trace session.");
        var requested = after.IsValid ? after.Sequence + 1 : 1UL;
        var oldest = _count == 0 ? _nextSequence : _nextSequence - (ulong)_count;
        var dropped = default(ServiceCycleTraceDropRange);
        if (requested < oldest)
        {
            dropped = new ServiceCycleTraceDropRange(_session, requested, oldest - 1);
            requested = oldest;
        }

        var available = latest >= requested ? latest - requested + 1 : 0;
        var copied = (int)Math.Min((ulong)destination.Length, available);
        if (copied != 0)
        {
            var oldestIndex = _count == _events.Length ? _nextIndex : 0;
            var offset = (int)(requested - oldest);
            var index = oldestIndex + offset;
            if (index >= _events.Length) index %= _events.Length;
            for (var i = 0; i < copied; i++)
            {
                destination[i] = _events[index];
                index++;
                if (index == _events.Length) index = 0;
            }
        }

        var cursorSequence = copied == 0 ? requested - 1 : requested + (ulong)copied - 1;
        return new ServiceCycleEventDrain(
            _session,
            copied,
            new ServiceCycleTraceCursor(_session, cursorSequence),
            dropped,
            OverwrittenTotalUnchecked,
            available > (ulong)copied);
    }

    private ulong OverwrittenTotalUnchecked => _count == _events.Length
        ? _nextSequence - 1 - (ulong)_events.Length
        : 0;

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The semantic event ring is single-owner.");
    }
}
