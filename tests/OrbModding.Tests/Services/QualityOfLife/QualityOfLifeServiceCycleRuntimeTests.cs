#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbAutomata.GameMcp;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.QualityOfLife;

public sealed class QualityOfLifeServiceCycleRuntimeTests
{
    [Fact]
    public void Published_passives_drive_native_mutes_only_for_eligible_supported_notifications()
    {
        var passives = QualityOfLifePolicy.SupportedPassives.Select(passive => Passive(passive.Uuid)).ToArray();
        passives[2].muted = true;
        passives[3].touched = false;
        passives[4].hidden = true;
        passives[5].silent = true;
        for (var index = 6; index < passives.Length; index++) passives[index].touched = false;
        var unrelated = Passive(Guid.NewGuid());
        var published = passives.Append(unrelated).ToArray();
        var originalEffects = published.Select(passive => passive.passiveEffectBlocks).ToArray();
        using var harness = new Harness(published);

        harness.DrainCurrentWorld();

        Assert.True(passives[0].muted);
        Assert.True(passives[1].muted);
        Assert.True(passives[2].muted);
        Assert.All(passives.Skip(3), passive => Assert.False(passive.muted));
        Assert.False(unrelated.muted);
        Assert.True(passives[0].touched);
        Assert.True(passives[1].touched);
        for (var index = 0; index < published.Length; index++)
            Assert.Same(originalEffects[index], published[index].passiveEffectBlocks);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
        Assert.Equal(ServiceActionDisposition.Committed, harness.Service.LastAction.Result.Disposition);
        Assert.Equal(0, harness.Service.RetryGate.Count);
        Assert.Empty(harness.Warnings);
        // Every Tick also asserts the actual number of changed native flags is at most one.
        Assert.Equal(2, harness.NewlyMutedCount);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("policy-off")]
    [InlineData("suite-disabled")]
    [InlineData("emergency")]
    public void Disabled_or_stopped_configuration_never_opens_a_mutation_cycle(string stop)
    {
        var config = Configuration();
        config = stop switch
        {
            "disabled" => config with { QualityOfLife = config.QualityOfLife with { Mode = QualityOfLifeOperationMode.Disabled } },
            "policy-off" => config with { QualityOfLife = config.QualityOfLife with { MutePassiveNotifications = false } },
            "suite-disabled" => config with { General = config.General with { Enabled = false } },
            "emergency" => config with { Safety = config.Safety with { EmergencyDisable = true } },
            _ => throw new ArgumentOutOfRangeException(nameof(stop)),
        };
        var target = Passive(KnownEntities.PassiveReflectiveMentalLearning.Uuid);
        using var harness = new Harness(new[] { target }, config);

        harness.WaitForStartGate();

        Assert.False(target.muted);
        Assert.False(harness.Service.LastCapture.IsPresent);
        Assert.False(harness.Service.LastAction.IsPresent);
        if (stop == "emergency") Assert.True(harness.EmergencyStopped);
        else Assert.False(harness.Service.LastStartDecision.Decision.ShouldStart);
    }

    [Theory]
    [InlineData("clean-empty", true)]
    [InlineData("missing", false)]
    [InlineData("unavailable", false)]
    [InlineData("skipped", false)]
    [InlineData("read-fault", false)]
    public void Empty_success_and_failed_or_missing_world_evidence_have_distinct_health(string evidence, bool ready)
    {
        var target = Passive(KnownEntities.PassiveReflectiveMentalLearning.Uuid);
        using var harness = new Harness(new[] { target }, evidence: evidence);

        harness.DrainCurrentWorld();

        Assert.False(target.muted);
        Assert.False(harness.Service.LastAction.IsPresent);
        Assert.Equal(ready ? FeatureStatusState.Operational : FeatureStatusState.NotReady, harness.Status.Current.State);
        if (!ready)
        {
            Assert.Equal(FeatureStatusReasonCode.RegistryNotReady, harness.Status.Current.Reason.Code);
            Assert.Contains("clean passive-ability world publication", harness.Status.Current.Reason.Summary);
        }
    }

