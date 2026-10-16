using System;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMath;

/// <summary>
/// The session turns verification into a bounded, on-demand action with one verdict. Its job is to
/// terminate reliably and to never overstate what it actually checked.
/// </summary>
public sealed class DifferentialVerificationSessionTests
{
    private static readonly Guid Entity = new("182ce873-3b20-4e74-8c5f-07f057666871");
    private static readonly Guid Resource = new("eab888ff-d8bd-4e46-81eb-639d5d562242");

    [Fact]
    public void ARunStopsAfterItsTickBudget()
    {
        var session = new DifferentialVerificationSession(tickBudget: 3, entityBudget: 1000);
        session.Start();

        var ticks = 0;
        while (session.WantsMoreWork())
        {
            session.RecordVerified();
            session.EndTick();
            ticks++;
            Assert.True(ticks <= 3, "the session must not run past its tick budget");
        }

        Assert.Equal(3, ticks);
    }

    [Fact]
    public void ARunStopsAfterItsEntityBudgetEvenWithTicksRemaining()
    {
        // Termination must not depend on the entity source running dry.
        var session = new DifferentialVerificationSession(tickBudget: 100, entityBudget: 5);
        session.Start();

        var verified = 0;
        while (session.WantsMoreWork())
        {
            while (session.HasEntityBudget())
            {
                session.RecordVerified();
                verified++;
                Assert.True(verified <= 5, "the session must not verify past its entity budget");
            }

            session.EndTick();
        }

        Assert.Equal(5, verified);
    }

    [Fact]
    public void AgreementAcrossEveryEntityReportsAnAgreement()
    {
        var session = new DifferentialVerificationSession();
        session.Start();
        session.Run.Compare(Entity, Resource.ToString(), new BigDouble(100d), new BigDouble(100d));
        session.RecordVerified();
        session.EndTick();

        var verdict = session.Complete();

        Assert.Equal(VerificationVerdict.Agree, verdict.Verdict);
        Assert.Equal("Game math AGREE: 1 compared.", verdict.Headline());
        Assert.Empty(verdict.Detail);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void ADisagreementReportsADisagreement()
    {
        var session = new DifferentialVerificationSession();
        session.Start();
        session.Run.Compare(Entity, Resource.ToString(), new BigDouble(100d), new BigDouble(150d));
        session.RecordVerified();
        session.EndTick();

        var verdict = session.Complete();

        Assert.Equal(VerificationVerdict.Disagree, verdict.Verdict);
        Assert.Equal(
            "Game math DISAGREE: 1 compared, 0 agree, 1 differ.", verdict.Headline());
        Assert.Equal(
            $"Mismatch: entity {Entity} [{Resource}] ours=100 theirs=150",
            Assert.Single(verdict.Detail));
    }

    [Fact]
    public void VerifyingNothingIsInconclusiveRatherThanSuccessful()
    {
        // The failure mode that would make the whole exercise worthless: a run that read nothing
        // and cheerfully reported a pass.
        var session = new DifferentialVerificationSession();
        session.Start();
        session.EndTick();

        var verdict = session.Complete();

        Assert.Equal(VerificationVerdict.Inconclusive, verdict.Verdict);
        Assert.Equal(
            "Game math INCONCLUSIVE: nothing could be verified — no entities were available to " +
            "check.",
            verdict.Headline());
        Assert.Equal(0, verdict.Compared);
    }

    [Fact]
    public void UnreadableEntitiesDowngradeAnOtherwiseCleanPass()
    {
        // Everything readable agreed, but coverage was incomplete. Reporting a clean pass here
        // would overstate what was checked.
        var session = new DifferentialVerificationSession();
        session.Start();
        session.Run.Compare(Entity, Resource.ToString(), new BigDouble(10d), new BigDouble(10d));
        session.RecordVerified();
        session.RecordUnverifiable("the cost contract was unavailable");
        session.EndTick();

        var verdict = session.Complete();

        Assert.Equal(VerificationVerdict.Incomplete, verdict.Verdict);
        Assert.Equal(
            "Game math INCOMPLETE: 1 compared, all agree — 1 of 2 entities could not be read — " +
            "the cost contract was unavailable",
            verdict.Headline());
    }

    [Fact]
    public void AnExpectedSkipIsNotAGapInCoverageAndDoesNotDowngradeAnAgreement()
    {
        // A skip the pass declared expected is not an entity that could not be read, so it neither
        // downgrades the verdict nor earns a line: an agreement renders as a count, not as rows.
        var session = new DifferentialVerificationSession("Concept drain");
        session.Start();
        session.Run.Compare(Entity, Resource.ToString(), new BigDouble(10d), new BigDouble(10d));
        session.RecordVerified();
        session.RecordExpectedSkip();
        session.RecordVerified();
        session.EndTick();

        var verdict = session.Complete();

        Assert.Equal(VerificationVerdict.Agree, verdict.Verdict);
        Assert.Equal("Concept drain AGREE: 1 compared.", verdict.Headline());
        Assert.Equal(1, session.ExpectedSkips);
    }

    [Fact]
    public void ARealDisagreementOutranksUnreadableEntities()
    {
        // When both are present the verdict must lead with the disagreement: a wrong port is a
        // worse problem than incomplete coverage.
        var session = new DifferentialVerificationSession();
        session.Start();
        session.Run.Compare(Entity, Resource.ToString(), new BigDouble(10d), new BigDouble(99d));
        session.RecordVerified();
        session.RecordUnverifiable("unreadable");
        session.EndTick();

        Assert.Equal(VerificationVerdict.Disagree, session.Complete().Verdict);
    }

    [Fact]
    public void ASessionIsNotRunningUntilStarted()
    {
        var session = new DifferentialVerificationSession();

        Assert.False(session.IsRunning);
        Assert.False(session.WantsMoreWork());
    }
}
