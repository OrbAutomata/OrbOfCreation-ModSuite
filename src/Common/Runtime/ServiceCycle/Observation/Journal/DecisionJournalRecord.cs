using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;

internal enum DecisionJournalRecordKind : byte
{
    DecisionSpan = 1,
    ConfigurationChanged = 2,
    StrategyChanged = 3,
    LifecycleChanged = 4,
    EmergencyEntered = 5,
    EmergencyCleared = 6,
    WorldGateHeld = 7,
    Action = 8,
}

internal enum DecisionJournalDecisionOutcomeKind : byte
{
    None = 0,
    Start = 1,
    Capture = 2,
    Batch = 3,
    Fault = 4,
}

/// <summary>One packed post-action outcome. Disposition and stable result code are never duplicated.</summary>
internal readonly struct DecisionJournalActionOutcome
{
    private const int DispositionShift = 29;
    private const uint CodeMask = (1u << DispositionShift) - 1;

    private DecisionJournalActionOutcome(uint value) => Value = value;

    internal uint Value { get; }
    internal ServiceActionDisposition Disposition =>
        (ServiceActionDisposition)(Value >> DispositionShift);
    internal int Code => checked((int)(Value & CodeMask));
    internal bool IsValid =>
        Disposition is >= ServiceActionDisposition.Committed and <= ServiceActionDisposition.Skipped &&
        Code > 0;

    internal static DecisionJournalActionOutcome From(in ServiceActionResult result)
    {
        if (!result.IsValid) throw new ArgumentException("A valid action result is required.", nameof(result));
        if ((uint)result.Code.Value > CodeMask)
            throw new ArgumentOutOfRangeException(nameof(result), "The action result code does not fit the journal wire value.");
        return new DecisionJournalActionOutcome(
            ((uint)result.Disposition << DispositionShift) | (uint)result.Code.Value);
    }

    internal static DecisionJournalActionOutcome Read(uint value)
    {
        var outcome = new DecisionJournalActionOutcome(value);
        return outcome.IsValid ? outcome : throw new FormatException("Invalid decision-journal action outcome.");
    }
}

