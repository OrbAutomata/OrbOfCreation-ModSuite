using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Diagnostics;

public sealed class AutoConceptFeatureStatusProjectorTests
{
    [Fact]
    public void OwnershipLossIsReportedBeforeFirstCycleReadiness()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: false,
            cycleObserved: false);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Reason);
    }

    [Fact]
    public void AnObservedOwnedCycleIsOperational()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true);

        Assert.Equal(FeatureStatusState.Operational, status.State);
        Assert.Equal(FeatureStatusReasonCode.None, status.Reason);
        Assert.Equal(string.Empty, status.Summary);
    }

    [Fact]
    public void TrainingWaitIsHighlightedWithoutTreatingItAsAFailure()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true,
            idle: new AutoConceptIdle(AutoConceptIdleReason.WaitingForTraining));

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.NativeBusy, status.Reason);
        Assert.Contains("training period", status.Summary);
    }

    [Fact]
    public void MissingUnlockedAssignableReplacementIsProgressionLocked()
    {
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true,
            idle: new AutoConceptIdle(AutoConceptIdleReason.NoUnlockedAssignableReplacement));

        Assert.Equal(FeatureStatusState.Locked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, status.Reason);
        Assert.Equal(
            "No other unlocked concept can be assigned.",
            status.Summary);
    }

    [Fact]
    public void AResourceHeldMoveSaysWhatItWouldDoAndWhy()
    {
        var recipe = System.Guid.Parse("11111111-1111-1111-1111-111111111111");
        var resource = System.Guid.Parse("22222222-2222-2222-2222-222222222222");
        var status = AutoConceptFeatureStatusProjector.Project(
            emergencyDisabled: false,
            owned: true,
            cycleObserved: true,
            idle: new AutoConceptIdle(
                AutoConceptIdleReason.RateReserve,
                AutoConceptIdleMove.Slot,
                recipe,
                System.Guid.Empty,
                resource));

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.TemporarySafetyBlock, status.Reason);
        Assert.Equal(
            "Auto Concept would slot " + recipe.ToString("D") + ", but its drain would leave " +
            resource.ToString("D") + " less than AutoConcept/RateReservePercent of its income.",
            status.Summary);
    }
}
