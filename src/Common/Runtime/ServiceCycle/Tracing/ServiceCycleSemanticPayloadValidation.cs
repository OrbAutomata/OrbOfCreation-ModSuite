using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing;

internal static partial class ServiceCycleSemanticPayloadValidation
{
    internal static void EnsureValid(ServiceCycleSemanticEventKind kind, in ServiceCycleSemanticPayload payload)
    {
        if (kind is < ServiceCycleSemanticEventKind.ConfigurationPublished or
            > ServiceCycleSemanticEventKind.PumpHeapSampled)
            throw new ArgumentOutOfRangeException(nameof(kind));

        var expected = ExpectedFields(kind);
        // Every field outside the kind's admissible options has to match exactly; the options are
        // free. A faulted action may or may not have reached the native boundary and a committed
        // action may have published instead of mutating, so both carry the mutation outcome only
        // sometimes; capture and action facts carry a frame only when a pump frame was open.
        if ((payload.Fields & ~OptionalFields(kind)) != expected)
            throw new ArgumentException("The semantic payload shape does not match its event kind.", nameof(payload));
        if (payload.TimestampTicks < 0 || payload.DurationTicks < 0 || payload.DeadlineTicks < 0 ||
            payload.FrameIdentity < 0 ||
            payload.ResponseDurationTicks < 0 || payload.ActionDurationTicks < 0 ||
            payload.CaptureDurationTicks < 0 || payload.TotalDurationTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(payload));
        if (payload.Code < 0 ||
            Has(expected, ServiceCycleSemanticFields.ActionIndex) && payload.ActionIndex < -1 ||
            Has(expected, ServiceCycleSemanticFields.ActionCount) && payload.ActionCount < 0 ||
            Has(expected, ServiceCycleSemanticFields.CommittedCount) && payload.CommittedCount < 0 ||
            Has(expected, ServiceCycleSemanticFields.UntouchedSuffixCount) && payload.UntouchedSuffixCount < 0 ||
            Has(expected, ServiceCycleSemanticFields.OccurrenceCount) && payload.OccurrenceCount <= 0 ||
            Has(expected, ServiceCycleSemanticFields.NativeCallTotals) && !NativeTotalsAreCoherent(in payload))
            throw new ArgumentOutOfRangeException(nameof(payload));

        if ((expected & ServiceCycleSemanticFields.Service) != 0 && payload.Service == 0 ||
            (expected & ServiceCycleSemanticFields.Lifecycle) != 0 && payload.Lifecycle == 0 ||
            (expected & ServiceCycleSemanticFields.Configuration) != 0 && payload.Configuration == 0 ||
            (expected & ServiceCycleSemanticFields.Strategy) != 0 && payload.Strategy == 0 ||
            (expected & ServiceCycleSemanticFields.World) != 0 && payload.World == 0 ||
            (expected & ServiceCycleSemanticFields.Capture) != 0 && payload.Capture == 0 ||
            (expected & ServiceCycleSemanticFields.Cycle) != 0 && payload.Cycle == 0 ||
            (expected & ServiceCycleSemanticFields.Batch) != 0 && payload.Batch == 0 ||
            (expected & ServiceCycleSemanticFields.Action) != 0 && payload.Action == 0 ||
            (expected & ServiceCycleSemanticFields.StatePublication) != 0 && payload.StatePublication == 0)
            throw new ArgumentException("A required semantic identity is zero.", nameof(payload));

        EnsureUnusedFieldsAreZero(in payload);
        ValidateEvent(kind, in payload);
    }

