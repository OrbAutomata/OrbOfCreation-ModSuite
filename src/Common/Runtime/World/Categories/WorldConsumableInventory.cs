using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal enum WorldConsumableListKind
{
    Inventory = 0,
    Hotbar = 1,
}

/// <summary>One exact position in either player-facing consumable reference list.</summary>
internal readonly struct WorldConsumableSlot
{
    internal WorldConsumableSlot(
        WorldConsumableListKind list,
        int position,
        Guid consumableId)
    {
        List = list;
        Position = position;
        ConsumableId = consumableId;
    }

    internal WorldConsumableListKind List { get; }
    internal int Position { get; }
    internal Guid ConsumableId { get; }
    internal bool Occupied => ConsumableId != Guid.Empty;
}

/// <summary>The two ordered lists and the game's frame-local use admission.</summary>
internal readonly struct WorldConsumableInventory
{
    internal WorldConsumableInventory(
        bool canUse,
        int inventoryMaximum,
        int hotbarMaximum,
        PublicationTable<WorldConsumableSlot> slots)
    {
        CanUse = canUse;
        InventoryMaximum = inventoryMaximum;
        HotbarMaximum = hotbarMaximum;
        Slots = slots;
    }

    internal bool CanUse { get; }
    internal int InventoryMaximum { get; }
    internal int HotbarMaximum { get; }
    internal PublicationTable<WorldConsumableSlot> Slots { get; }

    internal static WorldConsumableInventory Empty => new(
        false,
        0,
        0,
        PublicationTable<WorldConsumableSlot>.Empty);
}

internal sealed class WorldConsumableInventoryBuffer
{
    private readonly List<WorldConsumableSlot> _slots = new();

    internal bool CanUse { get; private set; }
    internal int InventoryMaximum { get; private set; }
    internal int HotbarMaximum { get; private set; }

    internal void Reset()
    {
        CanUse = false;
        InventoryMaximum = 0;
        HotbarMaximum = 0;
        _slots.Clear();
    }

    internal void Set(bool canUse, int inventoryMaximum, int hotbarMaximum)
    {
        CanUse = canUse;
        InventoryMaximum = inventoryMaximum;
        HotbarMaximum = hotbarMaximum;
    }

    internal void Append(in WorldConsumableSlot slot) => _slots.Add(slot);

    internal WorldConsumableInventory Build()
    {
        var values = _slots.ToArray();
        Array.Sort(values, static (left, right) =>
        {
            var byList = left.List.CompareTo(right.List);
            return byList != 0 ? byList : left.Position.CompareTo(right.Position);
        });
        return new WorldConsumableInventory(
            CanUse,
            InventoryMaximum,
            HotbarMaximum,
            values.Length == 0
                ? PublicationTable<WorldConsumableSlot>.Empty
                : PublicationTable<WorldConsumableSlot>.Create(values));
    }
}

/// <summary>
/// Captures Inventory's two native list variables once per shared world pass. No list or Unity
/// reference survives the capture.
/// </summary>
internal sealed class WorldConsumableInventoryReader : IWorldCategoryReader
{
    private const BindingFlags AnyStatic =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _inventoryType;
    private readonly Type? _listType;
    private readonly Type? _consumableType;
    private readonly Func<object?>? _instance;
    private readonly Func<object, object?>? _inventoryList;
    private readonly Func<object, object?>? _hotbarList;
    private readonly Func<object, IList?>? _values;
    private readonly Func<object, int>? _maximum;
    private readonly Func<object, Guid>? _identity;
    private readonly Func<bool>? _canUse;
    private readonly string _unavailable;

    internal WorldConsumableInventoryReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _inventoryType = resolveType("Inventory");
        _listType = resolveType("ConsumableRefListVariable");
        _consumableType = resolveType("ConsumableSO");
        if (_inventoryType is null || _listType is null || _consumableType is null)
        {
            _unavailable =
                "Inventory, ConsumableRefListVariable, and ConsumableSO are all required";
            return;
        }

