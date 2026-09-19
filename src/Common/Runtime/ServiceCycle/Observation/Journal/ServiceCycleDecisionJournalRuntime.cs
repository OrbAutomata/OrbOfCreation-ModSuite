using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal sealed class ServiceCycleDecisionJournalRuntime : IDisposable
{
    private readonly SuiteFramePump _pump;
    private readonly BufferedDecisionJournalRecordSink _sink;
    private readonly ServiceCycleDecisionJournalObserver _observer;
    private readonly DecisionJournalServiceBaseline[] _baselines;
    private readonly object _ownership;
    private readonly int _ownerThreadId;
    private DecisionJournalRuntimeState _state = DecisionJournalRuntimeState.Initializing;
    private bool _attached;
    private bool _ownershipReleased;
    private bool _stopRequested;
    private bool _disposed;

    internal ServiceCycleDecisionJournalRuntime(
        SuiteFramePump pump,
        IRestartAwareTraceSegmentStorage storage,
        DecisionJournalRunId run,
        int maximumCommittedSegments,
        int blockCount,
        MonotonicDuration checkpointInterval)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        if (storage is null) throw new ArgumentNullException(nameof(storage));
        if (!run.IsValid) throw new ArgumentException("A valid journal run is required.", nameof(run));
        if (maximumCommittedSegments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCommittedSegments));
        if (blockCount < 3) throw new ArgumentOutOfRangeException(nameof(blockCount));
        if (checkpointInterval.Ticks <= 0)
            throw new ArgumentOutOfRangeException(nameof(checkpointInterval));
        var serviceCapacity = pump.ServiceCapacity;
        if (serviceCapacity <= 0)
            throw new InvalidOperationException("The decision journal requires at least one registered service.");
        var startedAt = pump.DiagnosticsNow;
        _baselines = new DecisionJournalServiceBaseline[serviceCapacity];
        _ownership = new object();
        _ownerThreadId = Environment.CurrentManagedThreadId;
        pump.ClaimDecisionJournalRuntime(_ownership);
        try
        {
            _sink = new BufferedDecisionJournalRecordSink(
                storage,
                run,
                maximumCommittedSegments,
                blockCount);
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            pump.ReleaseDecisionJournalRuntime(_ownership);
            throw;
        }
        var coalescer = new DecisionJournalCoalescer(
            serviceCapacity,
            _sink,
            checkpointInterval,
            startedAt);
        _observer = new ServiceCycleDecisionJournalObserver(coalescer, serviceCapacity);
    }

    internal DecisionJournalRuntimeSnapshot Snapshot
    {
        get
        {
            EnsureOwner();
            var transport = _sink.TransportMetrics;
            RefreshTerminalState(transport.Status);
            var consumer = _sink.ConsumerMetrics;
            return CreateSnapshot(in transport, in consumer);
        }
    }

    internal DecisionJournalRuntimeSnapshot Tick()
    {
        EnsureOwner();
        if (_disposed)
        {
            return Snapshot;
        }
        if (IsTerminal(_state) && !_attached) return Snapshot;
        var transport = _sink.TransportMetrics;
        var consumer = _sink.ConsumerMetrics;
        if (_attached && (_observer.IsFaulted || consumer.CannotContinue ||
                transport.Status != BufferedSegmentStatus.Running))
        {
            BeginFaultStop();
            return Snapshot;
        }
        RefreshTerminalState(transport.Status);
        if (IsTerminal(_state) || _stopRequested) return CreateSnapshot(in transport, in consumer);
        if (transport.Status == BufferedSegmentStatus.Initializing)
        {
            _state = DecisionJournalRuntimeState.Initializing;
            return CreateSnapshot(in transport, in consumer);
        }
        if (transport.Status != BufferedSegmentStatus.Running)
        {
            _state = DecisionJournalRuntimeState.Stopping;
            return CreateSnapshot(in transport, in consumer);
        }
        if (_attached)
        {
            _state = DecisionJournalRuntimeState.Recording;
            return CreateSnapshot(in transport, in consumer);
        }

        _state = DecisionJournalRuntimeState.Arming;
        try
        {
            _attached = _pump.TryAttachDecisionJournal(_observer, _baselines);
            if (_observer.IsFaulted)
            {
                BeginFaultStop();
                return Snapshot;
            }
            if (_attached) _state = DecisionJournalRuntimeState.Recording;
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            BeginFaultStop();
            return Snapshot;
        }
        return CreateSnapshot(in transport, in consumer);
    }

    internal void RequestStop()
    {
        EnsureOwner();
        if (_stopRequested || IsTerminal(_state) && !_attached) return;
        _stopRequested = true;
        StopProducer(producerFailed: false);
        RefreshTerminalState();
    }

    internal DecisionJournalRuntimeSnapshot Flush()
    {
        EnsureOwner();
        if (_disposed || IsTerminal(_state) || _stopRequested || !_attached) return Snapshot;

        _observer.Flush(_pump.DiagnosticsNow);
        if (_observer.IsFaulted)
        {
            BeginFaultStop();
            return Snapshot;
        }

        var transport = _sink.TransportMetrics;
        var consumer = _sink.ConsumerMetrics;
        RefreshTerminalState(transport.Status);
        return CreateSnapshot(in transport, in consumer);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        RequestStop();
        _disposed = true;
        DrainAtShutdown();
    }

    /// <summary>
    /// Ends the journal with the pump, and answers whether its writer finished what it was holding.
    /// </summary>
    /// <remarks>
    /// The drain is awaited after the pump goes rather than before it, so the writer spends the
    /// pump's own teardown finishing its segments instead of making the host wait twice.
    /// </remarks>
    internal bool DisposeWithPump()
    {
        EnsureOwner();
        RequestStop();
        _disposed = true;
        if (_ownershipReleased)
        {
            _pump.Dispose();
            return DrainAtShutdown();
        }

        _pump.DisposeOwnedByDecisionJournal(_ownership);
        _ownershipReleased = true;
        return DrainAtShutdown();
    }

    /// <summary>
    /// Waits, bounded, for the writer to make the records it already accepted durable.
    /// </summary>
    /// <remarks>
    /// The journal seals its open decisions into the transport as it stops, and those last segments
    /// used to be left to a background thread the process could end at any point. A sink still
    /// admitting records reports a drain that did not finish rather than waiting for a stop that a
    /// contained observer failure swallowed — silence there would read exactly like a clean end.
    /// </remarks>
    private bool DrainAtShutdown() =>
        _sink.WaitForDrain(BufferedSegmentShutdown.DrainBound);

    private void BeginFaultStop()
    {
        if (_stopRequested) return;
        _stopRequested = true;
        StopProducer(producerFailed: true);
        RefreshTerminalState();
    }

    private void StopProducer(bool producerFailed)
    {
        if (producerFailed && _sink.TransportMetrics.Status == BufferedSegmentStatus.Running)
            _sink.FailProducer();
        if (_attached)
        {
            _pump.DetachDecisionJournal(_observer);
            _attached = false;
            _observer.Stop(_pump.DiagnosticsNow);
        }
        else
        {
            _sink.Stop();
        }
        _state = DecisionJournalRuntimeState.Stopping;
    }

    private void RefreshTerminalState()
    {
        RefreshTerminalState(_sink.TransportMetrics.Status);
    }

    private void RefreshTerminalState(BufferedSegmentStatus status)
    {
        _state = status switch
        {
            BufferedSegmentStatus.Stopping or BufferedSegmentStatus.Faulting =>
                DecisionJournalRuntimeState.Stopping,
            BufferedSegmentStatus.Stopped => DecisionJournalRuntimeState.Stopped,
            BufferedSegmentStatus.Faulted => DecisionJournalRuntimeState.Faulted,
            _ => _state,
        };
        if (IsTerminal(_state) && !_attached && !_ownershipReleased)
        {
            _pump.ReleaseDecisionJournalRuntime(_ownership);
            _ownershipReleased = true;
        }
    }

    /// <summary>
    /// Every snapshot carries whatever named the producer's failure.
    /// </summary>
    /// <remarks>
    /// The observer keeps the first contained exception, so reading it here rather than latching it
    /// at <see cref="BeginFaultStop"/> means a fault raised while stopping is reported too, and no
    /// ordering between the fault and the snapshot can lose it.
    /// </remarks>
    private DecisionJournalRuntimeSnapshot CreateSnapshot(
        in BufferedSegmentMetrics transport,
        in DecisionJournalConsumerMetrics consumer) => new(
        _state,
        _attached,
        in transport,
        in consumer,
        _observer.FaultException,
        _observer.FaultSite);

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Decision-journal control must remain on its owning main thread.");
    }

    private static bool IsTerminal(DecisionJournalRuntimeState state) =>
        state is DecisionJournalRuntimeState.Stopped or DecisionJournalRuntimeState.Faulted;
}
