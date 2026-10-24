using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// What a plot's author decided about it, as opposed to what has happened to it since.
/// </summary>
/// <remarks>
/// <para>
/// These are the facts a safety check asks before it lets an action run: whether the plot drives an
/// action of its own, and how many phases it authors. They change only when the game's content
/// changes, which is why they are collected as facts rather than asked as a question at the action
/// boundary — the verdict a consumer draws from them is that consumer's policy, on its own worker.
/// </para>
/// <para>
/// Keyed by the plot rather than carrying its identity, because the plot itself is already a row in
/// its own category. A second table claiming the same uuid would make the live identity walk report a
/// collision that is not one.
/// </para>
/// </remarks>
internal readonly struct WorldPlotAuthoring
{
    internal WorldPlotAuthoring(Guid plotNodeId, Guid autoActionId, int phaseCount)
    {
        PlotNodeId = plotNodeId;
        AutoActionId = autoActionId;
        PhaseCount = phaseCount;
    }

    internal Guid PlotNodeId { get; }

    /// <summary>
    /// The action the plot runs by itself, or <see cref="Guid.Empty"/> when it authors none.
    /// </summary>
    internal Guid AutoActionId { get; }

    /// <summary>How many phase descriptors the plot authors.</summary>
    internal int PhaseCount { get; }
}

/// <summary>One authored phase of one plot.</summary>
/// <remarks>
/// A phase is not an entity — it is a position in the plot's authored cycle — so this is keyed by the
/// plot and the position, in the order the plot lists them.
/// </remarks>
internal readonly struct WorldPlotPhaseDescriptor
{
    internal WorldPlotPhaseDescriptor(
        Guid plotNodeId,
        int ordinal,
        int phase,
        double phaseTimeSeconds,
        int processType,
        int exitPhase)
    {
        PlotNodeId = plotNodeId;
        Ordinal = ordinal;
        Phase = phase;
        PhaseTimeSeconds = phaseTimeSeconds;
        ProcessType = processType;
        ExitPhase = exitPhase;
    }

    internal Guid PlotNodeId { get; }

    /// <summary>The descriptor's position in the plot's own list.</summary>
    internal int Ordinal { get; }

    /// <summary>Which phase this describes, as the game's enum's underlying integer.</summary>
    internal int Phase { get; }

    internal double PhaseTimeSeconds { get; }

    /// <summary>How the phase's timers run, as the game's enum's underlying integer.</summary>
    internal int ProcessType { get; }

    /// <summary>Which phase this one leaves to, as the game's enum's underlying integer.</summary>
    internal int ExitPhase { get; }
}

/// <summary>
/// Identity lookup over the authoring table.
/// </summary>
internal static class WorldPlotAuthoringLookup
{
    internal static readonly Comparison<WorldPlotAuthoring> Order =
        static (left, right) => left.PlotNodeId.CompareTo(right.PlotNodeId);

    internal static bool TryFind(
        PublicationTable<WorldPlotAuthoring> table,
        Guid plotNodeId,
        out WorldPlotAuthoring row)
    {
        return WorldLookup.TryFindFirst(table.AsSpan(), plotNodeId,
            static (row, key) => row.PlotNodeId.CompareTo(key), out row);
    }
}

/// <summary>
/// Range lookup over the phase table, which is keyed by plot and then position.
/// </summary>
/// <remarks>
/// A plot authors several phases, so this is <see cref="WorldPurchaseCostLookup"/>'s shape: a binary
/// search for the plot's first row, then a forward walk.
/// </remarks>
internal static class WorldPlotPhaseDescriptorLookup
{
    internal static readonly Comparison<WorldPlotPhaseDescriptor> Order =
        static (left, right) =>
        {
            var byPlot = left.PlotNodeId.CompareTo(right.PlotNodeId);
            return byPlot != 0 ? byPlot : left.Ordinal.CompareTo(right.Ordinal);
        };

    internal static bool TryFindRange(
        PublicationTable<WorldPlotPhaseDescriptor> table,
        Guid plotNodeId,
        out int start,
        out int count)
    {
        return WorldLookup.TryFindRange(table.AsSpan(), plotNodeId,
            static (row, key) => row.PlotNodeId.CompareTo(key), out start, out count, resetOnMiss: true);
    }
}

/// <summary>
/// A third walk of the plot registry, for the two authoring tables. It claims no identities: the
/// plots are already claimed by their own category.
/// </summary>
internal sealed class WorldPlotAuthoringReader : IWorldCategoryReader
{
    private readonly Type? _plotType;
    private readonly Func<IList?>? _plots;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _plotId;
    private readonly Func<object, Guid>? _autoActionId;
    private readonly Func<object, IList?>? _phaseInfos;
    private readonly Func<object, int>? _phase;
    private readonly Func<object, double>? _phaseTime;
    private readonly Func<object, int>? _processType;
    private readonly Func<object, int>? _exitPhase;

    internal WorldPlotAuthoringReader(Type? plotType)
    {
        _plotType = plotType;
        _plots = NativeAccessorBinder.StaticListAccessor(plotType, "All");
        if (plotType is null)
        {
            _unavailable = "the PlotNodeSO type was not found on this build";
            return;
        }

        var bind = new WorldMemberBinding(plotType, "PlotNodeSO");
        _plotId = bind.StableUuid();
        _autoActionId = bind.ReferenceGuid("autoAction");
        _phaseInfos = bind.CollectionField("phaseInfos");

        var info = bind.Elements(
            bind.CollectionElementType("phaseInfos"), "PlotNodePhaseInfo");
        _phase = info.EnumField("phase");
        _phaseTime = info.Field<double>("phaseTime");
        _processType = info.EnumField("processType");
        _exitPhase = info.EnumField("exitPhase");

        _unavailable = bind.Failure;
    }

    public string Category => "plot authoring";

    public bool IsAvailable => _plotType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var authoring = frame.PlotAuthoring;
        var phases = frame.PlotPhaseDescriptors;
        authoring.Reset();
        phases.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var plots = _plots?.Invoke();
        if (plots is null)
            return WorldCategoryReport.Missing(Category, "the PlotNodeSO registry was unreadable");

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
                if (Read(plot, authoring, phases, out readOwnerId)) sampled++;
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                WorldSkipTally.Skip(
                    ref skipped,
                    ref firstFailure,
                    WorldFailures.ReadFault($"reading a plot's authoring threw", ex, readOwnerId));
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private bool Read(
        object plot,
        WorldRelationBuffer<WorldPlotAuthoring> authoring,
        WorldRelationBuffer<WorldPlotPhaseDescriptor> phases,
        out Guid readOwnerId)
    {
        readOwnerId = Guid.Empty;
        var plotId = readOwnerId = _plotId!(plot);
        if (plotId == Guid.Empty) return false;

        var infos = _phaseInfos!(plot);
        var count = infos?.Count ?? 0;
        for (var index = 0; index < count; index++)
        {
            var info = infos![index];
            if (info is null) continue;

            phases.Append(new WorldPlotPhaseDescriptor(
                plotId,
                index,
                _phase!(info),
                _phaseTime!(info),
                _processType!(info),
                _exitPhase!(info)));
        }

        authoring.Append(new WorldPlotAuthoring(plotId, _autoActionId!(plot), count));
        return true;
    }
}
