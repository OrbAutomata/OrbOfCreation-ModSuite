using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>Allocation-free evidence for one caller-supplied frame identity.</summary>
public readonly struct SuiteFramePumpReport
{
    internal SuiteFramePumpReport(
        long frameIdentity,
        bool accepted,
        int startingOrdinal,
        int responsesAcquired,
        int actionsAttempted,
        int capturesAttempted,
        int cyclesStarted,
        int worldGateDeferrals,
        int emergencyBatchesRejected,
        long lifecyclePositionTransitions,
        MonotonicDuration responseDuration,
        MonotonicDuration actionDuration,
        MonotonicDuration captureDuration,
        MonotonicDuration totalDuration)
    {
        FrameIdentity = frameIdentity;
        Accepted = accepted;
        StartingOrdinal = startingOrdinal;
        ResponsesAcquired = responsesAcquired;
        ActionsAttempted = actionsAttempted;
        CapturesAttempted = capturesAttempted;
        CyclesStarted = cyclesStarted;
        WorldGateDeferrals = worldGateDeferrals;
        EmergencyBatchesRejected = emergencyBatchesRejected;
        LifecyclePositionTransitions = lifecyclePositionTransitions;
        ResponseDuration = responseDuration;
        ActionDuration = actionDuration;
        CaptureDuration = captureDuration;
        TotalDuration = totalDuration;
    }

    public long FrameIdentity { get; }
    public bool Accepted { get; }
    public int StartingOrdinal { get; }
    public int ResponsesAcquired { get; }
    public int ActionsAttempted { get; }
    /// <summary>How many services read the game on the main thread this frame.</summary>
    /// <remarks>
    /// One service can, so this is zero or one. Kept apart from <see cref="CyclesStarted"/> because
    /// it is the only main-thread cost a service's own code can put on the frame before its cycle
    /// even queues, and that is worth watching on its own.
    /// </remarks>
    public int CapturesAttempted { get; }

    /// <summary>How many services the runtime opened a cycle for this frame.</summary>
    public int CyclesStarted { get; }

    /// <summary>
    /// How many services the world freshness gate held closed this frame.
    /// </summary>
    /// <remarks>
    /// The one counter that separates a quiet frame from a stalled one. A held service attempts
    /// nothing, so without this a collector that stopped publishing looks like a suite with no work
    /// to do: zero cycles, zero actions, no faults. A number here that never returns to zero says
    /// the readings stopped arriving, not that the services ran out of things to want.
    /// </remarks>
    public int WorldGateDeferrals { get; }
    public int EmergencyBatchesRejected { get; }
    /// <summary>
    /// Exact count of physical runner-position state transitions observed during this accepted frame.
    /// </summary>
    public long LifecyclePositionTransitions { get; }
    public MonotonicDuration ResponseDuration { get; }
    public MonotonicDuration ActionDuration { get; }
    public MonotonicDuration CaptureDuration { get; }
    public MonotonicDuration TotalDuration { get; }
}
