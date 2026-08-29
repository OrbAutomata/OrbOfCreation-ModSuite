using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One player-owned Brewing Station instance and its screen-visible staged recipe.</summary>
internal readonly struct WorldCraftingStation : IWorldEntity
{
    internal WorldCraftingStation(
        Guid stationId,
        Guid structureTypeId,
        Guid recipeId,
        Guid firstIngredientId,
        Guid secondIngredientId,
        Guid outputId,
        bool loaded,
        bool active,
        int level,
        int minimumLevel,
        int maximumLevel)
    {
        StationId = stationId;
        StructureTypeId = structureTypeId;
        RecipeId = recipeId;
        FirstIngredientId = firstIngredientId;
        SecondIngredientId = secondIngredientId;
        OutputId = outputId;
        Loaded = loaded;
        Active = active;
        Level = level;
        MinimumLevel = minimumLevel;
        MaximumLevel = maximumLevel;
    }

    internal Guid StationId { get; }
    public Guid EntityId => StationId;
    internal Guid StructureTypeId { get; }
    internal Guid RecipeId { get; }
    internal Guid FirstIngredientId { get; }
    internal Guid SecondIngredientId { get; }
    internal Guid OutputId { get; }
    internal bool Loaded { get; }
    internal bool Active { get; }
    internal int Level { get; }
    internal int MinimumLevel { get; }
    internal int MaximumLevel { get; }
}

internal enum WorldCraftingStationOptionKind
{
    FirstIngredient = 0,
    SecondIngredient = 1,
    Output = 2,
}

/// <summary>One choice rendered by one of the Brewing Station's three selector strips.</summary>
internal readonly struct WorldCraftingStationOption
{
    internal WorldCraftingStationOption(
        Guid stationId,
        WorldCraftingStationOptionKind kind,
        Guid optionId,
        bool available)
    {
        StationId = stationId;
        Kind = kind;
        OptionId = optionId;
        Available = available;
    }

    internal Guid StationId { get; }
    internal WorldCraftingStationOptionKind Kind { get; }
    internal Guid OptionId { get; }
    internal bool Available { get; }
}

