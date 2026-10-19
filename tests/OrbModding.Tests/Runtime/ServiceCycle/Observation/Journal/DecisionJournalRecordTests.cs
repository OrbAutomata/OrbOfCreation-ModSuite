using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class DecisionJournalRecordTests
{
    [Fact]
    public void EquivalentConsecutiveCyclesCoalesceIntoOneSpan()
    {
        var first = DecisionJournalRecord.Decision(CreateObservation(1, 10, faultOccurrence: 1));
        var second = DecisionJournalRecord.Decision(CreateObservation(2, 20, faultOccurrence: 2));

        var span = first.Coalesce(in second);

        Assert.Equal(2, span.RepeatCount);
        Assert.Equal((ulong)1, span.FirstCycle);
        Assert.Equal((ulong)2, span.LastCycle);
        Assert.Equal(10, span.FirstTimestampTicks);
        Assert.Equal(21, span.LastTimestampTicks);
        Assert.Equal(1, span.FirstFaultOccurrence);
        Assert.Equal(2, span.LastFaultOccurrence);
        Assert.Equal(DecisionJournalDecisionOutcomeKind.Fault, span.DecisionOutcomeKind);
        Assert.Equal(CommonActionResultCodes.AdapterFault.Value, span.DecisionOutcomeCode);
    }

    [Fact]
    public void OneActionCarriesExactAttributionAndOneOutcome()
    {
        var candidate = new Guid("11111111-1111-1111-1111-111111111111");
        var list = new Guid("22222222-2222-2222-2222-222222222222");
        var view = new Guid("33333333-3333-3333-3333-333333333333");
        var attribution = ServiceActionJournalAttribution.Routed(
            candidate,
            ServiceActionNativeTypeId.StructureSO,
            list,
            view);
        var context = new ServiceActionContext(
            Identity(1),
            new BatchId(1),
            new ActionId(1),
            0,
            new MonotonicTimestamp(10));
        var result = ServiceActionResult.Rejected(CommonActionResultCodes.PolicyRejected);
        var fact = new ServiceActionFact(
            context,
            result,
            new MonotonicTimestamp(10),
            new MonotonicTimestamp(11));
        var observation = new DecisionJournalActionObservation(
            new ServiceCycleTraceServiceId(1),
            in fact,
            in attribution);

        var record = DecisionJournalRecord.Action(in observation);

        Assert.Equal(DecisionJournalRecordKind.Action, record.Kind);
        Assert.Equal(candidate, record.Attribution.CandidateId);
        Assert.Equal(ServiceActionNativeTypeId.StructureSO, record.Attribution.NativeType);
        Assert.Equal(list, record.Attribution.ListId);
        Assert.Equal(view, record.Attribution.ViewId);
        Assert.Equal(ServiceActionDisposition.Rejected, record.ActionOutcome.Disposition);
        Assert.Equal(CommonActionResultCodes.PolicyRejected.Value, record.ActionOutcome.Code);
    }

    [Fact]
    public void NativeAttributionRequiresBothCandidateUuidAndExactType()
    {
        Assert.Throws<ArgumentException>(() => ServiceActionJournalAttribution.Native(
            Guid.Empty,
            ServiceActionNativeTypeId.StructureSO));
        Assert.Throws<ArgumentException>(() => new ServiceActionJournalAttribution(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ServiceActionNativeTypeId.NotApplicable,
            Guid.Empty,
            Guid.Empty,
            ServiceActionRouteStatus.NotApplicable));
    }

    [Fact]
    public void TheNativeTypeRosterIsAppendOnlyAndPinned()
    {
        Assert.Equal(9, Enum.GetValues(typeof(ServiceActionNativeTypeId)).Length);
        Assert.Equal(0, (ushort)ServiceActionNativeTypeId.NotApplicable);
        Assert.Equal(1, (ushort)ServiceActionNativeTypeId.StructureSO);
        Assert.Equal(2, (ushort)ServiceActionNativeTypeId.UpgradeSO);
        Assert.Equal(3, (ushort)ServiceActionNativeTypeId.PlotNodeActionSO);
        Assert.Equal(4, (ushort)ServiceActionNativeTypeId.SpellRecipeSO);
        Assert.Equal(5, (ushort)ServiceActionNativeTypeId.AlchemyRecipeSO);
        Assert.Equal(6, (ushort)ServiceActionNativeTypeId.ConsumableSO);
        Assert.Equal(7, (ushort)ServiceActionNativeTypeId.CraftingRecipeSO);
        Assert.Equal(8, (ushort)ServiceActionNativeTypeId.EquipmentSO);
    }

    [Fact]
    public void MissingCycleBreaksOtherwiseEquivalentSpan()
    {
        var first = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var third = DecisionJournalRecord.Decision(CreateObservation(3, 20));

        Assert.False(first.CanCoalesceWith(in third));
    }

    /// <summary>
    /// Lifecycle names a service; configuration, strategy and emergency name the suite.
    /// </summary>
    /// <remarks>
    /// The suite publishes one configuration record and one strategy bulletin that every service
    /// reads, so a change is one record. Attributing it to a service produced N identical records
    /// and implied a per-service generation the runtime does not have.
    /// </remarks>
    [Fact]
    public void ServiceAndSuiteWideTransitionsHaveDistinctIdentityRules()
    {
        var service = new ServiceCycleTraceServiceId(1);
        var lifecycle = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.LifecycleChanged,
            service,
            2,
            new MonotonicTimestamp(10));
        var configuration = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.ConfigurationChanged,
            default,
            2,
            new MonotonicTimestamp(15));
        var emergency = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.EmergencyEntered,
            default,
            0,
            new MonotonicTimestamp(20),
            code: 1);

        var worldGate = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.WorldGateHeld,
            service,
            2,
            new MonotonicTimestamp(25),
            code: 1);

        Assert.Equal((ulong)2, lifecycle.Lifecycle);
        Assert.Equal((ulong)2, worldGate.Lifecycle);
        Assert.True(worldGate.Service.IsValid);
        Assert.Equal((ulong)2, configuration.Configuration);
        Assert.False(configuration.Service.IsValid);
        Assert.Equal(DecisionJournalRecordKind.EmergencyEntered, emergency.Kind);
        Assert.Throws<ArgumentException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.ConfigurationChanged,
            service,
            2,
            default));
        Assert.Throws<ArgumentException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.StrategyChanged,
            service,
            2,
            default));
        Assert.Throws<ArgumentException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.EmergencyCleared,
            service,
            0,
            default));
        Assert.Throws<ArgumentOutOfRangeException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.ConfigurationChanged,
            default,
            0,
            default));
        Assert.Throws<ArgumentException>(() => DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.WorldGateHeld,
            default,
            2,
            default));
    }

    [Fact]
    public void UnavailableCaptureRetainsIdentityWithoutInventingStrategy()
    {
        var projection = default(ServiceStateProjectionSnapshot);
        var fault = default(ServiceFault);
        var terminal = default(BatchReceipt);
        var observation = new DecisionJournalObservation(
            new ServiceCycleTraceServiceId(1),
            lifecycle: 1,
            configuration: 1,
            strategy: 0,
            cycle: 4,
            default,
            default,
            CommonServiceDecisionCodes.Ready.Value,
            CommonServiceDecisionCodes.CaptureUnavailable.Value,
            true,
            WakePolicy.AfterDecision(new MonotonicDuration(5)),
            false,
            in projection,
            in fault,
            in terminal);

        var record = DecisionJournalRecord.Decision(in observation);

        Assert.Equal((ulong)4, record.FirstCycle);
        Assert.Equal(DecisionJournalDecisionOutcomeKind.Capture, record.DecisionOutcomeKind);
        Assert.Equal(CommonServiceDecisionCodes.CaptureUnavailable.Value, record.DecisionOutcomeCode);
    }

    [Fact]
    public void CaptureFaultRetainsIdentityWithoutInventingDecisionOrStrategy()
    {
        var projection = default(ServiceStateProjectionSnapshot);
        var fault = new ServiceFault(
            ServiceFaultCategory.Capture,
            CommonActionResultCodes.AdapterFault,
            1,
            new MonotonicTimestamp(7));
        var terminal = default(BatchReceipt);
        var observation = new DecisionJournalObservation(
            new ServiceCycleTraceServiceId(1),
            lifecycle: 1,
            configuration: 1,
            strategy: 0,
            cycle: 5,
            default,
            default,
            CommonServiceDecisionCodes.Ready.Value,
            captureDecisionCode: 0,
            true,
            WakePolicy.Immediate,
            false,
            in projection,
            in fault,
            in terminal);

        var record = DecisionJournalRecord.Decision(in observation);

        Assert.Equal((ulong)5, record.FirstCycle);
        Assert.Equal(ServiceFaultCategory.Capture, record.FaultCategory);
        Assert.Equal(DecisionJournalDecisionOutcomeKind.Fault, record.DecisionOutcomeKind);
        Assert.Equal(CommonActionResultCodes.AdapterFault.Value, record.DecisionOutcomeCode);
    }
}