    [Theory]
    [InlineData("eligibility")]
    [InlineData("epoch")]
    [InlineData("ownership")]
    [InlineData("permit")]
    [InlineData("identity")]
    public void Pending_actions_revalidate_live_authority_after_the_first_planned_mutation(string changed)
    {
        var first = Passive(QualityOfLifePolicy.SupportedPassives[0].Uuid);
        var second = Passive(QualityOfLifePolicy.SupportedPassives[1].Uuid);
        using var harness = new Harness(new[] { first, second });
        harness.PumpUntil(() => first.muted, "the first notification mute");
        Assert.False(second.muted);
        Assert.True(harness.Service.ActiveBatch.IsPresent);
        var expected = changed switch
        {
            "eligibility" => QualityOfLifeActionResultCodes.NotEligible,
            "epoch" => CommonActionResultCodes.LifecycleReplaced,
            "identity" => QualityOfLifeActionResultCodes.IdentityUnavailable,
            "ownership" or "permit" => QualityOfLifeActionResultCodes.MutationPermitUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(changed)),
        };
        switch (changed)
        {
            case "eligibility": second.touched = false; break;
            case "epoch": harness.AdvanceNativeEpoch(); break;
            case "ownership": harness.OwnsFamily = false; break;
            case "permit": harness.PermitAvailable = false; break;
            case "identity": harness.ReplaceWithWrongNativeType(second.GetGuid()); break;
        }

        harness.DrainCurrentWorld();

        Assert.True(first.muted);
        Assert.False(second.muted);
        Assert.Equal(expected, harness.Service.LastAction.Result.Code);
        Assert.Equal(ServiceActionDisposition.Rejected, harness.Service.LastAction.Result.Disposition);
        Assert.Equal(changed == "identity" ? 1 : 0, harness.Service.RetryGate.Count);
        if (changed == "identity")
        {
            var retry = harness.Service.RetryGate.Latest;
            Assert.Equal(QualityOfLifeService.RetryTarget(second.GetGuid()), retry.Target);
            Assert.Equal(QualityOfLifeActionResultCodes.IdentityUnavailable, retry.Code);
            Assert.Equal(harness.Service.LastAction.Result.Reason, retry.Reason);
            Assert.Contains("EquipmentTypeSO, not PassiveAbilitySO", retry.Reason);
            Assert.Equal(FeatureStatusState.Degraded, harness.Status.Current.State);
            Assert.Equal(FeatureStatusReasonCode.NativeMutationFailed, harness.Status.Current.Reason.Code);
            Assert.Equal("Retrying " + EntityIdentityFormatter.Format(second.GetGuid()) + ": " + retry.Reason,
                harness.Status.Current.Reason.Summary);
        }
        if (changed == "ownership")
        {
            Assert.Equal(FeatureStatusState.TemporarilyBlocked, harness.Status.Current.State);
            Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, harness.Status.Current.Reason.Code);
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceCycleRegistry _registry;
        private readonly AutomataServiceCycleHost _host;
        private readonly AutomataServiceCycleRuntime _runtime;
        private readonly PassiveAbilitySO[] _native;
        private readonly IDictionary _nativeRegistry = new Hashtable();
        private readonly WorldGeneration _worldGeneration = new(2);
        private long _epoch = 1;
        private long _frame = 10;
        private readonly int _initialMuted;

        internal Harness(PassiveAbilitySO[] passives, SuiteRuntimeConfiguration? config = null, string evidence = "clean")
        {
            config ??= Configuration();
            _native = passives;
            _initialMuted = passives.Count(passive => passive.muted);
            foreach (var passive in passives) _nativeRegistry.Add(passive.GetGuid(), passive);
            var configured = config.QualityOfLife.Mode == QualityOfLifeOperationMode.Active;
            Status = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(), new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, QualityOfLifeFeatureContribution.FeatureId),
                "Quality of Life", configured,
                configured ? FeatureStatusState.NotReady : FeatureStatusState.ConfigurationDisabled,
                new FeatureStatusReason(configured ? FeatureStatusReasonCode.GameplayNotReady : FeatureStatusReasonCode.ConfigurationDisabled,
                    configured ? "Waiting for a world publication." : "Quality of Life is disabled."), _epoch));
            var gameActions = new LifecycleGameActions();
#if SERVICE_CYCLE_PROFILE
            var profileProbe = new ServiceCycleProfileProbe();
