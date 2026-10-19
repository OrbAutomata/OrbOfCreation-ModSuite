using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.SpellLeveling.Runtime.ServiceCycle;

/// <summary>
/// Policy tests for the stateless Spell Leveling worker. Worlds are built directly so each term the
/// evaluator tests — the configuration gate, discovery, mastery readiness, affordability, the ranking rule and the
/// capability the level-all upgrade grants — can be exercised on its own.
/// </summary>
/// <remarks>
/// Leveling prerequisites remain boundary-only. Affordability is published planning evidence and is
/// re-read by the action boundary immediately before spending.
/// </remarks>
public sealed class SpellLevelCycleEvaluatorTests
{
    private static readonly Guid Ember = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Frost = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Gale = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void WaitsForTheNextPublicationWhenThereIsNoWork()
    {
        var actions = Plan(World(), Config(), out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void NonOperationalConfigurationPlansNothingButStillReschedules()
    {
        var world = World(Spell(Ember, discovered: true, ready: true));

        Assert.Empty(Plan(world, Config(autoLevelSpells: false), out var disabledWake));
        Assert.Equal(WakePolicyKind.OnPublication, disabledWake.Kind);
        Assert.Empty(Plan(world, Config(mode: AutoBuyOperationMode.Disabled), out _));
        Assert.Empty(Plan(world, Config(enabled: false), out _));
        Assert.Empty(Plan(world, Config(emergencyDisabled: true), out _));
    }

    [Fact]
    public void AnUndiscoveredSpellIsNeverPlanned()
    {
        var world = World(Spell(Ember, discovered: false, ready: true));

        Assert.Empty(Plan(world, Config(), out _, out var metrics));
        Assert.Equal(1, metrics.Exclusions.Undiscovered);
        Assert.Equal(0, metrics.ReadySpells);
    }

    [Fact]
    public void ADiscoveredSpellWithNoBankedExperienceIsNeverPlanned()
    {
        // The one fact the snapshot exists to carry. Without it the worker would propose this spell
        // every cycle and the boundary would refuse it every cycle, forever.
        var world = World(Spell(Ember, discovered: true, ready: false));

        Assert.Empty(Plan(world, Config(), out _, out var metrics));
        Assert.Equal(1, metrics.Exclusions.NotReady);
        Assert.Equal(0, metrics.ReadySpells);
    }

    [Fact]
    public void AReadySpellWhosePublishedCostIsUnaffordableIsQuietPlanningBackpressure()
    {
        var world = World(Spell(Ember, discovered: true, ready: true, affordable: false));

        Assert.Empty(Plan(world, Config(), out _, out var metrics));
        Assert.Equal(1, metrics.Exclusions.Unaffordable);
        Assert.Equal(1, metrics.ReadySpells);
        Assert.Equal(0, metrics.PlannedActions);
    }

    [Fact]
    public void LevelAllPlansWhenAnyReadySpellIsPublishedAffordable()
    {
        var world = World(
            new[]
            {
                Spell(Ember, discovered: true, ready: true, affordable: false, masteryLevel: 1),
                Spell(Frost, discovered: true, ready: true, affordable: true, masteryLevel: 2),
            },
            LevelAllUpgrade(level: 1, queuedLevels: 0));

        var action = Assert.Single(Plan(world, Config(), out _, out var metrics));
        Assert.Equal(SpellLevelActionKind.All, action.Kind);
        Assert.Equal(KnownEntities.UnlockLevelAllSpells.Uuid, action.Uuid);
        Assert.Equal(1, metrics.Exclusions.Unaffordable);
    }

    [Fact]
    public void TheLowestMasteryReadySpellIsPlanned()
    {
        var world = World(
            Spell(Gale, discovered: true, ready: true, masteryLevel: 7),
            Spell(Ember, discovered: true, ready: true, masteryLevel: 2),
            Spell(Frost, discovered: true, ready: true, masteryLevel: 5));

        var action = Assert.Single(Plan(world, Config(), out _, out var metrics));
        Assert.Equal(Ember, action.Uuid);
        Assert.Equal(3, metrics.ReadySpells);
        Assert.Equal(1, metrics.PlannedActions);

        // Exactly one action per cycle: the other two ready spells are attributed, not dropped.
        Assert.Equal(2, metrics.Exclusions.Outranked);
        Assert.Equal(
            metrics.CapturedSpells,
            metrics.ReadySpells + metrics.Exclusions.Undiscovered + metrics.Exclusions.NotReady);
    }

    [Fact]
    public void SpellsOnTheSameMasteryLevelAreRankedByIdentitySoTheChoiceIsReproducible()
    {
        var forward = World(
            Spell(Ember, discovered: true, ready: true, masteryLevel: 3),
            Spell(Frost, discovered: true, ready: true, masteryLevel: 3));
        var reversed = World(
            Spell(Frost, discovered: true, ready: true, masteryLevel: 3),
            Spell(Ember, discovered: true, ready: true, masteryLevel: 3));

        Assert.Equal(Ember, Assert.Single(Plan(forward, Config(), out _)).Uuid);
        Assert.Equal(Ember, Assert.Single(Plan(reversed, Config(), out _)).Uuid);
    }

    [Fact]
    public void WithoutTheUpgradeTheWorkerPlansASingleLevel()
    {
        var world = World(Spell(Ember, discovered: true, ready: true));

        var action = Assert.Single(Plan(world, Config(), out _, out var metrics));
        Assert.Equal(SpellLevelActionKind.Single, action.Kind);
        Assert.Equal(AutoSpellLevelCapability.Single, metrics.Capability);
    }

    [Fact]
    public void ACommittedLevelAllUpgradePromotesThePlanToTheNativeBatch()
    {
        var world = World(
            new[] { Spell(Ember, discovered: true, ready: true) },
            LevelAllUpgrade(level: 1, queuedLevels: 0));

        var action = Assert.Single(Plan(world, Config(), out _, out var metrics));
        Assert.Equal(SpellLevelActionKind.All, action.Kind);
        Assert.Equal(AutoSpellLevelCapability.All, metrics.Capability);
    }

    [Fact]
    public void AQueuedButUncommittedLevelAllUpgradeDoesNotGrantTheBatch()
    {
        // The game has not applied the upgrade until the level lands, so treating queued as owned
        // would fire a native batch the game refuses. The legacy runtime read GetPurchaseLevel() for
        // exactly this reason, and the published Reading.Level is that same number.
        var world = World(
            new[] { Spell(Ember, discovered: true, ready: true) },
            LevelAllUpgrade(level: 0, queuedLevels: 1));

        var action = Assert.Single(Plan(world, Config(), out _, out var metrics));
        Assert.Equal(SpellLevelActionKind.Single, action.Kind);
        Assert.Equal(AutoSpellLevelCapability.Single, metrics.Capability);
    }

    [Fact]
    public void AnEmptyWorldPlansNothingAndClaimsNoCapabilityItCannotBack()
    {
        Assert.Empty(Plan(World(), Config(), out _, out var metrics));
        Assert.Equal(0, metrics.CapturedSpells);
        Assert.Equal(AutoSpellLevelCapability.Single, metrics.Capability);
    }

    [Fact]
    public void ThePlannedActionCarriesTheEpochTheWorldWasCollectedUnder()
    {
        var world = World(
            new[] { Spell(Ember, discovered: true, ready: true) },
            upgrade: null,
            collectedAtEpoch: 4242);

        Assert.Equal(4242, Assert.Single(Plan(world, Config(), out _)).CollectedAtEpoch);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static WorldSpellRecipe Spell(
        Guid id,
        bool discovered,
        bool ready,
        bool affordable = true,
        int masteryLevel = 0) =>
        new(
            id,
            discovered,
            discRarityLevel: 0,
            masteryXp: default,
            masteryLevel,
            ready,
            affordable,
            hiddenDiscovery: false,
            isRequiredDiscovery: false,
            penaltyUsageCost: 0,
            castSpeed: 1d,
            baseCharges: 1,
            repeatInstantEffects: false,
            spellPowerMod: default,
            spellCostMod: default,
            spellCdSpeedMod: default,
            spellDurationMod: default,
            spellSpecialMod: default,
            spellXpMod: default,
            hasAlertedThisMastery: false);

    private static WorldUpgrade LevelAllUpgrade(int level, int queuedLevels) =>
        new(
            new RawUpgradeSample(
                KnownEntities.UnlockLevelAllSpells.Uuid,
                level,
                maxLevel: 1,
                available: true,
                queuedLevels,
                buildTime: default,
                developmentTime: 0d,
                cachedCostLevel: level),
            isBounded: true,
            isExhausted: level >= 1,
            remainingLevels: Math.Max(0, 1 - level),
            committedLevel: level + queuedLevels,
            isDeveloping: queuedLevels > 0,
            developmentProgress: 0d);

    private static GameWorldState World(params WorldSpellRecipe[] spells) =>
        World(spells, upgrade: null);

    private static GameWorldState World(
        WorldSpellRecipe[] spells,
        WorldUpgrade? upgrade,
        long collectedAtEpoch = 1) =>
        new()
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(spells),
            Upgrades = upgrade is null
                ? PublicationTable<WorldUpgrade>.Empty
                : PublicationTable<WorldUpgrade>.Create(new[] { upgrade.Value }),
            CollectedAtEpoch = collectedAtEpoch,
        };

    private static SuiteRuntimeConfiguration Config(
        bool enabled = true,
        bool emergencyDisabled = false,
        AutoBuyOperationMode mode = AutoBuyOperationMode.Active,
        bool autoLevelSpells = true) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = enabled },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = emergencyDisabled },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = mode,
                AutoLevelSpells = autoLevelSpells,
            },
        };

    private static IReadOnlyList<SpellLevelCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        out WakePolicy wake) =>
        Plan(world, config, out wake, out _);

    private static IReadOnlyList<SpellLevelCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        out WakePolicy wake,
        out SpellLevelDecisionMetrics metrics)
    {
        var store = new ReusableActionStore<SpellLevelCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<SpellLevelCycleAction>(store);
        wake = SpellLevelCycleEvaluator.Evaluate(
            world, in config, ServiceRetryGateSnapshot.Empty, default, writer, out metrics);

        var actions = new List<SpellLevelCycleAction>(store.Count);
        while (!store.IsComplete)
        {
            actions.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }

        return actions;
    }
}
