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

namespace OrbModding.Tests.Services.AutoConcept.Runtime.ServiceCycle;

[Trait("Category", "AutoConceptReliability")]
public sealed class AutoConceptNativeJourneyIntegrationTests : IDisposable
{
    private static readonly Guid ActiveId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReplacementId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public AutoConceptNativeJourneyIntegrationTests() =>
        IdScriptableObject.RuntimeLookup.Clear();

    public void Dispose() =>
        IdScriptableObject.RuntimeLookup.Clear();

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void NativeJourneyRotatesAssignsSettlesAndAddsDepth()
    {
        var type = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var activeRecipe = Recipe(ActiveId, "Active", type, maximum: 1);
        var replacementRecipe = Recipe(ReplacementId, "Replacement", type, maximum: 3);
        var active = Install(activeRecipe, replacementRecipe);
        active.TypelessSlots = 1;
        active.value.Add(new AlchemyInstance(activeRecipe) { quantity = 1, queuedQuantity = 1 });
        using var native = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var config = new AutoConceptConfiguration();

        var removeBelief = new AutoConceptPlanBelief(1, 1, 1, Guid.Empty, 0);
        var remove = new AutoConceptCycleAction(
            AutoConceptActionKind.RotateOut,
            ActiveId,
            1,
            ReplacementId,
            1,
            in removeBelief);
        var removed = native.Submit(in remove, in config);

        Assert.True(removed.Verified, removed.Reason);
        Assert.DoesNotContain(active.value, value => value.reference == activeRecipe);

        var assignBelief = new AutoConceptPlanBelief(0, 0, 3, Guid.Empty, 0);
        var assign = new AutoConceptCycleAction(
            AutoConceptActionKind.Add,
            ReplacementId,
            1,
            Guid.Empty,
            1,
            in assignBelief);
        var assigned = native.Submit(in assign, in config);

        Assert.True(assigned.Verified, assigned.Reason);
        var replacement = Assert.Single(active.value, value => value.reference == replacementRecipe);
        Assert.Equal(1, replacement.queuedQuantity);
        replacement.quantity = 1;

        var depthBelief = new AutoConceptPlanBelief(1, 1, 3, Guid.Empty, 0);
        var depth = new AutoConceptCycleAction(
            AutoConceptActionKind.Add,
            ReplacementId,
            3,
            Guid.Empty,
            1,
            in depthBelief);
        var deepened = native.Submit(in depth, in config);

        Assert.True(deepened.Verified, deepened.Reason);
        Assert.Equal(2, deepened.AppliedDelta);
        Assert.Equal(3, replacement.queuedQuantity);
    }

    private static AlchemyRecipeSO Recipe(
        Guid id,
        string name,
        AlchemyTypeSO type,
        double maximum) =>
        new(id.ToString("D"), name, new[] { type })
        {
            coreType = type,
            maxUsageSlots = new ValueModifierRecord(new BigDouble(maximum, 0)),
        };

    private static AlchemyInstanceListVariable Install(params AlchemyRecipeSO[] recipes)
    {
        var active = new AlchemyInstanceListVariable();
        active.SetGuid(new Guid(AutoConceptNativeAdapter.ActiveConceptsUuid));
        var recipeList = new AlchemyRecipeListVariable { value = recipes.ToList() };
        recipeList.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
        IdScriptableObject.RuntimeLookup[new Guid(AutoConceptNativeAdapter.ActiveConceptsUuid)] = active;
        IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = recipeList;
        return active;
    }
}

