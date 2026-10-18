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

internal static class WorldCraftingStationLookup
{
    internal static bool TryFind(
        PublicationTable<WorldCraftingStation> table,
        Guid stationId,
        out WorldCraftingStation station) =>
        WorldLookup.TryFind(table, stationId, out station);
}

/// <summary>Reads the exact selectors and controls owned by <c>UIBrewingStation</c>.</summary>
internal sealed class WorldCraftingStationReader : IWorldCategoryReader
{
    private readonly Type? _structureType;
    private readonly Type? _stationType;
    private readonly Type? _typeElementType;
    private readonly Func<IList?>? _structures;
    private readonly Func<object, Guid>? _structureId;
    private readonly Func<object, IList?>? _instances;
    private readonly Func<object, Guid>? _elementId;
    private readonly Func<object, Guid>? _stationId;
    private readonly Func<object, object?>? _stationReference;
    private readonly Func<object, Guid>? _recipeId;
    private readonly Func<object, int, object?>? _ingredient;
    private readonly Func<object, object?>? _output;
    private readonly Func<object, bool>? _loaded;
    private readonly Func<object, bool>? _active;
    private readonly Func<object, int>? _level;
    private readonly Func<object, int>? _minimumLevel;
    private readonly Func<object, int>? _maximumLevel;
    private readonly string _unavailable;

    internal WorldCraftingStationReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _structureType = resolveType("CraftingStructureSO");
        _stationType = resolveType("CraftingStructure");
        var instanceListType = resolveType("CraftingStructureListVariable");
        _typeElementType = resolveType("CraftingStructureSO+TypeElement");
        var tooltipableInterface = resolveType("ITooltipable");
        var tooltipableObject = resolveType("TooltipableObject");
        if (_structureType is null || _stationType is null || instanceListType is null ||
            _typeElementType is null || tooltipableInterface is null || tooltipableObject is null)
        {
            _unavailable = "the Brewing Station types were not found on this build";
            return;
        }

        _structures = NativeAccessorBinder.StaticListAccessor(_structureType, "All");
        _structureId = NativeAccessorBinder.Call<Guid>(_structureType, "GetGuid");
        _instances = ThroughList(
            NativeAccessorBinder.Reference(_structureType, "instances", instanceListType),
            NativeAccessorBinder.CallList(instanceListType, "GetAll", _stationType));
        _elementId = NativeAccessorBinder.CallReferenceGuid(
            _typeElementType, "GetTooltipable", tooltipableInterface, tooltipableObject);
        _stationId = NativeAccessorBinder.Call<Guid>(_stationType, "GetGuid");
        _stationReference = NativeAccessorBinder.CallObject(_stationType, "get_reference", _structureType);
        _recipeId = NativeAccessorBinder.ReferenceGuid(_stationType, "recipeId");
        _ingredient = NativeAccessorBinder.CallObject<int>(_stationType, "GetIngredient", _typeElementType);
        _output = NativeAccessorBinder.CallObject(_stationType, "GetOutput", _typeElementType);
        _loaded = NativeAccessorBinder.Call<bool>(_stationType, "IsLoaded");
        _active = NativeAccessorBinder.Call<bool>(_stationType, "IsActive");
        _level = NativeAccessorBinder.Call<int>(_stationType, "GetLevel");
        _minimumLevel = NativeAccessorBinder.Call<int>(_stationType, "GetMinSelectedLevel");
        _maximumLevel = NativeAccessorBinder.Call<int>(_stationType, "GetMaxSelectedLevel");

        _unavailable = IsBound()
            ? string.Empty
            : "the Brewing Station selector or lifecycle members were unavailable";
    }

    public string Category => "crafting stations";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        ResetRows(frame);
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
                    sampled++;
                }
            }
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected,
                sampled, skipped, firstFailure);
        }
        catch (Exception exception) when (exception is TargetInvocationException or
            ArgumentException or InvalidOperationException or OverflowException)
        {
            ResetRows(frame);
            return WorldCategoryReport.Missing(Category,
                "reading Brewing Stations threw: " + exception.GetBaseException().Message);
        }
    }

    private static void ResetRows(GameWorldCycleFrame frame)
    {
        frame.CraftingStations.Reset();
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

    private bool IsBound() =>
        _structures is not null && _structureId is not null && _instances is not null &&
        _elementId is not null && _stationId is not null && _stationReference is not null &&
        _recipeId is not null && _ingredient is not null && _output is not null &&
        _loaded is not null && _active is not null && _level is not null &&
        _minimumLevel is not null && _maximumLevel is not null;

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}
