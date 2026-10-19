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
/// <remarks>
/// The rows carry a plot's identity without claiming it, so <see cref="WorldLookup"/> — which is
/// constrained to entities — cannot serve them. The search is the same binary one over the same sort.
/// </remarks>
internal static class WorldPlotAuthoringLookup
{
    internal static bool TryFind(
        PublicationTable<WorldPlotAuthoring> table,
        Guid plotNodeId,
        out WorldPlotAuthoring row)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].PlotNodeId.CompareTo(plotNodeId);
            if (comparison == 0)
            {
                row = rows[middle];
                return true;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        row = default;
        return false;
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
    internal static bool TryFindRange(
        PublicationTable<WorldPlotPhaseDescriptor> table,
        Guid plotNodeId,
        out int start,
        out int count)
    {
        start = 0;
        count = 0;

        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].PlotNodeId.CompareTo(plotNodeId);
            if (comparison == 0)
            {
                found = middle;
                high = middle - 1;
                continue;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        if (found < 0) return false;

        start = found;
        while (start + count < rows.Length && rows[start + count].PlotNodeId == plotNodeId) count++;
        return true;
    }
}

/// <summary>Every plot's authoring as read, held where a cycle can own it.</summary>
internal sealed class WorldPlotAuthoringBuffer
{
    private const int InitialCapacity = 32;

    private WorldPlotAuthoring[] _samples = new WorldPlotAuthoring[InitialCapacity];
    private int _count;

    internal int Count => _count;

    internal ref readonly WorldPlotAuthoring this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in WorldPlotAuthoring sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

/// <summary>Every authored phase as read.</summary>
internal sealed class WorldPlotPhaseDescriptorBuffer
{
    private const int InitialCapacity = 64;

    private WorldPlotPhaseDescriptor[] _samples = new WorldPlotPhaseDescriptor[InitialCapacity];
    private int _count;

    internal int Count => _count;

    internal ref readonly WorldPlotPhaseDescriptor this[int index] => ref _samples[index];

    internal void Reset() => _count = 0;

    internal void Append(in WorldPlotPhaseDescriptor sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

/// <summary>Publishes the authoring readings, sorted by plot.</summary>
internal static class WorldPlotAuthoringDeriver
{
    internal static PublicationTable<WorldPlotAuthoring> Build(WorldPlotAuthoringBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldPlotAuthoring>.Empty;

        var derived = new WorldPlotAuthoring[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) derived[index] = buffer[index];

        Array.Sort(derived, 0, derived.Length, AuthoringComparer.ByPlot);
        return PublicationTable<WorldPlotAuthoring>.Create(derived, derived.Length);
    }

    private sealed class AuthoringComparer : IComparer<WorldPlotAuthoring>
    {
        internal static readonly IComparer<WorldPlotAuthoring> ByPlot = new AuthoringComparer();

        public int Compare(WorldPlotAuthoring left, WorldPlotAuthoring right) =>
            left.PlotNodeId.CompareTo(right.PlotNodeId);
    }
}

/// <summary>Publishes the phase readings, sorted by plot and then position.</summary>
internal static class WorldPlotPhaseDescriptorDeriver
{
    internal static PublicationTable<WorldPlotPhaseDescriptor> Build(
        WorldPlotPhaseDescriptorBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldPlotPhaseDescriptor>.Empty;

        var derived = new WorldPlotPhaseDescriptor[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) derived[index] = buffer[index];

        Array.Sort(derived, 0, derived.Length, PhaseComparer.ByPlotThenOrdinal);
        return PublicationTable<WorldPlotPhaseDescriptor>.Create(derived, derived.Length);
    }

    private sealed class PhaseComparer : IComparer<WorldPlotPhaseDescriptor>
    {
        internal static readonly IComparer<WorldPlotPhaseDescriptor> ByPlotThenOrdinal =
            new PhaseComparer();

        public int Compare(WorldPlotPhaseDescriptor left, WorldPlotPhaseDescriptor right)
        {
            var byPlot = left.PlotNodeId.CompareTo(right.PlotNodeId);
            return byPlot != 0 ? byPlot : left.Ordinal.CompareTo(right.Ordinal);
        }
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
        var firstFailure = string.Empty;

        for (var index = 0; index < plots.Count; index++)
        {
            var plot = plots[index];
            if (plot is null) continue;

            try
            {
                if (Read(plot, authoring, phases)) sampled++;
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = $"reading a plot's authoring threw: {ex.GetBaseException().Message}";
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private bool Read(
        object plot,
        WorldPlotAuthoringBuffer authoring,
        WorldPlotPhaseDescriptorBuffer phases)
    {
        var plotId = _plotId!(plot);
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
