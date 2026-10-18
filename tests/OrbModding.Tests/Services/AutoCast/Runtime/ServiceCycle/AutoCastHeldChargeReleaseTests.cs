using System;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Runtime.ServiceCycle;

/// <summary>
/// A full-charge hold Auto Cast took is let go when Auto Cast stops being allowed to hold it.
/// </summary>
/// <remarks>
/// The game keeps every charge input source until it is removed (<c>Spell.SetChargeInput</c> adds
/// and removes a named source; <c>Spell.Increment</c> keeps charging while any source remains). A
/// source Auto Cast leaves behind makes every later cast of that spell, the player's own included,
/// charge to full.
/// </remarks>
public sealed class AutoCastHeldChargeReleaseTests : IDisposable
{
    private const long NativeEpoch = 7;
    private static readonly Guid Ember = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public AutoCastHeldChargeReleaseTests() => ResetNativeState();

    public void Dispose() => ResetNativeState();

    [Theory]
    [InlineData("ownership")]
    [InlineData("emergency")]
    [InlineData("manual-pause")]
    [InlineData("shutdown")]
    public void AHeldChargeIsReleasedWhenAutoCastMayNoLongerHoldIt(string transition)
    {
        var spell = Equip(Ember);
        using var fixture = new Fixture();
        fixture.FrameUntil(() => spell.HoldingCharge);

        switch (transition)
        {
            case "ownership":
                fixture.Owned = false;
                fixture.Frame();
                break;
            case "emergency":
                fixture.Configure(enabled: true, emergencyDisable: true);
                fixture.Frame();
                break;
            case "manual-pause":
                AutoCastManualSignal.NotifySpellFire();
                fixture.PublishWorld(4);
                fixture.Frame();
                fixture.Frame();
                break;
            case "shutdown":
                fixture.Runtime.DisposeRegistration();
                break;
        }

        Assert.False(spell.HoldingCharge);
    }

    [Fact]
    public void AHoldIsKeptWhileAutoCastMayStillHoldIt()
    {
        var spell = Equip(Ember);
        using var fixture = new Fixture();
        fixture.FrameUntil(() => spell.HoldingCharge);

        fixture.Frame();

        Assert.True(spell.HoldingCharge);
    }

    [Fact]
    public void AChargeTheMcpCallerHoldsIsNotAutoCastsToRelease()
    {
        var spell = Equip(Ember);
        using var fixture = new Fixture(enabled: false);
        fixture.Owned = false;
        spell.SetChargeInput("player", true);

        fixture.Frame();

        Assert.True(spell.HoldingCharge);
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
        global::TargetingManager.Reset();
        global::Spell.FireSignal = AutoCastManualSignal.NotifySpellFire;
        global::BattleManager.instance = null;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceCycleRegistry _registry;
        private readonly AutomataFeatureStatusReporter _status;
        private long _frame;

        public Fixture(bool enabled = true)
        {
            _registry = new ServiceCycleRegistry(1, new LifecycleGeneration(NativeEpoch));
            Configure(enabled, emergencyDisable: false);
            _status = new AutomataFeatureStatusReporter(
                new FeatureStatusRegistry(),
                new FeatureStatusSnapshot(
                    new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoCastFeatureId),
                    "Auto Cast",
                    true,
                    FeatureStatusState.NotReady,
                    new FeatureStatusReason(
                        FeatureStatusReasonCode.GameplayNotReady,
                        "Gameplay lifecycle is not ready."),
                    lifecycleGeneration: NativeEpoch));
            var feature = new AutoCastServiceCycleFeature(new AutoCastFeatureDependencies(
                () => NativeEpoch,
                () => Owned,
                new AutoCastManualPauseState(),
                _status));
            Runtime = (AutoCastFeatureRuntime)feature.Register(
                new AutomataServiceCycleFeatureContext(
                    _registry, new ConfigGeneration(1), NativeEpoch, new LifecycleGameActions()));
            PublishWorld(2);
            _registry.Seal();
            Pump = new SuiteFramePump(_registry);
        }

        public bool Owned { get; set; } = true;

        public AutoCastFeatureRuntime Runtime { get; }

        public SuiteFramePump Pump { get; }

        public void Configure(bool enabled, bool emergencyDisable) =>
            _registry.ConfigurationPublication.Publish(new SuiteRuntimeConfiguration
            {
                General = new SuiteGeneralConfiguration { Enabled = true },
                Safety = new SuiteSafetyConfiguration { EmergencyDisable = emergencyDisable },
                AutoCast = new AutoCastConfiguration
                {
                    Mode = enabled ? AutoCastOperationMode.Active : AutoCastOperationMode.Disabled,
                    FullCharge = true,
                    RitualAware = false,
                    ManualPauseSeconds = 30f,
                },
            });

        public void FrameUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "the condition never held");
                Frame();
                Thread.Sleep(1);
            }
        }

        public void PublishWorld(ulong generation) =>
            _registry.WorldPublication.Publish(ChargingWorld(), new WorldGeneration(generation));

        public void Frame()
        {
            var report = Pump.PumpFrame(++_frame);
            Runtime.ObserveFrame(Pump, in report);
        }

        public void Dispose()
        {
            Pump.Dispose();
            _registry.Dispose();
            _status.Dispose();
        }

        private static GameWorldState ChargingWorld()
        {
            var slots = new WorldSpellSlotBuffer();
            var slot = new WorldSpellSlot(
                0, Ember,
                occupied: true,
                casting: false,
                readyingCast: false,
                attuning: false,
                channeled: false,
                toggled: false,
                chargeable: true,
                castReady: true,
                chargeAvailable: true,
                resourcesCovered: true,
                currentCharges: 1,
                maximumCharges: 1,
                cooldownRemaining: default);
            slots.Append(in slot);
            return new GameWorldState
            {
                CollectedAtEpoch = NativeEpoch,
                SpellSlots = WorldSpellSlotDeriver.Build(slots),
                SpellCosts = PublicationTable<WorldSpellCost>.Empty,
            };
        }
    }
}
