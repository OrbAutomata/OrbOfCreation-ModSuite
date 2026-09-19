using System;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// The verifier is the thing that decides whether the ported math is trustworthy, so its own
/// judgement has to be right: it must not pass a real disagreement, and must not fail on last-bit
/// noise.
/// </summary>
public sealed class DifferentialVerificationTests
{
    private static readonly Guid Structure = new("182ce873-3b20-4e74-8c5f-07f057666871");
    private static readonly Guid Water = new("eab888ff-d8bd-4e46-81eb-639d5d562242");

    [Fact]
    public void IdenticalValuesAreExact()
    {
        Assert.Equal(
            DifferentialOutcome.Exact,
            DifferentialRun.Classify(new BigDouble(1234d), new BigDouble(1234d)));
    }

    [Fact]
    public void LastBitNoiseIsToleratedRatherThanReportedAsFailure()
    {
        // A differing operation order can move the final bit. That is worth counting, not alarming.
        var ours = new BigDouble(1d, 30);
        var theirs = new BigDouble(1.0000000000001d, 30);

        Assert.Equal(DifferentialOutcome.Close, DifferentialRun.Classify(ours, theirs));
    }

    [Fact]
    public void ARealFormulaErrorIsCaught()
    {
        // A one-percent error is small enough to look plausible in game and must still fail.
        Assert.Equal(
            DifferentialOutcome.Mismatch,
            DifferentialRun.Classify(new BigDouble(100d), new BigDouble(101d)));

        // And it must be caught at astronomical magnitudes too, which is why the comparison is
        // relative rather than absolute.
        Assert.Equal(
            DifferentialOutcome.Mismatch,
            DifferentialRun.Classify(new BigDouble(1d, 30), new BigDouble(1.01d, 30)));
    }

    [Fact]
    public void ToleranceIsRelativeSoLargeMagnitudesAreNotWavedThrough()
    {
        // An absolute epsilon would treat a 1e18 discrepancy at 1e30 as negligible. It is not:
        // relative to scale it is 1e-12 and sits right at the boundary, while ten times that must
        // fail. This pins that the check does not get weaker as numbers grow.
        Assert.Equal(
            DifferentialOutcome.Mismatch,
            DifferentialRun.Classify(new BigDouble(1d, 30), new BigDouble(1.00000000001d, 30)));
    }

    [Fact]
    public void NonFiniteValuesOnlyOneSideNeverPass()
    {
        Assert.Equal(
            DifferentialOutcome.NotComparable,
            DifferentialRun.Classify(BigDouble.NaN, new BigDouble(5d)));
        Assert.Equal(
            DifferentialOutcome.NotComparable,
            DifferentialRun.Classify(new BigDouble(5d), BigDouble.PositiveInfinity));

        // Agreeing about an edge case is agreement.
        Assert.Equal(
            DifferentialOutcome.Exact,
            DifferentialRun.Classify(BigDouble.NaN, BigDouble.NaN));
    }

    [Fact]
    public void ARunTalliesOutcomesAndFailsOnlyOnRealDisagreement()
    {
        var run = new DifferentialRun();

        run.Compare(Structure, Water.ToString(), new BigDouble(100d), new BigDouble(100d));
        run.Compare(Structure, Water.ToString(), new BigDouble(1d, 30), new BigDouble(1.0000000000001d, 30));
        Assert.True(run.Passed);
        Assert.Equal(1, run.ExactCount);
        Assert.Equal(1, run.CloseCount);

        run.Compare(Structure, Water.ToString(), new BigDouble(100d), new BigDouble(150d));
        Assert.False(run.Passed);
        Assert.Equal(1, run.MismatchCount);
        Assert.Equal(3, run.Compared);
    }

    [Fact]
    public void RecordedFailuresAreCappedButTheCountIsNot()
    {
        // A systematically wrong port would disagree on every entity. The tally must stay accurate
        // while the retained detail stays bounded, so a bad run reports honestly instead of
        // exhausting memory.
        var run = new DifferentialRun(sampleLimit: 2);

        for (var index = 0; index < 50; index++)
        {
            run.Compare(Structure, Water.ToString(), new BigDouble(index + 1), new BigDouble((index + 1) * 2));
        }

        Assert.Equal(50, run.MismatchCount);
        Assert.Equal(2, run.Failures.Count);
        Assert.Equal(
            "… 48 further disagreements not recorded.",
            run.Finding().Detail[run.Finding().Detail.Count - 1]);
    }

    [Fact]
    public void AMerelyCloseComparisonIsCountedAndNeverGivenARow()
    {
        // Twenty rows a call printed two byte-identical numbers behind two full UUIDs. Close is
        // agreement; what survives it is the count, which is the early warning worth keeping.
        var run = new DifferentialRun();
        run.Compare(Structure, Water.ToString(), new BigDouble(1d, 30), new BigDouble(1.0000000000001d, 30));

        var finding = run.Finding();

        Assert.Equal(VerificationVerdict.Agree, finding.Verdict);
        Assert.Equal(1, finding.WithinTolerance);
        Assert.Empty(finding.Detail);
        Assert.Empty(run.Failures);
    }

    [Fact]
    public void TheFindingStatesAgreementOrDisagreementInOneVocabulary()
    {
        var agreeing = new DifferentialRun();
        agreeing.Compare(Structure, Water.ToString(), new BigDouble(10d), new BigDouble(10d));
        Assert.Equal("Game math AGREE: 1 compared.", agreeing.Finding().Headline());

        var disagreeing = new DifferentialRun();
        disagreeing.Compare(Structure, Water.ToString(), new BigDouble(10d), new BigDouble(20d));
        Assert.Equal(
            "Game math DISAGREE: 1 compared, 0 agree, 1 differ.",
            disagreeing.Finding().Headline());

        // An empty run must not read as agreement — nothing verified is not the same as verified.
        Assert.Equal(
            VerificationVerdict.Inconclusive, new DifferentialRun().Finding().Verdict);
    }
}
