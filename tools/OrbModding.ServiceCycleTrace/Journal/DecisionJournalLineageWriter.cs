using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;

namespace OrbModding.ServiceCycleTrace.Journal;

internal sealed class DecisionJournalLineageWriter
{
    private readonly TextWriter _writer;
    private DecisionJournalRunId _run;

    internal DecisionJournalLineageWriter(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _writer.WriteLine("## Retained record lineage");
        _writer.WriteLine();
        _writer.WriteLine(
            "Rows follow durable record sequence within each run. They are not a global wall-clock or physical-thread timeline.");
        _writer.WriteLine();
    }

    internal void Write(DecisionJournalSegmentDocument segment)
    {
        if (segment.Run != _run)
        {
            _run = segment.Run;
            _writer.Write("### Run `");
            _writer.Write(segment.Run.Value.ToString("x16", CultureInfo.InvariantCulture));
            _writer.WriteLine("`");
            _writer.WriteLine();
        }

        for (var index = 0; index < segment.Records.Length; index++)
        {
            var sequence = checked(segment.FirstRecordSequence + (ulong)index);
            WriteRecord(sequence, in segment.Records[index]);
        }
    }

    internal void Complete(bool hasRecords)
    {
        if (!hasRecords) _writer.WriteLine("No committed journal records were retained.");
    }

    private void WriteRecord(ulong sequence, in DecisionJournalRecord record)
    {
        _writer.Write("- `#");
        _writer.Write(sequence.ToString(CultureInfo.InvariantCulture));
        _writer.Write("` ticks `");
        _writer.Write(record.FirstTimestampTicks.ToString(CultureInfo.InvariantCulture));
        if (record.LastTimestampTicks != record.FirstTimestampTicks)
        {
            _writer.Write("..");
            _writer.Write(record.LastTimestampTicks.ToString(CultureInfo.InvariantCulture));
        }
        _writer.Write("` (");
        _writer.Write(TraceMetric.ToMilliseconds(
            record.LastTimestampTicks - record.FirstTimestampTicks)
            .ToString("F3", CultureInfo.InvariantCulture));
        _writer.Write(" ms) — ");

        if (record.Kind == DecisionJournalRecordKind.DecisionSpan) WriteDecision(in record);
        else if (record.Kind == DecisionJournalRecordKind.Action) WriteAction(in record);
        else WriteTransition(in record);
        _writer.WriteLine();
    }

    private void WriteDecision(in DecisionJournalRecord record)
    {
        _writer.Write("service `");
        _writer.Write(record.Service.Value.ToString(CultureInfo.InvariantCulture));
        _writer.Write("` idle/failure span x");
        _writer.Write(record.RepeatCount.ToString("N0", CultureInfo.InvariantCulture));
        _writer.Write("; lifecycle `");
        _writer.Write(record.Lifecycle.ToString(CultureInfo.InvariantCulture));
        _writer.Write('`');
        WriteRange("cycle", record.FirstCycle, record.LastCycle);
        _writer.Write("; outcome ");
        _writer.Write(record.DecisionOutcomeKind);
        _writer.Write('/');
        WriteCode(DecisionJournalValueNames.Decision(record.DecisionOutcomeCode), record.DecisionOutcomeCode);
        _writer.Write("; fault ");
        WriteFault(in record);
    }

    private void WriteAction(in DecisionJournalRecord record)
    {
        _writer.Write("service `");
        _writer.Write(record.Service.Value.ToString(CultureInfo.InvariantCulture));
        _writer.Write("` action `");
        _writer.Write(record.ActionOrdinal.ToString(CultureInfo.InvariantCulture));
        _writer.Write("`; cycle `");
        _writer.Write(record.FirstCycle.ToString(CultureInfo.InvariantCulture));
        _writer.Write("`; candidate `");
        _writer.Write(record.Attribution.CandidateId.ToString("D", CultureInfo.InvariantCulture));
        _writer.Write("`; native type `");
        _writer.Write(record.Attribution.NativeType);
        _writer.Write("`; action kind `");
        _writer.Write(record.Attribution.ActionKind.ToString(CultureInfo.InvariantCulture));
        _writer.Write("`; route `");
        _writer.Write(record.Attribution.RouteStatus);
        _writer.Write("`; list/view `");
        _writer.Write(record.Attribution.ListId.ToString("D", CultureInfo.InvariantCulture));
        _writer.Write('/');
        _writer.Write(record.Attribution.ViewId.ToString("D", CultureInfo.InvariantCulture));
        _writer.Write("`; outcome ");
        _writer.Write(record.ActionOutcome.Disposition);
        _writer.Write('/');
        WriteCode(DecisionJournalValueNames.Action(record.ActionOutcome.Code), record.ActionOutcome.Code);
    }

    private void WriteTransition(in DecisionJournalRecord record)
    {
        if (record.Service.IsValid)
        {
            _writer.Write("service `");
            _writer.Write(record.Service.Value.ToString(CultureInfo.InvariantCulture));
            _writer.Write("` ");
        }
        _writer.Write(record.Kind);
        switch (record.Kind)
        {
            case DecisionJournalRecordKind.ConfigurationChanged:
                WriteGeneration(record.Configuration);
                break;
            case DecisionJournalRecordKind.StrategyChanged:
                WriteGeneration(record.Strategy);
                break;
            case DecisionJournalRecordKind.LifecycleChanged:
                WriteGeneration(record.Lifecycle);
                _writer.Write("; state ");
                WriteCode(DecisionJournalValueNames.Lifecycle(record.TransitionCode), record.TransitionCode);
                break;
            case DecisionJournalRecordKind.WorldGateHeld:
                WriteGeneration(record.Lifecycle);
                _writer.Write("; waiting on ");
                WriteCode(DecisionJournalValueNames.WorldGate(record.TransitionCode), record.TransitionCode);
                break;
            case DecisionJournalRecordKind.EmergencyEntered:
            case DecisionJournalRecordKind.EmergencyCleared:
                _writer.Write("; reason ");
                WriteCode(DecisionJournalValueNames.Emergency(record.TransitionCode), record.TransitionCode);
                break;
        }
    }

    private void WriteGeneration(ulong generation)
    {
        _writer.Write("; generation `");
        _writer.Write(generation.ToString(CultureInfo.InvariantCulture));
        _writer.Write('`');
    }

    private void WriteRange(string name, ulong first, ulong last)
    {
        _writer.Write("; ");
        _writer.Write(name);
        _writer.Write(" `");
        if (first == 0)
        {
            _writer.Write("Unavailable");
        }
        else
        {
            _writer.Write(first.ToString(CultureInfo.InvariantCulture));
            if (last != first)
            {
                _writer.Write("..");
                _writer.Write(last.ToString(CultureInfo.InvariantCulture));
            }
        }
        _writer.Write('`');
    }

    private void WriteFault(in DecisionJournalRecord record)
    {
        if (record.FaultCategory == 0)
        {
            _writer.Write("None");
            return;
        }
        _writer.Write(record.FaultCategory);
        _writer.Write('/');
        WriteCode(DecisionJournalValueNames.Action(record.FaultCode), record.FaultCode);
        _writer.Write(" occurrences ");
        _writer.Write(record.FirstFaultOccurrence.ToString(CultureInfo.InvariantCulture));
        if (record.LastFaultOccurrence != record.FirstFaultOccurrence)
        {
            _writer.Write("..");
            _writer.Write(record.LastFaultOccurrence.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void WriteCode(string name, int code)
    {
        _writer.Write(name);
        _writer.Write(" (`");
        _writer.Write(code.ToString(CultureInfo.InvariantCulture));
        _writer.Write("`)");
    }
}
