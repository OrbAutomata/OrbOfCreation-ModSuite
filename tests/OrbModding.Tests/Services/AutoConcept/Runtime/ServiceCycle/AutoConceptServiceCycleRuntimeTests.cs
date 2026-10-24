#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbAutomata.GameMcp;
#endif
using OrbModding.TestSupport;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

namespace OrbModding.Tests.Services.AutoConcept.Runtime.ServiceCycle;

/// <summary>
/// A Concept add or remove that did not land backs that Concept's action of that kind off on the
/// retry gate and hands the turn to the next Concept, and the status says which Concept and why. A
/// commit or a Concept that stops being a candidate ends the episode. Only a contract mismatch the
/// game proved faults the feature until the next lifecycle, refusing every action without backing
/// one off; a list that is empty or unreadable during load is retried. The test clock moves only
/// when a test means to bring a wait due.
/// </summary>
[Collection(GameActionFaultLogCollection.Name)]
public sealed class AutoConceptServiceCycleRuntimeTests : IDisposable
{
    private static readonly Guid Thought = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Insight = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Notion = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly MonotonicDuration FirstWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(250));
    private static readonly MonotonicDuration PastTheServiceWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromMilliseconds(300));
    private static readonly MonotonicDuration PastEveryWait = MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(30));
    private readonly List<string> _faultLog = new();

    public AutoConceptServiceCycleRuntimeTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        GameActionFaultLog.ConfigureLog(_faultLog.Add);
    }

    public void Dispose()
    {
        GameActionFaultLog.ConfigureLog(null);
        IdScriptableObject.RuntimeLookup.Clear();
    }

    [Fact]
    public void AnAddThatDidNotLandBacksThatConceptOffWhileTheOtherConceptIsSlotted()
    {
        var thought = Concept(Thought, "Thought");
        var insight = Concept(Insight, "Insight");
        using var harness = new Harness(thought, insight);
        harness.Active.SuppressAddMutationOf = thought;

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var faulted = harness.RetryGate.Latest;
        var status = harness.Status.Current;
        harness.Advance(PastTheServiceWait);
        harness.PumpWorldsUntil(() => harness.Queued(insight) == 1);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(AutoConceptServicePolicies.RetryTarget(AutoConceptActionKind.Add, Thought), faulted.Target);
        Assert.Equal(FirstWait, faulted.Backoff);
        Assert.True(harness.RetryGate.TryFind(faulted.Target, out _));
        Assert.Equal(0, harness.Queued(thought));
        Assert.Equal(FeatureStatusReasonCode.NativeMutationFailed, status.Reason.Code);
        Assert.Contains(
            "is backing off adding " + EntityIdentityFormatter.Format(Thought) + " after 1 failed attempt(s)",
            status.Reason.Summary);
        Assert.Contains("queued stacks went from 0 to 0", status.Reason.Summary);
    }

    [Fact]
    public void AnUnexpectedThrowBeforeAnAddLandsStillLetsTheNextConceptBeSlotted()
    {
        const string cause = "injected defect before the add landed";
        var thought = Concept(Thought, "Thought");
        var insight = Concept(Insight, "Insight");
        using var harness = new Harness(thought, insight);
        harness.Active.AddFailure = new NullReferenceException(cause);
        harness.Active.AddFailureFor = thought;

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        var faulted = harness.RetryGate.Latest.Target;
        harness.Advance(PastTheServiceWait);
        harness.PumpWorldsUntil(() => harness.Queued(insight) == 1);

        Assert.Equal(AutoConceptServicePolicies.RetryTarget(AutoConceptActionKind.Add, Thought), faulted);
        Assert.Equal(0, harness.Queued(thought));
        Assert.Contains(_faultLog, entry => entry.Contains(cause, StringComparison.Ordinal));
    }

    [Fact]
    public void ARemoveThatDidNotLandHandsTheTurnToTheNextConceptBeforeItIsTriedAgain()
    {
        var thought = Concept(Thought, "Thought");
        var insight = Concept(Insight, "Insight");
        insight.discovered = false;
        using var harness = new Harness(thought, insight);
        harness.PumpWorldsUntil(() => harness.Queued(thought) == 1);
        harness.PumpUntilTheNewestWorldIsEvaluated();
        harness.Active.SuppressRemoveMutation = true;
        harness.DrainRatio = new BigDouble(0.5);

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        var remove = harness.RetryGate.Latest.Target;
        insight.discovered = true;
        harness.Advance(PastTheServiceWait);
        harness.PumpWorldsUntil(() =>
            harness.Queued(insight) == 1 ||
            harness.RetryGate.TryFind(remove, out var retried) && retried.ConsecutiveFailures > 1);

        Assert.Equal(AutoConceptServicePolicies.RetryTarget(AutoConceptActionKind.RemoveOwned, Thought), remove);
        Assert.Equal(1, harness.Queued(insight));
    }

    [Fact]
    public void AnAddThatLandsWhenItIsDueEndsTheConceptsEpisode()
    {
        var thought = Concept(Thought, "Thought");
        using var harness = new Harness(thought);
        harness.Active.SuppressAddMutationOf = thought;

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        harness.Active.SuppressAddMutationOf = null;
        harness.Advance(PastTheServiceWait);
        harness.PumpWorldsUntil(() => harness.Queued(thought) == 1);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(0, harness.RetryGate.Count);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    [Fact]
    public void AnAddThatIsBackingOffDoesNotHoldBackARemoveOfTheSameConcept()
    {
        var thought = Concept(Thought, "Thought", maximum: 2);
        using var harness = new Harness(thought);
        harness.PumpWorldsUntil(() => harness.Queued(thought) == 2);
        harness.PumpUntilTheNewestWorldIsEvaluated();
        harness.Active.SuppressAddMutationOf = thought;
        thought.maxUsageSlots = new ValueModifierRecord(new BigDouble(3, 0));

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        var add = harness.RetryGate.Latest.Target;
        harness.DrainRatio = new BigDouble(0.5);
        harness.Advance(PastTheServiceWait);
        harness.PumpWorldsUntil(() => harness.Queued(thought) == 0);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(AutoConceptServicePolicies.RetryTarget(AutoConceptActionKind.Add, Thought), add);
        Assert.True(harness.RetryGate.TryFind(add, out _));
        Assert.False(harness.RetryGate.TryFind(
            AutoConceptServicePolicies.RetryTarget(AutoConceptActionKind.RemoveOwned, Thought), out _));
    }

    [Fact]
    public void AConceptThatStopsBeingACandidateWhileItBacksOffEndsItsEpisodeBeforeItIsDue()
    {
        var thought = Concept(Thought, "Thought");
        using var harness = new Harness(thought);
        harness.Active.SuppressAddMutationOf = thought;

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        thought.discovered = false;
        harness.Advance(PastTheServiceWait);
        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 0);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(0, harness.Queued(thought));
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    [Fact]
    public void AnAddThatLandsBeforeTheGameThrowsCommitsAndLogsTheThrow()
    {
        var thought = Concept(Thought, "Thought");
        using var harness = new Harness(thought);
        harness.Active.AddFailureAfterWrite = new NullReferenceException("injected defect after the add landed");

        harness.PumpWorldsUntil(() => harness.Queued(thought) == 1);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(0, harness.RetryGate.Count);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
        Assert.Contains(_faultLog, entry => entry.Contains("injected defect after the add landed", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAddTheGameThrewOnSaysSoWithoutTheExceptionsTextAndLogsIt()
    {
        const string cause = "injected add failure";
        var thought = Concept(Thought, "Thought");
        using var harness = new Harness(thought);
        harness.Active.AddFailure = new InvalidOperationException(cause);

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var status = harness.Status.Current;
        var faulted = harness.RetryGate.Latest;

        Assert.Contains("after the game threw.", status.Reason.Summary);
        Assert.Contains("is in the suite log.", status.Reason.Summary);
        Assert.DoesNotContain(cause, status.Reason.Summary);
        Assert.DoesNotContain(cause, faulted.Reason);
        Assert.DoesNotContain("InvalidOperationException", faulted.Reason);
        Assert.Contains(_faultLog, entry => entry.Contains(cause, StringComparison.Ordinal));
    }

    [Fact]
    public void ConceptRecipesWhoseContentsAreUnreadableDuringLoadAreRetriedInTheSameLifecycle()
    {
        var thought = Concept(Thought, "Thought");
        using var harness = new Harness(thought);
        harness.Recipes.value = null!;

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        harness.Recipes.value = new List<AlchemyRecipeSO> { thought };
        harness.Advance(PastEveryWait);
        harness.PumpWorldsUntil(() => harness.Queued(thought) == 1);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(0, harness.RetryGate.Count);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    [Fact]
    public void AnEmptyConceptRecipesListIsRetriedInTheSameLifecycle()
    {
        var thought = Concept(Thought, "Thought");
        using var harness = new Harness(thought);
        harness.Recipes.value = new List<AlchemyRecipeSO>();

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        harness.Recipes.value = new List<AlchemyRecipeSO> { thought };
        harness.Advance(PastEveryWait);
        harness.PumpWorldsUntil(() => harness.Queued(thought) == 1);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(0, harness.RetryGate.Count);
    }

    [Fact]
    public void AConceptRecipesReadThatThrowsIsRetriedAndItsCauseStaysInTheLog()
    {
        const string cause = "injected concept registry read failure";
        var thought = Concept(Thought, "Thought");
        using var harness = new Harness(thought);
        harness.Recipes.GetGuidFailure = new InvalidOperationException(cause);

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Degraded);
        var status = harness.Status.Current;
        harness.Recipes.GetGuidFailure = null;
        harness.Advance(PastEveryWait);
        harness.PumpWorldsUntil(() => harness.Queued(thought) == 1);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.DoesNotContain(cause, status.Reason.Summary);
        Assert.Contains(_faultLog, entry => entry.Contains(cause, StringComparison.Ordinal));
        Assert.Equal(0, harness.RetryGate.Count);
    }

    [Fact]
    public void AFaultThatRepeatsUnderAFreshLogReferenceEachAttemptIsWarnedOnce()
    {
        const string cause = "injected concept registry read failure";
        var thought = Concept(Thought, "Thought");
        using var harness = new Harness(thought);
        harness.Recipes.GetGuidFailure = new InvalidOperationException(cause);

        harness.PumpWorldsUntil(() => harness.RetryGate.Count == 1);
        harness.Advance(PastEveryWait);
        harness.PumpWorldsUntil(() => harness.RetryGate.Latest.ConsecutiveFailures == 2);
        harness.Advance(PastEveryWait);
        harness.PumpWorldsUntil(() => harness.RetryGate.Latest.ConsecutiveFailures == 3);

        Assert.True(_faultLog.Count(entry => entry.Contains(cause, StringComparison.Ordinal)) >= 3);
        Assert.Single(harness.Warnings, warning => warning.StartsWith("Auto Concept did not complete", StringComparison.Ordinal));
    }

    [Fact]
    public void AProvenConceptContractMismatchFaultsTheFeatureForTheLifecycleAndTheNextOneProbesAgain()
    {
        const string held = "until the next scene or load";
        var thought = Concept(Thought, "Thought");
        var notion = Concept(Notion, "Notion");
        var impostor = new AlchemyRecipeSO(
            Insight.ToString("D"), "Impostor", new[] { new AlchemyTypeSO("ordinary-alchemy") });
        using var harness = new Harness(thought, notion);
        harness.Recipes.value = new List<AlchemyRecipeSO> { impostor };

        harness.PumpWorldsUntil(() => harness.Status.Current.State == FeatureStatusState.Faulted);
        var status = harness.Status.Current;
        harness.Recipes.value = new List<AlchemyRecipeSO> { thought, notion };
        harness.Advance(PastEveryWait);
        harness.PumpUntilTheNewestWorldIsEvaluated();
        harness.PumpUntilTheNewestWorldIsEvaluated();
        var heldAfterTheWait = harness.Queued(thought) + harness.Queued(notion);
        var gateWhileHeld = harness.RetryGate.Count;
        var stateWhileHeld = harness.Status.Current.State;
        harness.ReplaceLifecycle();
        harness.PumpWorldsUntil(() => harness.Queued(thought) == 1 && harness.Queued(notion) == 1);
        harness.PumpUntilTheNewestWorldIsEvaluated();

        Assert.Equal(FeatureStatusReasonCode.InvariantViolation, status.Reason.Code);
        Assert.Contains(held, status.Reason.Summary);
        Assert.Equal(0, heldAfterTheWait);
        Assert.Equal(0, gateWhileHeld);
        Assert.Equal(FeatureStatusState.Faulted, stateWhileHeld);
        Assert.Single(harness.Warnings, warning => warning.Contains(held, StringComparison.Ordinal));
        Assert.Equal(0, harness.RetryGate.Count);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
    }

    private static AlchemyRecipeSO Concept(Guid id, string name, int maximum = 1)
    {
        var type = new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        return new AlchemyRecipeSO(id.ToString("D"), name, new[] { type })
        {
            coreType = type,
            maxUsageSlots = new ValueModifierRecord(new BigDouble(maximum, 0)),
        };
    }

    private sealed class Harness : IDisposable
    {
        private readonly AlchemyRecipeSO[] _published;
        private readonly ServiceCycleRegistry _registry;
        private readonly AutomataServiceCycleRuntime _runtime;
        private readonly ThreadSafeTestClock _clock = new(1_000);
        private readonly AutomataServiceCycleHost _host;
        private long _epoch = 1;
        private long _frame = 10;
        private ulong _generation = 2;

        internal Harness(params AlchemyRecipeSO[] published)
        {
            var gameActions = new LifecycleGameActions();
#if SERVICE_CYCLE_PROFILE
            var profileProbe = new ServiceCycleProfileProbe();
#endif
            _published = published;
            Active = new AlchemyInstanceListVariable();
            Active.SetGuid(new Guid(AutoConceptNativeAdapter.ActiveConceptsUuid));
            Recipes = new AlchemyRecipeListVariable { value = published.ToList() };
            Recipes.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
            IdScriptableObject.RuntimeLookup[new Guid(AutoConceptNativeAdapter.ActiveConceptsUuid)] = Active;
            IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = Recipes;

            Status = new AutomataFeatureStatusReporter(
                new FeatureStatusRegistry(),
                new FeatureStatusSnapshot(
                    new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoConceptFeatureId),
                    "Auto Concept",
                    true,
                    FeatureStatusState.NotReady,
                    new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "not ready"),
                    _epoch));
            _registry = new ServiceCycleRegistry(
                1,
                Configuration(),
                new ConfigGeneration(1),
                new LifecycleGeneration((ulong)_epoch),
                _clock);
            var feature = new AutoConceptServiceCycleFeature(
                new AutoConceptFeatureDependencies(
                    () => _epoch,
                    static () => true,
                    Status,
                    Warnings.Enqueue)).Register(
                new AutomataServiceCycleFeatureContext(
                    _registry, new ConfigGeneration(1), _epoch, gameActions
#if SERVICE_CYCLE_PROFILE
            , profileProbe
#endif
            ));
            _registry.WorldPublication.Publish(World(), new WorldGeneration(_generation));
            _host = new AutomataServiceCycleHost(_registry, () => _frame++, pumpTiming: null, semanticTrace: null
#if SERVICE_CYCLE_PROFILE
            , profileProbe: profileProbe
#endif
            );
            _runtime = new AutomataServiceCycleRuntime(
                () => _epoch,
                _registry.Configuration,
                _host,
                new IAutomataServiceCycleFeatureRuntime[] { feature },
                new ConfigGeneration(1),
                gameActions
#if SERVICE_CYCLE_PROFILE
            , new GameMcpCommandDispatcher(_host, _registry.Configuration, gameActions,
                new IAutomataServiceCycleFeatureRuntime[] { feature })
#endif
            );
            feature.ActivateDiagnostics();
        }

        internal AlchemyInstanceListVariable Active { get; }
        internal AlchemyRecipeListVariable Recipes { get; }
        internal AutomataFeatureStatusReporter Status { get; }
        internal ConcurrentQueue<string> Warnings { get; } = new();
        internal BigDouble DrainRatio { get; set; } = new(1);
        internal ServiceRetryGateSnapshot RetryGate => Service.RetryGate;

        private ServiceCycleServiceDiagnosticsSnapshot Service =>
            AvailableServiceDiagnostics.Await(_host.Pump, AutoConceptServicePolicies.ServiceId);

        internal int Queued(AlchemyRecipeSO recipe) =>
            Active.value.SingleOrDefault(instance => ReferenceEquals(instance.reference, recipe))?.queuedQuantity ?? 0;

        internal void Advance(MonotonicDuration duration) => _clock.Advance(duration);

        internal void ReplaceLifecycle()
        {
            _epoch++;
            _runtime.InvalidateLifecycle();
        }

        internal void PumpWorldsUntil(Func<bool> condition)
        {
            var deadline = Stopwatch.StartNew();
            while (!condition())
            {
                PumpWorld();
                if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                    throw new TimeoutException("Auto Concept did not reach the expected state.");
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
                    throw new TimeoutException("Auto Concept did not evaluate the newest world.");
                Thread.Yield();
            }
            _runtime.Tick(0);
        }

        public void Dispose()
        {
            _runtime.Dispose();
            _registry.Dispose();
            Status.Dispose();
        }

        private static bool EvaluatedAndDrained(ServiceCycleServiceDiagnosticsSnapshot service, WorldGeneration world) =>
            service.LatestProjection.Context.Cycle.World == world && !service.ActiveBatch.IsPresent;

        private void PumpWorld()
        {
            Active.RebuildCounts();
            _generation = Math.Max(_generation + 1, checked((ulong)_frame));
            _registry.WorldPublication.Publish(World(), new WorldGeneration(_generation));
            _runtime.Tick(0);
        }

        private GameWorldState World()
        {
            var concepts = new WorldConceptRecipeBuffer();
            var recipes = new WorldAlchemyRecipe[_published.Length];
            var instances = new WorldAlchemyInstanceBuffer();
            for (var index = 0; index < _published.Length; index++)
            {
                var recipe = _published[index];
                var id = Guid.Parse(recipe.uuid);
                var core = recipe.coreType.GetGuid();
                var concept = new WorldConceptRecipe(id, core, Active.CanAddInstance(recipe));
                concepts.Append(in concept);
                recipes[index] = Row(id, core, recipe.discovered, recipe.GetMaxUsageSlots());
                var live = Active.value.SingleOrDefault(item => ReferenceEquals(item.reference, recipe));
                if (live is null) continue;
                var instance = new WorldAlchemyInstance(
                    id, live.quantity, live.queuedQuantity, drainReadable: true, drainRatio: DrainRatio);
                instances.Append(in instance);
            }
            return new GameWorldState
            {
                CollectedAtFrame = 10,
                CollectedAtEpoch = _epoch,
                AlchemyRecipes = WorldTable.Create(recipes),
                ConceptRecipes = WorldAlchemyRowDeriver.Build(concepts),
                AlchemyInstances = WorldAlchemyRowDeriver.Build(instances),
            };
        }

        private static WorldAlchemyRecipe Row(Guid id, Guid core, bool discovered, int maximum) =>
            new(
                id,
                core,
                discovered,
                0,
                0,
                0,
                default,
                0,
                default,
                false,
                true,
                true,
                16,
                false,
                default,
                new BigDouble(100),
                default,
                default,
                new BigDouble(100),
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                new BigDouble(maximum),
                new BigDouble(16),
                default);
    }

    private static SuiteRuntimeConfiguration Configuration() =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = AutoConceptOperationMode.Active,
                SlotManagement = AutoConceptSlotManagementMode.PreserveManual,
                TrainingPeriodSeconds = 10,
                MinimumDrainRatio = 0.95f,
            },
        };
}
