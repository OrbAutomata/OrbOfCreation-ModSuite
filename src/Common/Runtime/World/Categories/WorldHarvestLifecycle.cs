using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldHarvestElementControl
{
    internal WorldHarvestElementControl(Guid elementId, bool visible, int active,
        int maximumAdditional, bool listSpaceAvailable, bool usageAffordable,
        bool addAvailable, bool removeAvailable)
    {
        ElementId = elementId;
        Visible = visible;
        Active = active;
        MaximumAdditional = maximumAdditional;
        ListSpaceAvailable = listSpaceAvailable;
        UsageAffordable = usageAffordable;
        AddAvailable = addAvailable;
        RemoveAvailable = removeAvailable;
    }

    internal Guid ElementId { get; }
    internal bool Visible { get; }
    internal int Active { get; }
    internal int MaximumAdditional { get; }
    internal bool ListSpaceAvailable { get; }
    internal bool UsageAffordable { get; }
    internal bool AddAvailable { get; }
    internal bool RemoveAvailable { get; }
}

internal readonly struct WorldHarvestActionControl
{
    internal WorldHarvestActionControl(Guid elementId, Guid actionId, bool visible,
        int active, int maximum, bool addAvailable, bool removeAvailable)
    {
        ElementId = elementId;
        ActionId = actionId;
        Visible = visible;
        Active = active;
        Maximum = maximum;
        AddAvailable = addAvailable;
        RemoveAvailable = removeAvailable;
    }

    internal Guid ElementId { get; }
    internal Guid ActionId { get; }
    internal bool Visible { get; }
    internal int Active { get; }
    internal int Maximum { get; }
    internal bool AddAvailable { get; }
    internal bool RemoveAvailable { get; }
}

internal enum WorldHarvestLifecycleCostKind
{
    ElementUsage = 1,
    NextActionDrain = 2,
}

