using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>
/// Read-only owner-thread view of the suite semantic stream. Runtime event emission remains private to
/// the Common frame-pump boundary; diagnostics and export code can only inspect or capture committed facts.
/// </summary>
public sealed class ServiceCycleSemanticTraceSource
{
    private readonly ServiceCycleSemanticRecorder _recorder;
    private string _emissionFaultCause = string.Empty;

    internal ServiceCycleSemanticTraceSource(ServiceCycleSemanticRecorder recorder) =>
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));

    public ServiceCycleTraceSessionId Session => _recorder.Session;
    public int Capacity => _recorder.Capacity;
    public int Count => _recorder.Count;
    public int ServiceCapacity => _recorder.ServiceCapacity;
    public ulong OverwrittenTotal => _recorder.OverwrittenTotal;
    public ServiceCycleTraceDropRange OverwrittenRange => _recorder.OverwrittenRange;
    public ServiceCycleTraceCursor Cursor => _recorder.Cursor;
    public bool EmissionFaulted => _emissionFaultCause.Length != 0;

    /// <summary>
    /// What threw when the stream stopped recording, or empty while it still records. The first fault
    /// isolates the stream for good, so there is only ever one.
    /// </summary>
    internal string EmissionFaultCause => _emissionFaultCause;

    public ServiceCycleEventDrain DrainSince(
        ServiceCycleTraceCursor after,
        Span<ServiceCycleSemanticEvent> destination) => _recorder.DrainSince(after, destination);

    public ServiceCycleTraceCapture CreateCapture(int capacity) => _recorder.CreateCapture(capacity);

    public ServiceCycleEventDrain PullCapture(ServiceCycleTraceCapture capture, int maximumEvents) =>
        _recorder.PullCapture(capture, maximumEvents);

    internal ServiceCycleSemanticRecorder Recorder => _recorder;

    internal void RecordEmissionFault(Exception exception)
    {
        var cause = GameActionFaultLog.Reason(
            "The recent-event buffer could not record an event.", exception, "ServiceCycle event trace");
        if (_emissionFaultCause.Length == 0) _emissionFaultCause = cause.Text;
    }
}
