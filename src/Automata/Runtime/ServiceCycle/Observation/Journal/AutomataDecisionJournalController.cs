using System;
using System.Globalization;
using BepInEx.Logging;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;

namespace OrbAutomata;

internal sealed class AutomataDecisionJournalController
{
    private readonly ServiceCycleDecisionJournalRuntime? _runtime;
    private readonly DecisionJournalStatusRegistration _status;
    private readonly ManualLogSource _log;
    private readonly string _artifactName;
    private DecisionJournalStatus _lastPublished = DecisionJournalStatus.Unavailable;
    private bool _disposed;

    private AutomataDecisionJournalController(
        ServiceCycleDecisionJournalRuntime? runtime,
        DecisionJournalStatusRegistration status,
        ManualLogSource log,
        string artifactName)
    {
        _runtime = runtime;
        _status = status;
        _log = log;
        _artifactName = artifactName;
    }

    internal static AutomataDecisionJournalController? TryCreate(
        SuiteFramePump pump,
        in AutomataDecisionJournalOptions options,
        ManualLogSource log)
    {
        if (pump is null) throw new ArgumentNullException(nameof(pump));
        if (!options.Enabled || options.Status is null || options.Source is null ||
            string.IsNullOrEmpty(options.ArtifactName))
        {
            throw new ArgumentException("Enabled decision-journal options are required.", nameof(options));
        }
        if (log is null) throw new ArgumentNullException(nameof(log));
        if (!options.Status.TryRegister(out var status) || status is null)
        {
            log.LogAutomataWarning("ServiceCycle decision-journal status already has a producer; this runtime will not start another writer.");
            return null;
        }

        ServiceCycleDecisionJournalRuntime runtime;
        try
        {
            var spec = options.Source.Create();
            runtime = new ServiceCycleDecisionJournalRuntime(
                pump,
                spec.Storage,
                spec.Run,
                spec.MaximumCommittedSegments,
                spec.BlockCount,
                spec.CheckpointInterval);
        }
        catch (Exception exception) when (!BufferedSegmentFailurePolicy.IsProcessFatal(exception))
        {
            var controller = new AutomataDecisionJournalController(
                null,
                status,
                log,
                options.ArtifactName);
            controller.PublishInitializationFailure(exception);
            log.LogAutomataError(
                "ServiceCycle decision-journal initialization failed; gameplay remains active: " +
                exception.GetBaseException().Message);
            return controller;
        }

        var recordingController = new AutomataDecisionJournalController(
            runtime,
            status,
            log,
            options.ArtifactName);
        recordingController.Publish(runtime.Snapshot);
        return recordingController;
    }

    internal DecisionJournalStatus Snapshot => _lastPublished;

    internal void Tick()
    {
        if (_disposed || _runtime is null) return;
        Publish(_runtime.Tick());
    }

    internal DecisionJournalStatus Flush()
    {
        if (_disposed || _runtime is null) return _lastPublished;
        Publish(_runtime.Flush());
        return _lastPublished;
    }

    internal bool DisposeWithPump()
    {
        if (_disposed) return _runtime is not null;
        _disposed = true;
        try
        {
            if (_runtime is null) return false;
            if (!_runtime.DisposeWithPump())
            {
                _log.LogAutomataError(
                    "ServiceCycle decision journal did not finish its shutdown drain within " +
                    BufferedSegmentShutdown.DrainBound.TotalSeconds + "s at " +
                    AutomataDecisionJournalPathPolicy.FormatRelativeArtifactPath(_artifactName) +
                    "; its last segments may not have reached disk.");
            }
            return true;
        }
        finally { _status.Dispose(); }
    }

    internal void Publish(DecisionJournalRuntimeSnapshot snapshot)
    {
        var status = MapStatus(in snapshot, _artifactName);
        if (!_status.Publish(status)) return;
        var priorState = _lastPublished.State;
        _lastPublished = status;
        if (priorState == status.State) return;
        if (status.State == DecisionJournalStatusState.Recording)
        {
            _log.LogAutomataInfo(
                "ServiceCycle decision journal is recording at " +
                AutomataDecisionJournalPathPolicy.FormatRelativeArtifactPath(_artifactName) + ".");
            if (status.IncompatibleSegmentsPruned > 0)
            {
                _log.LogAutomataWarning(
                    "ServiceCycle decision journal discarded " +
                    status.IncompatibleSegmentsPruned.ToString(CultureInfo.InvariantCulture) +
                    " incompatible segments it could not continue from at " +
                    AutomataDecisionJournalPathPolicy.FormatRelativeArtifactPath(_artifactName) + ".");
            }
        }
        else if (status.State == DecisionJournalStatusState.Faulted)
        {
            _log.LogAutomataError(
                "ServiceCycle decision journal stopped after " + status.Result + FormatFault(status) +
                "; gameplay remains active.");
        }
    }

