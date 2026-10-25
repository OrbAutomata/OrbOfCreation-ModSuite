using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One plot-and-action pair as read: whether the plot offers the action, whether it has an instance
/// of it, and what the native prerequisite latch proves.
/// </summary>
/// <remarks>
/// A pair is not an entity — neither side owns it and it has no identity of its own — which is why
/// this is its own table rather than a field on either category. It is also the only shape that can
/// answer "may this action be started on this plot", because every term in that question is a
/// function of both.
/// </remarks>
internal readonly struct RawPlotAction
{
    internal RawPlotAction(
        Guid plotNodeId,
        Guid plotNodeActionId,
        int offeredCount,
        int instanceCount,
        PlotActionPrerequisiteEvidence prerequisiteEvidence)
    {
        PlotNodeId = plotNodeId;
        PlotNodeActionId = plotNodeActionId;
        OfferedCount = offeredCount;
        InstanceCount = instanceCount;
        PrerequisiteEvidence = prerequisiteEvidence;
    }

    internal Guid PlotNodeId { get; }

    internal Guid PlotNodeActionId { get; }

    /// <summary>
    /// How many times the plot's <c>availableActions</c> names this action. Carried as a count rather
    /// than a flag because a consumer that requires the pair to be unambiguous cannot tell "offered"
    /// from "offered twice" otherwise.
    /// </summary>
    internal int OfferedCount { get; }

    /// <summary>
    /// How many of the plot's runtime action instances reference this action. The count is the term
    /// a consumer that needs the pair to be unambiguous asks for; which instance, and what is in it,
    /// is a row per instance in <see cref="WorldPlotActionInstance"/>.
    /// </summary>
    internal int InstanceCount { get; }

    /// <summary>
    /// What the native latch proves. A true latch is a prior successful native validation; a false
    /// latch needs exact validation at the action boundary and is never published as an unmet
    /// prerequisite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Prerequisites.Container.Check()</c> is a write: it stamps <c>gameId</c> and, on success,
    /// sets <c>available</c> and leaves it set. Collection reads that field instead. The field is
    /// therefore a one-way latch, and it carries no second flag saying whether the game ever evaluated
    /// it — so an unset latch is two situations at once: prerequisites the game checked and found
    /// unsatisfied, and prerequisites the game has not looked at yet. Nothing readable tells them
    /// apart.
    /// </para>
    /// <para>
    /// Which is why this is named for what it proves rather than for what it is easily taken to mean.
    /// True is a verdict: the game checked and they passed. False is the absence of a verdict and is
    /// carried as an exact request for native validation. A pair discovered only through an instance
    /// has no latch reading at all and remains unknown.
    /// </para>
    /// </remarks>
    internal PlotActionPrerequisiteEvidence PrerequisiteEvidence { get; }

    /// <summary>Legacy diagnostic projection; policy must use <see cref="PrerequisiteEvidence"/>.</summary>
    internal bool PrerequisitesConfirmed =>
        PrerequisiteEvidence == PlotActionPrerequisiteEvidence.NativeLatchedTrue;
}

/// <summary>What one published plot-action prerequisite reading can honestly prove.</summary>
internal enum PlotActionPrerequisiteEvidence
{
    Unknown = 0,
    NativeLatchedTrue = 1,
    UnknownNeedsNativeValidation = 2,
}

