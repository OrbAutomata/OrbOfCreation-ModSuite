using System;
using System.Collections.Generic;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using OrbModding.Common;
using Xunit;

namespace OrbModding.ProfileTests;

internal static class AutoHarvestProfileTestSupport
{
    internal static AutoHarvestCycleActionAdapter CreateActionAdapter(
        BindingPort bindings,
        AutomataProfileOperations operations) => new(
            bindings,
            new NoOpMutationPort(),
            new GatePort(),
            new ContractCircuit(),
            () => true,
            () => true,
            operations,
            bindings);

    /// <summary>
    /// The two supported pairs, collected the way production collects them.
    /// </summary>
    /// <remarks>
    /// The entities are put into the shared stub registries, collected once, and taken straight back
    /// out. The snapshot is immutable and outlives them, but the window in between is a mutation of
    /// process-global lists that a whole world is enumerated from, so the class that builds one of
    /// these belongs to <see cref="NativeRegistryCollection"/>.
    /// </remarks>
    internal sealed class StubbedHarvestWorld
    {
        internal StubbedHarvestWorld()
        {
            var plots = new List<PlotNodeSO>();
            var actions = new List<PlotNodeActionSO>();
            Fruit = Add(
                plots,
                actions,
                AutoHarvestKnownIds.FruitTreePlot,
                AutoHarvestKnownIds.FruitTreeCollect);
            Treasure = Add(
                plots,
                actions,
                AutoHarvestKnownIds.TreasureTreePlot,
                AutoHarvestKnownIds.TreasureTreeCollect);

            PlotNodeSO.All.AddRange(plots);
            PlotNodeActionSO.All.AddRange(actions);
            try
            {
                var collector = new GameWorldCollector();
                collector.Collect();
                Snapshot = collector.Build();
            }
            finally
            {
                foreach (var plot in plots) PlotNodeSO.All.Remove(plot);
                foreach (var action in actions) PlotNodeActionSO.All.Remove(action);
            }
        }

        internal (string Plot, string Action) Fruit { get; }
        internal (string Plot, string Action) Treasure { get; }
        internal GameWorldState Snapshot { get; }

        private static (string Plot, string Action) Add(
            List<PlotNodeSO> plots,
            List<PlotNodeActionSO> actions,
            string plotUuid,
            string actionUuid)
        {
            var action = new PlotNodeActionSO { elementCost = 1 };
            action.SetGuid(new Guid(actionUuid));
            action.prerequisites.available = true;
            var plot = new PlotNodeSO { visible = true };
            plot.SetGuid(new Guid(plotUuid));
            plot.phaseInstances.Add(new PlotNodePhaseInstance(PlotNodePhases.Idle, 4));
            plot.availableActions.Add(action);
            plot.GetActionInstances().Add(new PlotNodeActionInstance(action));
            plots.Add(plot);
            actions.Add(action);
            return (plotUuid, actionUuid);
        }
    }

    internal static SuiteRuntimeConfiguration Configuration() => AutoHarvestConfigurationFactory.Create(
        masterEnabled: true,
        emergencyDisabled: false,
        activeMode: true,
        fruitSelected: true,
        treasureSelected: true);

