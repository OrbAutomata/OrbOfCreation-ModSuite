namespace OrbModding.Common.Runtime.ServiceCycle.Diagnostics;

public enum ServiceCycleEvaluationTimingAvailability
{
    NotAvailable = 0,
    Available = 1,
    Contended = 2,
}

public enum ServiceCycleStorageDiagnosticsAvailability
{
    NotAvailable = 0,
    Exact = 1,
    LastPublished = 2,
    HandoffContended = 3,
}

public enum ServiceCycleOperationalPhase
{
    Idle = 1,
    Capturing = 2,
    Evaluating = 3,
    DrainingBatch = 4,
    RetryBackoff = 5,
    EmergencyStopped = 6,
    Faulted = 7,
}

/// <summary>Neutral public projection of the internal half-duplex handoff phase.</summary>
public enum ServiceCycleHandoffDiagnosticsPhase
{
    Empty = 0,
    RequestReady = 1,
    Evaluating = 2,
    ResponseReady = 3,
    MainOwnedBatch = 4,
    Stopping = 5,
    Stopped = 6,
}
