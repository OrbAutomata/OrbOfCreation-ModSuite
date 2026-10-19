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

namespace OrbModding.Tests.Services.AutoCast.Runtime.ServiceCycle;

/// <summary>
/// A cast the fire hook did not see backs that spell's cast off on the retry gate, doubling from a
/// quarter second, while the rest of the loadout keeps its turns and the status says which spell and
/// why. The test clock moves only when a test means to bring a retry due.
/// </summary>
[Collection(GameActionFaultLogCollection.Name)]
public sealed class AutoCastServiceCycleRuntimeTests : IDisposable
{
    private static readonly Guid Ember = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Frost = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly MonotonicDuration FirstWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration PastTheFirstWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(300));

    public AutoCastServiceCycleRuntimeTests() => ResetNativeState();

    public void Dispose()
    {
        GameActionFaultLog.ConfigureLog(null);
        ResetNativeState();
    }

    [Fact]
    public void ACastTheHookDidNotSeeBacksThatSpellOffDoublingWhileTheRestOfTheLoadoutKeepsCasting()
    {
        var ember = Equip(Ember);
        ember.EmitFireSignal = false;
        var frost = Equip(Frost);
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var first = harness.RetryGate.Latest;
        var status = harness.Status.Current;
        harness.Advance(PastTheFirstWait);
        harness.PumpWorldsUntil(() => ember.FireCalls == 2);
        var second = harness.RetryGate.Latest;
        var frostCastsBeforeTheWindow = frost.FireCalls;
        harness.Advance(PastTheFirstWait);
        harness.PumpWorldsUntil(() => frost.FireCalls > frostCastsBeforeTheWindow + 1);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(AutoCastServicePolicies.RetryTarget(AutoCastActionKind.Fire, Ember), first.Target);
        Assert.Equal(FirstWait, first.Backoff);
        Assert.Equal(2, second.ConsecutiveFailures);
        Assert.Equal(FirstWait + FirstWait, second.Backoff);
        Assert.Equal(2, ember.FireCalls);
        Assert.Equal(FeatureStatusReasonCode.NativeMutationFailed, status.Reason.Code);
        Assert.Contains(
            "is backing off casting " + EntityIdentityFormatter.Format(Ember) + " after 1 failed attempt(s)",
            status.Reason.Summary);
        Assert.Contains(
            $"Auto Cast pressed {EntityIdentityFormatter.PlayerName(Ember)}, and no cast started.",
            status.Reason.Summary);
    }

    [Fact]
    public void ACastThatCommitsWhenItIsDueEndsTheSpellsEpisode()
    {
        var ember = Equip(Ember);
        ember.EmitFireSignal = false;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        ember.EmitFireSignal = true;
        harness.Advance(PastTheFirstWait);
        harness.PumpWorldsUntil(() => ember.FireCalls == 2);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.False(harness.RetryGate.TryFind(
            AutoCastServicePolicies.RetryTarget(AutoCastActionKind.Fire, Ember), out _));
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    [Fact]
    public void ASpellTakenOffTheBarWhileBackingOffEndsItsEpisode()
    {
        var ember = Equip(Ember);
        ember.EmitFireSignal = false;
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        global::SpellManager.instance!.activeSpells.Remove(ember);
        harness.Advance(PastTheFirstWait);
        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 0);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(1, ember.FireCalls);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    [Fact]
    public void AnUnexpectedThrowAfterTheCastLandedCommitsAndLogsWithoutBackingOff()
    {
        var log = new List<string>();
        GameActionFaultLog.ConfigureLog(log.Add);
        var ember = Equip(Ember);
        global::SpellManager.instance!.ThrowAfterFireSpellIndex =
            new NullReferenceException("injected defect after the cast landed");
        using var harness = new Harness();

        harness.PumpWorldsUntil(() => ember.FireCalls == 1);
        ember.NativeCanCast = false;
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(1, ember.FireCalls);
        Assert.Equal(0, harness.RetryGate.Count);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
        Assert.Contains("System.NullReferenceException: injected defect after the cast landed", Assert.Single(log));
    }

    private static global::Spell Equip(Guid spellId)
    {
        var spell = new global::Spell(new global::SpellRecipeSO { uuid = spellId.ToString("D") });
        global::SpellManager.instance!.activeSpells.Add(spell);
        return spell;
    }

    private static void ResetNativeState()
    {
        global::SpellManager.instance = new global::SpellManager();
        global::SpellManager.NativeCanCast = true;
        global::SettingsManager.CancellableSpells = true;
        global::TargetingManager.Reset();
        global::Spell.FireSignal = AutoCastManualSignal.NotifySpellFire;
        global::BattleManager.instance = null;
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
                    new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoCastFeatureId),
                    "Auto Cast",
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
            var feature = new AutoCastServiceCycleFeature(
                new AutoCastFeatureDependencies(
                    static () => Epoch,
                    static () => true,
                    new AutoCastManualPauseState(),
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
            AvailableServiceDiagnostics.Await(_host.Pump, AutoCastServicePolicies.ServiceId);

        internal void Advance(MonotonicDuration duration) => _clock.Advance(duration);

        internal void PumpWorldsUntil(Func<bool> condition)
        {
            var deadline = Stopwatch.StartNew();
            while (!condition())
            {
                PumpWorld();
                if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                    throw new TimeoutException("Auto Cast did not reach the expected state.");
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
                    throw new TimeoutException("Auto Cast did not evaluate the newest world.");
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

        private static GameWorldState World()
        {
            var slots = new WorldSpellSlotBuffer();
            var spells = global::SpellManager.instance!.activeSpells.value;
            for (var index = 0; index < spells.Count; index++)
            {
                var spell = spells[index];
                var occupied = spell.get_reference() is not null;
                var slot = new WorldSpellSlot(
                    index,
                    occupied ? Guid.Parse(spell.get_reference()!.uuid) : Guid.Empty,
                    occupied,
                    casting: false,
                    readyingCast: false,
                    attuning: false,
                    channeled: false,
                    toggled: false,
                    chargeable: false,
                    castReady: spell.NativeCanCast,
                    chargeAvailable: true,
                    resourcesCovered: true,
                    currentCharges: 1,
                    maximumCharges: 1,
                    cooldownRemaining: default);
                slots.Append(in slot);
            }
            return new GameWorldState
            {
                CollectedAtFrame = 10,
                CollectedAtEpoch = Epoch,
                SpellSlots = WorldSpellSlotDeriver.Build(slots),
                SpellCosts = PublicationTable<WorldSpellCost>.Empty,
            };
        }
    }

    private static SuiteRuntimeConfiguration Configuration() =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoCast = new AutoCastConfiguration
            {
                Mode = AutoCastOperationMode.Active,
                FullCharge = false,
                RitualAware = false,
            },
        };
}
