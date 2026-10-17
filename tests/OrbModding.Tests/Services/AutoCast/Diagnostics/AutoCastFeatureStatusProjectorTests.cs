using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Diagnostics;

/// <summary>
/// What Auto Cast's running service says about runtime health. Saved intent is joined centrally.
/// </summary>
public sealed class AutoCastFeatureStatusProjectorTests : IDisposable
{
    public AutoCastFeatureStatusProjectorTests() =>
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            new EntityIdentityName(ChannelSpark, "SpellRecipeSO", "Channel Spark", "ChannelSpark"),
            new EntityIdentityName(Firebolt, "SpellRecipeSO", "Firebolt", "Firebolt"),
        }));

    public void Dispose() =>
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Unbound(1));

    [Fact]
    public void TheEmergencyStopOutranksWhoOwnsTheActionFamily()
    {
        // A suite that has stood down entirely is a bigger fact than which plugin holds a lease.
        var status = Project(emergencyDisabled: true, owned: false, manualPaused: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.EmergencyDisabled, status.Reason);
        Assert.Equal(AutoCastFeatureStatusProjector.EmergencyDisabledSummary, status.Summary);
    }

    [Fact]
    public void AnotherPluginOwningSpellCastingIsReportedAsSuch()
    {
        var status = Project(owned: false, manualPaused: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, status.Reason);
    }

    [Fact]
    public void TheManualPauseIsTheLastBlockingTermBecauseItIsTheOneThatResolvesItself()
    {
        var status = Project(manualPaused: true, cycleObserved: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.ManualPause, status.Reason);
        Assert.Equal(AutoCastFeatureStatusProjector.ManualPauseSummary, status.Summary);
    }

    [Fact]
    public void AServiceThatHasNotRunYetSaysSoRatherThanClaimingToBeWorking()
    {
        var status = Project(cycleObserved: false);

        Assert.Equal(FeatureStatusState.NotReady, status.State);
        Assert.Equal(FeatureStatusReasonCode.GameplayNotReady, status.Reason);
    }

    [Fact]
    public void EverythingClearAndACycleObservedReadsOperational()
    {
        var status = Project(cycleObserved: true);

        Assert.Equal(FeatureStatusState.Operational, status.State);
        Assert.Equal(FeatureStatusReasonCode.None, status.Reason);
    }

    [Fact]
    public void AnAttackSpellHeldOutsideARitualIsNamedWithItsReason()
    {
        var status = AutoCastFeatureStatusProjector.Project(
            false, true, false, true, AutoCastExclusion.OutsideRitual, Firebolt);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.TemporarySafetyBlock, status.Reason);
        Assert.Equal(
            "Auto Cast holds Firebolt: no ritual is running. AutoCast/RitualAware turns this off.",
            status.Summary);
    }

    [Fact]
    public void AChannelHeldDuringARitualIsNamedWithItsReason()
    {
        var status = AutoCastFeatureStatusProjector.Project(
            false, true, false, true, AutoCastExclusion.RitualChannel, ChannelSpark);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(
            "Auto Cast holds Channel Spark: a channel would block attack spells during the ritual. " +
            "AutoCast/RitualAware turns this off.",
            status.Summary);
    }

    [Fact]
    public void TheManualPauseOutranksARitualHold()
    {
        var status = AutoCastFeatureStatusProjector.Project(
            false, true, true, true, AutoCastExclusion.OutsideRitual, Firebolt);

        Assert.Equal(FeatureStatusReasonCode.ManualPause, status.Reason);
    }

    private static readonly Guid Firebolt = Guid.Parse("db0694f1-82bf-4ecf-a139-1341efee2bb8");
    private static readonly Guid ChannelSpark = Guid.Parse("47e1359f-ce70-45b9-b438-59a0af696abd");

    private static AutoCastFeatureStatus Project(
        bool emergencyDisabled = false,
        bool owned = true,
        bool manualPaused = false,
        bool cycleObserved = true) =>
        AutoCastFeatureStatusProjector.Project(
            emergencyDisabled,
            owned,
            manualPaused,
            cycleObserved);
}