/// <summary>One resource drain row currently shown for an active or staged Brewing Station.</summary>
internal readonly struct WorldCraftingStationDrain
{
    internal WorldCraftingStationDrain(Guid stationId, Guid resourceId, BigDouble amount)
    {
        StationId = stationId;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid StationId { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal static class WorldCraftingStationLookup
{
    internal static bool TryFind(
        PublicationTable<WorldCraftingStation> table,
        Guid stationId,
        out WorldCraftingStation station) =>
        WorldLookup.TryFind(table, stationId, out station);

    internal static bool TryFindOptions(
        PublicationTable<WorldCraftingStationOption> table,
        Guid stationId,
        out int start,
        out int count) => TryFindRange(table, stationId, static row => row.StationId, out start, out count);

    internal static bool TryFindDrains(
        PublicationTable<WorldCraftingStationDrain> table,
        Guid stationId,
        out int start,
        out int count) => TryFindRange(table, stationId, static row => row.StationId, out start, out count);

    private static bool TryFindRange<TRow>(
        PublicationTable<TRow> table,
        Guid stationId,
        Func<TRow, Guid> id,
        out int start,
        out int count)
        where TRow : struct
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (id(rows[middle]).CompareTo(stationId) < 0) low = middle + 1;
            else high = middle - 1;
        }
        start = low;
        count = 0;
        while (start + count < rows.Length && id(rows[start + count]) == stationId) count++;
        return count > 0;
    }
}

/// <summary>Reads the exact selectors and controls owned by <c>UIBrewingStation</c>.</summary>
internal sealed class WorldCraftingStationReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _structureType;
    private readonly Type? _stationType;
    private readonly Type? _typeElementType;
    private readonly Func<IList?>? _structures;
    private readonly Func<object, Guid>? _structureId;
    private readonly Func<object, IList?>? _instances;
    private readonly Func<object, IList?>? _ingredientLists;
    private readonly Func<object, IList?>? _elements;
    private readonly Func<object, Guid>? _elementId;
    private readonly Func<object, bool>? _elementAvailable;
    private readonly Func<object, Guid>? _stationId;
    private readonly Func<object, object?>? _stationReference;
    private readonly Func<object, Guid>? _recipeId;
    private readonly Func<object, int, object?>? _ingredient;
    private readonly Func<object, object?>? _output;
    private readonly Func<object, IList?>? _outputList;
    private readonly Func<object, object, bool>? _outputVisible;
    private readonly Func<object, bool>? _loaded;
    private readonly Func<object, bool>? _active;
    private readonly Func<object, int>? _level;
    private readonly Func<object, int>? _minimumLevel;
    private readonly Func<object, int>? _maximumLevel;
    private readonly Func<object, object?>? _currentDrain;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _costResource;
    private readonly Func<object, BigDouble>? _costAmount;
    private readonly string _unavailable;

    internal WorldCraftingStationReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _structureType = resolveType("CraftingStructureSO");
        _stationType = resolveType("CraftingStructure");
        var instanceListType = resolveType("CraftingStructureListVariable");
        var listElementType = resolveType("CraftingStructureSO+TypeListElement");
        _typeElementType = resolveType("CraftingStructureSO+TypeElement");
        var tooltipableInterface = resolveType("ITooltipable");
        var tooltipableObject = resolveType("TooltipableObject");
        var costType = resolveType("ResourceCostList");
        var tupleType = resolveType("ResourceTuple");
        if (_structureType is null || _stationType is null || instanceListType is null ||
            listElementType is null ||
            _typeElementType is null || tooltipableInterface is null || tooltipableObject is null ||
            costType is null || tupleType is null)
        {
            _unavailable = "the Brewing Station types were not found on this build";
            return;
        }

        _structures = NativeAccessorBinder.StaticListAccessor(_structureType, "All");
        _structureId = NativeAccessorBinder.Call<Guid>(_structureType, "GetGuid");
        _instances = ThroughList(
            NativeAccessorBinder.Reference(_structureType, "instances", instanceListType),
            NativeAccessorBinder.CallList(instanceListType, "GetAll", _stationType));
        _ingredientLists = NativeAccessorBinder.CollectionField(_structureType, "ingredientLists");
        _elements = NativeAccessorBinder.CallList(listElementType, "GetElements", _typeElementType);
        _elementId = NativeAccessorBinder.CallReferenceGuid(
            _typeElementType, "GetTooltipable", tooltipableInterface, tooltipableObject);
        _elementAvailable = NativeAccessorBinder.Call<bool>(_typeElementType, "IsAvailable");
        _stationId = NativeAccessorBinder.Call<Guid>(_stationType, "GetGuid");
        _stationReference = NativeAccessorBinder.CallObject(_stationType, "get_reference", _structureType);
        _recipeId = NativeAccessorBinder.ReferenceGuid(_stationType, "recipeId");
        _ingredient = NativeAccessorBinder.CallObject<int>(_stationType, "GetIngredient", _typeElementType);
        _output = NativeAccessorBinder.CallObject(_stationType, "GetOutput", _typeElementType);
        _outputList = NativeAccessorBinder.CallList(_stationType, "GetOutputList", _typeElementType);
        _outputVisible = NativeAccessorBinder.CallWithObjectArgument<bool>(
            _stationType, "IsOutputVisible", _typeElementType);
        _loaded = NativeAccessorBinder.Call<bool>(_stationType, "IsLoaded");
        _active = NativeAccessorBinder.Call<bool>(_stationType, "IsActive");
        _level = NativeAccessorBinder.Call<int>(_stationType, "GetLevel");
        _minimumLevel = NativeAccessorBinder.Call<int>(_stationType, "GetMinSelectedLevel");
        _maximumLevel = NativeAccessorBinder.Call<int>(_stationType, "GetMaxSelectedLevel");
        _currentDrain = NativeAccessorBinder.CallObject(_stationType, "GetCurrentDrain", costType);
        _costEntries = NativeAccessorBinder.CallList(costType, "GetEntries", tupleType);
        _costResource = NativeAccessorBinder.ReferenceGuid(tupleType, "resource");
        _costAmount = NativeAccessorBinder.Call<BigDouble>(tupleType, "GetValue");

        _unavailable = IsBound()
            ? string.Empty
            : "the Brewing Station selector, lifecycle, or drain members were unavailable";
    }

    public string Category => "crafting stations";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        frame.CraftingStations.Reset();
        frame.CraftingStationOptions.Reset();
        frame.CraftingStationDrains.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        try
        {
            var structures = _structures!();
            if (structures is null)
                return WorldCategoryReport.Missing(Category, "CraftingStructureSO.All is unavailable");
            var sampled = 0;
            var skipped = 0;
            var firstFailure = string.Empty;
            for (var structureIndex = 0; structureIndex < structures.Count; structureIndex++)
            {
                var structure = structures[structureIndex];
                if (structure is null || structure.GetType() != _structureType)
                {
                    Skip(ref skipped, ref firstFailure,
                        "a CraftingStructureSO registry entry had an unexpected native type");
                    continue;
                }
                var structureId = _structureId!(structure);
                var instances = _instances!(structure);
                for (var instanceIndex = 0; instanceIndex < (instances?.Count ?? 0); instanceIndex++)
                {
                    var station = instances![instanceIndex];
                    if (station is null || station.GetType() != _stationType ||
                        !ReferenceEquals(_stationReference!(station), structure))
                    {
                        Skip(ref skipped, ref firstFailure,
                            "a Brewing Station instance had an unexpected native type or owner");
                        continue;
                    }
                    var stationId = _stationId!(station);
                    if (stationId == Guid.Empty || !claimed.Add(stationId))
                    {
                        Skip(ref skipped, ref firstFailure,
                            "a Brewing Station instance had an empty or duplicate identity");
                        continue;
                    }
                    var first = _ingredient!(station, 0);
                    var second = _ingredient!(station, 1);
                    var output = _output!(station);
                    frame.CraftingStations.Append(new WorldCraftingStation(
                        stationId,
                        structureId,
                        _recipeId!(station),
                        ReadElementId(first),
                        ReadElementId(second),
                        ReadElementId(output),
                        _loaded!(station),
                        _active!(station),
                        _level!(station),
                        _minimumLevel!(station),
                        _maximumLevel!(station)));
                    AppendIngredientOptions(stationId, structure, 0,
                        WorldCraftingStationOptionKind.FirstIngredient, frame.CraftingStationOptions);
                    AppendIngredientOptions(stationId, structure, 1,
                        WorldCraftingStationOptionKind.SecondIngredient, frame.CraftingStationOptions);
                    AppendOutputOptions(stationId, station, frame.CraftingStationOptions);
                    AppendDrains(stationId, station, frame.CraftingStationDrains);
                    sampled++;
                }
            }
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected,
                sampled, skipped, firstFailure);
        }
        catch (Exception exception) when (exception is TargetInvocationException or
            ArgumentException or InvalidOperationException or OverflowException)
        {
            return WorldCategoryReport.Missing(Category,
                "reading Brewing Stations threw: " + exception.GetBaseException().Message);
        }
    }

    private Guid ReadElementId(object? element) =>
        element is null || element.GetType() != _typeElementType ? Guid.Empty : _elementId!(element);

    private static Func<object, IList?>? ThroughList(
        Func<object, object?>? readReference,
        Func<object, IList?>? readList)
    {
        if (readReference is null || readList is null) return null;
        return source => readReference(source) is { } reference ? readList(reference) : null;
    }

    private void AppendIngredientOptions(
        Guid stationId,
        object structure,
        int slot,
        WorldCraftingStationOptionKind kind,
        WorldRelationBuffer<WorldCraftingStationOption> destination)
    {
        var lists = _ingredientLists!(structure);
        if (lists is null || slot >= lists.Count || lists[slot] is not { } list) return;
        var elements = _elements!(list);
        for (var index = 0; index < (elements?.Count ?? 0); index++)
        {
            var element = elements![index];
            if (element is null || element.GetType() != _typeElementType) continue;
            var id = _elementId!(element);
            if (id != Guid.Empty)
                destination.Append(new WorldCraftingStationOption(
                    stationId, kind, id, _elementAvailable!(element)));
        }
    }

    private void AppendOutputOptions(
        Guid stationId,
        object station,
        WorldRelationBuffer<WorldCraftingStationOption> destination)
    {
        var elements = _outputList!(station);
        for (var index = 0; index < (elements?.Count ?? 0); index++)
        {
            var element = elements![index];
            if (element is null || element.GetType() != _typeElementType) continue;
            var id = _elementId!(element);
            if (id != Guid.Empty)
                destination.Append(new WorldCraftingStationOption(
                    stationId, WorldCraftingStationOptionKind.Output, id,
                    _outputVisible!(station, element)));
        }
    }

    private void AppendDrains(
        Guid stationId,
        object station,
        WorldRelationBuffer<WorldCraftingStationDrain> destination)
    {
        var drain = _currentDrain!(station);
        if (drain is null) return;
        var entries = _costEntries!(drain);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index];
            if (entry is null) continue;
            var resourceId = _costResource!(entry);
            if (resourceId != Guid.Empty)
                destination.Append(new WorldCraftingStationDrain(
                    stationId, resourceId, _costAmount!(entry)));
        }
    }

    private bool IsBound() =>
        _structures is not null && _structureId is not null && _instances is not null &&
        _ingredientLists is not null && _elements is not null && _elementId is not null &&
        _elementAvailable is not null && _stationId is not null && _stationReference is not null &&
        _recipeId is not null && _ingredient is not null && _output is not null &&
        _outputList is not null && _outputVisible is not null && _loaded is not null &&
        _active is not null && _level is not null && _minimumLevel is not null &&
        _maximumLevel is not null && _currentDrain is not null && _costEntries is not null &&
        _costResource is not null && _costAmount is not null;

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}

internal static class WorldCraftingStationDeriver
{
    internal static PublicationTable<WorldCraftingStationOption> BuildOptions(
        WorldRelationBuffer<WorldCraftingStationOption> buffer) =>
        WorldScribeRelationDeriver.Build(buffer, static (left, right) =>
        {
            var station = left.StationId.CompareTo(right.StationId);
            if (station != 0) return station;
            var kind = ((int)left.Kind).CompareTo((int)right.Kind);
            return kind != 0 ? kind : left.OptionId.CompareTo(right.OptionId);
        });

    internal static PublicationTable<WorldCraftingStationDrain> BuildDrains(
        WorldRelationBuffer<WorldCraftingStationDrain> buffer) =>
        WorldScribeRelationDeriver.Build(buffer, static (left, right) =>
        {
            var station = left.StationId.CompareTo(right.StationId);
            return station != 0 ? station : left.ResourceId.CompareTo(right.ResourceId);
        });
}