    private static void ValidateEvent(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload payload)
    {
        switch (kind)
        {
            case ServiceCycleSemanticEventKind.ConfigurationPublished:
            case ServiceCycleSemanticEventKind.StrategyPublished:
            case ServiceCycleSemanticEventKind.EmergencyEntered:
            case ServiceCycleSemanticEventKind.EmergencyCleared:
            case ServiceCycleSemanticEventKind.LifecycleRequested:
            case ServiceCycleSemanticEventKind.LifecycleActivated:
            case ServiceCycleSemanticEventKind.LifecycleRetired:
            case ServiceCycleSemanticEventKind.LifecycleConstructionDeferred:
            case ServiceCycleSemanticEventKind.RetryScheduled:
            case ServiceCycleSemanticEventKind.FaultObserved:
            case ServiceCycleSemanticEventKind.FaultRecovered:
            case ServiceCycleSemanticEventKind.PumpCompleted:
            case ServiceCycleSemanticEventKind.StartAttempted:
            case ServiceCycleSemanticEventKind.StartDeferred:
            case ServiceCycleSemanticEventKind.StartFaulted:
            case ServiceCycleSemanticEventKind.StartReady:
                ValidateLifecycleAndContext(kind, in payload);
                break;
            case ServiceCycleSemanticEventKind.CycleQueued:
            case ServiceCycleSemanticEventKind.CycleStarted:
            case ServiceCycleSemanticEventKind.CycleCompleted:
            case ServiceCycleSemanticEventKind.CycleOrphaned:
            case ServiceCycleSemanticEventKind.CycleFaulted:
            case ServiceCycleSemanticEventKind.CaptureStarted:
            case ServiceCycleSemanticEventKind.CaptureCompleted:
            case ServiceCycleSemanticEventKind.CaptureUnavailable:
            case ServiceCycleSemanticEventKind.CaptureFaulted:
            case ServiceCycleSemanticEventKind.EvaluationStarted:
            case ServiceCycleSemanticEventKind.EvaluationCompleted:
            case ServiceCycleSemanticEventKind.ProjectionFaulted:
            case ServiceCycleSemanticEventKind.EvaluationFaulted:
            case ServiceCycleSemanticEventKind.EvaluationDeferred:
                ValidateCycleAndEvaluation(kind, in payload);
                break;
            case ServiceCycleSemanticEventKind.StatePublished:
            case ServiceCycleSemanticEventKind.BatchPublished:
            case ServiceCycleSemanticEventKind.ActionAttempted:
            case ServiceCycleSemanticEventKind.ActionCommitted:
            case ServiceCycleSemanticEventKind.ActionSkipped:
            case ServiceCycleSemanticEventKind.ActionRejected:
            case ServiceCycleSemanticEventKind.ActionFaulted:
            case ServiceCycleSemanticEventKind.BatchCompleted:
            case ServiceCycleSemanticEventKind.BatchAborted:
            case ServiceCycleSemanticEventKind.BatchOrphaned:
                ValidateExecution(kind, in payload);
                break;
            case ServiceCycleSemanticEventKind.WorldCategoryCollected:
                // The identity is a position in the pass it was read by, so a span outside its own
                // pass is an emitter that lost track of which collection it is describing.
                Require(
                    payload.WorldCategory > 0 && payload.WorldCategory <= payload.WorldPassCategories,
                    nameof(payload));
                break;
            case ServiceCycleSemanticEventKind.PumpHeapSampled:
                // The count only rises, and a heap that reads zero was never read. A census is whole or
                // absent, and a reservation smaller than its use was never read either.
                var censusFields = payload.Fields & ServiceCycleSemanticPayload.CensusFields;
                Require(
                    payload.CollectionsAtPumpEnd >= payload.CollectionsAtPumpStart &&
                    payload.HeapAtPumpStart > 0 && payload.HeapAtPumpEnd > 0 &&
                    payload.TotalDurationTicks == 0 &&
                    (payload.CapturesMeasured > 0 || payload.CaptureHeapGrowth == 0) &&
                    (censusFields == 0 || censusFields == ServiceCycleSemanticPayload.CensusFields &&
                        payload.HeapCensus is { } census && census.ReservedHeap >= payload.HeapAtPumpEnd &&
                        census.LiveWorldSnapshots >= 0 && census.ScriptableObjects >= 0 &&
                        census.GameObjects >= 0 && census.MonoBehaviours >= 0 && payload.CyclesStarted == 0 &&
                        payload.WorldGateDeferrals == 0 && payload.EmergencyBatchesRejected == 0 &&
                        payload.LifecycleTransitions == 0),
                    nameof(payload));
                break;
        }
    }

    private static bool Has(ServiceCycleSemanticFields fields, ServiceCycleSemanticFields value) =>
        (fields & value) != 0;

    private static void Require(bool condition, string parameterName)
    {
        if (!condition) throw new ArgumentException("The semantic payload contains incoherent values.", parameterName);
    }
}