    /// <summary>
    /// Names the observation the journal died in, and what it threw.
    /// </summary>
    /// <remarks>
    /// A producer fault used to reach the log as "stopped after ProducerFailed" and nothing else,
    /// which says only that the journal is gone — not what the runtime disagreed with itself about.
    /// </remarks>
    private static string FormatFault(DecisionJournalStatus status)
    {
        if (status.FaultSite.Length == 0) return string.Empty;
        var site = " at " + status.FaultSite;
        return status.FaultMessage.Length == 0 ? site : site + ": " + status.FaultMessage;
    }

    private void PublishInitializationFailure(Exception exception)
    {
        var status = new DecisionJournalStatus(
            DecisionJournalStatusState.Faulted,
            acceptedRecords: 0,
            writtenRecords: 0,
            discardedRecords: 0,
            bytesWritten: 0,
            writtenSegments: 0,
            retainedSegments: 0,
            evictedSegments: 0,
            startupPrunedSegments: 0,
            incompatibleSegmentsPruned: 0,
            staleTemporaryFilesRemoved: 0,
            pendingBlocks: 0,
            peakPendingBlocks: 0,
            firstIncompleteSequence: 1,
            DecisionJournalStatusResult.InitializationFailed,
            _artifactName,
            nameof(TryCreate),
            exception.GetBaseException().Message);
        _status.Publish(status);
        _lastPublished = status;
    }

    internal static DecisionJournalStatus MapStatus(
        in DecisionJournalRuntimeSnapshot snapshot,
        string artifactName)
    {
        var transport = snapshot.Transport;
        var consumer = snapshot.Consumer;
        return new DecisionJournalStatus(
            MapState(snapshot.State),
            transport.AcceptedRecords,
            transport.WrittenRecords,
            transport.DiscardedRecords,
            transport.BytesWritten,
            transport.WrittenBlocks,
            consumer.RetainedSegments,
            consumer.EvictedSegments,
            consumer.StartupPrunedSegments,
            consumer.IncompatibleSegmentsPruned,
            consumer.StaleTemporaryFilesRemoved,
            transport.PendingBlocks,
            transport.PeakPendingBlocks,
            transport.FirstIncompleteSequence,
            MapResult(transport.FaultReason, consumer.FaultReason),
            artifactName,
            snapshot.FaultSite ?? string.Empty,
            FaultMessage(snapshot.FaultException, snapshot.FaultSite));
    }

    private static string FaultMessage(Exception? exception, string? site) =>
        exception is null || site is null ? string.Empty : exception.GetBaseException().Message;

    private static DecisionJournalStatusState MapState(DecisionJournalRuntimeState state) => state switch
    {
        DecisionJournalRuntimeState.Initializing => DecisionJournalStatusState.Initializing,
        DecisionJournalRuntimeState.Arming => DecisionJournalStatusState.Arming,
        DecisionJournalRuntimeState.Recording => DecisionJournalStatusState.Recording,
        DecisionJournalRuntimeState.Stopping => DecisionJournalStatusState.Stopping,
        DecisionJournalRuntimeState.Stopped => DecisionJournalStatusState.Stopped,
        DecisionJournalRuntimeState.Faulted => DecisionJournalStatusState.Faulted,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static DecisionJournalStatusResult MapResult(
        BufferedSegmentFaultReason transport,
        DecisionJournalConsumerFaultReason consumer)
    {
        if (transport == BufferedSegmentFaultReason.None)
            return DecisionJournalStatusResult.None;
        if (consumer == DecisionJournalConsumerFaultReason.RetentionFailed)
            return DecisionJournalStatusResult.RetentionFailed;
        if (consumer == DecisionJournalConsumerFaultReason.OrdinalExhausted)
            return DecisionJournalStatusResult.OrdinalExhausted;
        return transport switch
        {
            BufferedSegmentFaultReason.None => DecisionJournalStatusResult.None,
            BufferedSegmentFaultReason.BufferExhausted => DecisionJournalStatusResult.BufferExhausted,
            BufferedSegmentFaultReason.SequenceExhausted => DecisionJournalStatusResult.SequenceExhausted,
            BufferedSegmentFaultReason.InitializationFailed => DecisionJournalStatusResult.InitializationFailed,
            BufferedSegmentFaultReason.WriteFailed => DecisionJournalStatusResult.WriteFailed,
            BufferedSegmentFaultReason.CompletionFailed => DecisionJournalStatusResult.CompletionFailed,
            BufferedSegmentFaultReason.ProducerFailed or BufferedSegmentFaultReason.ProducerStopped =>
                DecisionJournalStatusResult.ProducerFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(transport)),
        };
    }
}
