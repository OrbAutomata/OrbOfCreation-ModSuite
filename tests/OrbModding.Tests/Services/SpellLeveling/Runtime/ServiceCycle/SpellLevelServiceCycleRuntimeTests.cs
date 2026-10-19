using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.SpellLeveling.Runtime.ServiceCycle;

/// <summary>
/// A level the game charged for without the level rising backs that spell off for a minute, doubling
/// to half an hour, while every other spell keeps levelling and the status says why. The test clock
/// moves only when a test means to bring a retry due.
/// </summary>
[Collection(GameActionFaultLogCollection.Name)]
public sealed class SpellLevelServiceCycleRuntimeTests : IDisposable
{
    private static readonly MonotonicDuration FirstWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1));
    private static readonly MonotonicDuration OneSecond = MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1));
    private static readonly MonotonicDuration HalfSecond = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(500));

    public SpellLevelServiceCycleRuntimeTests() => ResetNativeState();

    public void Dispose()
    {
        GameActionFaultLog.ConfigureLog(null);
        ResetNativeState();
    }

    [Fact]
    public void APaidLevelThatDoesNotRiseBacksThatSpellOffAMinuteDoublingWhileOtherSpellsKeepLevelling()
    {
        var broken = Spell(masteryLevel: 1);
        broken.SuppressLevelMutation = true;
        var healthy = Spell(masteryLevel: 5);
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var first = harness.RetryGate.Latest;
        var status = harness.Status.Current;
        harness.Advance(OneSecond);
        harness.PumpWorldsUntil(() => healthy.masteryLevel == 6);
        harness.Advance(FirstWait - OneSecond - OneSecond);
        harness.PumpUntilTheNewestWorldIsEvaluated();
        var paymentsBeforeDue = broken.levelCost.PerformCalls;
        harness.Advance(OneSecond + OneSecond);
        harness.PumpWorldsUntil(() => broken.levelCost.PerformCalls == 2);
        var second = harness.RetryGate.Latest;

        Assert.Equal(SpellLevelServicePolicies.RetryTarget(Id(broken)), first.Target);
        Assert.Equal(FirstWait, first.Backoff);
        Assert.Equal(1, paymentsBeforeDue);
        Assert.Equal(1, broken.masteryLevel);
        Assert.Equal(2, second.ConsecutiveFailures);
        Assert.Equal(MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(2)), second.Backoff);
        Assert.Equal(FeatureStatusReasonCode.NativeMutationFailed, status.Reason.Code);
        Assert.Contains("after 1 failed attempt(s)", status.Reason.Summary);
        Assert.Contains(
            $"Spell Leveling paid for a level of {EntityIdentityFormatter.PlayerName(Id(broken))} and the level did not rise",
            status.Reason.Summary);
    }

    [Fact]
    public void ALevelThatCommitsAfterItsBackOffEndsTheSpellsEpisode()
    {
        var spell = Spell(masteryLevel: 1);
        spell.SuppressLevelMutation = true;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        spell.SuppressLevelMutation = false;
        harness.Advance(FirstWait + OneSecond);
        harness.PumpWorldsUntil(() => spell.masteryLevel == 2);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.False(harness.RetryGate.TryFind(SpellLevelServicePolicies.RetryTarget(Id(spell)), out _));
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
        Assert.Equal(2, spell.levelCost.PerformCalls);
    }

    [Fact]
    public void AnUnexpectedThrowAfterPaymentWithNoLevelBacksTheSpellOffAndLogsTheExceptionUnderItsReference()
    {
        var log = new List<string>();
        GameActionFaultLog.ConfigureLog(log.Add);
        var spell = Spell(masteryLevel: 1);
        spell.SuppressLevelMutation = true;
        spell.ThrowAfterLevelMutation = new NullReferenceException("injected failure after payment");
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var entry = harness.RetryGate.Latest;
        harness.Advance(FirstWait - OneSecond);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(FirstWait, entry.Backoff);
        Assert.Equal(1, spell.levelCost.PerformCalls);
        var logged = Assert.Single(log);
        Assert.Contains("System.NullReferenceException: injected failure after payment", logged);
        Assert.DoesNotContain("injected failure", harness.Status.Current.Reason.Summary);
        Assert.Contains(" Reference " + logged.Split(' ')[3] + " is in the suite log.", harness.Status.Current.Reason.Summary);
    }

    [Fact]
    public void AnUnexpectedThrowAfterTheLevelLandedCommitsAndLogsWithoutBackingOff()
    {
        var log = new List<string>();
        GameActionFaultLog.ConfigureLog(log.Add);
        var spell = Spell(masteryLevel: 1);
        spell.ThrowAfterLevelMutation = new NullReferenceException("injected defect after the level landed");
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => spell.masteryLevel == 2);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(0, harness.RetryGate.Count);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
        Assert.Contains("System.NullReferenceException: injected defect after the level landed", Assert.Single(log));
    }

    [Fact]
    public void AnInitialisationThrowBacksOffLoudlyAndBindsOnTheRetry()
    {
        var log = new List<string>();
        GameActionFaultLog.ConfigureLog(log.Add);
        var spell = Spell(masteryLevel: 1);
        var recipes = global::SpellManager.instance!.availableSpellRecipes.value;
        global::SpellManager.instance.availableSpellRecipes.value = null!;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var status = harness.Status.Current;
        global::SpellManager.instance.availableSpellRecipes.value = recipes;
        harness.Advance(OneSecond);
        harness.PumpWorldsUntil(() => spell.masteryLevel == 2);

        Assert.Equal(FeatureStatusReasonCode.NativeMutationFailed, status.Reason.Code);
        Assert.Contains("spell-level contract initialization failed", status.Reason.Summary);
        Assert.DoesNotContain("available spell recipe contents are unavailable", status.Reason.Summary);
        Assert.Contains("available spell recipe contents are unavailable", log[0]);
    }

    [Fact]
    public void ABrokenLevelAllBacksOffItsOwnUpgradeWhileSingleLevelsKeepCommittingUntilItIsDue()
    {
        var behind = Spell(masteryLevel: 1);
        var ahead = Spell(masteryLevel: 5);
        LevelAllUpgrade().level = 1;
        global::SpellManager.instance!.SuppressLevelAll = true;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var backOff = harness.RetryGate.Latest;
        harness.Advance(OneSecond);
        harness.PumpWorldsUntil(() => behind.masteryLevel == 2);
        var batchesWhileASpellIsStillReady = global::SpellManager.instance.TryLevelAllCalls;
        var aheadStillReady = ahead.readyToLevel;
        harness.PumpWorldsUntil(() => ahead.masteryLevel == 6);
        global::SpellManager.instance.SuppressLevelAll = false;
        behind.readyToLevel = true;
        ahead.readyToLevel = true;
        harness.Advance(FirstWait);
        harness.PumpWorldsUntil(() => global::SpellManager.instance.TryLevelAllCalls == 2);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(LevelAllTarget, backOff.Target);
        Assert.Equal(FirstWait, backOff.Backoff);
        Assert.True(aheadStillReady);
        Assert.Equal(1, batchesWhileASpellIsStillReady);
        Assert.Equal(3, behind.masteryLevel);
        Assert.Equal(7, ahead.masteryLevel);
        Assert.False(harness.RetryGate.TryFind(LevelAllTarget, out _));
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    [Fact]
    public void ABatchDueAgainRaisesASpellStillBackingOffWhileSinglesInTheWindowPassItOver()
    {
        var stuck = Spell(masteryLevel: 1);
        stuck.SuppressLevelMutation = true;
        var other = Spell(masteryLevel: 5);
        var stuckTarget = SpellLevelServicePolicies.RetryTarget(Id(stuck));
        LevelAllUpgrade().level = 1;
        global::SpellManager.instance!.SuppressLevelAll = true;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.RetryGate.TryFind(LevelAllTarget, out _));
        harness.Advance(OneSecond);
        harness.PumpWorldsUntil(() => harness.RetryGate.TryFind(stuckTarget, out _));
        harness.Advance(OneSecond);
        harness.PumpWorldsUntil(() => other.masteryLevel == 6);
        harness.PumpUntilTheNewestWorldIsEvaluated();
        var stuckPaymentsInTheWindow = stuck.levelCost.PerformCalls;
        stuck.SuppressLevelMutation = false;
        global::SpellManager.instance.SuppressLevelAll = false;
        harness.Advance(FirstWait - OneSecond - OneSecond + HalfSecond);
        harness.PumpWorldsUntil(() => global::SpellManager.instance.TryLevelAllCalls == 2);
        var stuckStillBackingOff = harness.RetryGate.TryFind(stuckTarget, out _);

        Assert.Equal(1, stuckPaymentsInTheWindow);
        Assert.Equal(2, stuck.masteryLevel);
        Assert.True(stuckStillBackingOff);
    }

    [Fact]
    public void ARepeatingCapabilityProbeFailureIsLoggedOnceAndAChangedOneIsLoggedAgain()
    {
        var log = new List<string>();
        GameActionFaultLog.ConfigureLog(log.Add);
        var throwing = Spell(masteryLevel: 1);
        throwing.readyToLevel = false;
        var healthy = Spell(masteryLevel: 5);
        var recipes = global::SpellManager.instance!.availableSpellRecipes.value;
        global::SpellManager.instance.availableSpellRecipes.value = null!;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => ProbeEntries(log).Count == 1);
        global::SpellManager.instance.availableSpellRecipes.value = recipes;
        throwing.levelingPrerequisites.CheckFailure = new InvalidOperationException("injected prerequisite failure");
        harness.Advance(OneSecond);
        harness.PumpWorldsUntil(() => healthy.masteryLevel == 6);
        harness.PumpUntilTheNewestWorldIsEvaluated();
        harness.PumpUntilTheNewestWorldIsEvaluated();
        harness.PumpUntilTheNewestWorldIsEvaluated();

        var probes = ProbeEntries(log);
        Assert.Equal(2, probes.Count);
        Assert.Contains("available spell recipe contents are unavailable", probes[0]);
        Assert.Contains("injected prerequisite failure", probes[1]);
    }

    [Fact]
    public void AProvenContractMismatchStaysBlockedPastEveryRetry()
    {
        var spell = Spell(masteryLevel: 1);
        var levelAll = global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid];
        global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid] =
            new global::StructureSO { uuid = KnownEntities.UnlockLevelAllSpells.Uuid.ToString("D") };
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid] = levelAll;
        harness.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1)));
        harness.PumpWorldsUntil(() => harness.RetryGate.Latest.ConsecutiveFailures == 2);

        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, spell.levelCost.PerformCalls);
        Assert.Contains("WrongType", harness.RetryGate.Latest.Reason);
    }

    private static List<string> ProbeEntries(List<string> log) =>
        log.FindAll(static entry => entry.Contains(
            " on " + SpellLevelServiceCycleDiagnosticsBridge.CapabilityScreen + ": ", StringComparison.Ordinal));

    private static readonly ServiceRetryTarget LevelAllTarget =
        new(KnownEntities.UnlockLevelAllSpells.Uuid, ServiceActionNativeTypeId.UpgradeSO);

    private static Guid Id(global::SpellRecipeSO spell) => Guid.Parse(spell.uuid);

    private static global::UpgradeSO LevelAllUpgrade() => global::UpgradeSO.All[0];

    private static global::SpellRecipeSO Spell(int masteryLevel)
    {
        var spell = new global::SpellRecipeSO
        {
            uuid = Guid.NewGuid().ToString(),
            masteryLevel = masteryLevel,
            discovered = true,
            readyToLevel = true,
        };
        spell.levelingPrerequisites.available = true;
        global::SpellRecipeSO.All.Add(spell);
        global::SpellManager.instance!.availableSpellRecipes.value.Add(spell);
        return spell;
    }

    private static void ResetNativeState()
    {
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::SpellRecipeSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::SpellManager.instance = new global::SpellManager();
        var levelAll = new global::UpgradeSO
        {
            uuid = KnownEntities.UnlockLevelAllSpells.Uuid.ToString("D"),
            level = 0,
            available = true,
        };
        global::UpgradeSO.All.Add(levelAll);
        global::IdScriptableObject.RuntimeLookup.Add(KnownEntities.UnlockLevelAllSpells.Uuid, levelAll);
    }

    private sealed class Harness : IDisposable
    {
        private const long Epoch = 1;
        private readonly ServiceCycleRegistry _registry;
        private readonly AutomataServiceCycleRuntime _runtime;
        private readonly ThreadSafeTestClock _clock = new(1_000);
        private readonly AutomataServiceCycleHost _host;
        private long _frame = 10;
        private ulong _generation = 2;

        internal Harness()
        {
            Status = new AutomataFeatureStatusReporter(
                new FeatureStatusRegistry(),
                new FeatureStatusSnapshot(
                    new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.SpellLevelFeatureId),
                    "Spell Leveling",
                    true,
                    FeatureStatusState.NotReady,
                    new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "not ready"),
                    Epoch));
            _registry = new ServiceCycleRegistry(
                1,
                Configuration(),
                new ConfigGeneration(1),
                new LifecycleGeneration((ulong)Epoch),
                _clock);
            var feature = new SpellLevelServiceCycleFeature(
                new SpellLevelFeatureDependencies(
                    static () => Epoch,
                    ownsActionFamily: static () => true,
                    new SpellLevelCapabilityState(),
                    Status)).Register(
                new AutomataServiceCycleFeatureContext(
                    _registry, new ConfigGeneration(1), Epoch, new LifecycleGameActions()));
            _registry.WorldPublication.Publish(World(), new WorldGeneration(_generation));
            _host = new AutomataServiceCycleHost(_registry, () => _frame++, pumpTiming: null, semanticTrace: null);
            _runtime = new AutomataServiceCycleRuntime(
                static () => Epoch,
                _registry.Configuration,
                _host,
                new IAutomataServiceCycleFeatureRuntime[] { feature },
                new ConfigGeneration(1),
                new LifecycleGameActions());
            feature.ActivateDiagnostics();
        }

        internal AutomataFeatureStatusReporter Status { get; }
        internal ServiceRetryGateSnapshot RetryGate => Service.RetryGate;

        private ServiceCycleServiceDiagnosticsSnapshot Service =>
            AvailableServiceDiagnostics.Await(_host.Pump, SpellLevelServicePolicies.ServiceId);

        internal void Advance(MonotonicDuration duration) => _clock.Advance(duration);

        internal void PumpWorldsUntil(Func<bool> condition)
        {
            var deadline = Stopwatch.StartNew();
            while (!condition())
            {
                PumpWorld();
                if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                    throw new TimeoutException("Spell Leveling did not reach the expected state.");
                Thread.Yield();
            }
        }

        internal void PumpUntilTheNewestWorldIsEvaluated()
        {
            PumpWorld();
            var newest = new WorldGeneration(_generation);
            var deadline = Stopwatch.StartNew();
            while (!EvaluatedAndDrained(Service, newest))
            {
                _runtime.Tick(0);
                if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                    throw new TimeoutException("Spell Leveling did not evaluate the newest world.");
                Thread.Yield();
            }
        }

        private static bool EvaluatedAndDrained(ServiceCycleServiceDiagnosticsSnapshot service, WorldGeneration world) =>
            service.LatestProjection.Context.Cycle.World == world && !service.ActiveBatch.IsPresent;

        private void PumpWorld()
        {
            _generation = Math.Max(_generation + 1, checked((ulong)_frame));
            _registry.WorldPublication.Publish(World(), new WorldGeneration(_generation));
            _runtime.Tick(0);
        }

        public void Dispose()
        {
            _runtime.Dispose();
            _registry.Dispose();
            Status.Dispose();
        }
    }

    private static SuiteRuntimeConfiguration Configuration() =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                AutoLevelSpells = true,
            },
        };

    private static GameWorldState World()
    {
        var rows = new List<WorldSpellRecipe>();
        foreach (var spell in global::SpellRecipeSO.All)
            rows.Add(new WorldSpellRecipe(
                Id(spell),
                spell.discovered,
                discRarityLevel: 0,
                masteryXp: default,
                spell.masteryLevel,
                spell.readyToLevel,
                masteryLevelAffordable: true,
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
                hasAlertedThisMastery: false));
        var levelAll = LevelAllUpgrade().level;
        return new GameWorldState
        {
            CollectedAtFrame = 10,
            CollectedAtEpoch = 1,
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(rows.ToArray()),
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(
                    new RawUpgradeSample(
                        KnownEntities.UnlockLevelAllSpells.Uuid,
                        levelAll,
                        maxLevel: 1,
                        available: true,
                        queuedLevels: 0,
                        buildTime: default,
                        developmentTime: 0d,
                        cachedCostLevel: levelAll),
                    isBounded: true,
                    isExhausted: levelAll >= 1,
                    remainingLevels: Math.Max(0, 1 - levelAll),
                    committedLevel: levelAll,
                    isDeveloping: false,
                    developmentProgress: 0d),
            }),
        };
    }
}
