using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

/// <summary>
/// Pins which health kind each native refusal becomes. The mapping is what decides the sentence the
/// player reads, so a refusal quietly folded back into a neighbour's bucket is a status line that
/// lies without anything failing.
/// </summary>
public sealed class AutoHarvestPairHealthMapperTests
{
    private static AutoHarvestPairHealthKind Health(AutoHarvestRejectionReason reason) =>
        AutoHarvestPairHealthMapper
            .FromDecision(AutoHarvestPair.FruitTree, new AutoHarvestPairDecision(false, reason))
            .Kind;

    /// <summary>
    /// The player's next move differs for each — reach the plot, wait for it to bear again, wait
    /// for the game to evaluate the prerequisite — so they cannot share a sentence, and they cannot
    /// share a sentence while they share a kind.
    /// </summary>
    [Fact]
    public void TheThreeLocksAreThreeDifferentHealthKinds()
    {
        Assert.Equal(
            AutoHarvestPairHealthKind.PlotNotVisible,
            Health(AutoHarvestRejectionReason.PlotNotVisible));
        Assert.Equal(
            AutoHarvestPairHealthKind.ActionNotOffered,
            Health(AutoHarvestRejectionReason.ActionUnavailable));
        Assert.Equal(
            AutoHarvestPairHealthKind.PrerequisitesNotConfirmed,
            Health(AutoHarvestRejectionReason.PrerequisitesNotConfirmed));
    }

    /// <summary>
    /// The unset prerequisite latch does not reach the player as a lock. The game sets it when it
    /// runs a check and passes; it never says whether a check has been run, so reporting an unset one
    /// as progression the player has not done is asserting something nothing in the snapshot knows.
    /// </summary>
    [Fact]
    public void AnUnconfirmedPrerequisiteIsNotReportedAsProgression()
    {
        Assert.NotEqual(
            AutoHarvestPairHealthKind.ProgressionLocked,
            Health(AutoHarvestRejectionReason.PrerequisitesNotConfirmed));
    }

    [Fact]
    public void TheRefusalsThatAlreadyHadTheirOwnKindKeepIt()
    {
        Assert.Equal(AutoHarvestPairHealthKind.NativeBusy, Health(AutoHarvestRejectionReason.NotReady));
        Assert.Equal(
            AutoHarvestPairHealthKind.NativeBusy,
            Health(AutoHarvestRejectionReason.AlreadyQueuedOrRunning));
        Assert.Equal(AutoHarvestPairHealthKind.QueueBlocked, Health(AutoHarvestRejectionReason.NoActionSlot));
        Assert.Equal(
            AutoHarvestPairHealthKind.ContractUnavailable,
            Health(AutoHarvestRejectionReason.DestructiveAction));
        Assert.Equal(
            AutoHarvestPairHealthKind.RegistryNotReady,
            Health(AutoHarvestRejectionReason.PlotVisibilityUnknown));
    }

    /// <summary>
    /// An unreadable term is not a lock. Evidence the collector could not obtain must not be
    /// presented to the player as progression they have not done.
    /// </summary>
    [Fact]
    public void AnUnreadableTermIsNotReportedAsALock()
    {
        Assert.Equal(
            AutoHarvestPairHealthKind.RegistryNotReady,
            Health(AutoHarvestRejectionReason.ActionAvailabilityUnknown));
        Assert.Equal(
            AutoHarvestPairHealthKind.RegistryNotReady,
            Health(AutoHarvestRejectionReason.PrerequisitesUnknown));
    }

    [Fact]
    public void ASubmittableDecisionIsEligible()
    {
        var decision = new AutoHarvestPairDecision(true, AutoHarvestRejectionReason.None);

        var health = AutoHarvestPairHealthMapper.FromDecision(AutoHarvestPair.TreasureTree, decision);

        Assert.Equal(AutoHarvestPairHealthKind.Eligible, health.Kind);
        Assert.True(health.Selected);
    }
}
