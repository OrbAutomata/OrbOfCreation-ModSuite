using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class DecisionJournalRecordCodecTests
{
    [Fact]
    public void DecisionRoundTripsEveryCompactField()
    {
        var expected = DecisionJournalRecord.Decision(CreateObservation(1, 10, faultOccurrence: 2));
        var actual = RoundTrip(in expected);

        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Service, actual.Service);
        Assert.Equal(expected.Lifecycle, actual.Lifecycle);
        Assert.Equal(expected.FirstCycle, actual.FirstCycle);
        Assert.Equal(expected.LastCycle, actual.LastCycle);
        Assert.Equal(expected.FirstTimestampTicks, actual.FirstTimestampTicks);
        Assert.Equal(expected.LastTimestampTicks, actual.LastTimestampTicks);
        Assert.Equal(expected.RepeatCount, actual.RepeatCount);
        Assert.Equal(expected.DecisionOutcomeKind, actual.DecisionOutcomeKind);
        Assert.Equal(expected.DecisionOutcomeCode, actual.DecisionOutcomeCode);
        Assert.Equal(expected.FaultCategory, actual.FaultCategory);
        Assert.Equal(expected.FaultCode, actual.FaultCode);
        Assert.Equal(expected.FirstFaultOccurrence, actual.FirstFaultOccurrence);
        Assert.Equal(expected.LastFaultOccurrence, actual.LastFaultOccurrence);
    }

    [Fact]
    public void ActionRoundTripsExactAttributionAndOneOutcome()
    {
        var candidate = new Guid("11111111-1111-1111-1111-111111111111");
        var list = new Guid("22222222-2222-2222-2222-222222222222");
        var view = new Guid("33333333-3333-3333-3333-333333333333");
        var attribution = ServiceActionJournalAttribution.Routed(
            candidate,
            ServiceActionNativeTypeId.StructureSO,
            list,
            view);
        var expected = ActionRecord(in attribution);

        var actual = RoundTrip(in expected);

        Assert.Equal(DecisionJournalRecordKind.Action, actual.Kind);
        Assert.Equal((ulong)1, actual.FirstCycle);
        Assert.Equal((ushort)1, actual.ActionOrdinal);
        Assert.Equal(candidate, actual.Attribution.CandidateId);
        Assert.Equal(ServiceActionNativeTypeId.StructureSO, actual.Attribution.NativeType);
        Assert.Equal(list, actual.Attribution.ListId);
        Assert.Equal(view, actual.Attribution.ViewId);
        Assert.Equal(ServiceActionRouteStatus.Resolved, actual.Attribution.RouteStatus);
        Assert.Equal(ServiceActionDisposition.Rejected, actual.ActionOutcome.Disposition);
        Assert.Equal(CommonActionResultCodes.PolicyRejected.Value, actual.ActionOutcome.Code);
    }

    [Fact]
    public void ActionRoundTripsTheCandidatesActionKind()
    {
        var candidate = new Guid("11111111-1111-1111-1111-111111111111");
        var attribution = ServiceActionJournalAttribution.Native(
            candidate,
            ServiceActionNativeTypeId.SpellRecipeSO,
            actionKind: 2);
        var expected = ActionRecord(in attribution);

        var actual = RoundTrip(in expected);

        Assert.Equal(candidate, actual.Attribution.CandidateId);
        Assert.Equal(ServiceActionNativeTypeId.SpellRecipeSO, actual.Attribution.NativeType);
        Assert.Equal((byte)2, actual.Attribution.ActionKind);
    }

    [Fact]
    public void WireRecordIsEightyBytes() =>
        Assert.Equal(80, DecisionJournalRecordCodec.RecordBytes);

    [Fact]
    public void GlobalTransitionRoundTripsWithoutServiceIdentity()
    {
        var expected = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.EmergencyEntered,
            default,
            0,
            new MonotonicTimestamp(42),
            code: 3);

        var actual = RoundTrip(in expected);

        Assert.Equal(expected.Kind, actual.Kind);
        Assert.False(actual.Service.IsValid);
        Assert.Equal(3, actual.TransitionCode);
    }

    [Fact]
    public void WorldGateHoldRoundTripsItsServiceLifecycleAndReason()
    {
        var expected = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.WorldGateHeld,
            new ServiceCycleTraceServiceId(2),
            5,
            new MonotonicTimestamp(42),
            code: 2);

        var actual = RoundTrip(in expected);

        Assert.Equal(DecisionJournalRecordKind.WorldGateHeld, actual.Kind);
        Assert.Equal(expected.Service, actual.Service);
        Assert.Equal((ulong)5, actual.Lifecycle);
        Assert.Equal(2, actual.TransitionCode);
    }

    [Fact]
    public void DecisionReservedBytesAreRejected()
    {
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        var bytes = Write(in record);
        bytes[^1] = 1;

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void ActionReservedBytesAreRejected()
    {
        var attribution = ServiceActionJournalAttribution.Native(
            new Guid("11111111-1111-1111-1111-111111111111"),
            ServiceActionNativeTypeId.StructureSO);
        var record = ActionRecord(in attribution);
        var bytes = Write(in record);
        bytes[13] = 1;

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    [Fact]
    public void MissingActionOutcomeIsRejected()
    {
        var attribution = ServiceActionJournalAttribution.Native(
            new Guid("11111111-1111-1111-1111-111111111111"),
            ServiceActionNativeTypeId.StructureSO);
        var record = ActionRecord(in attribution);
        var bytes = Write(in record);
        bytes.AsSpan(4, 4).Clear();

        Assert.Throws<FormatException>(() => DecisionJournalRecordCodec.Read(bytes));
    }

    private static DecisionJournalRecord ActionRecord(in ServiceActionJournalAttribution attribution)
    {
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
        return DecisionJournalRecord.Action(in observation);
    }

    private static DecisionJournalRecord RoundTrip(in DecisionJournalRecord record) =>
        DecisionJournalRecordCodec.Read(Write(in record));

    private static byte[] Write(in DecisionJournalRecord record)
    {
        var bytes = new byte[DecisionJournalRecordCodec.RecordBytes];
        DecisionJournalRecordCodec.Write(bytes, in record);
        return bytes;
    }
}
