using System;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed class DecisionJournalCoalescer : IDecisionJournalObservationSink
{
    private readonly IDecisionJournalRecordSink _sink;
    private readonly DecisionJournalRecord[] _open;
    private readonly bool[] _hasOpen;
    private readonly ulong[] _actionCycles;
    private readonly MonotonicDuration _checkpointInterval;
    private MonotonicTimestamp _nextCheckpoint;
    private MonotonicTimestamp _lastObservedAt;
    private bool _stopped;

    internal DecisionJournalCoalescer(
        int serviceCapacity,
        IDecisionJournalRecordSink sink,
        MonotonicDuration checkpointInterval,
        MonotonicTimestamp startedAt)
    {
        if (serviceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        if (checkpointInterval.Ticks <= 0) throw new ArgumentOutOfRangeException(nameof(checkpointInterval));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _open = new DecisionJournalRecord[serviceCapacity];
        _hasOpen = new bool[serviceCapacity];
        _actionCycles = new ulong[serviceCapacity];
        _checkpointInterval = checkpointInterval;
        _nextCheckpoint = startedAt.AddSaturated(checkpointInterval);
        _lastObservedAt = startedAt;
    }

    public bool IsFaulted { get; private set; }
    internal bool IsStopped => _stopped;

    public void ObserveAction(in DecisionJournalActionObservation observation)
    {
        if (IsFaulted) return;
        EnsureRunning(observation.Fact.CompletedAt);
        var index = ServiceIndex(observation.Service);
        if (!AppendOpen(index)) return;
        var record = DecisionJournalRecord.Action(in observation);
        if (!_sink.TryAppend(in record))
        {
            IsFaulted = true;
            return;
        }
        _actionCycles[index] = observation.Fact.Context.Cycle.Cycle.Value;
    }

    public void Observe(in DecisionJournalObservation observation)
    {
        if (IsFaulted) return;
        EnsureRunning(observation.LastObservedAt);
        var index = ServiceIndex(observation.Service);
        if (observation.Terminal.IsPresent && _actionCycles[index] == observation.Cycle)
        {
            _actionCycles[index] = 0;
            // The action record already carries the ordinary terminal outcome. A fault is not
            // ordinary terminal accounting: it remains independently visible in the decision span.
            if (!observation.Fault.IsValid) return;
        }
        var next = DecisionJournalRecord.Decision(in observation);
        if (!_hasOpen[index])
        {
            _open[index] = next;
            _hasOpen[index] = true;
            return;
        }
        if (_open[index].CanCoalesceWith(in next))
        {
            _open[index] = _open[index].Coalesce(in next);
            return;
        }
        if (!AppendOpen(index)) return;
        _open[index] = next;
        _hasOpen[index] = true;
    }

    public void ObserveTransition(in DecisionJournalRecord transition)
    {
        if (IsFaulted) return;
        DecisionJournalRecordValidation.Validate(in transition);
        if (transition.Kind == DecisionJournalRecordKind.DecisionSpan)
            throw new ArgumentException("A transition record is required.", nameof(transition));
        EnsureRunning(new MonotonicTimestamp(transition.LastTimestampTicks));
        if (transition.Service.IsValid)
        {
            var index = ServiceIndex(transition.Service);
            if (!AppendOpen(index)) return;
            _actionCycles[index] = 0;
        }
        else if (!AppendAllOpen())
        {
            return;
        }
        else
        {
            Array.Clear(_actionCycles, 0, _actionCycles.Length);
        }
        if (!_sink.TryAppend(in transition)) IsFaulted = true;
    }

    public void BreakServiceSpan(
        ServiceCycleTraceServiceId service,
        MonotonicTimestamp observedAt)
    {
        if (IsFaulted) return;
        EnsureRunning(observedAt);
        var index = ServiceIndex(service);
        if (AppendOpen(index)) _actionCycles[index] = 0;
    }

    public void Advance(MonotonicTimestamp now)
    {
        if (IsFaulted) return;
        EnsureRunning(now);
        if (now < _nextCheckpoint) return;
        if (AppendAllOpen() && !_sink.TryFlush()) IsFaulted = true;
        _nextCheckpoint = now.AddSaturated(_checkpointInterval);
    }

    public void Flush(MonotonicTimestamp now)
    {
        if (IsFaulted) return;
        EnsureRunning(now);
        if (AppendAllOpen() && !_sink.TryFlush()) IsFaulted = true;
        _nextCheckpoint = now.AddSaturated(_checkpointInterval);
    }

    public void Stop(MonotonicTimestamp now)
    {
        if (_stopped) return;
        if (now < _lastObservedAt) throw new ArgumentOutOfRangeException(nameof(now));
        if (!IsFaulted && AppendAllOpen() && !_sink.TryFlush()) IsFaulted = true;
        _sink.Stop();
        _stopped = true;
        _lastObservedAt = now;
    }

    private bool AppendAllOpen()
    {
        for (var index = 0; index < _open.Length; index++)
            if (!AppendOpen(index)) return false;
        return true;
    }

    private bool AppendOpen(int index)
    {
        if (!_hasOpen[index]) return true;
        if (!_sink.TryAppend(in _open[index]))
        {
            IsFaulted = true;
            return false;
        }
        _open[index] = default;
        _hasOpen[index] = false;
        return true;
    }

    private int ServiceIndex(ServiceCycleTraceServiceId service)
    {
        if (!service.IsValid || service.Value > (ulong)_open.Length)
            throw new ArgumentOutOfRangeException(nameof(service));
        return checked((int)service.Value - 1);
    }

    private void EnsureRunning(MonotonicTimestamp observedAt)
    {
        if (_stopped) throw new InvalidOperationException("The decision journal is stopped.");
        if (observedAt < _lastObservedAt)
            throw new ArgumentOutOfRangeException(nameof(observedAt), "Journal observations must be monotonic.");
        _lastObservedAt = observedAt;
    }
}
