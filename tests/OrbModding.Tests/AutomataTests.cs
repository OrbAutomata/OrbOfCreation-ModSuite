using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using UnityEngine;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutomataTests
{
    [Fact]
    public void DecisionLogGate_ThrottlesRepeatedStateButLogsTransitions()
    {
        var gate = new DecisionLogGate(TimeSpan.FromSeconds(30));

        Assert.True(gate.ShouldLog("none", TimeSpan.Zero));
        Assert.False(gate.ShouldLog("none", TimeSpan.FromSeconds(1)));
        Assert.True(gate.ShouldLog("candidate-a", TimeSpan.FromSeconds(2)));
        Assert.True(gate.ShouldLog("candidate-a", TimeSpan.FromSeconds(32)));
    }

    [Fact]
    public void DefaultConfiguration_IsReadyForReleaseUse()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());

        Assert.Equal(AutoBuyOperationMode.Active, config.AutoBuyMode.Value);
        Assert.Equal(AutoBuyAffordabilityMode.Excess100, config.AutoBuyAffordability.Value);
        Assert.Equal(AutoBuyAffordabilityMode.Excess100, config.UpgradeAffordability.Value);
        Assert.Equal(1, config.LeaveQueueSlots.Value);
        Assert.Equal("0", config.AbsoluteReserve.Value);
        Assert.Equal(0.0f, config.RelativeReserveMultiplier.Value);
        Assert.Equal(AutoCastOperationMode.Disabled, config.AutoCastMode.Value);
        Assert.Equal(AutoConceptOperationMode.Disabled, config.AutoConceptMode.Value);
        Assert.Equal(AutoHarvestOperationMode.Disabled, config.AutoHarvestMode.Value);
        Assert.True(config.AutoHarvestFruitTrees.Value);
        Assert.True(config.AutoHarvestTreasureTrees.Value);
        Assert.Equal(AutoItemsOperationMode.Disabled, config.AutoItemsMode.Value);
        Assert.True(config.AutoItemsUseScrolls.Value);
        Assert.True(config.AutoItemsUseRelics.Value);
        Assert.Empty(config.AutoItemsTemporaryItemAllowlist.Value);
        Assert.Equal(AutoConceptSlotManagementMode.TimedCycle, config.AutoConceptSlotManagement.Value);
        Assert.True(config.AutoConceptShowToggleButton.Value);
        Assert.True(config.AutoLevelSpells.Value);
        Assert.Equal(30, config.AutoConceptTrainingPeriodSeconds.Value);
        Assert.True(config.Current.CanStartAutoBuyActively);
        Assert.False(config.Current.CanStartAutoCastActively);
        Assert.False(config.Current.CanStartAutoConceptActively);
        Assert.False(config.Current.CanStartAutoHarvestActively);
        Assert.False(config.Current.CanStartAutoItemsActively);
    }

    [Fact]
    public void AutoConceptToggleSwitchesModeWithoutReplacingConfiguredIntent()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var changes = new AutomataConfigurationStore(config, (_, _) => { });
        var toggle = new AutoConceptToggleControl(
            changes,
            () =>
            {
                var enabled = config.Current.AutoConcept.Mode == AutoConceptOperationMode.Active;
                return new FeatureStatusSnapshot(
                    new FeatureStatusKey(
                        PluginIds.SuiteGuid,
                        AutomataFeatureStatuses.AutoConceptFeatureId),
                    "Auto Concept",
                    enabled,
                    enabled
                        ? FeatureStatusState.Operational
                        : FeatureStatusState.ConfigurationDisabled);
            });

        Assert.Equal(AutoCastToggleVisualState.Off, toggle.State);
        toggle.Toggle();
        Assert.Equal(AutoConceptOperationMode.Active, config.AutoConceptMode.Value);
        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
        config.EmergencyDisable.Value = true;
        Assert.Equal(AutoCastToggleVisualState.On, toggle.State);
        toggle.Toggle();
        Assert.Equal(AutoConceptOperationMode.Disabled, config.AutoConceptMode.Value);
        Assert.Equal(AutoCastToggleVisualState.Off, toggle.State);
    }

    [Fact]
    public void AutomataLogTimestamp_IncludesLocalDateTimeMillisecondsAndOffset()
    {
        var timestamp = new DateTimeOffset(2026, 7, 17, 9, 35, 12, TimeSpan.FromHours(2)).AddMilliseconds(345);

        var message = AutomataLoggingExtensions.WithTimestamp("ConceptRecipes runtime list is empty", timestamp);

        Assert.Equal("[2026-07-17 09:35:12.345 +02:00] ConceptRecipes runtime list is empty", message);
    }

    [Fact]
    public void AutoConceptRanksLowestEffectiveMasteryWithStableTieBreaks()
    {
        var ranked = AutoConceptBalancer.Rank(new[]
        {
            new ConceptProgress("z", 2, 0.1, true),
            new ConceptProgress("b", 1, 0.8, true),
            new ConceptProgress("a", 1, 0.8, true),
            new ConceptProgress("locked", 0, 0.0, false),
            new ConceptProgress("early", 1, 0.2, true),
        });

        Assert.Equal(new[] { "early", "a", "b", "z" }, System.Linq.Enumerable.Select(ranked, item => item.Uuid));
    }

    [Theory]
    [InlineData(0, 0.9, 1, 0.0, true)]
    [InlineData(1, 0.2, 1, 0.8, true)]
    [InlineData(1, 0.8, 1, 0.8, false)]
    [InlineData(2, 0.0, 1, 0.9, false)]
    public void AutoConceptRotatesOnlyForStrictlyLowerMastery(
        int candidateLevel,
        double candidateProgress,
        int activeLevel,
        double activeProgress,
        bool expected)
    {
        var candidate = new ConceptProgress("candidate", candidateLevel, candidateProgress, true);
        var active = new ConceptProgress("active", activeLevel, activeProgress, true);

        Assert.Equal(expected, AutoConceptBalancer.HasStrictlyLowerMastery(candidate, active));
    }

    [Theory]
    [InlineData(4, 0.9, 5, 0.0, false)]
    [InlineData(5, 0.4, 5, 0.5, false)]
    [InlineData(5, 0.5, 5, 0.5, true)]
    [InlineData(6, 0.0, 5, 0.9, true)]
    public void AutoConceptCatchUpUsesCapturedEffectiveMastery(
        int currentLevel,
        double currentProgress,
        int targetLevel,
        double targetProgress,
        bool expected)
    {
        var current = new ConceptProgress("current", currentLevel, currentProgress, true);
        var target = new ConceptProgress("target", targetLevel, targetProgress, true);

        Assert.Equal(expected, AutoConceptBalancer.HasReached(current, target));
    }

    [Theory]
    [InlineData(100.0, 109.9, 10, false)]
    [InlineData(100.0, 110.0, 10, true)]
    [InlineData(100.0, 99.0, 10, false)]
    [InlineData(0.0, 9.9, 1, false)]
    [InlineData(0.0, 3600.0, 9999, true)]
    public void AutoConceptTrainingPeriodStartsAtSettledAssignment(
        double startedAt,
        double current,
        int configuredPeriod,
        bool expected)
    {
        Assert.Equal(
            expected,
            AutoConceptBalancer.HasTrainingPeriodElapsed(startedAt, current, configuredPeriod));
    }

    [Fact]
    public void TimedConceptCycleDoesNotEndWhenMasteryCatchesUpEarly()
    {
        var current = new ConceptProgress("current", 5, 0.9, true);
        var target = new ConceptProgress("target", 2, 0.0, true);

        Assert.False(AutoConceptBalancer.HasTrainingSessionCompleted(
            AutoConceptSlotManagementMode.TimedCycle,
            current,
            target,
            100.0,
            109.9,
            10));
        Assert.True(AutoConceptBalancer.HasTrainingSessionCompleted(
            AutoConceptSlotManagementMode.TimedCycle,
            current,
            target,
            100.0,
            110.0,
            10));
        Assert.True(AutoConceptBalancer.HasTrainingSessionCompleted(
            AutoConceptSlotManagementMode.RotateAll,
            current,
            target,
            100.0,
            100.1,
            10));
    }

    [Fact]
    public void TimedConceptCycleUsesFullRotationWithoutMasteryOrdering()
    {
        Assert.True(AutoConceptBalancer.UsesFullRotation(AutoConceptSlotManagementMode.TimedCycle));
        Assert.False(AutoConceptBalancer.RequiresLowerMastery(AutoConceptSlotManagementMode.TimedCycle));
        Assert.False(AutoConceptBalancer.UsesFullRotation(AutoConceptSlotManagementMode.PreserveManual));
        Assert.True(AutoConceptBalancer.RequiresLowerMastery(AutoConceptSlotManagementMode.RotateAll));
    }

    [Fact]
    public void TimedConceptCyclePrioritizesNeverAndLeastRecentlyAssignedConcepts()
    {
        Assert.True(AutoConceptBalancer.CompareTimedCycleOrder(null, "new", 1, "old") < 0);
        Assert.True(AutoConceptBalancer.CompareTimedCycleOrder(2, "older", 3, "newer") < 0);
        Assert.True(AutoConceptBalancer.CompareTimedCycleOrder(null, "a", null, "b") < 0);
    }

    [Fact]
    public void TimedConceptCycleDoesNotLetResourceBlockedCandidateStarveSafeCandidate()
    {
        Assert.False(AutoConceptBalancer.ResourceSafeTimedCandidatePrecedes(
            false,
            null,
            "resource-at-zero",
            4,
            "resource-safe"));
        Assert.True(AutoConceptBalancer.ResourceSafeTimedCandidatePrecedes(
            true,
            null,
            "resource-restored",
            4,
            "current"));
    }

    [Theory]
    [InlineData(null, false, "zero-quantity state is unavailable")]
    [InlineData(true, false, "is at zero")]
    [InlineData(false, true, "")]
    public void AutoConceptPositiveDrainRequiresAuthoritativeNonzeroResource(
        bool? nativeIsAtZero,
        bool expected,
        string expectedReason)
    {
        Assert.Equal(expected, AutoConceptResourcePolicy.TryAcceptPositiveDrain(nativeIsAtZero, out var reason));
        Assert.Equal(expectedReason, reason);
    }
}