/// <summary>One plot-and-action pair as published.</summary>
internal readonly struct WorldPlotAction
{
    internal WorldPlotAction(
        in RawPlotAction reading,
        int elementCost,
        bool elementCostKnown,
        bool hasEnoughForOneInstance,
        int maximumRemainingInstances)
    {
        Reading = reading;
        ElementCost = elementCost;
        ElementCostKnown = elementCostKnown;
        HasEnoughForOneInstance = hasEnoughForOneInstance;
        MaximumRemainingInstances = maximumRemainingInstances;
    }

    internal RawPlotAction Reading { get; }

    internal Guid PlotNodeId => Reading.PlotNodeId;

    internal Guid PlotNodeActionId => Reading.PlotNodeActionId;

    /// <summary>
    /// What one run of this action costs the plot, from <c>PlotNodeActionSO.GetElementCost(plot)</c>.
    /// Meaningless unless <see cref="ElementCostKnown"/>.
    /// </summary>
    internal int ElementCost { get; }

    /// <summary>
    /// Whether the cost could be computed at all. It cannot when the action scales its cost by size
    /// <em>and</em> names other nodes to take that size from: the multiplier is then a product over
    /// those nodes' next-size percentages, which is a chain this suite has not ported. An unknown
    /// cost is published as unknown rather than as the unscaled one, which would be too cheap.
    /// </summary>
    internal bool ElementCostKnown { get; }

    /// <summary>
    /// Whether the plot has enough left for one run, from
    /// <c>PlotNodeActionInstance.HasEnoughForOneInstance()</c>. False when the cost is unknown.
    /// </summary>
    internal bool HasEnoughForOneInstance { get; }

    /// <summary>
    /// How many more runs the plot could support, from
    /// <c>PlotNodeActionInstance.GetMaximumRemInstances()</c>. Zero when the cost is unknown.
    /// </summary>
    internal int MaximumRemainingInstances { get; }
}

/// <summary>
/// One of a plot's runtime action instances: which action it runs, how much of it, and whether it is
/// under way.
/// </summary>
/// <remarks>
/// <para>
/// An instance has no identity of its own — it is a position in the plot's list, holding a reference
/// to an action — so it is keyed by the pair it belongs to and its position in that list. A plot can
/// hold several instances of one action, which is why the pair's count alone cannot answer "which
/// one": that question is what the action boundary re-derives live today.
/// </para>
/// <para>
/// Whether the instance is visible is deliberately absent. <c>IsVisible()</c> reaches
/// <c>Prerequisites.Container.Check()</c>, which is a write, and the latch it writes is already
/// published as the pair's <see cref="RawPlotAction.PrerequisiteEvidence"/>.
/// </para>
/// </remarks>
internal readonly struct WorldPlotActionInstance
{
    internal WorldPlotActionInstance(
        Guid plotNodeId,
        Guid plotNodeActionId,
        int ordinal,
        int quantity,
        bool engaged,
        bool empty,
        bool referenceResolved)
    {
        PlotNodeId = plotNodeId;
        PlotNodeActionId = plotNodeActionId;
        Ordinal = ordinal;
        Quantity = quantity;
        Engaged = engaged;
        Empty = empty;
        ReferenceResolved = referenceResolved;
    }

    internal Guid PlotNodeId { get; }

    /// <summary>
    /// Which action the instance references, or <see cref="Guid.Empty"/> when the reference names
    /// nothing this pass could resolve.
    /// </summary>
    internal Guid PlotNodeActionId { get; }

    /// <summary>The instance's position in the plot's own list.</summary>
    internal int Ordinal { get; }

    /// <summary>How much of the action the instance is running, from <c>GetActualQuantity()</c>.</summary>
    internal int Quantity { get; }

    /// <summary>Whether the instance is under way rather than merely present, from <c>IsEngaged()</c>.</summary>
    internal bool Engaged { get; }

    /// <summary>Whether the instance holds nothing, from <c>IsEmpty()</c>.</summary>
    internal bool Empty { get; }

    /// <summary>
    /// Whether the reference resolved to an action at all. Published rather than dropped: "the plot
    /// holds an instance we could not identify" is a fact, and a table that simply omitted it would
    /// report the plot as holding one instance fewer than it does.
    /// </summary>
    internal bool ReferenceResolved { get; }
}

