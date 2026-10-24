#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal enum GameMcpWorldPageKind
{
    Rows,
    MasteryExperience,
    Targeting,
}

/// <summary>One category's identity, table, read policy and gameplay capabilities.</summary>
internal sealed class GameMcpEntityCapabilityDescriptor
{
    private readonly GameMcpCommandKind[] _capabilities;
    private readonly GameMcpWorldTable? _table;
    private readonly Func<GameWorldState, GameMcpEntityCapabilityDescriptor, object, GameMcpValue>? _project;
    private readonly Func<GameWorldState, GameMcpEntityCapabilityDescriptor, object, int, int, GameMcpValue>? _list;
    private readonly Func<GameWorldState, object, bool>? _affordable;
    private readonly Func<object, bool>? _discovered;

    internal GameMcpEntityCapabilityDescriptor(
        string category,
        string expectedNativeType,
        GameMcpCommandKind[] capabilities,
        GameMcpWorldTable? table = null,
        string[]? reports = null,
        string[]? failureReports = null,
        string[]? scan = null,
        string[]? columns = null,
        Func<GameWorldState, GameMcpEntityCapabilityDescriptor, object, GameMcpValue>? projector = null,
        Func<GameWorldState, GameMcpEntityCapabilityDescriptor, object, int, int, GameMcpValue>? listProjector = null,
        bool declaresList = false,
        GameMcpWorldPageKind pageKind = GameMcpWorldPageKind.Rows,
        bool localizesRequirementFailures = false,
        Func<GameWorldState, object, bool>? affordable = null,
        Func<object, bool>? discovered = null)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
        ExpectedNativeType = expectedNativeType ?? throw new ArgumentNullException(nameof(expectedNativeType));
        _capabilities = capabilities is null
            ? Array.Empty<GameMcpCommandKind>()
            : (GameMcpCommandKind[])capabilities.Clone();
        _table = table;
        ReportCategories = reports is null ? Array.Empty<string>() : (string[])reports.Clone();
        FailureOnlyReportCategories = failureReports is null ? Array.Empty<string>() : (string[])failureReports.Clone();
        ScanFields = scan is null ? Array.Empty<string>() : (string[])scan.Clone();
        ListColumns = columns is null ? Array.Empty<string>() : (string[])columns.Clone();
        _project = projector;
        _list = listProjector;
        DeclaresList = declaresList;
        PageKind = pageKind;
        LocalizesRequirementFailures = localizesRequirementFailures;
        _affordable = affordable;
        _discovered = discovered;
    }

    internal string Category { get; }
    internal string Name => Category;
    internal string ExpectedNativeType { get; }
    internal IReadOnlyList<GameMcpCommandKind> Capabilities => _capabilities;
    internal string WorldPropertyName => Table.PropertyName;
    internal string RowTypeName => Table.RowTypeName;
    internal string IdentityMode => Table.IdentityMode;
    internal string[] ReportCategories { get; }
    internal string[] FailureOnlyReportCategories { get; }
    internal string[] ScanFields { get; }
    internal string[] ListColumns { get; }
    internal bool DeclaresList { get; }
    internal GameMcpWorldPageKind PageKind { get; }
    internal bool LocalizesRequirementFailures { get; }
    internal bool Priced => _affordable is not null;
    internal bool Discoverable => _discovered is not null;
    private GameMcpWorldTable Table => _table ??
        throw new InvalidOperationException("category " + Category + " has no table");

    internal int Count(GameWorldState world) => Table.Count(world);
    internal object Row(GameWorldState world, int index) => Table.Row(world, index);
    internal bool TryIdentity(object row, out Guid identity) => Table.TryIdentity(row, out identity);
    internal GameMcpValue Project(GameWorldState world, object row) =>
        (_project ?? throw new InvalidOperationException("category " + Category + " has no row projector"))(world, this, row);
    internal GameMcpValue ProjectList(GameWorldState world, object row, int offset, int limit) =>
        (_list ?? throw new InvalidOperationException("category " + Category + " has no list projector"))(world, this, row, offset, limit);
    internal bool IsAffordable(GameWorldState world, object row) =>
        (_affordable ?? throw new InvalidOperationException("category " + Category + " has no price filter"))(world, row);
    internal bool IsDiscovered(object row) =>
        (_discovered ?? throw new InvalidOperationException("category " + Category + " has no discovery filter"))(row);

    internal bool Supports(GameMcpCommandKind capability)
    {
        for (var index = 0; index < _capabilities.Length; index++)
            if (_capabilities[index] == capability) return true;
        return false;
    }

    internal IEnumerable<string> Problems()
    {
        var prefix = "category " + Category + " has no ";
        if (Category.Length == 0) yield return "category has no name";
        if (ExpectedNativeType.Length == 0) yield return prefix + "native type";
        if (_table is null) yield return prefix + "table";
        if (ReportCategories.Length == 0) yield return prefix + "collection reports";
        if (ScanFields.Length == 0) yield return prefix + "scan fields";
        if (ListColumns.Length == 0) yield return prefix + "list columns";
        if (_project is null) yield return prefix + "row projector";
        if (_list is null) yield return prefix + "list projector";
    }
}
#endif
