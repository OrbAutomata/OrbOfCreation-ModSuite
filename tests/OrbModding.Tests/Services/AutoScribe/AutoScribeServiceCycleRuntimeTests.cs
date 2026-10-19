using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

/// <summary>
/// A craft the game charges for without admitting any work backs its own recipe off for a minute,
/// doubling up to half an hour, while Auto Scribe stays running and says what it paid. The test clock
/// moves only when a test means to bring a retry due.
/// </summary>
[Collection(GameActionFaultLogCollection.Name)]
public sealed class AutoScribeServiceCycleRuntimeTests : IDisposable
{
    private static readonly MonotonicDuration FirstWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1));

    public AutoScribeServiceCycleRuntimeTests() => AutoScribeNativeFixture.ResetStubs();

    public void Dispose()
    {
        GameActionFaultLog.ConfigureLog(null);
        AutoScribeNativeFixture.ResetStubs();
    }

    [Fact]
    public void APaidCraftWithNoWorkBacksItsRecipeOffAMinuteDoublingWhileAutoScribeStaysDegraded()
    {
        using var harness = new Harness();
        harness.Native.Recipe.ThrowAfterPurchase = true;

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var first = harness.RetryGate.Latest;
        var status = harness.Status.Current;
        harness.Advance(FirstWait - MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
        harness.PumpUntilTheNewestWorldIsEvaluated();
        var purchasesBeforeDue = harness.Native.Recipe.PurchaseCalls;
        harness.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(2)));
        harness.PumpWorldsUntil(() => harness.Native.Recipe.PurchaseCalls == 2);
        var second = harness.RetryGate.Latest;

        Assert.Equal(AutoScribeServicePolicies.RetryTarget(harness.Native.Action.RecipeId), first.Target);
        Assert.Equal(AutoScribeActionResultCodes.PostPaymentFault, first.Code);
        Assert.Equal(FirstWait, first.Backoff);
        Assert.False(first.IsQuiet);
        Assert.Equal(1, purchasesBeforeDue);
        Assert.Equal(2, second.ConsecutiveFailures);
        Assert.Equal(MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(2)), second.Backoff);
        Assert.Equal(FeatureStatusReasonCode.NativeMutationFailed, status.Reason.Code);
        Assert.Contains("after 1 failed attempt(s)", status.Reason.Summary);
        Assert.Contains(
            $"Auto Scribe paid 5 {EntityIdentityFormatter.PlayerName(harness.Native.Resource.GetGuid())} for " +
            $"{EntityIdentityFormatter.PlayerName(harness.Native.Action.RecipeId)} and saw no work after Payment:",
            status.Reason.Summary);
        Assert.True(status.ConfiguredEnabled);
    }

    [Fact]
    public void AnUnexpectedThrowAfterPaymentBacksTheRecipeOffAsLongAndLogsTheException()
    {
        var log = new List<string>();
        GameActionFaultLog.ConfigureLog(log.Add);
        using var harness = new Harness();
        harness.Native.Recipe.AfterPurchaseFault = new NullReferenceException("injected failure after purchase");

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var entry = harness.RetryGate.Latest;
        harness.Advance(FirstWait - MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(1)));
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(AutoScribeActionResultCodes.PostPaymentFault, entry.Code);
        Assert.Equal(FirstWait, entry.Backoff);
        Assert.Equal(1, harness.Native.Recipe.PurchaseCalls);
        var logged = Assert.Single(log);
        Assert.Contains("System.NullReferenceException: injected failure after purchase", logged);
        Assert.DoesNotContain("injected failure", harness.Status.Current.Reason.Summary);
        Assert.Contains(" Reference " + logged.Split(' ')[3] + " is in the suite log.", harness.Status.Current.Reason.Summary);
    }

    private sealed class Harness : IDisposable
    {
        private const long Epoch = 1;
        private readonly ServiceCycleRegistry _registry;
        private readonly AutomataServiceCycleRuntime _runtime;
        private readonly ThreadSafeTestClock _clock = new(1_000);
        private readonly AutoScribeIdentityProfile _profile = AutoScribeIdentityCatalog.Audited;
        private readonly AutoScribeNativeFixture _native = new();
        private readonly AutomataServiceCycleHost _host;
        private long _frame = 10;
        private ulong _generation = 2;

        internal Harness()
        {
            Native = _native.Build(_profile);
            Status = new AutomataFeatureStatusReporter(
                new FeatureStatusRegistry(),
                new FeatureStatusSnapshot(
                    new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoScribeFeatureId),
                    "Auto Scribe",
                    true,
                    FeatureStatusState.NotReady,
                    new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "not ready"),
                    Epoch));
            _registry = new ServiceCycleRegistry(
                1,
                Configuration(_profile.Roles[0].Key.Value),
                new ConfigGeneration(1),
                new LifecycleGeneration((ulong)Epoch),
                _clock);
            var feature = (AutoScribeServiceCycleFeature.Runtime)new AutoScribeServiceCycleFeature(
                new AutoScribeFeatureDependencies(
                    _native.Resolver(static () => Epoch),
                    _profile,
                    static () => Epoch,
                    ownsActionFamily: static () => true,
                    tryCaptureMutationPermit: static () => true,
                    readOwnershipFailure: static () => string.Empty,
                    Status)).Register(
                new AutomataServiceCycleFeatureContext(
                    _registry, new ConfigGeneration(1), Epoch, new LifecycleGameActions()));
            _registry.WorldPublication.Publish(World(_profile, Epoch), new WorldGeneration(_generation));
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

        internal AutoScribeNativeFixtureState Native { get; }
        internal AutomataFeatureStatusReporter Status { get; }
        internal ServiceRetryGateSnapshot RetryGate => Service.RetryGate;

        private ServiceCycleServiceDiagnosticsSnapshot Service =>
            AvailableServiceDiagnostics.Await(_host.Pump, AutoScribeServicePolicies.ServiceId);

        internal void Advance(MonotonicDuration duration) => _clock.Advance(duration);

        internal void PumpWorldsUntil(Func<bool> condition)
        {
            var deadline = Stopwatch.StartNew();
            while (!condition())
            {
                PumpWorld();
                if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                    throw new TimeoutException("Auto Scribe did not reach the expected state.");
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
                    throw new TimeoutException("Auto Scribe did not evaluate the newest world.");
                Thread.Yield();
            }
        }

        private static bool EvaluatedAndDrained(ServiceCycleServiceDiagnosticsSnapshot service, WorldGeneration world) =>
            service.LatestProjection.Context.Cycle.World == world && !service.ActiveBatch.IsPresent;

        internal void PumpWorld()
        {
            _generation = Math.Max(_generation + 1, checked((ulong)_frame));
            _registry.WorldPublication.Publish(World(_profile, Epoch), new WorldGeneration(_generation));
            _runtime.Tick(0);
        }

        public void Dispose()
        {
            _runtime.Dispose();
            _registry.Dispose();
            Status.Dispose();
        }
    }

    private static SuiteRuntimeConfiguration Configuration(string roles) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = AutoScribeOperationMode.Active,
                Roles = roles,
            },
        };

    private static GameWorldState World(AutoScribeIdentityProfile profile, long epoch)
    {
        var recipes = new List<WorldScribeRecipe>();
        var consumables = new List<WorldConsumable>();
        var targets = new List<WorldScrollTarget>();
        var evidence = new List<WorldScrollTargetEvidence>();
        for (var index = 0; index < profile.Roles.Count; index++)
        {
            var role = profile.Roles[index];
            if (!role.IsProducible) continue;
            recipes.Add(new WorldScribeRecipe(
                role.Recipe!.Value.Uuid,
                profile.RecipeType.Uuid,
                role.Scroll.Uuid,
                visible: true,
                usesQuantityAsLevel: true));
            consumables.Add(Scroll(role.Scroll.Uuid));
            targets.Add(new WorldScrollTarget(
                role.Scroll.Uuid,
                role.Enchantment.Uuid,
                Guid.NewGuid()));
            evidence.Add(new WorldScrollTargetEvidence(
                role.Scroll.Uuid,
                role.Enchantment.Uuid,
                1));
        }

        return new GameWorldState
        {
            CollectedAtFrame = 10,
            CollectedAtEpoch = epoch,
            CollectionCategories = Table(new WorldCollectionCategoryStatus(
                ScrollCoveragePlanner.CollectionCategory,
                WorldCategoryOutcome.Collected,
                sampled: 1,
                skipped: 0,
                firstFailure: string.Empty)),
            ScribeRecipes = Table(recipes.ToArray()),
            ScribeQueues = Table(
                new WorldScribeQueue(
                    profile.ActiveInstances.Uuid,
                    isAutomatic: false,
                    used: 0,
                    maximum: 1),
                new WorldScribeQueue(
                    profile.AutomaticInstances.Uuid,
                    isAutomatic: true,
                    used: 0,
                    maximum: 1)),
            Consumables = Table(consumables.ToArray()),
            ScrollTargets = Table(targets.ToArray()),
            ScrollTargetEvidence = Table(evidence.ToArray()),
            CraftingRecipeTypes = Table(new WorldCraftingRecipeType(
                profile.RecipeType.Uuid,
                startingLevel: 1,
                maxStartingLevel: 3,
                craftVerb: "Scribe",
                isLevelType: true,
                initiated: true,
                magnitudeLoss: 0,
                magnitudeTime: 0,
                magnitudeIncrement: BigDouble.Zero)),
        };
    }

    private static WorldConsumable Scroll(Guid scrollId)
    {
        var modifiers = default(RawConsumableModifiers);
        return new WorldConsumable(
            scrollId,
            visible: true,
            randomized: true,
            quantity: 0,
            queuedQuantity: 0,
            maximumCarryLoad: 4,
            gainedSince: 0,
            maxCreatedLevel: 3,
            currentPrepTime: BigDouble.Zero,
            currentCooldown: BigDouble.Zero,
            currentCooldownTime: BigDouble.Zero,
            in modifiers,
            preparationTime: 0,
            canBeRandomized: true,
            hasDuration: false,
            durationBase: 0,
            queueOnStart: false);
    }

    private static PublicationTable<T> Table<T>(params T[] rows)
        where T : struct => PublicationTable<T>.Create(rows, rows.Length);
}
