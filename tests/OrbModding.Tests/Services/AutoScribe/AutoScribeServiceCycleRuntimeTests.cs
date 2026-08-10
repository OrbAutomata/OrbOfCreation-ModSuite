using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeServiceCycleRuntimeTests
{
    [Fact]
    public void QuarantineStopsFurtherSubmissionAndFaultGrowthUntilLifecycleReplacement()
    {
        var profile = AutoScribeIdentityCatalog.Audited;
        var clock = new ThreadSafeTestClock(100);
        var actions = new QuarantiningActionPort();
        var definition = AutoScribeService.Define(
            profile,
            actions,
            ownsActionFamily: static () => true,
            isQuarantined: () => actions.IsQuarantined);
        using var registry = new ServiceCycleRegistry(1, clock);
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(7),
            ServiceActionDispatchPolicy.Bounded(1));
        registry.WorldPublication.Publish(World(profile, epoch: 7), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;

        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => actions.ExecutionCount == 1,
            clock);

        Assert.True(actions.IsQuarantined);
        Assert.Equal(1, registration.Runner.Snapshot.Fault.OccurrenceCount);
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => registration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty,
            clock);

        for (var publication = 1000UL; publication <= 1002UL; publication++)
        {
            registry.WorldPublication.Publish(World(profile, epoch: 7), new WorldGeneration(publication));
            clock.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(11)));
            var attempt = registration.Runner.TryStartCycle(clock.Now);

            Assert.False(attempt.Queued);
            Assert.False(registration.Runner.Snapshot.LastStartDecision.Decision.ShouldStart);
            Assert.Equal(
                AutoScribeServiceDecisionCodes.Quarantined,
                registration.Runner.Snapshot.LastStartDecision.Decision.Code);
            Assert.Equal(1, actions.ExecutionCount);
            Assert.Equal(1, registration.Runner.Snapshot.Fault.OccurrenceCount);
        }

        actions.InvalidateLifecycle();
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(8)));
        var configuration = Configuration();
        var context = default(ServiceCycleStartContext);
        var resumed = AutoScribeService.ShouldStart(
            in configuration,
            in context,
            ownsActionFamily: static () => true,
            isQuarantined: () => actions.IsQuarantined);

        Assert.False(actions.IsQuarantined);
        Assert.True(resumed.ShouldStart);
        Assert.Equal(CommonServiceDecisionCodes.Ready, resumed.Code);
        Assert.False(registration.Runner.Snapshot.Fault.IsValid);
    }

    private static SuiteRuntimeConfiguration Configuration() =>
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
                magnitudeIncrement: BigDouble.Zero,
                powerModifiers: 0,
                speedModifiers: 0,
                costModModifiers: 0,
                costIncrementModModifiers: 0,
                efficiencyModModifiers: 0,
                autoPenaltyModModifiers: 0,
                multiPenaltyModModifiers: 0)),
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

    private sealed class QuarantiningActionPort : IAutoScribeCycleActionPort
    {
        internal int ExecutionCount { get; private set; }
        internal bool IsQuarantined { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoScribeCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            ExecutionCount++;
            IsQuarantined = true;
            return ServiceActionResult.Faulted(
                AutoScribeActionResultCodes.VerificationFailed);
        }

        internal void InvalidateLifecycle() => IsQuarantined = false;
    }
}