#endif
            _registry = new ServiceCycleRegistry(1, config, new ConfigGeneration(1),
                new LifecycleGeneration((ulong)_epoch), new ThreadSafeTestClock(1_000));
            var feature = new QualityOfLifeServiceCycleFeature(new QualityOfLifeFeatureDependencies(
                TestRegistryResolver.For(() => _epoch, _nativeRegistry), () => _epoch,
                () => OwnsFamily, () => PermitAvailable, () => "Notification-mute ownership is unavailable.",
                Status, Warnings.Add)).Register(new AutomataServiceCycleFeatureContext(
                    _registry, new ConfigGeneration(1), _epoch, gameActions
#if SERVICE_CYCLE_PROFILE
                    , profileProbe
#endif
                    ));
            _registry.WorldPublication.Publish(World(passives, evidence, _epoch), _worldGeneration);
            _host = new AutomataServiceCycleHost(_registry, () => _frame++, pumpTiming: null, semanticTrace: null
#if SERVICE_CYCLE_PROFILE
                , profileProbe: profileProbe
#endif
                );
            _runtime = new AutomataServiceCycleRuntime(() => _epoch, _registry.Configuration, _host,
                new[] { feature }, new ConfigGeneration(1), gameActions
#if SERVICE_CYCLE_PROFILE
                , new GameMcpCommandDispatcher(_host, _registry.Configuration, gameActions, new[] { feature })
#endif
                );
            feature.ActivateDiagnostics();
        }

        internal AutomataFeatureStatusReporter Status { get; }
        internal List<string> Warnings { get; } = new();
        internal bool OwnsFamily { get; set; } = true;
        internal bool PermitAvailable { get; set; } = true;
        internal bool EmergencyStopped => _runtime.EmergencyStopEngaged;
        internal int NewlyMutedCount => _native.Count(passive => passive.muted) - _initialMuted;
        internal ServiceCycleServiceDiagnosticsSnapshot Service =>
            AvailableServiceDiagnostics.Await(_host.Pump, QualityOfLifeService.ServiceId);

        internal void AdvanceNativeEpoch() => _epoch++;

        internal void ReplaceWithWrongNativeType(Guid id)
        {
            var replacement = new EquipmentTypeSO();
            replacement.SetGuid(id);
            _nativeRegistry[id] = replacement;
        }

        internal void WaitForStartGate()
        {
            Tick();
            PumpUntil(() => EmergencyStopped || Service.LastStartDecision.IsPresent, "the configuration start gate");
        }

        internal void DrainCurrentWorld()
        {
            PumpUntil(() => Service.LatestProjection.Context.Cycle.World == _worldGeneration && !Service.ActiveBatch.IsPresent,
                "the published Quality of Life cycle to finish");
        }

        internal void PumpUntil(Func<bool> condition, string expectation)
        {
            var deadline = new ServiceCycleTestDeadline.Budget();
            deadline.For(() =>
            {
                if (condition()) return true;
                Tick();
                return condition();
            }, expectation);
        }

        private void Tick()
        {
            var before = NewlyMutedCount;
            _runtime.Tick(0);
            Assert.InRange(NewlyMutedCount - before, 0, 1);
        }

        public void Dispose()
        {
            _runtime.Dispose();
            _registry.Dispose();
            Status.Dispose();
        }
    }

    private static SuiteRuntimeConfiguration Configuration() => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        QualityOfLife = new QualityOfLifeConfiguration { Mode = QualityOfLifeOperationMode.Active },
    };

    private static GameWorldState World(PassiveAbilitySO[] native, string evidence, long epoch)
    {
        var rows = evidence == "clean-empty" ? Array.Empty<WorldPassiveAbility>() : native.Select(Row).OrderBy(row => row.EntityId).ToArray();
        var categories = evidence == "missing" ? Array.Empty<WorldCollectionCategoryStatus>() : new[]
        {
            new WorldCollectionCategoryStatus("passive abilities",
                evidence == "unavailable" ? WorldCategoryOutcome.Unavailable : WorldCategoryOutcome.Collected,
                sampled: rows.Length, skipped: evidence == "skipped" ? 1 : 0,
                firstFailure: evidence == "read-fault"
                    ? new WorldCollectionFailure("Native passive read failed.", hasReadFault: true)
                    : default),
        };
        return new GameWorldState
        {
            CollectedAtFrame = 10,
            CollectedAtEpoch = epoch,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(categories, categories.Length),
            PassiveAbilities = PublicationTable<WorldPassiveAbility>.Create(rows, rows.Length),
        };
    }

    private static WorldPassiveAbility Row(PassiveAbilitySO passive) => new(
        passive.GetGuid(), passive.muted, passive.touched, passive.hidden, passive.silent, passive.global,
        passive.startOnCooldown, passive.ignoreReactionCooldown, passive.reactionTokenCost, passive.maxTokens,
        passive.minTokenForEffect, passive.tokenIndividuateDuration, passive.applyWhileRecharging,
        passive.expireAttachedStatusEffect, BigDouble.Zero);

    private static PassiveAbilitySO Passive(Guid id)
    {
        var passive = new PassiveAbilitySO { touched = true };
        passive.SetGuid(id);
        return passive;
    }
}