internal readonly struct WorldHarvestLifecycleCost
{
    internal WorldHarvestLifecycleCost(Guid elementId, Guid actionId,
        WorldHarvestLifecycleCostKind kind, Guid resourceId, BigDouble amount)
    {
        ElementId = elementId;
        ActionId = actionId;
        Kind = kind;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid ElementId { get; }
    internal Guid ActionId { get; }
    internal WorldHarvestLifecycleCostKind Kind { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

/// <summary>Reads the exact active harvest lists and every element/action choice they serve.</summary>
internal sealed class WorldHarvestLifecycleReader : IWorldCategoryReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Guid ActiveElementsId =
        new("5a9f8001-3ae2-4799-86b6-5198763e0fe2");
    private static readonly Guid ActiveActionsId =
        new("e4a9d4c3-61cc-4f94-bab9-7bc8e841cc32");

    private readonly RuntimeIdentityRegistryBinding _registry;
    private readonly Type? _elementType;
    private readonly Type? _actionType;
    private readonly Type? _instanceType;
    private readonly Type? _elementListType;
    private readonly Type? _actionListType;
    private readonly Func<IList?>? _elements;
    private readonly Func<object, Guid>? _elementId;
    private readonly Func<object, bool>? _elementVisible;
    private readonly Func<object, bool>? _elementAvailable;
    private readonly Func<object, BigDouble>? _elementMaximum;
    private readonly Func<object, object?>? _elementUsage;
    private readonly Func<object, IList?>? _elementActions;
    private readonly Func<object, object, int>? _elementStacks;
    private readonly Func<object, bool>? _elementListRoom;
    private readonly Func<object, IList?>? _activeActions;
    private readonly Func<object, bool>? _actionListRoom;
    private readonly Func<object, Guid>? _instanceActionId;
    private readonly Func<object, Guid>? _instanceElementId;
    private readonly Func<object, bool>? _instanceVisible;
    private readonly Func<object, int>? _instanceMaximum;
    private readonly Func<object, int>? _instanceCount;
    private readonly Func<object, object?>? _instanceBaseCost;
    private readonly Func<object, int, object?>? _instanceScaling;
    private readonly Func<object, BigDouble>? _drainCostMod;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, Guid>? _costResource;
    private readonly Func<object, BigDouble>? _costAmount;
    private readonly Func<object, bool>? _costEnough;
    private readonly string _unavailable;

    internal WorldHarvestLifecycleReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        var registryType = resolveType("IdScriptableObject");
        _registry = new RuntimeIdentityRegistryBinding(
            () => registryType, requireStableIdentityContract: false);
        _elementType = resolveType("HarvestElementSO");
        _actionType = resolveType("HarvestActionSO");
        _instanceType = resolveType("HarvestActionInstance");
        _elementListType = resolveType("HarvestElementListVariable");
        _actionListType = resolveType("HarvestActionInstanceListVariable");
        var costType = resolveType("ResourceCostList");
        var tupleType = resolveType("ResourceTuple");
        var scalingType = resolveType("ScalingInfo");

        _elements = NativeAccessorBinder.StaticListAccessor(_elementType, "All");
        _elementId = NativeAccessorBinder.Call<Guid>(_elementType, "GetGuid");
        _elementVisible = NativeAccessorBinder.Call<bool>(_elementType, "IsVisible");
        _elementAvailable = NativeAccessorBinder.Call<bool>(_elementType, "IsAvailable");
        _elementMaximum = NativeAccessorBinder.Call<BigDouble>(_elementType, "MaximumNumberInstances");
        _elementUsage = NativeAccessorBinder.Reference(_elementType, "usageCost", costType);
        _elementActions = NativeAccessorBinder.CallList(_elementType, "GetActionInstances", _instanceType);
        _elementStacks = NativeAccessorBinder.CallWithObjectArgument<int>(
            _elementListType, "GetStacks", _elementType);
        _elementListRoom = NativeAccessorBinder.Call<bool>(_elementListType, "HasEmptySpot");
        _activeActions = NativeAccessorBinder.CollectionField(_actionListType, "value");
        _actionListRoom = NativeAccessorBinder.Call<bool>(_actionListType, "HasEmptySpot");
        _instanceActionId = NativeAccessorBinder.CallReferenceGuid(_instanceType, "GetAction");
        _instanceElementId = NativeAccessorBinder.CallReferenceGuid(_instanceType, "GetElement");
        _instanceVisible = NativeAccessorBinder.Call<bool>(_instanceType, "IsVisible");
        _instanceMaximum = NativeAccessorBinder.Call<int>(_instanceType, "GetMaximumInstances");
        _instanceCount = NativeAccessorBinder.Field<int>(_instanceType, "instances");
        _instanceBaseCost = NativeAccessorBinder.CallObject(
            _instanceType, "ComputeResourceCost", costType);
        _instanceScaling = BindBoxedCall<int>(_instanceType, "GetScalingInfo", scalingType);
        _drainCostMod = NativeAccessorBinder.Call<BigDouble>(scalingType, "GetDrainCostMod");
        _costEntries = NativeAccessorBinder.CallList(costType, "GetEntries", tupleType);
        _costResource = NativeAccessorBinder.ReferenceGuid(tupleType, "resource");
        _costAmount = NativeAccessorBinder.Call<BigDouble>(tupleType, "GetValue");
        _costEnough = NativeAccessorBinder.Call<bool>(costType, "HasEnough");

        _unavailable = IsBound()
            ? string.Empty
            : "the active harvest element/action list or decision members were unavailable";
    }

    public string Category => "harvest lifecycle";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        frame.HarvestElementControls.Reset();
        frame.HarvestActionControls.Reset();
        frame.HarvestLifecycleCosts.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var source = _registry.Read();
        if (!source.IsReady || source.Registry is null)
            return WorldCategoryReport.Missing(Category, source.Reason);
        var elementList = source.Registry[ActiveElementsId];
        var actionList = source.Registry[ActiveActionsId];
        if (elementList is null || actionList is null)
            return WorldCategoryReport.Missing(Category, "the active harvest lists are not ready");
        if (elementList.GetType() != _elementListType || actionList.GetType() != _actionListType)
            return WorldCategoryReport.Missing(Category,
                "an active harvest list had an unexpected native type");

        try
        {
            var elements = _elements!();
            var activeActions = _activeActions!(actionList);
            var active = BuildActive(activeActions);
            var sampled = 0;
            var skipped = 0;
            var firstFailure = string.Empty;
            for (var index = 0; index < (elements?.Count ?? 0); index++)
            {
                var element = elements![index];
                if (element is null || element.GetType() != _elementType)
                {
                    Skip(ref skipped, ref firstFailure,
                        "a harvest element registry entry had an unexpected native type");
                    continue;
                }
                var elementId = _elementId!(element);
                if (elementId == Guid.Empty)
                {
                    Skip(ref skipped, ref firstFailure,
                        "a harvest element had no stable identity");
                    continue;
                }
                var visible = _elementVisible!(element) && _elementAvailable!(element);
                var current = _elementStacks!(elementList, element);
                var maximumAdditional = Math.Max(_elementMaximum!(element).ToInt(), 0);
                var usage = _elementUsage!(element);
                var usageEnough = usage is not null && _costEnough!(usage);
                var listSpace = current > 0 || _elementListRoom!(elementList);
                var addAvailable = visible && maximumAdditional > 0 && usageEnough &&
                    listSpace;
                frame.HarvestElementControls.Append(new WorldHarvestElementControl(
                    elementId, visible, current, maximumAdditional,
                    listSpace, usageEnough,
                    addAvailable, current > 0));
                if (visible && usage is not null)
                    AppendCosts(elementId, Guid.Empty,
                        WorldHarvestLifecycleCostKind.ElementUsage, usage,
                        frame.HarvestLifecycleCosts);

                var prototypes = _elementActions!(element);
                for (var actionIndex = 0; actionIndex < (prototypes?.Count ?? 0); actionIndex++)
                {
                    var prototype = prototypes![actionIndex];
                    if (prototype is null || prototype.GetType() != _instanceType ||
                        _instanceElementId!(prototype) != elementId)
                    {
                        Skip(ref skipped, ref firstFailure,
                            "a harvest action prototype had an unexpected type or owner");
                        continue;
                    }
                    var actionId = _instanceActionId!(prototype);
                    if (actionId == Guid.Empty)
                    {
                        Skip(ref skipped, ref firstFailure,
                            "a harvest action prototype had no action identity");
                        continue;
                    }
                    active.TryGetValue((elementId, actionId), out var currentInstance);
                    var count = currentInstance is null ? 0 : _instanceCount!(currentInstance);
                    var maximum = _instanceMaximum!(prototype);
                    var actionVisible = visible && _instanceVisible!(prototype);
                    var actionAdd = actionVisible && count < maximum &&
                        (currentInstance is not null || _actionListRoom!(actionList));
                    frame.HarvestActionControls.Append(new WorldHarvestActionControl(
                        elementId, actionId, actionVisible, count, maximum,
                        actionAdd, count > 0));
                    if (actionAdd)
                        AppendNextActionCosts(elementId, actionId, prototype, count + 1,
                            frame.HarvestLifecycleCosts);
                }
                sampled++;
            }
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected,
                sampled, skipped, firstFailure);
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or OverflowException)
        {
            return WorldCategoryReport.Missing(Category,
                "reading harvest lifecycle state threw: " + exception.GetBaseException().Message);
        }
    }

    private Dictionary<(Guid Element, Guid Action), object> BuildActive(IList? values)
    {
        var active = new Dictionary<(Guid, Guid), object>();
        for (var index = 0; index < (values?.Count ?? 0); index++)
        {
            var instance = values![index];
            if (instance is null || instance.GetType() != _instanceType) continue;
            var key = (_instanceElementId!(instance), _instanceActionId!(instance));
            if (key.Item1 != Guid.Empty && key.Item2 != Guid.Empty && !active.ContainsKey(key))
                active.Add(key, instance);
        }
        return active;
    }

    private void AppendNextActionCosts(Guid elementId, Guid actionId, object prototype,
        int count, WorldRelationBuffer<WorldHarvestLifecycleCost> destination)
    {
        var baseCost = _instanceBaseCost!(prototype);
        var scaling = _instanceScaling!(prototype, Math.Max(count, 1));
        if (baseCost is null || scaling is null) return;
        // The drain modifier is applied here rather than by ResourceCostList.Multiply, which builds
        // a whole second list to hold one scalar product per entry. GameCostMath.Multiply is that
        // product, and the cost parity pass is what proves the two agree.
        AppendCosts(
            elementId,
            actionId,
            WorldHarvestLifecycleCostKind.NextActionDrain,
            baseCost,
            OrbGameMath.AsPercent(_drainCostMod!(scaling)),
            destination);
    }

    private void AppendCosts(Guid elementId, Guid actionId,
        WorldHarvestLifecycleCostKind kind, object cost,
        WorldRelationBuffer<WorldHarvestLifecycleCost> destination) =>
        AppendCosts(elementId, actionId, kind, cost, BigDouble.One, destination);

    private void AppendCosts(Guid elementId, Guid actionId,
        WorldHarvestLifecycleCostKind kind, object cost, BigDouble factor,
        WorldRelationBuffer<WorldHarvestLifecycleCost> destination)
    {
        var entries = _costEntries!(cost);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index];
            if (entry is null) continue;
            var resourceId = _costResource!(entry);
            if (resourceId != Guid.Empty)
                destination.Append(new WorldHarvestLifecycleCost(
                    elementId, actionId, kind, resourceId,
                    _costAmount!(entry) * factor));
        }
    }

    private bool IsBound() =>
        _elementType is not null && _actionType is not null && _instanceType is not null &&
        _elementListType is not null && _actionListType is not null && _elements is not null &&
        _elementId is not null && _elementVisible is not null && _elementAvailable is not null &&
        _elementMaximum is not null && _elementUsage is not null && _elementActions is not null &&
        _elementStacks is not null && _elementListRoom is not null && _activeActions is not null &&
        _actionListRoom is not null && _instanceActionId is not null &&
        _instanceElementId is not null && _instanceVisible is not null &&
        _instanceMaximum is not null && _instanceCount is not null &&
        _instanceBaseCost is not null && _instanceScaling is not null &&
        _drainCostMod is not null && _costEntries is not null &&
        _costResource is not null && _costAmount is not null && _costEnough is not null;

    private static Func<object, TArgument, object?>? BindBoxedCall<TArgument>(
        Type? owner, string name, Type? exactResult)
    {
        if (owner is null || exactResult is null) return null;
        var method = owner.GetMethod(name, Instance, null, new[] { typeof(TArgument) }, null);
        if (method is null || method.ReturnType != exactResult) return null;
        try
        {
            var source = Expression.Parameter(typeof(object), "source");
            var argument = Expression.Parameter(typeof(TArgument), "argument");
            return Expression.Lambda<Func<object, TArgument, object?>>(
                Expression.Convert(
                    Expression.Call(Expression.Convert(source, owner), method, argument),
                    typeof(object)), source, argument).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}

internal static class WorldHarvestLifecycleDeriver
{
    internal static PublicationTable<WorldHarvestElementControl> BuildElements(
        WorldRelationBuffer<WorldHarvestElementControl> buffer) =>
        WorldScribeRelationDeriver.Build(buffer,
            static (left, right) => left.ElementId.CompareTo(right.ElementId));

    internal static PublicationTable<WorldHarvestActionControl> BuildActions(
        WorldRelationBuffer<WorldHarvestActionControl> buffer) =>
        WorldScribeRelationDeriver.Build(buffer, static (left, right) =>
        {
            var element = left.ElementId.CompareTo(right.ElementId);
            return element != 0 ? element : left.ActionId.CompareTo(right.ActionId);
        });

    internal static PublicationTable<WorldHarvestLifecycleCost> BuildCosts(
        WorldRelationBuffer<WorldHarvestLifecycleCost> buffer) =>
        WorldScribeRelationDeriver.Build(buffer, static (left, right) =>
        {
            var element = left.ElementId.CompareTo(right.ElementId);
            if (element != 0) return element;
            var action = left.ActionId.CompareTo(right.ActionId);
            if (action != 0) return action;
            var kind = ((int)left.Kind).CompareTo((int)right.Kind);
            return kind != 0 ? kind : left.ResourceId.CompareTo(right.ResourceId);
        });
}