/// <summary>
/// One resource a plot action drains each second while it runs on a plot, as the plot's diamond
/// tooltip prints it: <c>actionDrain</c> scaled by the plot's
/// <c>GetPersistentScalingInfo(1).GetDrainCostMod()</c>. Keyed by the pair because the modifier is
/// the plot's.
/// </summary>
internal readonly struct WorldPlotActionDrain
{
    internal WorldPlotActionDrain(
        Guid plotNodeId,
        Guid plotNodeActionId,
        int ordinal,
        Guid resourceId,
        BigDouble amount)
    {
        PlotNodeId = plotNodeId;
        PlotNodeActionId = plotNodeActionId;
        Ordinal = ordinal;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid PlotNodeId { get; }

    internal Guid PlotNodeActionId { get; }

    /// <summary>The entry's position in the authored list, which is the tooltip's order.</summary>
    internal int Ordinal { get; }

    internal Guid ResourceId { get; }

    /// <summary>Per second, before the resource's own spend rounding.</summary>
    internal BigDouble Amount { get; }
}

internal static class WorldPlotActionDrainLookup
{
    internal static readonly Comparison<WorldPlotActionDrain> Order =
        static (left, right) =>
        {
            var plot = left.PlotNodeId.CompareTo(right.PlotNodeId);
            if (plot != 0) return plot;
            var action = left.PlotNodeActionId.CompareTo(right.PlotNodeActionId);
            return action != 0 ? action : left.Ordinal.CompareTo(right.Ordinal);
        };

    /// <summary>The pair's drain rows: the half-open range sorted by authored order.</summary>
    internal static ReadOnlySpan<WorldPlotActionDrain> For(
        PublicationTable<WorldPlotActionDrain> table,
        Guid plotNodeId,
        Guid plotNodeActionId)
    {
        WorldLookup.TryFindRange(table.AsSpan(), (Plot: plotNodeId, Action: plotNodeActionId),
            static (row, key) => Compare(row, key.Plot, key.Action), out var start, out var count);
        return table.AsSpan().Slice(start, count);
    }

    private static int Compare(in WorldPlotActionDrain row, Guid plotNodeId, Guid plotNodeActionId)
    {
        var byPlot = row.PlotNodeId.CompareTo(plotNodeId);
        return byPlot != 0 ? byPlot : row.PlotNodeActionId.CompareTo(plotNodeActionId);
    }
}

/// <summary>
/// Range lookup over the instance table, which is sorted by plot, then action, then position.
/// </summary>
/// <remarks>
/// A pair can hold several instances, so this answers with a range rather than a row — the shape
/// purchase costs already use, and for the same reason: a search that landed anywhere inside the
/// pair's rows and walked forward would report some of its instances as all of them.
/// </remarks>
internal static class WorldPlotActionInstanceLookup
{
    /// <summary>
    /// By plot, then action, then position, so one pair's instances are contiguous and in the order
    /// the plot holds them.
    /// </summary>
    internal static readonly Comparison<WorldPlotActionInstance> Order =
        static (left, right) =>
        {
            var byPlot = left.PlotNodeId.CompareTo(right.PlotNodeId);
            if (byPlot != 0) return byPlot;

            var byAction = left.PlotNodeActionId.CompareTo(right.PlotNodeActionId);
            return byAction != 0 ? byAction : left.Ordinal.CompareTo(right.Ordinal);
        };

    /// <summary>
    /// The half-open row range belonging to the pair. Both indices are zero when the pair holds no
    /// instance, which is the honest reading of a plot that is not running the action.
    /// </summary>
    internal static bool TryFindRange(
        PublicationTable<WorldPlotActionInstance> table,
        Guid plotNodeId,
        Guid plotNodeActionId,
        out int start,
        out int count)
    {
        return WorldLookup.TryFindRange(table.AsSpan(), (Plot: plotNodeId, Action: plotNodeActionId),
            static (row, key) => Compare(row, key.Plot, key.Action), out start, out count, resetOnMiss: true);
    }

    private static int Compare(in WorldPlotActionInstance row, Guid plotNodeId, Guid plotNodeActionId)
    {
        var byPlot = row.PlotNodeId.CompareTo(plotNodeId);
        return byPlot != 0 ? byPlot : row.PlotNodeActionId.CompareTo(plotNodeActionId);
    }
}

/// <summary>
/// Exact lookup over the plot-action table, which is sorted by plot and then action.
/// </summary>
/// <remarks>
/// Not <see cref="WorldLookup"/>: that keys on a single identity and a pair has two. The table is
/// sorted on both, so one binary search over the composite key answers the whole question.
/// </remarks>
internal static class WorldPlotActionLookup
{
    internal static bool TryFind(
        PublicationTable<WorldPlotAction> table,
        Guid plotNodeId,
        Guid plotNodeActionId,
        out WorldPlotAction row)
    {
        return WorldLookup.TryFindFirst(table.AsSpan(), (Plot: plotNodeId, Action: plotNodeActionId),
            static (row, key) => Compare(row, key.Plot, key.Action), out row);
    }

    private static int Compare(in WorldPlotAction row, Guid plotNodeId, Guid plotNodeActionId)
    {
        var byPlot = row.PlotNodeId.CompareTo(plotNodeId);
        return byPlot != 0 ? byPlot : row.PlotNodeActionId.CompareTo(plotNodeActionId);
    }
}

/// <summary>
/// Answers "may this action be started on this plot" for every pair, on the worker.
/// </summary>
/// <remarks>
/// Cross-table like <see cref="WorldPurchaseCostDeriver"/> and for the same reason: every term is a
/// function of both sides. The plot supplies what is left to act on and its size modifier, the action
/// supplies what one run costs and how that cost scales.
/// </remarks>
internal sealed class WorldPlotActionDeriver
{
    private readonly PublicationTable<WorldPlotNode> _plotNodes;
    private readonly PublicationTable<WorldPlotNodeAction> _actions;

    internal WorldPlotActionDeriver(
        PublicationTable<WorldPlotNode> plotNodes,
        PublicationTable<WorldPlotNodeAction> actions)
    {
        _plotNodes = plotNodes ?? throw new ArgumentNullException(nameof(plotNodes));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    internal PublicationTable<WorldPlotAction> Build(WorldRelationBuffer<RawPlotAction> buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldPlotAction>.Empty;

        return WorldRelationTable.Build(buffer, reading => Derive(in reading),
            static (left, right) =>
            {
                var plot = left.PlotNodeId.CompareTo(right.PlotNodeId);
                return plot != 0 ? plot : left.PlotNodeActionId.CompareTo(right.PlotNodeActionId);
            });
    }

    private WorldPlotAction Derive(in RawPlotAction reading)
    {
        if (!WorldLookup.TryFind(_plotNodes, reading.PlotNodeId, out var plot) ||
            !WorldLookup.TryFind(_actions, reading.PlotNodeActionId, out var action))
        {
            return new WorldPlotAction(in reading, 0, false, false, 0);
        }

        if (!TryElementCost(in plot, in action, out var cost))
            return new WorldPlotAction(in reading, 0, false, false, 0);

        // Which of the plot's two remainders applies is the action's choice, and the two differ:
        // an any-state cost draws on everything the node has, a phase cost only on what is idle.
        var remaining = action.UseAnyStateForCost
            ? plot.RemainingTotalQuantity
            : plot.RemainingQuantity;

        // GetMaximumRemInstances() branches on the *authored* cost, not the size-scaled one, and
        // short-circuits a non-positive authored cost to the flat instance ceiling. The two agree —
        // the scaling branch floors at one — but the game's own condition is the one worth keeping.
        // Negative remainders are clamped away rather than published: the game lets its own answer go
        // negative, and "how many more runs fit" has no reading below none.
        var maximum = action.ElementCost <= 0 ? MaximumInstances : remaining / cost;
        return new WorldPlotAction(in reading, cost, true, cost <= remaining, Math.Max(maximum, 0));
    }

    /// <summary>
    /// Ported from <c>PlotNodeActionSO.GetElementCost(PlotNodeSO)</c>.
    /// </summary>
    /// <remarks>
    /// The unported branch is the third one. When an action both scales its cost by size and names
    /// other nodes to take that size from, the multiplier is a product of those nodes' next-size
    /// percentages, and neither the node list nor that chain is collected. It fails rather than
    /// falling back to the unscaled cost, which would be too cheap in the one direction that makes a
    /// consumer start an action the plot cannot pay for.
    /// </remarks>
    private static bool TryElementCost(
        in WorldPlotNode plot,
        in WorldPlotNodeAction action,
        out int cost)
    {
        var authored = action.ElementCost;
        if (!action.UseSizeModForCost || authored <= 0)
        {
            cost = authored;
            return true;
        }

        if (action.SizeModNodeCount != 0)
        {
            cost = 0;
            return false;
        }

        var sizePercent = OrbGameMath.AsPercent(plot.Reading.SizeMod);
        if (sizePercent == BigDouble.Zero)
        {
            cost = 0;
            return false;
        }

        cost = Math.Max(BigDouble.Floor(authored / sizePercent).ToInt(), 1);
        return true;
    }

    /// <summary>
    /// <c>PlotNodeActionInstance.GetMaximumInstances()</c>, which is the literal constant the game
    /// returns for every action rather than anything derived. Collection copies the value instead of
    /// calling the member, so <c>plot-lifecycle.instance-maximum-action</c> carries this owner as a
    /// mirrored usage beside the action boundary's reflected one.
    /// </summary>
    private const int MaximumInstances = 10000;


}

/// <summary>
/// Reads which actions each plot offers and which of them it holds an instance of. A second walk of
/// the plot registry, because a pair is not one row per entity.
/// </summary>
/// <remarks>
/// It claims no identities: both sides of a pair are already claimed by their own categories.
/// </remarks>
internal sealed class WorldPlotActionReader : IWorldCategoryReader
{
    private readonly WorldDruidryLevelCapture _levelEconomy;
    private bool _captureLevelEconomy;
    private readonly Type? _plotType;
    private readonly Func<IList?>? _plots;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _plotId;
    private readonly Func<object, IList?>? _offered;
    private readonly Func<object, IList?>? _instances;
    private readonly Func<object, Guid>? _actionId;
    private readonly Func<object, bool>? _prerequisitesConfirmed;
    private readonly Func<object, Guid>? _instanceActionGuid;
    private readonly Func<object, string?>? _instanceActionId;
    private readonly Func<object, int>? _instanceQuantity;
    private readonly Func<object, bool>? _instanceEngaged;
    private readonly Func<object, bool>? _instanceEmpty;
    private readonly Func<object, object?>? _instanceAction;
    private readonly Func<object, int, object?>? _instanceScaling;
    private readonly Func<object, object?>? _actionDrain;
    private readonly Func<object, IList?>? _drainEntries;
    private readonly Func<object, Guid>? _drainResource;
    private readonly Func<object, BigDouble>? _drainAmount;
    private readonly Func<object, BigDouble>? _drainCostMod;

    internal WorldPlotActionReader(Type? plotType, Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        _levelEconomy = new WorldDruidryLevelCapture(resolveType);
        _plotType = plotType;
        _plots = NativeAccessorBinder.StaticListAccessor(plotType, "All");
        if (plotType is null)
        {
            _unavailable = "the PlotNodeSO type was not found on this build";
            return;
        }

        var bind = new WorldMemberBinding(plotType, "PlotNodeSO");
        _plotId = bind.StableUuid();
        _offered = bind.CollectionField("availableActions");
        _instances = bind.CollectionField("actionInstances");

        var costType = resolveType("ResourceCostList");
        var tupleType = resolveType("ResourceTuple");
        var scalingType = resolveType("ScalingInfo");

        var actionType = bind.CollectionElementType("availableActions");
        var action = bind.Elements(actionType, "PlotNodeActionSO");
        _actionId = action.StableUuid();
        _prerequisitesConfirmed = action.NestedField<bool>("prerequisites", "available");
        _actionDrain = action.Reference("actionDrain", costType);

        var cost = bind.Elements(costType, "ResourceCostList");
        _drainEntries = cost.CallList("GetEntries", tupleType);
        var entry = bind.Elements(tupleType, "ResourceTuple");
        _drainResource = entry.ReferenceGuid("resource");
        _drainAmount = entry.Call<BigDouble>("GetValue");
        _drainCostMod = bind.Elements(scalingType, "ScalingInfo").Call<BigDouble>("GetDrainCostMod");

        var instance = bind.Elements(
            bind.CollectionElementType("actionInstances"), "PlotNodeActionInstance");
        _instanceAction = instance.CallObject("GetAction", actionType);
        _instanceScaling = instance.CallObject<int>("GetPersistentScalingInfo", scalingType);
        _instanceActionGuid = instance.NestedField<Guid>("refObj", "_guid");
        _instanceActionId = instance.NestedField<string?>("refObj", "idStr");
        _instanceQuantity = instance.Call<int>("GetActualQuantity");
        _instanceEngaged = instance.Call<bool>("IsEngaged");
        _instanceEmpty = instance.Call<bool>("IsEmpty");

        _unavailable = bind.Failure;
    }

    public string Category => "plot actions";

    public bool IsAvailable => _plotType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        _captureLevelEconomy = frame.CaptureLevelEconomy;
        if (_captureLevelEconomy)
        {
            _levelEconomy.Begin();
            frame.LevelEconomy.SetDruidryInputs(new WorldDruidryLevelInputs("The plot action snapshot is unavailable."));
        }
        var buffer = frame.PlotActions;
        var instances = frame.PlotActionInstances;
        var drains = frame.PlotActionDrains;
        buffer.Reset();
        instances.Reset();
        drains.Reset();
        if (!IsAvailable)
        {
            frame.LevelEconomy.SetDruidryInputs(_levelEconomy.Finish(false));
            return WorldCategoryReport.Missing(Category, _unavailable);
        }

        var plots = _plots?.Invoke();
        if (plots is null)
        {
            frame.LevelEconomy.SetDruidryInputs(_levelEconomy.Finish(false));
            return WorldCategoryReport.Missing(Category, "the PlotNodeSO registry was unreadable");
        }

        var sampled = 0;
        var skipped = 0;
        var firstFailure = default(WorldCollectionFailure);

        for (var index = 0; index < plots.Count; index++)
        {
            var plot = plots[index];
            if (plot is null) continue;

            var readOwnerId = Guid.Empty;
            try
            {
                sampled += Read(plot, buffer, instances, drains, out readOwnerId);
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                WorldSkipTally.Skip(
                    ref skipped,
                    ref firstFailure,
                    WorldFailures.ReadFault($"reading a plot's actions threw", ex, readOwnerId));
            }
        }

        if (_captureLevelEconomy) frame.LevelEconomy.SetDruidryInputs(_levelEconomy.Finish(skipped == 0));
        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private int Read(
        object plot,
        WorldRelationBuffer<RawPlotAction> buffer,
        WorldRelationBuffer<WorldPlotActionInstance> instances,
        WorldRelationBuffer<WorldPlotActionDrain> drains,
        out Guid readOwnerId)
    {
        readOwnerId = Guid.Empty;
        var plotId = readOwnerId = _plotId!(plot);
        if (plotId == Guid.Empty) return 0;

        var appended = Offered(plot, plotId, buffer);
        Instantiated(plot, plotId, buffer, instances, drains);
        return appended;
    }

    private int Offered(object plot, Guid plotId, WorldRelationBuffer<RawPlotAction> buffer)
    {
        var offered = _offered!(plot);
        if (offered is null) return 0;

        var appended = 0;
        for (var index = 0; index < offered.Count; index++)
        {
            var action = offered[index];
            if (action is null) continue;

            var actionId = _actionId!(action);
            if (actionId == Guid.Empty) continue;

            var existing = IndexOf(buffer, plotId, actionId);
            if (existing >= 0)
            {
                ref readonly var previous = ref buffer[existing];
                buffer.Replace(existing, new RawPlotAction(
                    plotId,
                    actionId,
                    previous.OfferedCount + 1,
                    previous.InstanceCount,
                    previous.PrerequisiteEvidence));
                continue;
            }

            buffer.Append(new RawPlotAction(
                plotId,
                actionId,
                1,
                0,
                _prerequisitesConfirmed!(action)
                    ? PlotActionPrerequisiteEvidence.NativeLatchedTrue
                    : PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation));
            appended++;
        }

        return appended;
    }

    /// <summary>
    /// Reads the plot's runtime action instances: one row each, and a count against the pairs already
    /// recorded. An instance of an action the plot no longer offers is counted too, appended with a
    /// zero offer count — a consumer asking whether the pair is unambiguous should see that, not miss
    /// it.
    /// </summary>
    /// <remarks>
    /// An instance whose reference resolves to nothing still gets a row, keyed on no action, because
    /// "the plot holds an instance we could not identify" is a fact. It gets no <em>pair</em>, since
    /// a pair with no action on one side is not one.
    /// </remarks>
    private void Instantiated(
        object plot,
        Guid plotId,
        WorldRelationBuffer<RawPlotAction> buffer,
        WorldRelationBuffer<WorldPlotActionInstance> rows,
        WorldRelationBuffer<WorldPlotActionDrain> drains)
    {
        var instances = _instances!(plot);
        if (instances is null) return;

        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            if (instance is null) continue;

            if (_captureLevelEconomy) _levelEconomy.Capture(plot, instance);
            var actionId = ReferencedAction(instance);
            rows.Append(new WorldPlotActionInstance(
                plotId,
                actionId,
                index,
                _instanceQuantity!(instance),
                _instanceEngaged!(instance),
                _instanceEmpty!(instance),
                actionId != Guid.Empty));

            if (actionId == Guid.Empty) continue;

            var existing = IndexOf(buffer, plotId, actionId);
            if (existing < 0 || buffer[existing].InstanceCount == 0)
                Drains(instance, plotId, actionId, drains);

            if (existing < 0)
            {
                // Unknown, because this walk never read the action's latch: the pair exists only
                // because an instance references it, and the reference is not the action object whose
                // prerequisites container the offered walk reads.
                buffer.Append(new RawPlotAction(
                    plotId,
                    actionId,
                    0,
                    1,
                    PlotActionPrerequisiteEvidence.Unknown));
                continue;
            }

            ref readonly var previous = ref buffer[existing];
            buffer.Replace(existing, new RawPlotAction(
                plotId,
                actionId,
                previous.OfferedCount,
                previous.InstanceCount + 1,
                previous.PrerequisiteEvidence));
        }
    }

    /// <summary>
    /// The index of the pair already recorded for this plot and action, or <c>-1</c>. A linear scan
    /// backwards from the end, which is where it will be: the reader finishes one plot before
    /// starting the next, so a plot's pairs are the tail while it is being read.
    /// </summary>
    private static int IndexOf(
        WorldRelationBuffer<RawPlotAction> buffer,
        Guid plotNodeId,
        Guid plotNodeActionId)
    {
        for (var index = buffer.Count - 1; index >= 0; index--)
        {
            ref readonly var sample = ref buffer[index];
            if (sample.PlotNodeId != plotNodeId) return -1;
            if (sample.PlotNodeActionId == plotNodeActionId) return index;
        }

        return -1;
    }

    /// <summary>
    /// What the diamond tooltip prints as the action's drain. The game's tooltip passes the active
    /// quantity, but nothing under <c>GetPersistentScalingInfo</c> reads it except the parallel ratio,
    /// and no authored plot action is parallel.
    /// </summary>
    private void Drains(
        object instance,
        Guid plotId,
        Guid actionId,
        WorldRelationBuffer<WorldPlotActionDrain> drains)
    {
        var action = _instanceAction!(instance)
            ?? throw new InvalidOperationException("a plot action instance referenced no action");
        var entries = _drainEntries!(_actionDrain!(action)
            ?? throw new InvalidOperationException("a plot action had no drain list"));
        if (entries is null || entries.Count == 0) return;

        var scaling = _instanceScaling!(instance, 1)
            ?? throw new InvalidOperationException("a plot action instance had no scaling info");
        var factor = OrbGameMath.AsPercent(_drainCostMod!(scaling));
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]
                ?? throw new InvalidOperationException("a plot action drain entry was null");
            var resourceId = _drainResource!(entry);
            if (resourceId == Guid.Empty)
                throw new InvalidOperationException("a plot action drain named no resource");
            drains.Append(new WorldPlotActionDrain(
                plotId, actionId, index, resourceId, _drainAmount!(entry) * factor));
        }
    }

    /// <summary>
    /// Which action an instance points at, reproducing <c>IdObjectRef.GetGuid()</c> without its
    /// memoisation. The original parses the serialized string into a cache field on first ask; that
    /// write would land on a boxed copy here rather than on the game's struct, which makes it
    /// harmless and pointless in equal measure. Reading both fields says the same thing and says it
    /// without the sleight of hand.
    /// </summary>
    private Guid ReferencedAction(object instance)
    {
        var cached = _instanceActionGuid!(instance);
        if (cached != Guid.Empty) return cached;

        var serialized = _instanceActionId!(instance);
        return string.IsNullOrEmpty(serialized) || !Guid.TryParse(serialized, out var parsed)
            ? Guid.Empty
            : parsed;
    }
}
