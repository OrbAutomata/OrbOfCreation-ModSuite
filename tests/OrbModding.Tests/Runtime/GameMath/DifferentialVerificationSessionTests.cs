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
    public void AgreementAcrossEveryEntityReportsAPass()
    {
        var session = new DifferentialVerificationSession();
        session.Start();
        session.Run.Compare(Entity, Resource.ToString(), new BigDouble(100d), new BigDouble(100d));
        session.RecordVerified();
        session.EndTick();

        var verdict = session.Complete();

        Assert.Contains("PASSED", verdict, StringComparison.Ordinal);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void ADisagreementReportsAFailure()
    {
        var session = new DifferentialVerificationSession();
        session.Start();
        session.Run.Compare(Entity, Resource.ToString(), new BigDouble(100d), new BigDouble(150d));
        session.RecordVerified();
        session.EndTick();

        Assert.Contains("FAILED", session.Complete(), StringComparison.Ordinal);
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

        Assert.Contains("INCONCLUSIVE", verdict, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSED", verdict, StringComparison.Ordinal);
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

        Assert.Contains("INCOMPLETE", verdict, StringComparison.Ordinal);
        Assert.Contains("the cost contract was unavailable", verdict, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSED", verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedSkipsStayVisibleWithoutDowngradingAPass()
    {
        var session = new DifferentialVerificationSession("Concept drain");
        session.Start();
        session.Run.Compare(Entity, Resource.ToString(), new BigDouble(10d), new BigDouble(10d));
        session.RecordVerified();
        session.RecordExpectedSkip();
        session.RecordVerified();
        session.EndTick();

        var verdict = session.Complete();

        Assert.Contains("PASSED", verdict, StringComparison.Ordinal);
        Assert.Contains("[1 entities, 1 expected skips]", verdict, StringComparison.Ordinal);
        Assert.DoesNotContain("INCOMPLETE", verdict, StringComparison.Ordinal);
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

        Assert.Contains("FAILED", session.Complete(), StringComparison.Ordinal);
    }

    [Fact]
    public void ASessionIsNotRunningUntilStarted()
    {
        var session = new DifferentialVerificationSession();

        Assert.False(session.IsRunning);
        Assert.False(session.WantsMoreWork());
    }
}
