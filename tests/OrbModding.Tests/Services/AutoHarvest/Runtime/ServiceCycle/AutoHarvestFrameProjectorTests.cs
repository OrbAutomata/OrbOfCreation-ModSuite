using System;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

public sealed class AutoHarvestFrameProjectorTests
{
    [Fact]
    public void UnselectedPairsAreNeitherLookedUpNorReported()
    {
        var frame = AutoHarvestFrameProjector.Project(
            Configuration(fruit: false, treasure: false),
            TestWorlds.Empty);

        Assert.Equal(AutoHarvestPairCaptureKind.NotSelected, frame.Fruit.Kind);
        Assert.Equal(AutoHarvestPairCaptureKind.NotSelected, frame.Treasure.Kind);
    }

    /// <summary>
    /// A world with no plots at all has not been collected yet, and no pair carries facts.
    /// </summary>
    /// <remarks>
    /// Distinct from a world that holds plots but not this pair, which is the test below. Reporting
    /// both as an unverified identity would hide "the game is still loading" behind "that pair does
    /// not exist", and the feature status would tell someone to go looking for a missing asset. It is
    /// the only unavailability the projection reports, and it always reaches the whole feature.
    /// </remarks>
    [Fact]
    public void AnUncollectedWorldIsRegistryNotReadyForTheWholeFeature()
    {
        var frame = AutoHarvestFrameProjector.Project(
            Configuration(fruit: true, treasure: true),
            TestWorlds.Empty);

        AssertUnavailable(frame.Fruit);
        AssertUnavailable(frame.Treasure);
    }

    /// <summary>
    /// A collected world that does not describe the supported pair is projected, and rejected on
    /// identity.
    /// </summary>
    /// <remarks>
    /// The projection succeeds. A world without the pair is not a failure and quarantines nothing — it
    /// is an ordinary fact, and the policy is what declines to act on it.
    /// </remarks>
    [Fact]
    public void APairTheWorldDoesNotHoldIsCapturedWithAnUnverifiedIdentity()
    {
        var frame = AutoHarvestFrameProjector.Project(
            Configuration(fruit: true, treasure: true),
            AutoHarvestTestWorlds.Harvestable(supported: false));

        Assert.Equal(AutoHarvestPairCaptureKind.Captured, frame.Fruit.Kind);
        Assert.Equal(AutoHarvestEvidenceState.Unknown, frame.Fruit.Facts.Identity);
        Assert.Equal(
            AutoHarvestRejectionReason.IdentityUnverified,
            AutoHarvestPolicy.EvaluatePair(
                AutoHarvestPair.FruitTree, selected: true, frame.Fruit.Facts).RejectionReason);
    }

    /// <summary>
    /// Each pair is captured with its own facts, read off the snapshot under its own identity.
    /// </summary>
    /// <remarks>
    /// The pairing test for the one above, and the only one that pins down which identity the
    /// projection looks up. The two pairs are made to differ — the treasure plot is not visible —
    /// because a projection that looked both up as the fruit one would otherwise produce two
    /// identical, correct answers and pass. Without this, a projection keyed on the wrong uuid
    /// entirely would also pass every failure test here, because everything it could find is the same
    /// "not in the world" answer.
    /// </remarks>
    [Fact]
    public void EachPairIsCapturedUnderItsOwnIdentity()
    {
        var frame = AutoHarvestFrameProjector.Project(
            Configuration(fruit: true, treasure: true),
            AutoHarvestTestWorlds.Harvestable(treasureVisible: false));

        Assert.Equal(AutoHarvestPairCaptureKind.Captured, frame.Fruit.Kind);
        Assert.Equal(AutoHarvestEvidenceState.Verified, frame.Fruit.Facts.Identity);
        Assert.Equal(AutoHarvestEvidenceState.Verified, frame.Fruit.Facts.PlotVisibility);
        Assert.Equal(AutoHarvestEvidenceState.Verified, frame.Fruit.Facts.ActionAvailability);
        Assert.Equal(
            PlotActionPrerequisiteEvidence.NativeLatchedTrue,
            frame.Fruit.Facts.Prerequisites);
        Assert.Equal(AutoHarvestEvidenceState.Verified, frame.Fruit.Facts.Readiness);

        Assert.Equal(AutoHarvestPairCaptureKind.Captured, frame.Treasure.Kind);
        Assert.Equal(AutoHarvestEvidenceState.Verified, frame.Treasure.Facts.Identity);
        Assert.Equal(AutoHarvestEvidenceState.Rejected, frame.Treasure.Facts.PlotVisibility);
    }

    /// <summary>
    /// The capture also carries each pair's safety verdict, drawn from the same snapshot as its facts.
    /// </summary>
    /// <remarks>
    /// The two pairs are made to differ again — the fruit action is re-authored to cost two of its
    /// plot — because a projection that audited one pair and copied the answer to the other would
    /// otherwise pass.
    /// </remarks>
    [Fact]
    public void EachPairIsCapturedWithItsOwnSafetyVerdict()
    {
        var frame = AutoHarvestFrameProjector.Project(
            Configuration(fruit: true, treasure: true),
            AutoHarvestTestWorlds.Harvestable(author: (_, action) => action.elementCost = 2));

        Assert.Equal(AutoHarvestActionSafetyState.Destructive, frame.Fruit.Safety);
        Assert.Equal(
            AutoHarvestActionSafetyState.NativePhaseCyclePreserving, frame.Treasure.Safety);
    }

    private static SuiteRuntimeConfiguration Configuration(bool fruit, bool treasure) =>
        AutoHarvestConfigurationFactory.Create(
            masterEnabled: true,
            emergencyDisabled: false,
            activeMode: true,
            fruitSelected: fruit,
            treasureSelected: treasure);

    private static void AssertUnavailable(in AutoHarvestPairCapture capture)
    {
        Assert.Equal(AutoHarvestPairCaptureKind.Unavailable, capture.Kind);
        Assert.Equal(AutoHarvestCaptureUnavailableReason.RegistryNotReady, capture.UnavailableReason);
    }
}
