using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoHarvestFeatureStatusProjectorTests
{
    [Theory]
    [InlineData((int)AutoHarvestPairHealthKind.ProgressionLocked)]
    [InlineData((int)AutoHarvestPairHealthKind.NativeBusy)]
    [InlineData((int)AutoHarvestPairHealthKind.QueueBlocked)]
    public void EligiblePairKeepsFeatureOperationalWhenSiblingIsOrdinarilyUnavailable(int siblingKind)
    {
        var fruit = AutoHarvestPairHealth.Eligible(AutoHarvestPair.FruitTree);
        var treasure = Selected(AutoHarvestPair.TreasureTree, (AutoHarvestPairHealthKind)siblingKind);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Operational, result.State);
        Assert.Equal(FeatureStatusReasonCode.None, result.Reason);
    }

    [Theory]
    [InlineData((int)AutoHarvestPairHealthKind.ContractUnavailable)]
    [InlineData((int)AutoHarvestPairHealthKind.RegistryNotReady)]
    public void EligiblePairReportsPartialCapabilityWhenSiblingHasFailed(int siblingKind)
    {
        var fruit = AutoHarvestPairHealth.Eligible(AutoHarvestPair.FruitTree);
        var treasure = Selected(AutoHarvestPair.TreasureTree, (AutoHarvestPairHealthKind)siblingKind);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Degraded, result.State);
        Assert.Equal(FeatureStatusReasonCode.PartialCapabilityUnavailable, result.Reason);
    }

    [Fact]
    public void UnlockedWaitingPairBeatsLockedSibling()
    {
        var fruit = Selected(AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.NativeBusy);
        var treasure = Selected(AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.ProgressionLocked);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal(FeatureStatusReasonCode.NativeBusy, result.Reason);
    }

    [Fact]
    public void AllSelectedLockedReportsLocked()
    {
        var fruit = Selected(AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.ProgressionLocked);
        var treasure = Selected(AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.ProgressionLocked);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Locked, result.State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, result.Reason);
    }

    [Fact]
    public void UnselectedSiblingDoesNotAffectSelectedLockedPair()
    {
        var fruit = Selected(AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.ProgressionLocked);
        var treasure = AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Locked, result.State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, result.Reason);
    }

    /// <summary>
    /// A plot that is simply bare this minute is not a plot the player has yet to unlock, and telling
    /// them the feature is Locked tells them to go and do something that would change nothing.
    /// </summary>
    [Fact]
    public void APlotThatIsNotOfferingItsActionIsBlockedRatherThanLocked()
    {
        var fruit = Selected(AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.ActionNotOffered);
        var treasure = Selected(AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.ProgressionLocked);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, result.State);
        Assert.Equal(FeatureStatusReasonCode.NativeBusy, result.Reason);
    }

    /// <summary>
    /// A waiting pair beside a failed one still reports the failure, because a pair that is merely
    /// waiting is a working pair and "one of two is broken" is the honest summary.
    /// </summary>
    [Fact]
    public void AFailedSiblingIsStillReportedBesideAPlotThatIsNotOffering()
    {
        var fruit = Selected(AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.ActionNotOffered);
        var treasure = Selected(AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.ContractUnavailable);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Degraded, result.State);
        Assert.Equal(FeatureStatusReasonCode.PartialCapabilityUnavailable, result.Reason);
    }

    [Fact]
    public void APlotTheGameHasNotRevealedIsStillLocked()
    {
        var fruit = Selected(AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.PlotNotVisible);
        var treasure = Selected(AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.PlotNotVisible);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Locked, result.State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, result.Reason);
    }

    /// <summary>
    /// A prerequisite the game has not evaluated is not progression the player owes. The latch it is
    /// read from is set when a check passes and says nothing about whether one has been run, so Locked
    /// would be the feature asserting something no reading supports.
    /// </summary>
    [Fact]
    public void AnUnconfirmedPrerequisiteWaitsRatherThanLocks()
    {
        var fruit = Selected(
            AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.PrerequisitesNotConfirmed);
        var treasure = Selected(
            AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.PrerequisitesNotConfirmed);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.NotReady, result.State);
        Assert.Equal(FeatureStatusReasonCode.GameplayNotReady, result.Reason);
    }

    /// <summary>
    /// A pair the game has not ruled on is neither working nor broken, so it neither masks a failed
    /// sibling nor counts as one. The failure is what the summary has to name.
    /// </summary>
    [Fact]
    public void AFailedSiblingIsStillReportedBesideAnUnconfirmedPrerequisite()
    {
        var fruit = Selected(
            AutoHarvestPair.FruitTree, AutoHarvestPairHealthKind.PrerequisitesNotConfirmed);
        var treasure = Selected(AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.ContractUnavailable);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.ContractUnavailable, result.State);
        Assert.Equal(FeatureStatusReasonCode.ContractUnavailable, result.Reason);
    }

    /// <summary>
    /// An eligible sibling still runs. Waiting on one pair's prerequisites is not a reason to report
    /// the feature as anything but working.
    /// </summary>
    [Fact]
    public void AnEligibleSiblingKeepsTheFeatureOperational()
    {
        var fruit = AutoHarvestPairHealth.Eligible(AutoHarvestPair.FruitTree);
        var treasure = Selected(
            AutoHarvestPair.TreasureTree, AutoHarvestPairHealthKind.PrerequisitesNotConfirmed);

        var result = AutoHarvestFeatureStatusProjector.Project(fruit, treasure);

        Assert.Equal(FeatureStatusState.Operational, result.State);
        Assert.Equal(FeatureStatusReasonCode.None, result.Reason);
    }

    private static AutoHarvestPairHealth Selected(
        AutoHarvestPair pair,
        AutoHarvestPairHealthKind kind) =>
        new(pair, selected: true, kind);
}
