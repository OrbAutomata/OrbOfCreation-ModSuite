using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// The shape of the answer the game math check gives. The verb exists to say whether the suite and
/// the game agree, so the response is pinned on saying it first, saying it once, and saying nothing
/// else when they do.
/// </summary>
public sealed class VerificationReportTests
{
    private const string Window =
        "generation=3 frame=48213 entities=6683 categories=60 collect=41.213ms " +
        "ported=118.4ms native=2249.1ms elapsed=2407.741ms memos=5677 drifted=730 dirty=3558 " +
        "uncalculated=612 widestDrift=2.46e121%@StructureSO.passiveCostMod";

    [Fact]
    public void An_all_agree_run_is_the_verdict_word_the_count_and_the_window()
    {
        var report = new VerificationReport();
        report.Add(VerificationFinding.Agree("Accessor parity", 1253));
        report.Add(VerificationFinding.Agree("Affordability parity", 409));
        report.Add(VerificationFinding.Agree("Published cost", 522));

        Assert.Equal(
            new[]
            {
                "AGREE — 2184 facts compared, 2184 agree, 0 differ.",
                "window: " + Window,
            },
            report.Render(Window));
    }

    [Fact]
    public void Only_the_checks_that_did_not_agree_render()
    {
        var report = new VerificationReport();
        report.Add(VerificationFinding.Agree("Accessor parity", 1253));
        report.Add(VerificationFinding.Disagree(
            "Published cost",
            522,
            2,
            new[]
            {
                "worst Stone Hut (00246c) water: ours=7.46e290 theirs=8.11e290",
                "  costScalingMod      ours=theirs=1.15e3",
            }));
        report.Add(VerificationFinding.Agree("Affordability parity", 409));

        Assert.Equal(
            new[]
            {
                "DISAGREE — 2184 facts compared, 2182 agree, 2 differ.",
                "Published cost DISAGREE: 522 compared, 520 agree, 2 differ.",
                "  worst Stone Hut (00246c) water: ours=7.46e290 theirs=8.11e290",
                "    costScalingMod      ours=theirs=1.15e3",
                "window: " + Window,
            },
            report.Render(Window));
    }

    [Fact]
    public void A_check_that_compared_nothing_is_counted_and_named_rather_than_read_as_agreement()
    {
        var report = new VerificationReport();
        report.Add(VerificationFinding.Agree("Accessor parity", 1253));
        report.Add(VerificationFinding.Inconclusive(
            "Exclusion parity", "no entity exposed both of the game's gates."));

        Assert.Equal(
            new[]
            {
                "INCOMPLETE — 1253 facts compared, 1253 agree, 0 differ. 1 check compared nothing.",
                "Exclusion parity INCONCLUSIVE: no entity exposed both of the game's gates.",
                "window: " + Window,
            },
            report.Render(Window));
    }

    [Fact]
    public void A_run_that_compared_nothing_at_all_is_inconclusive_rather_than_agreement()
    {
        var report = new VerificationReport();
        report.Add(VerificationFinding.Inconclusive(
            "World collection",
            "it faulted before it could compare anything, so nothing here is a verdict about the " +
            "game (NullReferenceException)."));

        Assert.Equal(VerificationVerdict.Inconclusive, report.Verdict);
        Assert.Equal(
            "INCONCLUSIVE — 0 facts compared, 0 agree, 0 differ. 1 check compared nothing.",
            report.Render(Window)[0]);
    }

    [Fact]
    public void Agreement_reached_only_within_tolerance_survives_as_a_count_not_as_rows()
    {
        var report = new VerificationReport();
        report.Add(VerificationFinding.Agree("Cost", 441, withinTolerance: 20));

        Assert.Equal(
            new[]
            {
                "AGREE — 441 facts compared, 441 agree, 0 differ. 20 agree only within tolerance.",
                "window: " + Window,
            },
            report.Render(Window));
    }

    [Fact]
    public void One_genuine_disagreement_outranks_every_gap_in_coverage()
    {
        var report = new VerificationReport();
        report.Add(VerificationFinding.Incomplete(
            "Category binding", 60, "2 categories did not bind against this build"));
        report.Add(VerificationFinding.Disagree("Rate", 640, 1, "first Water: ours=1e3 theirs=2e3"));

        Assert.Equal(VerificationVerdict.Disagree, report.Verdict);
    }

    [Fact]
    public void Short_coverage_outranks_agreement_because_a_pass_over_a_subset_is_not_a_pass()
    {
        var report = new VerificationReport();
        report.Add(VerificationFinding.Agree("Rate", 640));
        report.Add(VerificationFinding.Incomplete(
            "Cost", 500, "22 of 522 entities could not be read — the cost contract was unavailable"));

        Assert.Equal(VerificationVerdict.Incomplete, report.Verdict);
        Assert.Equal(
            new[]
            {
                "INCOMPLETE — 1140 facts compared, 1140 agree, 0 differ.",
                "Cost INCOMPLETE: 500 compared, all agree — 22 of 522 entities could not be read — " +
                "the cost contract was unavailable",
                "window: " + Window,
            },
            report.Render(Window));
    }
}
