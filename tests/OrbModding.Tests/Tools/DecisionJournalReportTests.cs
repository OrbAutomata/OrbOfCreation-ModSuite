using System;
using System.IO;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.Journal;
using OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal.DecisionJournalTestData;

namespace OrbModding.Tests.Tools;

public sealed class DecisionJournalReportTests
{
    [Fact]
    public void ReportRendersRetainedNumericEvidenceWithoutInventingUnavailableFacts()
    {
        using var fixture = new DecisionJournalTestDirectory();
        var decision = DecisionJournalRecord.Decision(CreateObservation(5, 100));
        var transition = DecisionJournalRecord.Transition(
            DecisionJournalRecordKind.ConfigurationChanged,
            default,
            generation: 2,
            new MonotonicTimestamp(120));
        fixture.WriteSegment(3, run: 11, firstSequence: 5, decision, transition);

        using var report = DecisionJournalReportReader.Read(fixture.Root);
        using var output = new StringWriter();
        DecisionJournalReport.Write(output, report);
        var text = output.ToString();

        Assert.Contains("Scope: Validated retained durable window", text);
        Assert.Contains("3 ordinal positions precede this window", text);
        Assert.Contains("Sequences 1..4 absent", text);
        Assert.Contains("Writer terminal state: Unavailable", text);
        Assert.Contains("Format: OSJD wire schema 3", text);
        Assert.Contains("## Numeric run/service view", text);
        Assert.Contains("| `000000000000000b` | 1 | 1 / 1 / 1 | 1 | 0 / 0 / 0 / 0 / 0 |", text);
        Assert.Contains("## Retained record lineage", text);
        Assert.Contains("`#5`", text);
        Assert.Contains("ConfigurationChanged; generation `2`", text);
        Assert.Contains("Suite-wide configuration / strategy publications: 1 / 0", text);
        Assert.Contains("omits empty pumps", text);
        Assert.DoesNotContain("Auto Harvest", text);
        Assert.DoesNotContain(fixture.Root, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderIgnoresOnlyExactOwnedTemporaryNamesAndRejectsInteriorGaps()
    {
        using var fixture = new DecisionJournalTestDirectory();
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        fixture.WriteSegment(1, run: 11, firstSequence: 1, record);
        fixture.WriteSegment(3, run: 11, firstSequence: 2, record);
        File.WriteAllBytes(
            fixture.SegmentPath(2) + ".tmp-" + new string('a', 32),
            new byte[] { 1 });

        var error = Assert.Throws<InvalidDataException>(() =>
            DecisionJournalReportReader.Read(fixture.Root));

        Assert.Contains("contiguous storage-ordinal suffix", error.Message);
    }

    [Fact]
    public void ReportPathCannotReplaceJournalEvidence()
    {
        using var fixture = new DecisionJournalTestDirectory();
        var path = fixture.WriteSegment(
            0,
            run: 11,
            firstSequence: 1,
            DecisionJournalRecord.Decision(CreateObservation(1, 10)));
        using var report = DecisionJournalReportReader.Read(fixture.Root);

        Assert.Throws<InvalidOperationException>(() => report.EnsureSafeReportPath(path));
        Assert.Throws<InvalidOperationException>(() => report.EnsureSafeReportPath(
            Path.Combine(Path.GetTempPath(), "journal-000001.osjd")));
        report.EnsureSafeReportPath(Path.Combine(fixture.Root, "report.md"));
    }

    [Fact]
    public void ARefusedReportNameSaysWhichNamesAreReserved()
    {
        using var fixture = new DecisionJournalTestDirectory();
        fixture.WriteSegment(
            0,
            run: 11,
            firstSequence: 1,
            DecisionJournalRecord.Decision(CreateObservation(1, 10)));
        using var report = DecisionJournalReportReader.Read(fixture.Root);

        var error = Assert.Throws<InvalidOperationException>(() => report.EnsureSafeReportPath(
            Path.Combine(Path.GetTempPath(), "journal-findings.md")));

        Assert.Equal(
            "A report cannot be named journal-findings.md: names starting with journal- or ending " +
            "with .osjd are reserved for decision-journal evidence.",
            error.Message);
    }

    [Fact]
    public void ReportKeepsTheSameNumericServiceSeparateAcrossRuns()
    {
        using var fixture = new DecisionJournalTestDirectory();
        var record = DecisionJournalRecord.Decision(CreateObservation(1, 10));
        fixture.WriteSegment(0, run: 11, firstSequence: 1, record);
        fixture.WriteSegment(1, run: 12, firstSequence: 1, record);

        using var report = DecisionJournalReportReader.Read(fixture.Root);
        using var output = new StringWriter();
        DecisionJournalReport.Write(output, report);

        Assert.Contains("| `000000000000000b` | 1 |", output.ToString());
        Assert.Contains("| `000000000000000c` | 1 |", output.ToString());
    }
}
