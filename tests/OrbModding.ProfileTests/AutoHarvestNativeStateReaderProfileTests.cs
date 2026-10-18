using OrbAutomata;
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;
using static OrbModding.ProfileTests.AutoHarvestProfileTestSupport;

namespace OrbModding.ProfileTests;

public sealed class AutoHarvestNativeStateReaderProfileTests
{
    [Fact]
    public void ProductionReaderCountsTheNativeWorkItPerforms()
    {
        var measurement = new CapturingMeasurementPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(measurement);
        var operations = new AutomataProfileOperations(probe);
        var fixture = NativeReaderFixture.Create();
        var reader = new AutoHarvestNativeStateReader(operations);
        var context = ActionContext(serviceOrdinal: 0, frameIdentity: 1);

        var activeStage = operations.Begin(
            ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence,
            context,
            ServiceCycleProfileTemperature.Warm);
        AutoHarvestActiveActionSnapshot active;
        try
        {
            active = reader.CaptureActiveActions(fixture.Resolved);
            activeStage.Complete();
        }
        finally
        {
            activeStage.Abandon();
        }

        var prototypeStage = operations.Begin(
            ServiceCycleProfileSpan.AutoHarvestActionPrototypeResolution,
            context,
            ServiceCycleProfileTemperature.Warm);
        object? prototype;
        ServiceActionResultCode admission;
        try
        {
            admission = reader.ValidateClickAdmission(fixture.Resolved, out prototype);
            prototypeStage.Complete();
        }
        finally
        {
            prototypeStage.Abandon();
        }

        Assert.True(active.IsValid);
        Assert.Equal(CommonActionResultCodes.Committed, admission);
        Assert.NotNull(prototype);
        Assert.Collection(
            measurement.Completed,
            item => AssertOperations(
                item,
                ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence,
                stableIdReads: 2,
                listEntries: 1),
            // Exact plot/row visibility, offered membership, affordability and remaining capacity
            // are all live action-boundary work.
            item => AssertOperations(
                item,
                ServiceCycleProfileSpan.AutoHarvestActionPrototypeResolution,
                stableIdReads: 2,
                listEntries: 1));
        Assert.Empty(measurement.Abandoned);
        Assert.Same(measurement, probe.Detach());
    }

    private static void AssertOperations(
        in CapturedMeasurement item,
        ServiceCycleProfileSpan span,
        uint stableIdReads,
        uint listEntries)
    {
        Assert.Equal((int)span, item.Context.StageCode);
        Assert.Equal(stableIdReads, item.Operations.StableIdReads);
        Assert.Equal(listEntries, item.Operations.ListEntries);
    }

    private sealed class NativeReaderFixture
    {
        private NativeReaderFixture(ResolvedAutoHarvestPair resolved) => Resolved = resolved;

        internal ResolvedAutoHarvestPair Resolved { get; }

        internal static NativeReaderFixture Create()
        {
            var plot = new PlotNodeSO { visible = true };
            var action = new PlotNodeActionSO();
            action.prerequisites.available = true;
            var instance = new PlotNodeActionInstance(plot, action) { quantity = 1 };
            plot.GetActionInstances().Add(instance);
            var active = new PlotNodeActionInstanceListVariable();
            active.value.Add(instance);
            Assert.True(AutoHarvestNativeBindings.TryCreate(out var bindings, out var reason), reason);
            var shared = new AutoHarvestSharedBinding(active, null!, null!, 1);
            var fruit = new AutoHarvestPairBinding(
                AutoHarvestPair.FruitTree,
                plot,
                action,
                plot.GetGuid().ToString("D"),
                action.GetGuid().ToString("D"),
                new TreasurePoolSO(),
                null!,
                null!,
                null!);
            return new NativeReaderFixture(
                new ResolvedAutoHarvestPair(bindings!, shared, fruit, fruit, null));
        }
    }
}