    internal static ServiceActionContext ActionContext(int serviceOrdinal, long frameIdentity)
    {
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal, frameIdentity);
        var identity = new ServiceCycleIdentity(
            AutoHarvestServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new WorldGeneration(1),
            new CycleId(5));
        return new ServiceActionContext(
            identity,
            new BatchId(1),
            new ActionId(1),
            actionIndex: 0,
            new MonotonicTimestamp(10),
            in coordinates);
    }

    internal static ServiceCycleProfileSpan[] Spans(List<CapturedMeasurement> measurements)
    {
        var result = new ServiceCycleProfileSpan[measurements.Count];
        for (var index = 0; index < measurements.Count; index++)
            result[index] = (ServiceCycleProfileSpan)measurements[index].Context.StageCode;
        return result;
    }

    internal sealed class BindingPort :
        IAutoHarvestBindingPort,
        IAutoHarvestProfileBindingObservation
    {
        private readonly AutomataProfileOperations _operations;
        private readonly bool _treasureAvailable;
        private bool _driftDuringNextResolve;
        private ServiceCycleProfileTemperature _temperature =
            ServiceCycleProfileTemperature.ColdProcess;

        internal BindingPort(
            AutomataProfileOperations operations,
            bool treasureAvailable = true,
            bool driftDuringNextResolve = false)
        {
            _operations = operations;
            _treasureAvailable = treasureAvailable;
            _driftDuringNextResolve = driftDuringNextResolve;
        }

        internal StubbedHarvestWorld World { get; } = new();

        public ServiceCycleProfileTemperature CurrentTemperature => _temperature;

        public ServiceCycleProfileTemperature PrepareTemperature() => _temperature;

        public AutoHarvestResolvedPairSet ResolvePairSet()
        {
            if (_driftDuringNextResolve)
            {
                _driftDuringNextResolve = false;
                _temperature = ServiceCycleProfileTemperature.LifecycleRebind;
            }
            _operations.AddStableIdRead();
            _operations.AddStableIdRead();
            var shared = new AutoHarvestSharedBinding(
                new object(),
                null!,
                null!,
                lifecycleGeneration: 1);
            var fruit = PairBinding(AutoHarvestPair.FruitTree);
            var treasure = _treasureAvailable
                ? PairBinding(AutoHarvestPair.TreasureTree)
                : null;
            var treasureFailure = _treasureAvailable
                ? default
                : AutoHarvestNativeFailure.Create(
                    AutoHarvestRuntimeFailureKind.Retryable,
                    AutoHarvestRuntimeFailureScope.Pair);
            return AutoHarvestResolvedPairSet.Create(
                null!,
                shared,
                fruit,
                default,
                treasure,
                treasureFailure);
        }


        public bool TryComplete(ServiceCycleProfileTemperature observed)
        {
            if (_temperature != observed) return false;
            _temperature = ServiceCycleProfileTemperature.Warm;
            return true;
        }

        private AutoHarvestPairBinding PairBinding(AutoHarvestPair pair)
        {
            var (plot, action) = pair == AutoHarvestPair.FruitTree ? World.Fruit : World.Treasure;
            return new AutoHarvestPairBinding(
                pair,
                new object(),
                new object(),
                plot,
                action,
                new object(),
                null!,
                null!,
                null!);
        }
    }


    internal sealed class CapturingMeasurementPort : IServiceCycleProfileMeasurementPort
    {
        private ulong _sequence;

        internal List<CapturedMeasurement> Completed { get; } = new();
        internal List<CapturedMeasurement> Abandoned { get; } = new();

        public bool TryBegin(
            in ServiceCycleProfileContext context,
            out ServiceCycleProfileMeasurementToken token)
        {
            token = new ServiceCycleProfileMeasurementToken(
                this,
                ++_sequence,
                in context,
                startedAtRawTicks: 0,
                allocatedBytes: 0);
            return true;
        }

        public ServiceCycleProfileMeasurementResult Complete(
            in ServiceCycleProfileMeasurementToken token,
            in ServiceCycleProfileOperationCounters operations)
        {
            Assert.True(operations.TrySnapshot(out var snapshot));
            Completed.Add(new CapturedMeasurement(token.Context, snapshot));
            return ServiceCycleProfileMeasurementResult.Accepted;
        }

        public ServiceCycleProfileMeasurementResult Abandon(
            in ServiceCycleProfileMeasurementToken token)
        {
            Abandoned.Add(new CapturedMeasurement(token.Context, default));
            return ServiceCycleProfileMeasurementResult.Accepted;
        }
    }

    private sealed class GatePort : IAutoHarvestGatePort
    {
        public void ObserveResolvedPairs(in AutoHarvestResolvedPairSet pairs)
        {
        }

        public bool IsQuarantined(AutoHarvestPair pair) => false;
        public void Quarantine(AutoHarvestPair pair)
        {
        }
    }

    private sealed class ContractCircuit : IAutoHarvestContractCircuit
    {
        public AutoHarvestNativeFailure FailureFor(AutoHarvestPair pair) => default;
        public void Block(AutoHarvestPair pair, AutoHarvestRuntimeFailureScope scope)
        {
        }
    }

    /// <summary>
    /// Stops at the binding stage. These tests measure resolution, not submission.
    /// </summary>
    private sealed class NoOpMutationPort : IAutoHarvestMutationPort
    {
        public AutoHarvestSubmissionResult Submit(
            in ResolvedAutoHarvestPair resolved,
            in AutoHarvestPairFacts facts,
            AutoHarvestActionSafetyState safety,
            in ServiceActionContext context) =>
            new(AutoHarvestSubmissionFailureCode.PolicyRevalidationRejected);
    }

}

internal readonly record struct CapturedMeasurement(
    ServiceCycleProfileContext Context,
    ServiceCycleProfileOperations Operations);