[Trait("Category", "AutoConceptReliability")]
public sealed class AutoConceptHeadlessJourneyTests
{
    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void TraceDerivedJourneyCannotChurnAndAdvancesAcrossFreshWorlds()
    {
        var scenario = new AutoConceptScenario(safeCandidates: 5, unsafeCandidates: 0);

        for (var second = 0; second <= 50; second++)
            scenario.RunAt(TimeSpan.FromSeconds(second));

        var rotations = scenario.Events
            .Where(item =>
                item.Action.Kind == AutoConceptActionKind.RotateOut &&
                item.Result.Disposition == ServiceActionDisposition.Committed)
            .ToArray();
        var assignments = scenario.Events
            .Where(item =>
                item.Action.Kind == AutoConceptActionKind.Add &&
                item.Action.Belief.Quantity == 0 &&
                item.Result.Disposition == ServiceActionDisposition.Committed)
            .ToArray();

        Assert.True(rotations.Length >= 4);
        for (var index = 1; index < rotations.Length; index++)
            Assert.True(
                rotations[index].AtTicks - rotations[index - 1].AtTicks >=
                TimeSpan.FromSeconds(10).Ticks,
                "A successful replacement rotated before its full training period elapsed.");

        Assert.Equal(4, assignments.Take(4).Select(item => item.Action.RecipeId).Distinct().Count());
        Assert.DoesNotContain(assignments, item => scenario.UnsafeIds.Contains(item.Action.RecipeId));
        foreach (var assignment in assignments)
            Assert.Contains(
                scenario.Events,
                item =>
                    item.AtTicks >= assignment.AtTicks &&
                    item.AtTicks <= assignment.AtTicks + TimeSpan.FromSeconds(2).Ticks &&
                    item.Action.Kind == AutoConceptActionKind.Add &&
                    item.Action.RecipeId == assignment.Action.RecipeId &&
                    item.Action.TargetOrDelta == 3 &&
                    item.Result.Disposition == ServiceActionDisposition.Committed);

        Assert.All(
            scenario.ReceiptObservations,
            observation => Assert.True(observation.ObservedWorld > observation.ReceiptWorld));
        Assert.True(scenario.Events.GroupBy(item => item.AtTicks).Max(group => group.Count()) <= 1);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void LargeTimedCycleSimulationIsRoundRobinAndActionBounded()
    {
        const int safeCandidates = 12;
        const int unsafeCandidates = 0;
        var scenario = new AutoConceptScenario(safeCandidates, unsafeCandidates);

        for (var second = 0; second <= 600; second++)
            scenario.RunAt(TimeSpan.FromSeconds(second));

        var assignments = scenario.Events
            .Where(item =>
                item.Action.Kind == AutoConceptActionKind.Add &&
                item.Action.Belief.Quantity == 0 &&
                item.Result.Disposition == ServiceActionDisposition.Committed)
            .ToArray();
        var rotations = scenario.Events
            .Where(item =>
                item.Action.Kind == AutoConceptActionKind.RotateOut &&
                item.Result.Disposition == ServiceActionDisposition.Committed)
            .ToArray();

        Assert.True(assignments.Length >= safeCandidates);
        Assert.Equal(
            safeCandidates,
            assignments.Take(safeCandidates).Select(item => item.Action.RecipeId).Distinct().Count());
        Assert.DoesNotContain(assignments, item => scenario.UnsafeIds.Contains(item.Action.RecipeId));
        for (var index = 1; index < rotations.Length; index++)
            Assert.True(
                rotations[index].AtTicks - rotations[index - 1].AtTicks >=
                TimeSpan.FromSeconds(10).Ticks);

        Assert.True(
            scenario.Events.GroupBy(item => item.AtTicks).Max(group => group.Count()) <=
            unsafeCandidates + 3);
        Assert.True(
            scenario.Events.Count <= 1 + 60 * (unsafeCandidates + 3),
            $"The bounded simulation emitted {scenario.Events.Count} actions.");
        Assert.All(
            scenario.ReceiptObservations,
            observation => Assert.True(observation.ObservedWorld > observation.ReceiptWorld));
    }

    [Fact]
    [Trait("Category", "HeadlessE2E")]
    public void PersistentlyRejectedReplacementIsRetriedOnEveryLaterWorld()
    {
        var scenario = new AutoConceptScenario(safeCandidates: 2, unsafeCandidates: 1);

        for (var second = 0; second <= 20; second++)
            scenario.RunAt(TimeSpan.FromSeconds(second));

        var rejections = scenario.Events
            .Where(item => item.Result.Code == AutoConceptActionResultCodes.SlotUnavailable)
            .ToArray();

        Assert.True(rejections.Length >= 5);
        Assert.All(
            rejections,
            item =>
            {
                Assert.Equal(AutoConceptActionKind.RotateOut, item.Action.Kind);
                Assert.Contains(item.Action.ReplacementId, scenario.UnsafeIds);
            });
        Assert.Equal(
            rejections.Length,
            rejections.Select(item => item.WorldGeneration).Distinct().Count());
        Assert.DoesNotContain(
            scenario.Events,
            item =>
                item.Action.Kind == AutoConceptActionKind.RotateOut &&
                item.Result.Disposition == ServiceActionDisposition.Committed);
        Assert.All(
            scenario.ReceiptObservations,
            observation => Assert.True(observation.ObservedWorld > observation.ReceiptWorld));
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void MultiSlotSimulationUsesResolvedCompletionTimesAndIndependentTraining()
    {
        const int activeSlots = 3;
        var scenario = new AutoConceptScenario(
            safeCandidates: 9,
            unsafeCandidates: 0,
            activeSlots);

        for (var second = 0; second <= 180; second++)
            scenario.RunAt(TimeSpan.FromSeconds(second));

        Assert.Equal(activeSlots, scenario.PeakSettledActiveCount);
        Assert.All(scenario.SettledActiveCounts, count => Assert.InRange(count, 0, activeSlots));

        var candidatesWithProgress = scenario.Candidates
            .Where(candidate => candidate.ActiveSeconds > 0)
            .ToArray();
        Assert.True(candidatesWithProgress.Length >= activeSlots + 1);
        Assert.Contains(candidatesWithProgress, candidate => candidate.AdvancementLevel > 0);
        Assert.Contains(candidatesWithProgress, candidate => candidate.MasteryLevel > 0);

        foreach (var candidate in candidatesWithProgress)
        {
            var expectedCompletions = (int)Math.Floor(
                candidate.SpeedAdjustedActiveSeconds /
                candidate.ResolvedCompletionSeconds);
            Assert.Equal(expectedCompletions, candidate.Completions);
        }

        var fastest = candidatesWithProgress.MinBy(
            candidate => candidate.ResolvedCompletionSeconds /
                         (candidate.SpeedPercentAt(1) / 100.0))!;
        var slowest = candidatesWithProgress.MaxBy(
            candidate => candidate.ResolvedCompletionSeconds /
                         (candidate.SpeedPercentAt(1) / 100.0))!;
        Assert.True(fastest.Completions > slowest.Completions);

        var lastActivation = new Dictionary<Guid, long>
        {
            [scenario.InitiallyActiveId] = 0,
        };
        foreach (var item in scenario.Events)
        {
            if (item.Result.Disposition != ServiceActionDisposition.Committed) continue;
            if (item.Action.Kind == AutoConceptActionKind.Add &&
                item.Action.Belief.Quantity == 0 &&
                item.Action.Belief.QueuedQuantity == 0)
            {
                lastActivation[item.Action.RecipeId] = item.AtTicks;
                continue;
            }
            if (item.Action.Kind != AutoConceptActionKind.RotateOut) continue;
            Assert.True(lastActivation.TryGetValue(item.Action.RecipeId, out var activatedAt));
            Assert.True(
                item.AtTicks - activatedAt >= TimeSpan.FromSeconds(10).Ticks,
                $"Concept {item.Action.RecipeId:D} rotated before its own training period elapsed.");
        }

        Assert.True(
            scenario.Events
                .Where(item =>
                    item.Action.Kind == AutoConceptActionKind.RotateOut &&
                    item.Result.Disposition == ServiceActionDisposition.Committed)
                .Select(item => item.Action.RecipeId)
                .Distinct()
                .Count() >= activeSlots);
        Assert.All(
            scenario.ReceiptObservations,
            observation => Assert.True(observation.ObservedWorld > observation.ReceiptWorld));
    }

    private sealed class AutoConceptScenario
    {
        private static readonly Guid Core =
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private readonly List<Candidate> _candidates = new();
        private readonly int _activeSlots;
        private readonly AutoConceptCycleActionAdapter _actions;
        private readonly SuiteRuntimeConfiguration _configuration;
        private AutoConceptCycleState _state =
            AutoConceptCycleState.Create(new LifecycleGeneration(1));
        private BatchReceipt _previousReceipt;
        private long? _lastRunAtTicks;
        private ulong _world;
        private ulong _batch;
        private ulong _action;

        internal AutoConceptScenario(
            int safeCandidates,
            int unsafeCandidates,
            int activeSlots = 1)
        {
            if (safeCandidates < 2) throw new ArgumentOutOfRangeException(nameof(safeCandidates));
            if (unsafeCandidates < 0) throw new ArgumentOutOfRangeException(nameof(unsafeCandidates));
            if (activeSlots < 1) throw new ArgumentOutOfRangeException(nameof(activeSlots));
            _activeSlots = activeSlots;

            var initiallyActive = CreateCandidate(
                Id(1),
                ordinal: 0,
                maximum: 3,
                unsafeReplacement: false);
            initiallyActive.Quantity = 1;
            initiallyActive.QueuedQuantity = 1;
            _candidates.Add(initiallyActive);
            for (var index = 0; index < unsafeCandidates; index++)
                _candidates.Add(CreateCandidate(
                    Id(index + 2),
                    index + 1,
                    maximum: 1,
                    unsafeReplacement: true));
            for (var index = 1; index < safeCandidates; index++)
                _candidates.Add(CreateCandidate(
                    Id(unsafeCandidates + index + 1),
                    unsafeCandidates + index,
                    maximum: 3,
                    unsafeReplacement: false));

            UnsafeIds = _candidates
                .Where(candidate => candidate.UnsafeReplacement)
                .Select(candidate => candidate.Id)
                .ToHashSet();
            _configuration = new SuiteRuntimeConfiguration
            {
                General = new SuiteGeneralConfiguration { Enabled = true },
                AutoConcept = new AutoConceptConfiguration
                {
                    Mode = AutoConceptOperationMode.Active,
                    SlotManagement = AutoConceptSlotManagementMode.TimedCycle,
                    TrainingPeriodSeconds = 10,
                    MinimumDrainRatio = 0.95f,
                },
            };
            _actions = new AutoConceptCycleActionAdapter(
                new SimulatedNativePort(_candidates, _activeSlots),
                () => 1,
                () => true);
        }

        internal Guid InitiallyActiveId => _candidates[0].Id;
        internal IReadOnlyList<Candidate> Candidates => _candidates;
        internal HashSet<Guid> UnsafeIds { get; }
        internal List<ScenarioEvent> Events { get; } = new();
        internal List<(ulong ReceiptWorld, ulong ObservedWorld)> ReceiptObservations { get; } = new();
        internal List<int> SettledActiveCounts { get; } = new();
        internal int PeakSettledActiveCount { get; private set; }

        internal void RunAt(TimeSpan at)
        {
            AdvanceProgress(at.Ticks);
            _world++;
            foreach (var candidate in _candidates)
                candidate.Quantity = candidate.QueuedQuantity;
            var active = _candidates.Count(candidate => candidate.Quantity > 0);
            SettledActiveCounts.Add(active);
            PeakSettledActiveCount = Math.Max(PeakSettledActiveCount, active);
            var world = BuildWorld();
            EvaluateAndExecute(at.Ticks, world);
        }

        private void AdvanceProgress(long atTicks)
        {
            if (!_lastRunAtTicks.HasValue)
            {
                _lastRunAtTicks = atTicks;
                return;
            }
            var elapsedSeconds = TimeSpan.FromTicks(
                Math.Max(0, atTicks - _lastRunAtTicks.Value)).TotalSeconds;
            _lastRunAtTicks = atTicks;
            foreach (var candidate in _candidates)
                candidate.Advance(elapsedSeconds);
        }

        private void EvaluateAndExecute(long atTicks, GameWorldState world)
        {
            var store = new ReusableActionStore<AutoConceptCycleAction>();
            store.BeginWrite();
            var writer = new ServiceActionWriter<AutoConceptCycleAction>(store);
            var identity = new ServiceCycleIdentity(
                AutoConceptServicePolicies.ServiceId,
                new LifecycleGeneration(1),
                new ConfigGeneration(1),
                StrategyGeneration.Initial,
                new WorldGeneration(_world),
                new CycleId(_world));
            var receipt = _previousReceipt;
            _previousReceipt = default;
            if (receipt.IsPresent)
                ReceiptObservations.Add((receipt.Cycle.World.Value, _world));
            var context = new ServiceCycleContext(
                identity,
                receipt,
                new MonotonicTimestamp(atTicks));

            AutoConceptCycleEvaluator.Evaluate(
                world,
                in _configuration,
                in context,
                ref _state,
                writer,
                out var metrics);
            _state.RecordDecision(in metrics);
            if (store.Count == 0) return;
            if (store.Count != 1)
                throw new InvalidOperationException($"Expected one action, observed {store.Count}.");

            var planned = store.GetCurrent();
            store.CommitCurrentAndClear();
            var actionContext = new ServiceActionContext(
                identity,
                new BatchId(++_batch),
                new ActionId(++_action),
                0,
                new MonotonicTimestamp(atTicks));
            var result = _actions.TryExecute(
                in planned,
                in _configuration,
                in actionContext);
            Events.Add(new ScenarioEvent(atTicks, _world, in planned, in result));
            _previousReceipt = result.Disposition == ServiceActionDisposition.Committed
                ? BatchReceipt.Completed(
                    identity,
                    new BatchId(_batch),
                    1,
                    1,
                    new ServiceNativeCallTotals(1, 1, 1),
                    new MonotonicTimestamp(atTicks))
                : BatchReceipt.Terminated(
                    identity,
                    new BatchId(_batch),
                    1,
                    0,
                    0,
                    result,
                    default,
                    new MonotonicTimestamp(atTicks));
        }

        private GameWorldState BuildWorld()
        {
            var occupied = _candidates.Count(
                candidate => Math.Max(candidate.Quantity, candidate.QueuedQuantity) > 0);
            var concepts = new WorldConceptRecipeBuffer();
            var recipes = new WorldAlchemyRecipe[_candidates.Count];
            var instances = new WorldAlchemyInstanceBuffer();
            for (var index = 0; index < _candidates.Count; index++)
            {
                var candidate = _candidates[index];
                var concept = new WorldConceptRecipe(
                    candidate.Id,
                    Core,
                    occupied < _activeSlots ||
                    Math.Max(candidate.Quantity, candidate.QueuedQuantity) > 0);
                concepts.Append(in concept);
                recipes[index] = Recipe(candidate);
                if (candidate.Quantity <= 0 && candidate.QueuedQuantity <= 0) continue;
                var instance = new WorldAlchemyInstance(
                    candidate.Id,
                    candidate.Quantity,
                    candidate.QueuedQuantity,
                    drainReadable: true,
                    drainRatio: new BigDouble(1));
                instances.Append(in instance);
            }
            return new GameWorldState
            {
                AlchemyRecipes = WorldTable.Create(recipes),
                ConceptRecipes = WorldAlchemyRowDeriver.Build(concepts),
                AlchemyInstances = WorldAlchemyRowDeriver.Build(instances),
                CollectedAtEpoch = 1,
            };
        }

        private static WorldAlchemyRecipe Recipe(Candidate candidate) =>
            new(
                candidate.Id,
                Core,
                true,
                0,
                candidate.AdvancementLevel,
                0,
                new BigDouble(candidate.MasteryXp),
                candidate.MasteryLevel,
                new BigDouble(candidate.RecipeTicks),
                false,
                true,
                true,
                candidate.BaseCompletionSeconds,
                false,
                default,
                new BigDouble(candidate.SpeedPercentAt(Math.Max(1, candidate.Quantity))),
                default,
                default,
                new BigDouble(candidate.TimeRequirementPercent),
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                new BigDouble(candidate.Maximum),
                new BigDouble(candidate.ResolvedCompletionSeconds),
                new BigDouble(candidate.RequiredMasteryXp));

        private static Candidate CreateCandidate(
            Guid id,
            int ordinal,
            int maximum,
            bool unsafeReplacement)
        {
            var resolvedCompletionSeconds = (ordinal % 3) switch
            {
                0 => 4.0,
                1 => 8.0,
                _ => 12.0,
            };
            var speedAtOne = (ordinal % 3) switch
            {
                0 => 200.0,
                1 => 100.0,
                _ => 50.0,
            };
            return new Candidate(
                id,
                maximum,
                unsafeReplacement,
                baseCompletionSeconds: 16,
                resolvedCompletionSeconds,
                requiredMasteryXp: 4,
                new[] { 0.0, speedAtOne, speedAtOne * 1.5, speedAtOne * 2.0 });
        }

        private static Guid Id(int value) =>
            Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
    }

    private sealed class SimulatedNativePort : IAutoConceptNativePort
    {
        private readonly IReadOnlyList<Candidate> _candidates;
        private readonly int _activeSlots;

        internal SimulatedNativePort(
            IReadOnlyList<Candidate> candidates,
            int activeSlots)
        {
            _candidates = candidates;
            _activeSlots = activeSlots;
        }

        public AutoConceptSubmission Submit(
            in AutoConceptCycleAction action,
            in AutoConceptConfiguration config)
        {
            var candidate = Find(action.RecipeId);
            if (action.Kind == AutoConceptActionKind.RotateOut)
            {
                var replacement = Find(action.ReplacementId);
                if (replacement.UnsafeReplacement)
                    return AutoConceptSubmission.Rejected(
                        AutoConceptPreflight.SlotUnavailable,
                        "simulated drained resource is at zero");
                var removed = candidate.QueuedQuantity;
                candidate.QueuedQuantity = 0;
                return AutoConceptSubmission.Attempted(
                    new NativeMutationCallOutcome(1, 1, 1),
                    NativeMutationOutcome.Verified,
                    string.Empty,
                    -removed);
            }

            var target = action.Kind == AutoConceptActionKind.RemoveOwned
                ? Math.Max(0, candidate.QueuedQuantity - action.TargetOrDelta)
                : Math.Min(candidate.Maximum, action.TargetOrDelta);
            if (candidate.QueuedQuantity == 0 &&
                target > 0 &&
                _candidates.Count(item => Math.Max(item.Quantity, item.QueuedQuantity) > 0) >=
                _activeSlots)
                return AutoConceptSubmission.Rejected(
                    AutoConceptPreflight.SlotUnavailable,
                    "simulated active concept slots are full");
            var delta = target - candidate.QueuedQuantity;
            candidate.QueuedQuantity = target;
            return AutoConceptSubmission.Attempted(
                new NativeMutationCallOutcome(1, 1, 1),
                NativeMutationOutcome.Verified,
                string.Empty,
                delta);
        }

        private Candidate Find(Guid id)
        {
            foreach (var candidate in _candidates)
                if (candidate.Id == id) return candidate;
            throw new InvalidOperationException($"Unknown simulated concept {id:D}.");
        }
    }

    private sealed class Candidate
    {
        private readonly double[] _speedPercentByQuantity;

        internal Candidate(
            Guid id,
            int maximum,
            bool unsafeReplacement,
            double baseCompletionSeconds,
            double resolvedCompletionSeconds,
            double requiredMasteryXp,
            double[] speedPercentByQuantity)
        {
            Id = id;
            Maximum = maximum;
            UnsafeReplacement = unsafeReplacement;
            BaseCompletionSeconds = baseCompletionSeconds;
            ResolvedCompletionSeconds = resolvedCompletionSeconds;
            RequiredMasteryXp = requiredMasteryXp;
            _speedPercentByQuantity = speedPercentByQuantity;
            if (BaseCompletionSeconds <= 0 ||
                ResolvedCompletionSeconds <= 0 ||
                RequiredMasteryXp <= 0 ||
                _speedPercentByQuantity.Length <= maximum)
                throw new ArgumentOutOfRangeException(
                    nameof(resolvedCompletionSeconds),
                    "Simulated native completion and speed values must be positive and complete.");
        }

        internal Guid Id { get; }
        internal int Maximum { get; }
        internal bool UnsafeReplacement { get; }
        internal double BaseCompletionSeconds { get; }
        internal double ResolvedCompletionSeconds { get; }
        internal double TimeRequirementPercent =>
            100.0 * ResolvedCompletionSeconds / BaseCompletionSeconds;
        internal double RequiredMasteryXp { get; }
        internal int Quantity { get; set; }
        internal int QueuedQuantity { get; set; }
        internal double RecipeTicks { get; private set; }
        internal double MasteryXp { get; private set; }
        internal int MasteryLevel { get; private set; }
        internal int AdvancementLevel { get; private set; }
        internal int Completions { get; private set; }
        internal double ActiveSeconds { get; private set; }
        internal double SpeedAdjustedActiveSeconds { get; private set; }

        internal double SpeedPercentAt(int quantity)
        {
            var index = Math.Clamp(quantity, 0, _speedPercentByQuantity.Length - 1);
            return _speedPercentByQuantity[index];
        }

        internal void Advance(double elapsedSeconds)
        {
            if (elapsedSeconds <= 0 || Quantity <= 0) return;
            ActiveSeconds += elapsedSeconds;
            var speedAdjusted = elapsedSeconds * SpeedPercentAt(Quantity) / 100.0;
            SpeedAdjustedActiveSeconds += speedAdjusted;
            RecipeTicks += speedAdjusted;
            var completionGuard = 0;
            while (RecipeTicks >= ResolvedCompletionSeconds)
            {
                if (++completionGuard > 1000)
                    throw new InvalidOperationException(
                        $"Completion loop did not converge for {Id:D}: requirement={ResolvedCompletionSeconds}, ticks={RecipeTicks}.");
                RecipeTicks -= ResolvedCompletionSeconds;
                Completions++;
                AdvancementLevel++;
                MasteryXp++;
                if (MasteryXp < RequiredMasteryXp) continue;
                MasteryXp -= RequiredMasteryXp;
                MasteryLevel++;
            }
        }
    }

    private readonly struct ScenarioEvent
    {
        internal ScenarioEvent(
            long atTicks,
            ulong worldGeneration,
            in AutoConceptCycleAction action,
            in ServiceActionResult result)
        {
            AtTicks = atTicks;
            WorldGeneration = worldGeneration;
            Action = action;
            Result = result;
        }

        internal long AtTicks { get; }
        internal ulong WorldGeneration { get; }
        internal AutoConceptCycleAction Action { get; }
        internal ServiceActionResult Result { get; }
    }
}
