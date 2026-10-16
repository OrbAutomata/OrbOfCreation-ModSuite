using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>
/// Stable owner-thread semantic-emission facade. Focused collaborators translate context/control,
/// cycle/evaluation, and batch/action facts; one causal writer owns ordering and delayed anchors.
/// </summary>
public sealed partial class ServiceCycleSemanticRecorder
{
    private readonly ServiceCycleSemanticCausalWriter _writer;
    private readonly ServiceCycleSemanticContextEmitter _context;
    private readonly ServiceCycleSemanticCycleEmitter _cycles;
    private readonly ServiceCycleSemanticAdmissionEmitter _admission;
    private readonly ServiceCycleSemanticEvaluationEmitter _evaluation;
    private readonly ServiceCycleSemanticBatchEmitter _batches;
    private readonly ServiceCycleSemanticWorldEmitter _world;
    private readonly ServiceCycleSemanticFrameCursor _frame = new();

    public ServiceCycleSemanticRecorder(
        ServiceCycleTraceSessionId session,
        int eventCapacity,
        int serviceCapacity,
        bool enabled = true)
        : this(session, eventCapacity, serviceCapacity, enabled, null) { }

    internal ServiceCycleSemanticRecorder(
        ServiceCycleTraceSessionId session,
        int eventCapacity,
        int serviceCapacity,
        bool enabled,
        IServiceCycleSemanticEventObserver? observer)
    {
        _writer = new ServiceCycleSemanticCausalWriter(
            session,
            eventCapacity,
            serviceCapacity,
            observer);
        _context = new ServiceCycleSemanticContextEmitter(_writer, enabled);
        _cycles = new ServiceCycleSemanticCycleEmitter(_writer, enabled);
        _admission = new ServiceCycleSemanticAdmissionEmitter(_writer, _frame, enabled);
        _evaluation = new ServiceCycleSemanticEvaluationEmitter(_writer, enabled);
        _batches = new ServiceCycleSemanticBatchEmitter(_writer, _frame, enabled);
        _world = new ServiceCycleSemanticWorldEmitter(_writer, enabled);
        Enabled = enabled;
    }

    /// <summary>
    /// Opens the frame that capture and action facts recorded from here on ran inside. The pump
    /// brackets its own frame with this; nothing else may, because nothing else knows.
    /// </summary>
    public void EnterFrame(long frameIdentity) => _frame.Enter(frameIdentity);

    public void LeaveFrame() => _frame.Leave();

    public bool Enabled { get; }
    public ServiceCycleTraceSessionId Session => _writer.Session;
    public int Capacity => _writer.Capacity;
    public int Count => _writer.Count;
    public ulong OverwrittenTotal => _writer.OverwrittenTotal;
    public ServiceCycleTraceDropRange OverwrittenRange => _writer.OverwrittenRange;
    public ServiceCycleTraceCursor Cursor => _writer.Cursor;
    internal ServiceCycleTraceIdentityMap Identities => _writer.Identities;
    internal int ServiceCapacity => _writer.ServiceCapacity;

    public void RegisterService(int ordinal, ServiceId service) =>
        _writer.RegisterService(ordinal, service);

    public ServiceCycleEventDrain DrainSince(
        ServiceCycleTraceCursor after,
        Span<ServiceCycleSemanticEvent> destination) =>
        _writer.DrainSince(after, destination);

    public ServiceCycleTraceCapture CreateCapture(int capacity) =>
        new(Session, capacity, ServiceCapacity);

    public ServiceCycleEventDrain PullCapture(ServiceCycleTraceCapture capture, int maximumEvents)
    {
        if (capture is null) throw new ArgumentNullException(nameof(capture));
        return _writer.PullCapture(capture, maximumEvents);
    }
}