internal readonly struct DecisionJournalRecord
{
    private DecisionJournalRecord(
        DecisionJournalRecordKind kind,
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        ulong firstCycle,
        ulong lastCycle,
        long firstTimestampTicks,
        long lastTimestampTicks,
        long repeatCount,
        DecisionJournalDecisionOutcomeKind decisionOutcomeKind,
        int decisionOutcomeCode,
        ServiceFaultCategory faultCategory,
        int faultCode,
        int firstFaultOccurrence,
        int lastFaultOccurrence,
        ulong generation,
        int transitionCode,
        ushort actionOrdinal,
        ServiceActionJournalAttribution attribution,
        DecisionJournalActionOutcome actionOutcome)
    {
        Kind = kind;
        Service = service;
        Lifecycle = lifecycle;
        FirstCycle = firstCycle;
        LastCycle = lastCycle;
        FirstTimestampTicks = firstTimestampTicks;
        LastTimestampTicks = lastTimestampTicks;
        RepeatCount = repeatCount;
        DecisionOutcomeKind = decisionOutcomeKind;
        DecisionOutcomeCode = decisionOutcomeCode;
        FaultCategory = faultCategory;
        FaultCode = faultCode;
        FirstFaultOccurrence = firstFaultOccurrence;
        LastFaultOccurrence = lastFaultOccurrence;
        Generation = generation;
        TransitionCode = transitionCode;
        ActionOrdinal = actionOrdinal;
        Attribution = attribution;
        ActionOutcome = actionOutcome;
    }

    internal DecisionJournalRecordKind Kind { get; }
    internal ServiceCycleTraceServiceId Service { get; }
    internal ulong Lifecycle { get; }
    internal ulong Configuration => Kind == DecisionJournalRecordKind.ConfigurationChanged ? Generation : 0;
    internal ulong Strategy => Kind == DecisionJournalRecordKind.StrategyChanged ? Generation : 0;
    internal ulong FirstCycle { get; }
    internal ulong LastCycle { get; }
    internal long FirstTimestampTicks { get; }
    internal long LastTimestampTicks { get; }
    internal long RepeatCount { get; }
    internal DecisionJournalDecisionOutcomeKind DecisionOutcomeKind { get; }
    internal int DecisionOutcomeCode { get; }
    internal ServiceFaultCategory FaultCategory { get; }
    internal int FaultCode { get; }
    internal int FirstFaultOccurrence { get; }
    internal int LastFaultOccurrence { get; }
    internal ulong Generation { get; }
    internal int TransitionCode { get; }
    internal ushort ActionOrdinal { get; }
    internal ServiceActionJournalAttribution Attribution { get; }
    internal DecisionJournalActionOutcome ActionOutcome { get; }

    internal static DecisionJournalRecord Decision(in DecisionJournalObservation observation)
    {
        // The journal deliberately compacts a cycle to one lossy outcome sentinel. Priority is fault,
        // then terminal batch, capture, and start; any lower-priority outcome is unrecoverable.
        var fault = observation.Fault;
        var outcomeKind = DecisionJournalDecisionOutcomeKind.None;
        var outcomeCode = 0;
        if (fault.IsValid)
        {
            outcomeKind = DecisionJournalDecisionOutcomeKind.Fault;
            outcomeCode = fault.Code.Value;
        }
        else if (observation.Terminal.IsPresent)
        {
            outcomeKind = DecisionJournalDecisionOutcomeKind.Batch;
            outcomeCode = observation.Terminal.ResultCode.Value;
        }
        else if (observation.CaptureDecisionCode != 0)
        {
            outcomeKind = DecisionJournalDecisionOutcomeKind.Capture;
            outcomeCode = observation.CaptureDecisionCode;
        }
        else if (observation.StartDecisionCode != 0)
        {
            outcomeKind = DecisionJournalDecisionOutcomeKind.Start;
            outcomeCode = observation.StartDecisionCode;
        }

        var record = new DecisionJournalRecord(
            DecisionJournalRecordKind.DecisionSpan,
            observation.Service,
            observation.Lifecycle,
            observation.Cycle,
            observation.Cycle,
            observation.FirstObservedAt.Ticks,
            observation.LastObservedAt.Ticks,
            1,
            outcomeKind,
            outcomeCode,
            fault.Category,
            fault.Code.Value,
            fault.OccurrenceCount,
            fault.OccurrenceCount,
            0,
            0,
            0,
            default,
            default);
        DecisionJournalRecordValidation.Validate(in record);
        return record;
    }

    internal static DecisionJournalRecord Action(in DecisionJournalActionObservation observation)
    {
        var context = observation.Fact.Context;
        var result = observation.Fact.Result;
        if (context.Action.Value == 0 || context.Action.Value > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(observation), "The action ordinal does not fit the journal wire value.");
        var record = new DecisionJournalRecord(
            DecisionJournalRecordKind.Action,
            observation.Service,
            0,
            context.Cycle.Cycle.Value,
            context.Cycle.Cycle.Value,
            observation.Fact.CompletedAt.Ticks,
            observation.Fact.CompletedAt.Ticks,
            1,
            DecisionJournalDecisionOutcomeKind.None,
            0,
            default,
            0,
            0,
            0,
            0,
            0,
            checked((ushort)context.Action.Value),
            observation.Attribution,
            DecisionJournalActionOutcome.From(in result));
        DecisionJournalRecordValidation.Validate(in record);
        return record;
    }

    internal static DecisionJournalRecord Transition(
        DecisionJournalRecordKind kind,
        ServiceCycleTraceServiceId service,
        ulong generation,
        MonotonicTimestamp observedAt,
        int code = 0)
    {
        if (kind is DecisionJournalRecordKind.DecisionSpan or DecisionJournalRecordKind.Action)
            throw new ArgumentOutOfRangeException(nameof(kind));
        var global = DecisionJournalRecordValidation.IsSuiteWide(kind);
        if (global != !service.IsValid)
            throw new ArgumentException("Suite-wide transitions must not carry a service identity.", nameof(service));
        var emergency = kind is DecisionJournalRecordKind.EmergencyEntered or DecisionJournalRecordKind.EmergencyCleared;
        if (!emergency && generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));

        var record = new DecisionJournalRecord(
            kind,
            service,
            kind is DecisionJournalRecordKind.LifecycleChanged or DecisionJournalRecordKind.WorldGateHeld
                ? generation : 0,
            0,
            0,
            observedAt.Ticks,
            observedAt.Ticks,
            1,
            DecisionJournalDecisionOutcomeKind.None,
            0,
            default,
            0,
            0,
            0,
            generation,
            code,
            0,
            default,
            default);
        DecisionJournalRecordValidation.Validate(in record);
        return record;
    }

    internal bool CanCoalesceWith(in DecisionJournalRecord next) =>
        Kind == DecisionJournalRecordKind.DecisionSpan &&
        next.Kind == Kind &&
        Service == next.Service &&
        Lifecycle == next.Lifecycle &&
        DecisionOutcomeKind == next.DecisionOutcomeKind &&
        DecisionOutcomeCode == next.DecisionOutcomeCode &&
        FaultCategory == next.FaultCategory &&
        FaultCode == next.FaultCode &&
        next.LastFaultOccurrence >= LastFaultOccurrence &&
        next.FirstTimestampTicks >= LastTimestampTicks &&
        SequenceFollows(LastCycle, next.FirstCycle);

    internal DecisionJournalRecord Coalesce(in DecisionJournalRecord next)
    {
        if (!CanCoalesceWith(in next))
            throw new ArgumentException("The journal decisions are not equivalent and consecutive.", nameof(next));
        var record = new DecisionJournalRecord(
            Kind,
            Service,
            Lifecycle,
            FirstCycle,
            next.LastCycle,
            FirstTimestampTicks,
            next.LastTimestampTicks,
            SaturatingAdd(RepeatCount, next.RepeatCount),
            DecisionOutcomeKind,
            DecisionOutcomeCode,
            FaultCategory,
            FaultCode,
            FirstFaultOccurrence,
            next.LastFaultOccurrence,
            0,
            0,
            0,
            default,
            default);
        DecisionJournalRecordValidation.Validate(in record);
        return record;
    }

    internal static DecisionJournalRecord ReadDecision(
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        ulong firstCycle,
        ulong lastCycle,
        long firstTimestampTicks,
        long lastTimestampTicks,
        long repeatCount,
        DecisionJournalDecisionOutcomeKind outcomeKind,
        int outcomeCode,
        ServiceFaultCategory faultCategory,
        int faultCode,
        int firstFaultOccurrence,
        int lastFaultOccurrence) =>
        new(DecisionJournalRecordKind.DecisionSpan, service, lifecycle, firstCycle, lastCycle,
            firstTimestampTicks, lastTimestampTicks, repeatCount, outcomeKind, outcomeCode,
            faultCategory, faultCode, firstFaultOccurrence, lastFaultOccurrence,
            0, 0, 0, default, default);

    internal static DecisionJournalRecord ReadAction(
        ServiceCycleTraceServiceId service,
        ulong cycle,
        long timestampTicks,
        ushort actionOrdinal,
        in ServiceActionJournalAttribution attribution,
        DecisionJournalActionOutcome outcome) =>
        new(DecisionJournalRecordKind.Action, service, 0, cycle, cycle, timestampTicks, timestampTicks,
            1, DecisionJournalDecisionOutcomeKind.None, 0, default, 0, 0, 0,
            0, 0, actionOrdinal, attribution, outcome);

    internal static DecisionJournalRecord ReadTransition(
        DecisionJournalRecordKind kind,
        ServiceCycleTraceServiceId service,
        ulong lifecycle,
        ulong generation,
        long timestampTicks,
        int code) =>
        new(kind, service, lifecycle, 0, 0, timestampTicks, timestampTicks, 1,
            DecisionJournalDecisionOutcomeKind.None, 0, default, 0, 0, 0,
            generation, code, 0, default, default);

    private static bool SequenceFollows(ulong previous, ulong next) =>
        previous == 0 ? next == 0 : previous != ulong.MaxValue && next == previous + 1;

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;
}
