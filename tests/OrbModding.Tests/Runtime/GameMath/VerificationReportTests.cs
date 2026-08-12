using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// The shape of the answer the game math check gives. The verb exists to say whether the suite and
/// the game agree, so the response is pinned on saying it first, saying it once, and then giving one
/// line per check — the agreeing ones included, so that a check which ran and a check which is
/// missing cannot read alike.
/// </summary>
public sealed class VerificationReportTests
{
    private const string Window =
        "generation=3 frame=48213 entities=6683 categories=60 collect=41.213ms " +
        "ported=118.4ms native=2249.1ms elapsed=2407.741ms memos=5677 drifted=730 dirty=3558 " +
        "uncalculated=612 widestDrift=2.46e121%@StructureSO.passiveCostMod";

    [Fact]
    public void An_all_agree_run_is_the_verdict_word_then_one_line_for_every_check_that_ran()
    {
        var report = new VerificationReport();
        report.Add(VerificationFinding.Agree("Accessor parity", 1253));
        report.Add(VerificationFinding.Agree("Affordability parity", 409));
        report.Add(VerificationFinding.Agree("Published cost", 522));

        Assert.Equal(
            new[]
            {
                "AGREE — 2184 facts compared, 2184 agree, 0 differ.",
                "Accessor parity AGREE: 1253 compared.",
                "Affordability parity AGREE: 409 compared.",
                "Published cost AGREE: 522 compared.",
                "window: " + Window,
            },
            report.Render(Window));
    }

    /// <summary>
    /// A check that ran and agreed and a check that never ran were the same silence, which is how a
    /// round could not establish whether one of the passes had run at all.
    /// </summary>
    [Fact]
    public void A_check_that_agreed_and_a_check_that_is_absent_no_longer_read_alike()
    {
        var ran = new VerificationReport();
        ran.Add(VerificationFinding.Agree("Accessor parity", 1253));
        ran.Add(VerificationFinding.Agree("Spell type layer", 6));

        var absent = new VerificationReport();
        absent.Add(VerificationFinding.Agree("Accessor parity", 1253));

        Assert.NotEqual(ran.Render(Window), absent.Render(Window));
        Assert.Contains("Spell type layer AGREE: 6 compared.", ran.Render(Window));
    }

    /// <summary>
    /// A check whose agreement rests on more than one count says which counts, in its own words, and
    /// may publish a fact beside its verdict without that fact being scored as one.
    /// </summary>
    [Fact]
    public void A_check_may_state_its_own_counts_and_one_fact_it_does_not_score()
    {
        var report = new VerificationReport();
        report.Add(VerificationFinding
            .Agree("Identities", 3323, counts: "3323 compared, 0 empty, 0 repeated within a table.")
            .WithNote("Shared identities: 1934 entities, 1389 detail rows filed under one of them."));

        Assert.Equal(VerificationVerdict.Agree, report.Verdict);
        Assert.Equal(
            new[]
            {
                "AGREE — 3323 facts compared, 3323 agree, 0 differ.",
                "Identities AGREE: 3323 compared, 0 empty, 0 repeated within a table.",
                "Shared identities: 1934 entities, 1389 detail rows filed under one of them.",
                "window: " + Window,
            },
            report.Render(Window));
    }

    [Fact]
    public void A_disagreement_renders_its_rows_and_the_agreeing_checks_still_render_their_line()
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
                "Accessor parity AGREE: 1253 compared.",
                "Published cost DISAGREE: 522 compared, 520 agree, 2 differ.",
                "  worst Stone Hut (00246c) water: ours=7.46e290 theirs=8.11e290",
                "    costScalingMod      ours=theirs=1.15e3",
                "Affordability parity AGREE: 409 compared.",
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
                "Accessor parity AGREE: 1253 compared.",
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
                "Cost AGREE: 441 compared.",
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
                "Rate AGREE: 640 compared.",
                "Cost INCOMPLETE: 500 compared, all agree — 22 of 522 entities could not be read — " +
                "the cost contract was unavailable",
                "window: " + Window,
            },
            report.Render(Window));
    }
}
