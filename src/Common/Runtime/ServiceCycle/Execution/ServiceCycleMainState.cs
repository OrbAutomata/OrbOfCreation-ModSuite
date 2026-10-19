using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal sealed class ServiceCycleMainState
{
    internal ServiceCycleMainState(ServiceFaultRecoveryPolicy faultRecoveryPolicy) =>
        RetryGate = new ServiceTargetRetryGate(faultRecoveryPolicy);

    internal readonly ServiceTargetRetryGate RetryGate;
    internal ConfigurationPublication? CycleConfiguration;
    internal ServiceCycleIdentity InFlightCycle;
    internal BatchId InFlightBatch;
    internal bool HasInFlightCycle;
    internal ServiceCycleIdentity ActiveCycle;
    internal BatchId ActiveBatch;
    internal WakePolicy ActiveWake;
    internal MonotonicTimestamp ResponsePublishedAt;
    internal MonotonicTimestamp NextWakeDue;
    internal bool HasWakeDue;
    internal ConfigGeneration WakeConfigurationGeneration;
    internal bool WakeInvalidatedByConfiguration;
    internal WorldGeneration WakeWorldGeneration;
    internal bool WakeInvalidatedByWorld;
    internal BatchReceipt PreviousReceipt;
    internal ServiceProjectionPublication Projection;
    internal ServiceFault LatestFault;
    internal ServiceNativeCallTotals NativeOutcome;
    internal int CommittedCount;

    internal int PreNativeSkippedCount;
    internal ConfigGeneration LatestConfigGeneration;
    internal int ActionCount;
    internal int ActionCursor;
    internal int ActionCapacity;
    internal int ActionHighWater;
    internal long ActionGrowthAllocations;
    internal int RetainedActionSlots;
    internal bool HasActiveBatch;
    internal ServiceStartDecisionFact LastStartDecision;
    internal ServiceCaptureFact LastCapture;
    internal ServiceActionFact LastAction;
    internal ServiceEvaluationTimingFact EvaluationTiming;

    internal void ScheduleWake(
        MonotonicTimestamp due,
        ConfigGeneration configurationGeneration,
        WorldGeneration worldGeneration = default,
        bool invalidatedByConfiguration = true,
        bool invalidatedByWorld = true)
    {
        if (!configurationGeneration.IsValid)
            throw new System.ArgumentException(
                "A valid configuration generation is required.",
                nameof(configurationGeneration));
        if (invalidatedByWorld && !worldGeneration.IsValid)
            throw new System.ArgumentException(
                "A valid world generation is required for a world-sensitive wake.",
                nameof(worldGeneration));
        NextWakeDue = due;
        WakeConfigurationGeneration = configurationGeneration;
        WakeInvalidatedByConfiguration = invalidatedByConfiguration;
        WakeWorldGeneration = worldGeneration;
        WakeInvalidatedByWorld = invalidatedByWorld;
        HasWakeDue = true;
    }

    internal void ClearWake()
    {
        HasWakeDue = false;
        WakeConfigurationGeneration = default;
        WakeInvalidatedByConfiguration = false;
        WakeWorldGeneration = default;
        WakeInvalidatedByWorld = false;
    }
}
