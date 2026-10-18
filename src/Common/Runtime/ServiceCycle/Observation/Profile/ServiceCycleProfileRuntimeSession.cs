#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal sealed class ServiceCycleProfileRuntimeSession : IDisposable
{
    private const int MaximumGroups = 256;
    private const int SamplesPerGroup = 8;
    private const int MaximumMeasurementDepth = 16;
    private const int BlockCount = 10;
    private const int RecordsPerBlock = 256;

    private readonly ServiceCycleProfileMeasurementRecorder _recorder;
    private readonly BufferedServiceCycleProfileSink _sink;
    private bool _stopped;
    private string _failureCause = string.Empty;

    internal ServiceCycleProfileRuntimeSession(
        ISegmentSessionStorage storage,
        ServiceCycleProfileSessionId session,
        IMonotonicClock monotonicClock,
        Guid buildId,
        bool traceActive,
        ServiceCycleProfileProbe probe)
    {
        Probe = probe ?? throw new ArgumentNullException(nameof(probe));
        var allocation = ServiceCycleProfileAllocationCapability.Probe(
            GcServiceCycleProfileAllocationCounter.Instance);
        var calibration = ServiceCycleProfileCalibrationPoint.Capture(
            StopwatchServiceCycleProfileRawClock.Instance,
            monotonicClock,
            buildId,
            traceActive,
            in allocation);
        _recorder = new ServiceCycleProfileMeasurementRecorder(
            in calibration,
            MaximumGroups,
            SamplesPerGroup,
            MaximumMeasurementDepth);
        var calibrationDocument = calibration.Calibration;
        _sink = new BufferedServiceCycleProfileSink(
            storage,
            session,
            in calibrationDocument,
            BlockCount,
            RecordsPerBlock);
        Probe.Attach(_recorder);
    }

    internal ServiceCycleProfileProbe Probe { get; }
    internal ServiceCycleProfileSinkSnapshot Snapshot => _sink.Snapshot;
    internal bool ManifestCommitted => _sink.ManifestCommitted;
    internal ServiceCycleProfileTerminalReason TerminalReason => _sink.TerminalReason;

    /// <summary>
    /// Which check stopped the profile and what threw, when the probe or the sink did; empty
    /// otherwise.
    /// </summary>
    internal string FailureCause => _failureCause;

    internal void Stop(ServiceCycleProfileTerminalReason reason)
    {
        if (_stopped) return;
        _stopped = true;
        Probe.Detach();
        if (Probe.Fault != ServiceCycleProfileProbeFault.None || !_recorder.Seal())
        {
            _failureCause = DescribeProbeFailure();
            _sink.Stop(ServiceCycleProfileTerminalReason.ProbeFailed);
            return;
        }

        for (var group = 0; group < _recorder.GroupCount; group++)
        {
            if (!Append(_recorder.GetAggregate(group))) return;
            for (var sample = 0; sample < _recorder.GetSampleCount(group); sample++)
                if (!Append(_recorder.GetSample(group, sample))) return;
        }
        _sink.Stop(reason);
    }

    public void Dispose()
    {
        Stop(ServiceCycleProfileTerminalReason.RuntimeShutdown);
        _sink.Dispose();
    }

    private bool Append(in ServiceCycleProfileRecord record)
    {
        var result = _sink.Append(in record);
        if (result == ServiceCycleProfileAppendResult.Accepted) return true;
        if (result == ServiceCycleProfileAppendResult.Unavailable)
        {
            _failureCause = "the profile sink stopped taking the measurements it was handed";
            _sink.Stop(ServiceCycleProfileTerminalReason.ProbeFailed);
        }
        return false;
    }

    private string DescribeProbeFailure()
    {
        var probe = Probe.Fault == ServiceCycleProfileProbeFault.None
            ? string.Empty
            : "the probe saw " + Probe.Fault;
        var recorder = _recorder.Fault == ServiceCycleProfileMeasurementFault.None
            ? string.Empty
            : "the recorder faulted with " + _recorder.Fault +
                (_recorder.AggregationFault == ServiceCycleProfileAggregationFault.None
                    ? string.Empty
                    : " (" + _recorder.AggregationFault + ")");
        var cause = probe.Length > 0 && recorder.Length > 0
            ? probe + " after " + recorder
            : probe + recorder;
        return (Probe.FaultException ?? _recorder.FaultException) is { } thrown
            ? cause + ": " + ExceptionDetail.Describe(thrown)
            : cause;
    }
}
#endif
