using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using OrbModding.Common;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Runtime.ServiceCycle;

public sealed class AutoConceptCycleEvaluatorTests
{
    private static readonly Guid Alpha = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Beta = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Gamma = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Core = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherCore = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Resource = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void WaitsForTheNextPublicationWhenThereIsNoWork()
    {
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var actions = Plan(World(), Config(), ref state, out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void BreadthStartsTheLowestMasteryUnassignedConcept()
    {
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 2), Recipe(Beta, masteryLevel: 1) },
            Array.Empty<WorldAlchemyInstance>());
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var action = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(AutoConceptActionKind.Add, action.Kind);
        Assert.Equal(Beta, action.RecipeId);
        Assert.Equal(1, action.TargetOrDelta);
    }

    [Fact]
    public void BreadthWaitsForASettledAssignment()
    {
        var world = World(
            new[] { Recipe(Alpha) },
            new[] { Instance(Alpha, quantity: 0, queued: 1) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        Assert.Empty(Plan(world, Config(), ref state, out _));
    }

    [Fact]
    public void DepthRaisesAnActiveConceptToTheNativeMasteryMaximum()
    {
        var world = World(
            new[] { Recipe(Alpha, maximum: 8) },
            new[] { Instance(Alpha, quantity: 2, queued: 2) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var action = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(AutoConceptActionKind.Add, action.Kind);
        Assert.Equal(8, action.TargetOrDelta);
    }

    [Fact]
    public void NativeBeliefProjectionUsesTheSamePublishedQuantityAndMasteryFacts()
    {
        var world = World(
            new[] { Recipe(Alpha, maximum: 8) },
            new[] { Instance(Alpha, quantity: 2, queued: 2) });

        Assert.True(AutoConceptPlanBeliefProjection.TryCreate(
            world,
            Alpha,
            out var belief,
            out var reason), reason);
        Assert.Equal(2, belief.Quantity);
        Assert.Equal(2, belief.QueuedQuantity);
        Assert.Equal(8, belief.MaximumQuantity);
        Assert.Equal(Core, belief.CoreTypeId);
        Assert.Equal(0, belief.AuthoredDrainResources);
    }

    [Fact]
    public void NativeBeliefProjectionExplainsAnUnknownRecipe()
    {
        var unknown = Guid.Parse("44444444-4444-4444-4444-444444444444");

        Assert.False(AutoConceptPlanBeliefProjection.TryCreate(
            World(),
            unknown,
            out _,
            out var reason));
        Assert.Contains(unknown.ToString("D"), reason);
        Assert.Contains("concept-recipes", reason);
    }

    [Fact]
    public void RotateAllRemovesTheHigherMasteryOccupantBeforeReplacingIt()
    {
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 4), Recipe(Beta, masteryLevel: 1) },
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var action = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(AutoConceptActionKind.RotateOut, action.Kind);
        Assert.Equal(Alpha, action.RecipeId);
        Assert.Equal(Beta, action.ReplacementId);
    }

    [Fact]
    public void PreserveManualOnlyRemovesAnEntirelyOwnedAssignment()
    {
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 4), Recipe(Beta, masteryLevel: 1) },
            new[] { Instance(Alpha, quantity: 3, queued: 3) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        state.BaselineCaptured = true;
        state.Ownership.ObserveBaseline(Alpha.ToString(), 0);
        state.Ownership.RecordAutomatedDelta(Alpha.ToString(), 3, 3);

        var action = Assert.Single(Plan(
            world, Config(slotMode: AutoConceptSlotManagementMode.PreserveManual),
            ref state, out _));

        Assert.Equal(AutoConceptActionKind.RemoveOwned, action.Kind);
        Assert.Equal(3, action.TargetOrDelta);
        Assert.Equal(Beta, action.ReplacementId);
    }

    [Fact]
    public void UnsafeOwnedDrainRollsBackBeforeBreadthOrRebalance()
    {
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 4), Recipe(Beta, masteryLevel: 1) },
            new[] { Instance(Alpha, quantity: 2, queued: 2, drainReadable: false) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        state.BaselineCaptured = true;
        state.Ownership.ObserveBaseline(Alpha.ToString(), 1);
        state.Ownership.RecordAutomatedDelta(Alpha.ToString(), 2, 1);

        var action = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(AutoConceptActionKind.RemoveOwned, action.Kind);
        Assert.Equal(Alpha, action.RecipeId);
        Assert.Equal(1, action.TargetOrDelta);
    }

    [Fact]
    public void CandidateCursorLetsBoundaryRefusalsAdvancePastTheFirstRankedRecipe()
    {
        var world = World(
            new[] { Recipe(Alpha), Recipe(Beta) },
            Array.Empty<WorldAlchemyInstance>());
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var first = Assert.Single(Plan(world, Config(), ref state, out _));
        var second = Assert.Single(PlanAt(
            world,
            Config(),
            ref state,
            decisionAtTicks: 0,
            out _,
            out _,
            previousResultCode: AutoConceptActionResultCodes.AssignmentUnsettled));

        Assert.Equal(Alpha, first.RecipeId);
        Assert.Equal(Beta, second.RecipeId);
    }

    [Fact]
    public void OperationalGuardsFailClosed()
    {
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var world = World(new[] { Recipe(Alpha) }, Array.Empty<WorldAlchemyInstance>());

        Assert.Empty(Plan(world, Config(enabled: false), ref state, out _));
        Assert.Empty(Plan(world, Config(emergencyDisabled: true), ref state, out _));
        Assert.Empty(Plan(world, Config(mode: AutoConceptOperationMode.Disabled), ref state, out _));
    }

    [Fact]
    public void TimedCycleRotatesToAnUnlockedConceptAfterTraining()
    {
        var world = World(
            new[]
            {
                Recipe(Alpha, maximum: 1),
                Recipe(Beta, maximum: 1),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(slotMode: AutoConceptSlotManagementMode.TimedCycle);

        Assert.Empty(PlanAt(
            world, config, ref state, decisionAtTicks: 0, out _, out var training));
        var action = Assert.Single(PlanAt(
            world, config, ref state, TimeSpan.FromSeconds(61).Ticks, out _, out _));

        Assert.Equal(AutoConceptIdleReason.WaitingForTraining, training.IdleReason);
        Assert.Equal(AutoConceptActionKind.RotateOut, action.Kind);
        Assert.Equal(Alpha, action.RecipeId);
        Assert.Equal(Beta, action.ReplacementId);
    }

    [Fact]
    [Trait("Category", "AutoConceptReliability")]
    public void TimedCycleDepthSettlementDoesNotRestartTheActiveSession()
    {
        var initial = World(
            new[]
            {
                Recipe(Alpha, maximum: 4),
                Recipe(Beta, maximum: 1),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var queued = World(
            new[]
            {
                Recipe(Alpha, maximum: 4),
                Recipe(Beta, maximum: 1),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 4) });
        var settled = World(
            new[]
            {
                Recipe(Alpha, maximum: 4),
                Recipe(Beta, maximum: 1),
            },
            new[] { Instance(Alpha, quantity: 4, queued: 4) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(
            slotMode: AutoConceptSlotManagementMode.TimedCycle,
            trainingSeconds: 30);

        var depth = Assert.Single(PlanAt(
            initial, config, ref state, decisionAtTicks: 0, out _, out _));
        Assert.Equal(AutoConceptDecisionKind.Depth, state.Decision.Kind);
        Assert.Equal(4, depth.TargetOrDelta);
        Assert.Empty(PlanAt(
            World(), config, ref state, TimeSpan.FromSeconds(1).Ticks,
            out _, out _, previousCommitted: true));
        Assert.True(state.HasPendingReceipt);
        Assert.Empty(PlanAt(
            queued, config, ref state, TimeSpan.FromSeconds(2).Ticks,
            out _, out _));
        Assert.False(state.HasPendingReceipt);
        Assert.Empty(PlanAt(
            settled, config, ref state, TimeSpan.FromSeconds(10).Ticks,
            out _, out _));

        var rotation = Assert.Single(PlanAt(
            settled, config, ref state, TimeSpan.FromSeconds(31).Ticks,
            out _, out _));

        Assert.Equal(AutoConceptActionKind.RotateOut, rotation.Kind);
        Assert.Equal(Alpha, rotation.RecipeId);
        Assert.Equal(Beta, rotation.ReplacementId);
    }

    [Fact]
    [Trait("Category", "AutoConceptReliability")]
    public void TimedCycleStartsAFullTrainingSessionAfterAnAutomatedReplacementSettles()
    {
        var recipes = new[]
        {
            Recipe(Alpha, maximum: 1),
            Recipe(Beta, maximum: 3),
            Recipe(Gamma, maximum: 1),
        };
        var initial = World(
            recipes,
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var released = World(recipes, Array.Empty<WorldAlchemyInstance>());
        var queued = World(
            recipes,
            new[] { Instance(Beta, quantity: 0, queued: 1) });
        var settledFirstQuantity = World(
            recipes,
            new[] { Instance(Beta, quantity: 1, queued: 1) });
        var queuedDepth = World(
            recipes,
            new[] { Instance(Beta, quantity: 1, queued: 3) });
        var settledDepth = World(
            recipes,
            new[] { Instance(Beta, quantity: 3, queued: 3) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(
            slotMode: AutoConceptSlotManagementMode.TimedCycle,
            trainingSeconds: 10);

        Assert.Empty(PlanAt(
            initial, config, ref state, decisionAtTicks: 0, out _, out _));
        var remove = Assert.Single(PlanAt(
            initial, config, ref state, TimeSpan.FromSeconds(11).Ticks, out _, out _));
        var add = Assert.Single(PlanAt(
            released,
            config,
            ref state,
            TimeSpan.FromSeconds(11.1).Ticks,
            out _,
            out _,
            previousCommitted: true));
        Assert.Empty(PlanAt(
            queued,
            config,
            ref state,
            TimeSpan.FromSeconds(11.2).Ticks,
            out _,
            out _,
            previousCommitted: true));
        var depth = Assert.Single(PlanAt(
            settledFirstQuantity,
            config,
            ref state,
            TimeSpan.FromSeconds(12).Ticks,
            out _,
            out _));
        Assert.Empty(PlanAt(
            queuedDepth,
            config,
            ref state,
            TimeSpan.FromSeconds(12.1).Ticks,
            out _,
            out _,
            previousCommitted: true));
        Assert.Empty(PlanAt(
            settledDepth, config, ref state, TimeSpan.FromSeconds(13).Ticks, out _, out var training));
        Assert.Empty(PlanAt(
            settledDepth, config, ref state, TimeSpan.FromSeconds(21).Ticks, out _, out _));
        var nextRotation = Assert.Single(PlanAt(
            settledDepth, config, ref state, TimeSpan.FromSeconds(22.1).Ticks, out _, out _));

        Assert.Equal(AutoConceptActionKind.RotateOut, remove.Kind);
        Assert.Equal(Beta, remove.ReplacementId);
        Assert.Equal(AutoConceptActionKind.Add, add.Kind);
        Assert.Equal(Beta, add.RecipeId);
        Assert.Equal(AutoConceptActionKind.Add, depth.Kind);
        Assert.Equal(Beta, depth.RecipeId);
        Assert.Equal(3, depth.TargetOrDelta);
        Assert.Equal(AutoConceptIdleReason.WaitingForTraining, training.IdleReason);
        Assert.Equal(AutoConceptActionKind.RotateOut, nextRotation.Kind);
        Assert.Equal(Beta, nextRotation.RecipeId);
        Assert.Equal(Gamma, nextRotation.ReplacementId);
    }

    [Fact]
    public void NegativeLiveRateRollsBackOwnedDepthBeforeTheResourceReachesZero()
    {
        var currentDrain = new[]
        {
            new WorldAlchemyCost(
                Alpha,
                WorldAlchemyCostKind.CurrentDrain,
                Resource,
                new BigDouble(1)),
        };
        var world = World(
            new[] { Recipe(Alpha, maximum: 2) },
            new[] { Instance(Alpha, quantity: 2, queued: 2) },
            resources: new[] { DrainingResource(Resource, quantity: 50, capacity: 100, trueRate: -1) },
            costs: currentDrain);
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        state.BaselineCaptured = true;
        state.Ownership.ObserveBaseline(Alpha.ToString(), 1);
        state.Ownership.RecordAutomatedDelta(Alpha.ToString(), 2, 1);

        var rollback = Assert.Single(Plan(world, Config(), ref state, out _));

        Assert.Equal(AutoConceptActionKind.RemoveOwned, rollback.Kind);
        Assert.Equal(Alpha, rollback.RecipeId);
        Assert.Equal(1, rollback.TargetOrDelta);
    }

    [Fact]
    public void APublishedRateReserveRefusalIsQuietPlanningBackpressure()
    {
        var world = World(
            new[] { Recipe(Alpha, maximum: 1) },
            Array.Empty<WorldAlchemyInstance>(),
            resources: new[] { DrainingResource(Resource, 50, 100, trueRate: 100, currentDrain: 0) },
            costs: new[]
            {
                new WorldAlchemyCost(
                    Alpha, WorldAlchemyCostKind.RecipeDrain, Resource, new BigDouble(60)),
            });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        Assert.Empty(Plan(
            world,
            Config(rateReservePercent: 50),
            ref state,
            out _));
    }

    [Fact]
    public void DepthPlansTheLargestOwnedFormulaTargetThatClearsTheReserve()
    {
        var world = World(
            new[] { Recipe(Alpha, maximum: 4) },
            new[] { Instance(Alpha, quantity: 1, queued: 1) },
            resources: new[] { DrainingResource(Resource, 50, 100, trueRate: 100, currentDrain: 10) },
            costs: new[]
            {
                new WorldAlchemyCost(
                    Alpha, WorldAlchemyCostKind.RecipeDrain, Resource, new BigDouble(10)),
                new WorldAlchemyCost(
                    Alpha, WorldAlchemyCostKind.CurrentDrain, Resource, new BigDouble(10)),
            },
            costScalingPerQuantity: 2);
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var action = Assert.Single(Plan(
            world,
            Config(rateReservePercent: 50),
            ref state,
            out _));

        Assert.Equal(2, action.TargetOrDelta);
    }

    [Fact]
    [Trait("Category", "AutoConceptReliability")]
    public void RejectedCandidateReentersPlanningOnTheNextWorldPublication()
    {
        var world = World(
            new[]
            {
                Recipe(Alpha, maximum: 1),
                Recipe(Beta, maximum: 1),
                Recipe(Gamma, maximum: 1),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(
            slotMode: AutoConceptSlotManagementMode.TimedCycle,
            trainingSeconds: 10);

        Assert.Empty(PlanAt(
            world, config, ref state, decisionAtTicks: 0, out _, out _));
        var rejected = Assert.Single(PlanAt(
            world, config, ref state, TimeSpan.FromSeconds(11).Ticks, out _, out _));
        var reconsidered = Assert.Single(PlanAt(
            world,
            config,
            ref state,
            TimeSpan.FromSeconds(12).Ticks,
            out _,
            out _,
            previousResultCode: AutoConceptActionResultCodes.SlotUnavailable));

        Assert.Equal(AutoConceptActionKind.RotateOut, rejected.Kind);
        Assert.Equal(Beta, rejected.ReplacementId);
        Assert.Equal(AutoConceptActionKind.RotateOut, reconsidered.Kind);
        Assert.Equal(Beta, reconsidered.ReplacementId);
    }

    [Fact]
    public void TimedCycleRotatesAcrossConceptTypesWhenAFreeSlotMustBeReleased()
    {
        var world = World(
            new[]
            {
                Recipe(Alpha, maximum: 1),
                Recipe(Beta, maximum: 1, coreTypeId: OtherCore),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 1) },
            cannotAddNow: Beta);
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(slotMode: AutoConceptSlotManagementMode.TimedCycle);

        Assert.Empty(PlanAt(
            world, config, ref state, decisionAtTicks: 0, out _, out _));
        var action = Assert.Single(PlanAt(
            world, config, ref state, TimeSpan.FromSeconds(61).Ticks, out _, out _));

        Assert.Equal(AutoConceptActionKind.RotateOut, action.Kind);
        Assert.Equal(Alpha, action.RecipeId);
        Assert.Equal(Beta, action.ReplacementId);
    }

    [Fact]
    public void TimedCycleHighlightsWhenOnlyAlternativeIsLocked()
    {
        var world = World(
            new[]
            {
                Recipe(Alpha, maximum: 1),
                Recipe(Beta, maximum: 1, discovered: false),
                Recipe(Gamma, maximum: 0, coreTypeId: OtherCore),
            },
            new[] { Instance(Alpha, quantity: 1, queued: 1) });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var config = Config(slotMode: AutoConceptSlotManagementMode.TimedCycle);

        Assert.Empty(PlanAt(
            world, config, ref state, decisionAtTicks: 0, out _, out _));
        var actions = PlanAt(
            world, config, ref state, TimeSpan.FromSeconds(61).Ticks,
            out _, out var idle);

        Assert.Empty(actions);
        Assert.Equal(3, idle.CapturedRecipes);
        Assert.Equal(2, idle.EligibleRecipes);
        Assert.Equal(AutoConceptDecisionKind.Idle, idle.Kind);
        Assert.Equal(
            AutoConceptIdleReason.NoUnlockedAssignableReplacement,
            idle.IdleReason);
    }

    [Fact]
    public void ShippedDefaultsRotateAMasteredConceptWhilePsiIsASliverOfItsHugeCap()
    {
        var bound = SuiteConfiguration.TryBind(new ConfigFile());
        Assert.True(bound.Success, bound.Status.Reason);
        var shipped = BepInExAutomataConfigurationReader.Read(bound.Config!.Automata);
        var config = shipped with
        {
            AutoConcept = shipped.AutoConcept with
            {
                Mode = AutoConceptOperationMode.Active,
                SlotManagement = AutoConceptSlotManagementMode.RotateAll,
            },
        };
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 29, maximum: 18), Recipe(Beta, masteryLevel: 0, maximum: 1) },
            new[] { Instance(Alpha, quantity: 18, queued: 18) },
            resources: new[] { DrainingResource(Resource, 2.57e19, 7.32e22, trueRate: 9.19e17, currentDrain: 7.75e9) },
            costs: new[]
            {
                new WorldAlchemyCost(Alpha, WorldAlchemyCostKind.RecipeDrain, Resource, new BigDouble(0.459)),
                new WorldAlchemyCost(Alpha, WorldAlchemyCostKind.CurrentDrain, Resource, new BigDouble(7.75e9)),
                new WorldAlchemyCost(Beta, WorldAlchemyCostKind.RecipeDrain, Resource, new BigDouble(0.459)),
            });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        var action = Assert.Single(Plan(world, config, ref state, out _));

        Assert.Equal(AutoConceptActionKind.RotateOut, action.Kind);
        Assert.Equal(Alpha, action.RecipeId);
        Assert.Equal(Beta, action.ReplacementId);
    }

    [Fact]
    public void AnIdleCycleNamesTheMoveItWouldMakeAndTheResourceThatHeldItBack()
    {
        var world = World(
            new[] { Recipe(Alpha, masteryLevel: 29, maximum: 18), Recipe(Beta, masteryLevel: 0, maximum: 1) },
            new[] { Instance(Alpha, quantity: 18, queued: 18) },
            resources: new[] { DrainingResource(Resource, 0, 100, trueRate: 100, currentDrain: 0) },
            costs: new[]
            {
                new WorldAlchemyCost(Beta, WorldAlchemyCostKind.RecipeDrain, Resource, new BigDouble(1)),
            });
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));

        Assert.Empty(PlanAt(world, Config(), ref state, 0, out _, out var metrics));

        Assert.Equal(
            new AutoConceptIdle(
                AutoConceptIdleReason.ResourceAtZero, AutoConceptIdleMove.Rotate, Beta, Alpha, Resource),
            metrics.Idle);
    }

    private static WorldAlchemyRecipe Recipe(
        Guid id,
        int masteryLevel = 0,
        double masteryXp = 0,
        double requiredXp = 100,
        int maximum = 4,
        bool discovered = true,
        Guid? coreTypeId = null) =>
        new(
            id, coreTypeId ?? Core, discovered, 0, 0, 0,
            new BigDouble(masteryXp), masteryLevel, default,
            false, false, false, 0, false,
            default, default, default, default, default, default, default, default,
            default, default, default, default, default, new BigDouble(maximum), default,
            new BigDouble(requiredXp));

    private static WorldAlchemyInstance Instance(
        Guid id,
        int quantity,
        int queued,
        bool drainReadable = true,
        double drainRatio = 1) =>
        new(id, quantity, queued, drainReadable, new BigDouble(drainRatio));

    private static GameWorldState World() =>
        World(Array.Empty<WorldAlchemyRecipe>(), Array.Empty<WorldAlchemyInstance>());

    private static GameWorldState World(
        WorldAlchemyRecipe[] recipes,
        WorldAlchemyInstance[] instances,
        long collectedAtEpoch = 1,
        Guid? cannotAddNow = null,
        WorldResource[]? resources = null,
        WorldAlchemyCost[]? costs = null,
        double costScalingPerQuantity = 0)
    {
        var concepts = new WorldRelationBuffer<WorldConceptRecipe>();
        foreach (var recipe in recipes)
        {
            var canAddNow = recipe.RecipeId != cannotAddNow;
            foreach (var instance in instances)
            {
                if (instance.RecipeId == recipe.RecipeId ||
                    Math.Max(instance.Quantity, instance.QueuedQuantity) <= 0) continue;
                foreach (var activeRecipe in recipes)
                    if (activeRecipe.RecipeId == instance.RecipeId &&
                        activeRecipe.CoreTypeId == recipe.CoreTypeId)
                        canAddNow = false;
            }
            var row = new WorldConceptRecipe(
                recipe.RecipeId,
                recipe.CoreTypeId,
                canAddNow);
            concepts.Append(in row);
        }
        var active = new WorldRelationBuffer<RawWorldAlchemyInstance>();
        foreach (var instance in instances) active.Append(in instance);

        var programs = new List<WorldModifierProgram>();
        var entries = new List<WorldModifierProgramEntry>();
        var bases = new List<WorldConceptDrainBasis>();
        foreach (var recipe in recipes)
        {
            AddRecord(programs, recipe.RecipeId, WorldModifierProgramRole.ConceptDrain, 100);
            AddRecord(programs, recipe.RecipeId, WorldModifierProgramRole.ConceptSpeed, 100);
            AddRecord(programs, recipe.RecipeId, WorldModifierProgramRole.ConceptFreeUsageSlots, 0);
            AddRecord(programs, recipe.RecipeId, WorldModifierProgramRole.ConceptOverdriveSpeed, 100);
            AddRecord(programs, recipe.RecipeId, WorldModifierProgramRole.ConceptOverdriveDrain, 100);
            programs.Add(new WorldModifierProgram(
                recipe.RecipeId, WorldModifierProgramRole.ConceptCompletionCost, false, 0, false, default));
            programs.Add(new WorldModifierProgram(
                recipe.RecipeId, WorldModifierProgramRole.ConceptDrainLevel, false, 0, false, default));
            programs.Add(new WorldModifierProgram(
                recipe.RecipeId, WorldModifierProgramRole.InstanceScalingCost, false, 0, false, default));
            programs.Add(new WorldModifierProgram(
                recipe.RecipeId, WorldModifierProgramRole.InstanceScalingSpeed, false, 0, false, default));
            if (costScalingPerQuantity != 0)
                entries.Add(new WorldModifierProgramEntry(
                    recipe.RecipeId, WorldModifierProgramRole.InstanceScalingCost,
                    WorldModifierProgramEntrySet.Modifier, 0, Guid.Empty,
                    GameValueModifierType.Raw, 0, new BigDouble(costScalingPerQuantity)));
            bases.Add(new WorldConceptDrainBasis(
                recipe.RecipeId, recipe.CoreTypeId, recipe.RecipeId, 0, 1,
                default, default, default, false, false));
        }

        return new GameWorldState
        {
            AlchemyRecipes = WorldTable.Create(recipes),
            ConceptRecipes = WorldRelationTable.Build(concepts, WorldConceptRecipeLookup.Order),
            AlchemyInstances = WorldAlchemyRowDeriver.Build(active),
            Resources = WorldTable.Create(resources ?? Array.Empty<WorldResource>()),
            AlchemyCosts = PublicationTable<WorldAlchemyCost>.Create(
                costs ?? Array.Empty<WorldAlchemyCost>()),
            ModifierPrograms = PublicationTable<WorldModifierProgram>.Create(programs.ToArray()),
            ModifierProgramEntries = PublicationTable<WorldModifierProgramEntry>.Create(entries.ToArray()),
            ConceptDrainBasis = PublicationTable<WorldConceptDrainBasis>.Create(bases.ToArray()),
            CollectedAtEpoch = collectedAtEpoch,
        };
    }

    private static void AddRecord(
        List<WorldModifierProgram> programs,
        Guid owner,
        WorldModifierProgramRole role,
        double memo) =>
        programs.Add(new WorldModifierProgram(owner, role, true, memo, false, new BigDouble(memo)));

    private static WorldResource DrainingResource(
        Guid id,
        double quantity,
        double capacity,
        double trueRate,
        double? currentDrain = null)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            id,
            new BigDouble(quantity),
            new BigDouble(capacity),
            visible: true,
            lifetimeQuantity: default,
            discoveryTime: default,
            quality: new BigDouble(100),
            gainRate: new BigDouble(100),
            drain: new BigDouble(currentDrain ?? -trueRate),
            reservation: default,
            usage: default,
            inLossMode: false,
            inRestMode: false,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        return new WorldResource(
            in reading,
            isCapped: true,
            headroom: new BigDouble(capacity - quantity),
            fillFraction: quantity / capacity,
            isAtCapacity: false,
            trueQuantity: new BigDouble(quantity),
            trueRate: new BigDouble(trueRate));
    }

    private static SuiteRuntimeConfiguration Config(
        bool enabled = true,
        bool emergencyDisabled = false,
        AutoConceptOperationMode mode = AutoConceptOperationMode.Active,
        AutoConceptSlotManagementMode slotMode = AutoConceptSlotManagementMode.RotateAll,
        int trainingSeconds = 60,
        float rateReservePercent = 0) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = enabled },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = emergencyDisabled },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = mode,
                SlotManagement = slotMode,
                TrainingPeriodSeconds = trainingSeconds,
                RateReservePercent = rateReservePercent,
                MinimumDrainRatio = 0.25f,
            },
        };

    private static IReadOnlyList<AutoConceptCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        ref AutoConceptCycleState state,
        out WakePolicy wake) =>
        PlanAt(world, config, ref state, 0, out wake, out _);

    private static IReadOnlyList<AutoConceptCycleAction> PlanAt(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        ref AutoConceptCycleState state,
        long decisionAtTicks,
        out WakePolicy wake,
        out AutoConceptDecisionMetrics metrics,
        bool previousCommitted = false,
        ServiceActionResultCode previousResultCode = default)
    {
        var store = new ReusableActionStore<AutoConceptCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoConceptCycleAction>(store);
        var identity = new ServiceCycleIdentity(
            new ServiceId("auto-concept"), new LifecycleGeneration(1),
            new ConfigGeneration(1), StrategyGeneration.Initial,
            new WorldGeneration(2), new CycleId(2));
        var receiptIdentity = new ServiceCycleIdentity(
            new ServiceId("auto-concept"), new LifecycleGeneration(1),
            new ConfigGeneration(1), StrategyGeneration.Initial,
            new WorldGeneration(1),
            new CycleId(1));
        var receipt = previousResultCode.IsValid
            ? BatchReceipt.Terminated(
                receiptIdentity,
                new BatchId(1),
                1,
                0,
                0,
                ServiceActionResult.Rejected(previousResultCode),
                default,
                new MonotonicTimestamp(decisionAtTicks))
            : previousCommitted
                ? BatchReceipt.Completed(
                    receiptIdentity,
                    new BatchId(1),
                    1,
                    1,
                    new ServiceNativeCallTotals(1, 1, 1),
                    new MonotonicTimestamp(decisionAtTicks))
                : default;
        var context = new ServiceCycleContext(
            identity, receipt, new MonotonicTimestamp(decisionAtTicks));
        wake = AutoConceptCycleEvaluator.Evaluate(
            world, in config, in context, ref state, writer, out metrics);
        state.RecordDecision(in metrics);

        var actions = new List<AutoConceptCycleAction>(store.Count);
        while (!store.IsComplete)
        {
            actions.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }
        return actions;
    }
}