        _instance = BindStaticReference(_inventoryType, "_instance", _inventoryType);
        _inventoryList = NativeAccessorBinder.Reference(
            _inventoryType,
            "allConsumables",
            _listType);
        _hotbarList = NativeAccessorBinder.Reference(_inventoryType, "hotBar", _listType);
        _values = NativeAccessorBinder.CollectionField(_listType, "value");
        _maximum = NativeAccessorBinder.Call<int>(_listType, "GetMax");
        _identity = NativeAccessorBinder.Call<Guid>(_consumableType, "GetGuid");
        _canUse = BindStaticCall<bool>(_inventoryType, "CanUseConsumable");
        _unavailable = _instance is null || _inventoryList is null || _hotbarList is null ||
            _values is null || _maximum is null || _identity is null || _canUse is null
                ? "the complete consumable inventory read binding set is unavailable"
                : string.Empty;
    }

    public string Category => "consumable inventory";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        frame.ConsumableInventory.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        try
        {
            var inventory = _instance!();
            if (inventory is null || inventory.GetType() != _inventoryType)
                return WorldCategoryReport.Missing(
                    Category,
                    "Inventory._instance was not initialized for this playing generation");
            var inventoryList = _inventoryList!(inventory);
            var hotbarList = _hotbarList!(inventory);
            if (inventoryList is null || inventoryList.GetType() != _listType ||
                hotbarList is null || hotbarList.GetType() != _listType)
            {
                return WorldCategoryReport.Missing(
                    Category,
                    "Inventory did not expose both exact ConsumableRefListVariable instances");
            }

            var inventoryValues = _values!(inventoryList);
            var hotbarValues = _values!(hotbarList);
            if (inventoryValues is null || hotbarValues is null)
                return WorldCategoryReport.Missing(
                    Category,
                    "one consumable reference list had no readable value list");

            frame.ConsumableInventory.Set(
                _canUse!(),
                _maximum!(inventoryList),
                _maximum!(hotbarList));
            var sampled = Append(
                WorldConsumableListKind.Inventory,
                inventoryValues,
                frame.ConsumableInventory);
            sampled += Append(
                WorldConsumableListKind.Hotbar,
                hotbarValues,
                frame.ConsumableInventory);
            return new WorldCategoryReport(
                Category,
                WorldCategoryOutcome.Collected,
                sampled,
                0,
                string.Empty);
        }
        catch (Exception ex)
        {
            frame.ConsumableInventory.Reset();
            return WorldCategoryReport.Missing(
                Category,
                "reading the consumable inventory threw: " + ex.GetBaseException().Message);
        }
    }

    private int Append(
        WorldConsumableListKind kind,
        IList values,
        WorldConsumableInventoryBuffer destination)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is null)
            {
                destination.Append(new WorldConsumableSlot(kind, index, Guid.Empty));
                continue;
            }
            if (value.GetType() != _consumableType)
                throw new InvalidOperationException(
                    kind + " slot " + index + " held a non-ConsumableSO value");
            var id = _identity!(value);
            if (id == Guid.Empty)
                throw new InvalidOperationException(
                    kind + " slot " + index + " held an unidentified ConsumableSO");
            destination.Append(new WorldConsumableSlot(kind, index, id));
        }
        return values.Count;
    }

    private static Func<object?>? BindStaticReference(
        Type owner,
        string name,
        Type exactType)
    {
        var field = owner.GetField(name, AnyStatic);
        if (field is null || !field.IsStatic || field.FieldType != exactType) return null;
        try
        {
            return Expression.Lambda<Func<object?>>(
                Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Func<T>? BindStaticCall<T>(Type owner, string name)
    {
        var method = owner.GetMethod(name, AnyStatic, null, Type.EmptyTypes, null);
        if (method is null || !method.IsStatic || method.ReturnType != typeof(T)) return null;
        try
        {
            return Expression.Lambda<Func<T>>(Expression.Call(method)).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
