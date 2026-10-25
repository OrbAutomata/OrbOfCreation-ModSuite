using System;
using OrbModding.Common;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public enum EmergencyStopReason
{
    UserRequested = 1,
    SafetyInterlock = 2,
    SuiteShutdown = 3,
}

public enum ServiceFaultCategory
{
    Capture = 1,
    Evaluation = 2,
    StateProjection = 3,
    ResponseValidation = 4,
    StateFactory = 5,
    ActionExecution = 6,
    NativeContract = 7,
    Storage = 8,
    LifecycleConstruction = 9,
    Start = 10,
}

public readonly struct ServiceFault
{
    public ServiceFault(
        ServiceFaultCategory category,
        ServiceActionResultCode code,
        int occurrenceCount,
        MonotonicTimestamp observedAt,
        string? reason = null)
    {
        if (category is < ServiceFaultCategory.Capture or > ServiceFaultCategory.Start)
            throw new ArgumentOutOfRangeException(nameof(category));
        if (!IsAllowedFaultCode(code))
            throw new ArgumentException("Faults require a feature code or the common adapter-fault code.", nameof(code));
        if (occurrenceCount <= 0) throw new ArgumentOutOfRangeException(nameof(occurrenceCount));
        Category = category;
        Code = code;
        OccurrenceCount = occurrenceCount;
        ObservedAt = observedAt;
        _reason = reason;
    }

    private readonly string? _reason;

    /// <summary>Authored failure detail and its suite-log reference; never the exception itself.</summary>
    public string Reason => _reason ?? string.Empty;
    public ServiceFaultCategory Category { get; }
    public ServiceActionResultCode Code { get; }
    public int OccurrenceCount { get; }
    public MonotonicTimestamp ObservedAt { get; }
    public bool IsValid =>
        Category is >= ServiceFaultCategory.Capture and <= ServiceFaultCategory.Start &&
        IsAllowedFaultCode(Code) &&
        OccurrenceCount > 0;

    private static bool IsAllowedFaultCode(ServiceActionResultCode code) =>
        code.IsValid && (code.IsFeatureCode || code == CommonActionResultCodes.AdapterFault);
}
