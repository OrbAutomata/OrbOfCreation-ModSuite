using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// What Auto Concept's action boundary hands its native port depends on who pressed.
/// </summary>
/// <remarks>
/// Round 15 met <c>ERR_LIMIT</c> "below the configured quantity floor" on <c>game_concept add</c>
/// while Auto Concept's Mode was Disabled. The dials belong to the worker: they say when it may
/// spend the player's rate on its own, and they say nothing about a press the player asked for.
/// </remarks>
public sealed class AutoConceptManualPressProfileTests
{
    private const long PlannedEpoch = 7;
    private static readonly Guid Recipe = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void TheManualVerbIsHandedNoneOfTheDialsTheAutomatedCycleKeeps()
    {
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = AutoConceptOperationMode.Disabled,
                RateReservePercent = 90.0f,
                MinimumResourcePercent = 80.0f,
            },
        };

        var pressed = new RecordingNativePort();
        var manual = Adapter(pressed).TryExecuteGameMcp(
            Add(), in configuration, Context());

        Assert.Equal(ServiceActionDisposition.Committed, manual.Disposition);
        Assert.True(pressed.Submitted);
        Assert.Null(pressed.Limits);

        var worked = new RecordingNativePort();
        var automated = Adapter(worked).TryExecute(
            Add(),
            new SuiteRuntimeConfiguration
            {
                General = configuration.General,
                AutoConcept = configuration.AutoConcept with
                {
                    Mode = AutoConceptOperationMode.Active,
                },
            },
            Context());

        Assert.Equal(ServiceActionDisposition.Committed, automated.Disposition);
        Assert.Equal(
            new AutoConceptResourceLimits(90.0f, 80.0f),
            worked.Limits);
    }

    private static AutoConceptCycleActionAdapter Adapter(IAutoConceptNativePort native) =>
        new(native, () => PlannedEpoch, () => true);

    private static AutoConceptCycleAction Add()
    {
        var belief = new AutoConceptPlanBelief(0, 0, 4, Guid.Empty, 1);
        return new AutoConceptCycleAction(
            AutoConceptActionKind.Add, Recipe, 1, Guid.Empty, PlannedEpoch, in belief);
    }

    private static ServiceActionContext Context() =>
        new(
            new ServiceCycleIdentity(
                AutoConceptServicePolicies.ServiceId,
                new LifecycleGeneration(1),
                new ConfigGeneration(1),
                StrategyGeneration.Initial,
                new WorldGeneration(1),
                new CycleId(1)),
            new BatchId(1),
            new ActionId(1),
            0,
            new MonotonicTimestamp(1));

    private sealed class RecordingNativePort : IAutoConceptNativePort
    {
        internal bool Submitted { get; private set; }

        internal AutoConceptResourceLimits? Limits { get; private set; }

        public AutoConceptSubmission Submit(
            in AutoConceptCycleAction action,
            AutoConceptResourceLimits? limits)
        {
            Submitted = true;
            Limits = limits;
            return AutoConceptSubmission.Attempted(
                new NativeMutationCallOutcome(1, 1, 1),
                NativeMutationOutcome.Verified,
                string.Empty,
                1);
        }
    }
}
