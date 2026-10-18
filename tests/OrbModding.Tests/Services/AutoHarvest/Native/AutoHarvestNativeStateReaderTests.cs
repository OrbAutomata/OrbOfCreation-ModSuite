using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Native;

public sealed class AutoHarvestNativeStateReaderTests
{
    /// <summary>
    /// The reader's whole remaining job at the action boundary: hand back the instance to submit
    /// into. Every fact the submission rests on now rides on the action.
    /// </summary>
    [Fact]
    public void ThePlotsOneInstanceOfThePairIsTheResolvedPrototype()
    {
        var world = PrototypeWorld.Create();
        var instance = world.AddInstance(world.Plot, world.Action);

        Assert.Same(instance, new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    /// <summary>
    /// Instances of other plots and actions are passed over, not counted.
    /// </summary>
    [Fact]
    public void InstancesOfOtherPairsAreNotMistakenForThePrototype()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.OtherPlot, world.OtherAction);
        var instance = world.AddInstance(world.Plot, world.Action);
        world.AddInstance(world.OtherPlot, world.OtherAction);

        Assert.Same(instance, new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    /// <summary>
    /// Two instances of the pair leave nothing to submit into: which one would be a guess.
    /// </summary>
    [Fact]
    public void TwoInstancesOfThePairResolveToNoPrototype()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.AddInstance(world.Plot, world.Action);

        Assert.Null(new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    [Fact]
    public void APlotHoldingNoInstanceOfTheActionResolvesToNoPrototype()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.OtherPlot, world.OtherAction);

        Assert.Null(new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    /// <summary>
    /// The pair's own action turning up under another plot is a contradiction, not an unrelated
    /// entry: nothing about the list can be trusted after it, so no prototype is resolved.
    /// </summary>
    [Fact]
    public void TheSupportedActionUnderAForeignPlotRefusesTheWholeList()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.AddInstance(world.OtherPlot, world.Action);

        Assert.Null(new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    [Fact]
    public void AnEmptyEntryInTheInstanceListRefusesTheWholeList()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.Instances.Add(null!);

        Assert.Null(new AutoHarvestNativeStateReader().ReadPrototype(world.Resolved));
    }

    [Theory]
    [InlineData(1031)]
    [InlineData(1032)]
    [InlineData(1033)]
    [InlineData(1034)]
    [InlineData(1035)]
    public void EachLiveClickTermReturnsItsOwnNamedRefusal(int failure)
    {
        var expected = new ServiceActionResultCode(failure, "expected");
        var world = PrototypeWorld.Create();
        var instance = world.AddInstance(world.Plot, world.Action);
        if (expected == AutoHarvestActionResultCodes.NativePlotVisibilityRefused)
            world.Plot.visible = false;
        else if (expected == AutoHarvestActionResultCodes.NativeOfferedInstanceMembershipRefused)
            world.Instances.Clear();
        else if (expected == AutoHarvestActionResultCodes.NativeActionRowVisibilityRefused)
            world.Action.prerequisites.available = false;
        else if (expected == AutoHarvestActionResultCodes.NativeHasEnoughForOneInstanceRefused)
            instance.EnoughForOneInstance = false;
        else if (expected == AutoHarvestActionResultCodes.NativeMaximumRemainingInstancesRefused)
            instance.MaximumRemainingInstances = 0;
        else
            throw new InvalidOperationException("unexpected click gate fixture");

        var actual = new AutoHarvestNativeStateReader().ValidateClickAdmission(
            world.Resolved,
            out _);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// The game's own exception arrives as itself, so only a failure the classifier expects is a
    /// named refusal; anything else is a defect and escapes the click gate.
    /// </summary>
    [Fact]
    public void AnUnexpectedExceptionThrownInsideANativeReadEscapesClickAdmission()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.Plot.ActionInstancesFailure = new NullReferenceException("injected instance list read failure");

        Assert.Throws<NullReferenceException>(() =>
            new AutoHarvestNativeStateReader().ValidateClickAdmission(world.Resolved, out _));
    }

    [Fact]
    public void AnExpectedExceptionThrownInsideANativeReadIsTheTermsNamedRefusal()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.Plot.ActionInstancesFailure = new InvalidOperationException("injected instance list read failure");

        Assert.Equal(
            AutoHarvestActionResultCodes.NativeOfferedInstanceMembershipRefused,
            new AutoHarvestNativeStateReader().ValidateClickAdmission(world.Resolved, out _));
    }

    [Fact]
    public void CurrentPairResolutionReReadsStableUuidAndExactNativeType()
    {
        var world = PrototypeWorld.Create();
        var currentPlot = new PlotNodeSO();
        currentPlot.SetGuid(world.Plot.GetGuid());
        var currentAction = new PlotNodeActionSO();
        currentAction.SetGuid(world.Action.GetGuid());
        var registry = new Dictionary<Guid, object>
        {
            [currentPlot.GetGuid()] = currentPlot,
            [currentAction.GetGuid()] = currentAction,
        };
        var resolver = new OrbModding.Common.TypedRegistryResolver(
            () => 1,
            () => OrbModding.Common.TypedRegistrySourceSnapshot.Ready((IDictionary)registry),
            value => ((IdScriptableObject)value).GetGuid());
        var reader = new AutoHarvestNativeStateReader(resolver);

        var succeeded = reader.TryResolveCurrentPair(world.Resolved, out var current);

        Assert.True(succeeded);
        Assert.Same(currentPlot, current.Target.Plot);
        Assert.Same(currentAction, current.Target.Action);
    }

    [Fact]
    public void ExistingRowNativeStubClampsToWrongAbsoluteMaximumWithoutClickAdmission()
    {
        var plot = new PlotNodeSO();
        var action = new PlotNodeActionSO();
        var existing = new PlotNodeActionInstance(plot, action)
        {
            quantity = 1,
            EnoughForOneInstance = false,
            MaximumInstances = 5,
            MaximumRemainingInstances = 0,
        };
        var active = new PlotNodeActionInstanceListVariable();
        active.value.Add(existing);

        active.AddInstance(new PlotNodeActionInstance(plot, action), 1);

        Assert.Equal(2, existing.quantity);
    }

    [Fact]
    public void FinalFreeActionEntryIsAvailableWhenNativeEntryEvidenceAgrees()
    {
        var state = State(emptyEntries: 1, nativeHasEmptyEntry: true);

        Assert.Equal(
            AutoHarvestEvidenceState.Verified,
            AutoHarvestWorldFacts.ProjectActionSlotAvailability(state));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void MissingEnumeratedOrNativeEmptyEntryRejectsAvailability(
        int emptyEntries,
        bool nativeHasEmptyEntry)
    {
        var state = State(emptyEntries, nativeHasEmptyEntry);

        Assert.Equal(
            AutoHarvestEvidenceState.Rejected,
            AutoHarvestWorldFacts.ProjectActionSlotAvailability(state));
    }

    [Fact]
    public void InvalidNativeStateLeavesAvailabilityUnknown()
    {
        Assert.Equal(
            AutoHarvestEvidenceState.Unknown,
            AutoHarvestWorldFacts.ProjectActionSlotAvailability(
                AutoHarvestSubmissionState.Invalid));
    }

    [Fact]
    public void SharedActiveActionSnapshotProjectsBothPairsWithoutLosingCommonEvidence()
    {
        var fruit = new AutoHarvestActivePairState(matchCount: 1, quantity: 2, engaged: true);
        var treasure = new AutoHarvestActivePairState(matchCount: 2, quantity: 5, engaged: false);
        var snapshot = new AutoHarvestActiveActionSnapshot(
            true,
            usedEntryCount: 3,
            emptyEntryCount: 1,
            nativeHasEmptyEntry: true,
            supportedCollectCount: 3,
            fruit,
            treasure);

        var fruitState = snapshot.Project(AutoHarvestPair.FruitTree);
        var treasureState = snapshot.Project(AutoHarvestPair.TreasureTree);

        Assert.Equal(3, fruitState.SupportedCollectCount);
        Assert.Equal(1, fruitState.PairMatchCount);
        Assert.Equal(2, fruitState.PairQuantity);
        Assert.True(fruitState.PairEngaged);
        Assert.Equal(3, treasureState.SupportedCollectCount);
        Assert.Equal(2, treasureState.PairMatchCount);
        Assert.Equal(5, treasureState.PairQuantity);
        Assert.False(treasureState.PairEngaged);
        Assert.Equal(fruitState.UsedEntryCount, treasureState.UsedEntryCount);
        Assert.Equal(fruitState.EmptyEntryCount, treasureState.EmptyEntryCount);
    }

    private static AutoHarvestSubmissionState State(
        int emptyEntries,
        bool nativeHasEmptyEntry) =>
        new(
            isValid: true,
            usedEntryCount: 2,
            emptyEntryCount: emptyEntries,
            nativeHasEmptyEntry,
            supportedCollectCount: 0,
            pairMatchCount: 0,
            pairQuantity: 0,
            pairEngaged: false);

    /// <summary>
    /// A fruit-tree pair on the game's plot types, bound through the shipping binding set, with the
    /// plot's instance list under test control.
    /// </summary>
    private sealed class PrototypeWorld
    {
        private PrototypeWorld(
            PlotNodeSO plot,
            PlotNodeActionSO action,
            in ResolvedAutoHarvestPair resolved)
        {
            Plot = plot;
            Action = action;
            Resolved = resolved;
        }

        internal PlotNodeSO Plot { get; }
        internal PlotNodeActionSO Action { get; }
        internal PlotNodeSO OtherPlot { get; } = new() { visible = true };
        internal PlotNodeActionSO OtherAction { get; } = new();
        internal ResolvedAutoHarvestPair Resolved { get; }
        internal List<PlotNodeActionInstance> Instances => Plot.GetActionInstances();

        internal static PrototypeWorld Create()
        {
            var plot = new PlotNodeSO { visible = true };
            var action = new PlotNodeActionSO();
            action.prerequisites.available = true;
            Assert.True(AutoHarvestNativeBindings.TryCreate(out var bindings, out var reason), reason);
            var binding = new AutoHarvestPairBinding(
                AutoHarvestPair.FruitTree,
                plot,
                action,
                plot.GetGuid().ToString("D"),
                action.GetGuid().ToString("D"),
                new TreasurePoolSO(),
                null!,
                null!,
                null!);
            var shared = new AutoHarvestSharedBinding(new PlotNodeActionInstanceListVariable(), null!, null!, 1);
            return new PrototypeWorld(
                plot,
                action,
                new ResolvedAutoHarvestPair(bindings!, shared, binding, binding, null));
        }

        /// <summary>
        /// Adds one instance to the pair's plot. The instance may name any plot and action — a plot
        /// holding an instance of something it does not offer is a shape the game produces.
        /// </summary>
        internal PlotNodeActionInstance AddInstance(PlotNodeSO plot, PlotNodeActionSO action)
        {
            var instance = new PlotNodeActionInstance(plot, action);
            Instances.Add(instance);
            return instance;
        }
    }
}
