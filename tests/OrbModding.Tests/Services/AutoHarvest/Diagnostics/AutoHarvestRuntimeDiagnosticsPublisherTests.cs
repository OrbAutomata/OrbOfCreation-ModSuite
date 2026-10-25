using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Diagnostics;

public sealed class AutoHarvestRuntimeDiagnosticsPublisherTests
{
    [Fact]
    public void PublisherRequiresAnExplicitDiagnosticsRegistry()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            new AutoHarvestRuntimeDiagnosticsPublisher(
                lifecycleGeneration: 1,
                AutoHarvestPairHealth.NotObserved(AutoHarvestPair.FruitTree),
                AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree),
                AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
                null!));
    }

    [Fact]
    public void PublisherMapsPartialUnlockEvidenceWithoutTreatingLockedSiblingAsFailure()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        using var publisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            lifecycleGeneration: 7,
            new AutoHarvestPairHealth(
                AutoHarvestPair.FruitTree,
                selected: true,
                AutoHarvestPairHealthKind.NativeBusy),
            new AutoHarvestPairHealth(
                AutoHarvestPair.TreasureTree,
                selected: true,
                AutoHarvestPairHealthKind.ProgressionLocked),
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            registry);

        var snapshot = Assert.Single(registry.GetSnapshot());
        Assert.Equal(PluginIds.SuiteGuid, snapshot.Key.PluginId);
        Assert.Equal(AutoHarvestFeatureContribution.FeatureId, snapshot.Key.FeatureId);
        Assert.Equal(AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName, snapshot.Implementation);
        Assert.Equal(7, snapshot.LifecycleGeneration);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, snapshot.Capabilities[0].State);
        Assert.Equal(FeatureStatusReasonCode.NativeBusy, snapshot.Capabilities[0].Reason.Code);
        Assert.Equal(FeatureStatusState.Locked, snapshot.Capabilities[1].State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, snapshot.Capabilities[1].Reason.Code);
    }

    /// <remarks>
    /// <para>
    /// A loop rather than a <c>[Theory]</c>: the health kind is an enum internal to the suite, and
    /// a public test method may not name one in its signature.
    /// </para>
    /// <para>
    /// <c>ProgressionLocked</c> is in the list without being produced any more. Its sentence is
    /// what a journal recorded before the prerequisite reading was corrected still renders as, and
    /// a rewrite of it would rewrite what those journals said.
    /// </para>
    /// </remarks>
    [Fact]
    public void PublisherGivesEachHarvestRefusalItsOwnSentence()
    {
        var expectations = new[]
        {
            (Kind: AutoHarvestPairHealthKind.PlotNotVisible,
                State: FeatureStatusState.Locked,
                Code: FeatureStatusReasonCode.ProgressionLocked,
                Summary: "The plot this harvest belongs to is not visible yet."),
            (Kind: AutoHarvestPairHealthKind.ProgressionLocked,
                State: FeatureStatusState.Locked,
                Code: FeatureStatusReasonCode.ProgressionLocked,
                Summary: "This harvest action's prerequisites have not been met."),
            (Kind: AutoHarvestPairHealthKind.PrerequisitesNotConfirmed,
                State: FeatureStatusState.NotReady,
                Code: FeatureStatusReasonCode.GameplayNotReady,
                Summary: "The game has not confirmed this harvest action's prerequisites yet."),
            (Kind: AutoHarvestPairHealthKind.ActionNotOffered,
                State: FeatureStatusState.TemporarilyBlocked,
                Code: FeatureStatusReasonCode.NativeBusy,
                Summary: "The plot is unlocked but is not offering this harvest action right now."),
        };

        foreach (var expected in expectations)
        {
            var registry = new RuntimeDiagnosticsRegistry();
            using var publisher = new AutoHarvestRuntimeDiagnosticsPublisher(
                lifecycleGeneration: 3,
                new AutoHarvestPairHealth(AutoHarvestPair.FruitTree, selected: true, expected.Kind),
                AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree),
                AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
                registry);

            var capability = Assert.Single(registry.GetSnapshot()).Capabilities[0];

            Assert.True(
                capability.State == expected.State &&
                capability.Reason.Code == expected.Code &&
                capability.Reason.Summary == expected.Summary,
                $"{expected.Kind} reported {capability.State}/{capability.Reason.Code}: " +
                capability.Reason.Summary);
        }

        // One sentence each, not one repeated — the defect this exists to prevent.
        Assert.Equal(
            expectations.Length,
            expectations.Select(expected => expected.Summary).Distinct().Count());
    }

    [Fact]
    public void PublisherSuppressesEquivalentStateAndPublishesLifecycleReplacement()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        var transitions = 0;
        registry.Transitioned += _ => transitions++;
        var fruit = AutoHarvestPairHealth.Eligible(AutoHarvestPair.FruitTree);
        var treasure = AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree);
        using var publisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            7, fruit, treasure, AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName, registry);

        publisher.PublishState(7, fruit, treasure);
        Assert.Equal(1, transitions);

        publisher.PublishState(8, fruit, treasure);
        var snapshot = Assert.Single(registry.GetSnapshot());
        Assert.Equal(8, snapshot.LifecycleGeneration);
        Assert.Equal(2, transitions);
    }

    [Fact]
    public void DisposalRemovesPublisherOwnership()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        var publisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            1,
            AutoHarvestPairHealth.NotSelected(AutoHarvestPair.FruitTree),
            AutoHarvestPairHealth.NotSelected(AutoHarvestPair.TreasureTree),
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            registry);

        publisher.Dispose();

        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public void FeatureScopedFailureProjectsAcrossEverySelectedCapability()
    {
        var registry = new RuntimeDiagnosticsRegistry();
        using var publisher = new AutoHarvestRuntimeDiagnosticsPublisher(
            3,
            new AutoHarvestPairHealth(
                AutoHarvestPair.FruitTree,
                selected: true,
                AutoHarvestPairHealthKind.ContractUnavailable,
                featureScoped: true),
            AutoHarvestPairHealth.NotObserved(AutoHarvestPair.TreasureTree),
            AutoHarvestServiceCycleDiagnosticsBridge.ImplementationName,
            registry);

        var capabilities = Assert.Single(registry.GetSnapshot()).Capabilities;
        Assert.All(capabilities, capability =>
            Assert.Equal(FeatureStatusState.ContractUnavailable, capability.State));
        Assert.All(capabilities, capability =>
            Assert.Contains("service", capability.Reason.Summary, System.StringComparison.OrdinalIgnoreCase));
    }

}
