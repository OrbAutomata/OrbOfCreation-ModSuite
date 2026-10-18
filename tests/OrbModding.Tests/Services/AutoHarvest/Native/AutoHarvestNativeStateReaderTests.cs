using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    public void AnInstanceOfTheWrongNativeTypeRefusesTheWholeList()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.Instances.Add("not an instance");

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
            world.Plot.Visible = false;
        else if (expected == AutoHarvestActionResultCodes.NativeOfferedInstanceMembershipRefused)
            world.Instances.Clear();
        else if (expected == AutoHarvestActionResultCodes.NativeActionRowVisibilityRefused)
            instance.RowVisible = false;
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

    [Fact]
    public void AnUnexpectedExceptionFromANativeReadEscapesClickAdmission()
    {
        var world = PrototypeWorld.Create();
        world.AddInstance(world.Plot, world.Action);
        world.Plot.InstancesUnreadable = true;

        Assert.Throws<NullReferenceException>(() =>
            new AutoHarvestNativeStateReader().ValidateClickAdmission(world.Resolved, out _));
    }

    [Fact]
    public void CurrentPairResolutionReReadsStableUuidAndExactNativeType()
    {
        var world = PrototypeWorld.Create();
        var currentPlot = new StubPlot();
        currentPlot.SetGuid(world.Plot.GetGuid());
        var currentAction = new StubAction();
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
    /// A fruit-tree pair bound to stub natives, with the plot's instance list under test control.
    /// The contract is the production one, filled by reflection the way the binder fills it.
    /// </summary>
    private sealed class PrototypeWorld
    {
        private PrototypeWorld(
            StubPlot plot,
            StubAction action,
            StubPlot otherPlot,
            StubAction otherAction,
            in ResolvedAutoHarvestPair resolved)
        {
            Plot = plot;
            Action = action;
            OtherPlot = otherPlot;
            OtherAction = otherAction;
            Resolved = resolved;
        }

        internal StubPlot Plot { get; }
        internal StubAction Action { get; }
        internal StubPlot OtherPlot { get; }
        internal StubAction OtherAction { get; }
        internal ResolvedAutoHarvestPair Resolved { get; }
        internal List<object> Instances => Plot.instances;

        internal static PrototypeWorld Create()
        {
            var plot = new StubPlot();
            var action = new StubAction();
            var types = Types();
            var binding = new AutoHarvestPairBinding(
                AutoHarvestPair.FruitTree,
                plot,
                action,
                plot.GetGuid().ToString("D"),
                action.GetGuid().ToString("D"),
                new object(),
                null!,
                null!,
                null!);
            var shared = new AutoHarvestSharedBinding(new object(), null!, null!, 1);
            return new PrototypeWorld(
                plot,
                action,
                new StubPlot(),
                new StubAction(),
                new ResolvedAutoHarvestPair(Contract(types), shared, binding, binding, null));
        }

        /// <summary>
        /// Adds one instance to the pair's plot. The instance may name any plot and action — a plot
        /// holding an instance of something it does not offer is a shape the game produces.
        /// </summary>
        internal StubInstance AddInstance(StubPlot plot, StubAction action)
        {
            var instance = new StubInstance(plot, action);
            Plot.instances.Add(instance);
            return instance;
        }

        private static AutoHarvestReflectionTypes Types()
        {
            var types = (AutoHarvestReflectionTypes)Activator.CreateInstance(
                typeof(AutoHarvestReflectionTypes),
                nonPublic: true)!;
            Set(types, nameof(AutoHarvestReflectionTypes.Plot), typeof(StubPlot));
            Set(types, nameof(AutoHarvestReflectionTypes.Action), typeof(StubAction));
            Set(types, nameof(AutoHarvestReflectionTypes.Instance), typeof(StubInstance));
            return types;
        }

        private static AutoHarvestReflectionContract Contract(AutoHarvestReflectionTypes types)
        {
            var constructor = typeof(AutoHarvestReflectionContract).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(AutoHarvestReflectionTypes) },
                modifiers: null)!;
            var contract = (AutoHarvestReflectionContract)constructor.Invoke(new object[] { types });
            Set(contract, nameof(AutoHarvestReflectionContract.PlotStableId),
                AutoHarvestStableIdAccessor.Bind(typeof(StubPlot)));
            Set(contract, nameof(AutoHarvestReflectionContract.ActionStableId),
                AutoHarvestStableIdAccessor.Bind(typeof(StubAction)));
            Set(contract, nameof(AutoHarvestReflectionContract.PlotIsVisible),
                typeof(StubPlot).GetMethod(nameof(StubPlot.IsVisible))!);
            Set(contract, nameof(AutoHarvestReflectionContract.PlotGetActionInstances),
                typeof(StubPlot).GetMethod(nameof(StubPlot.GetActionInstances))!);
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceGetElement),
                typeof(StubInstance).GetMethod(nameof(StubInstance.GetElement))!);
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceGetAction),
                typeof(StubInstance).GetMethod(nameof(StubInstance.GetAction))!);
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceIsVisible),
                typeof(StubInstance).GetMethod(nameof(StubInstance.IsVisible))!);
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceHasEnoughForOneInstance),
                typeof(StubInstance).GetMethod(nameof(StubInstance.HasEnoughForOneInstance))!);
            Set(contract, nameof(AutoHarvestReflectionContract.InstanceGetMaximumRemInstances),
                typeof(StubInstance).GetMethod(nameof(StubInstance.GetMaximumRemInstances))!);
            return contract;
        }

        private static void Set(object target, string property, object value) =>
            target.GetType().GetProperty(property)!.SetValue(target, value);
    }

    private sealed class StubPlot : IdScriptableObject
    {
        public readonly List<object> instances = new();
        public bool Visible { get; set; } = true;
        public bool InstancesUnreadable { get; set; }

        public bool IsVisible() => Visible;
        public IList GetActionInstances() =>
            InstancesUnreadable ? new UnreadableInstanceList() : instances;
    }

    private sealed class UnreadableInstanceList : ArrayList
    {
        public override IEnumerator GetEnumerator() =>
            throw new NullReferenceException("injected instance list read failure");
    }

    private sealed class StubAction : IdScriptableObject
    {
    }

    private sealed class StubInstance
    {
        private readonly StubPlot _plot;
        private readonly StubAction _action;

        internal StubInstance(StubPlot plot, StubAction action)
        {
            _plot = plot;
            _action = action;
        }

        internal bool RowVisible { get; set; } = true;
        internal bool EnoughForOneInstance { get; set; } = true;
        internal int MaximumRemainingInstances { get; set; } = 1;

        public StubPlot GetElement() => _plot;
        public StubAction GetAction() => _action;
        public bool IsVisible() => RowVisible;
        public bool HasEnoughForOneInstance() => EnoughForOneInstance;
        public int GetMaximumRemInstances() => MaximumRemainingInstances;
    }
}
