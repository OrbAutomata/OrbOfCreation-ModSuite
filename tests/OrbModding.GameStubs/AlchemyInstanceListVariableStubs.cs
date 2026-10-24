using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class AlchemyInstanceListVariable : AbstractListVariable<AlchemyInstance>
{
    public bool SuppressAddMutation { get; set; }
    public bool SuppressRemoveMutation { get; set; }
    public bool ThrowOnCanAdd { get; set; }
    public Exception CanAddFailure { get; set; } = new InvalidOperationException("CanAddInstance failed");

    /// <summary>The game throws asking about this one recipe, after earlier ones answered.</summary>
    public AlchemyRecipeSO? ThrowOnCanAddFor { get; set; }

    /// <summary>The write lands, then the game throws and its instances stop reading.</summary>
    public bool BreakAfterMutation { get; set; }

    /// <summary>An add of this one recipe returns without writing anything.</summary>
    public AlchemyRecipeSO? SuppressAddMutationOf { get; set; }

    /// <summary>The game throws on an add before writing it.</summary>
    public Exception? AddFailure { get; set; }

    /// <summary>When set, <see cref="AddFailure"/> is thrown only on an add of this one recipe.</summary>
    public AlchemyRecipeSO? AddFailureFor { get; set; }

    /// <summary>The add lands, then the game throws while its instances keep reading.</summary>
    public Exception? AddFailureAfterWrite { get; set; }
    private int numTypelessSlots = 16;
    private int numEmptyTypelessSlots = 16;
    private readonly Dictionary<AlchemyTypeSO, int> typeCounts = new();

    public int TypelessSlots
    {
        get => numTypelessSlots;
        set { numTypelessSlots = value; numEmptyTypelessSlots = value; }
    }
    public Dictionary<AlchemyTypeSO, int> TypeSlots { get; } =
        new Dictionary<AlchemyTypeSO, int>();

    public bool CanAddInstance(AlchemyRecipeSO recipe)
    {
        if (ThrowOnCanAdd || ReferenceEquals(recipe, ThrowOnCanAddFor))
            throw CanAddFailure;
        // Native HasInstance -> FindInstance calls the reference getter on each stored element.
        // A capacity placeholder is AlchemyInstance(null), not a literal null element.
        var instance = value.Find(item => ReferenceEquals(item.get_reference(), recipe));
        if (instance is not null) return true;
        return recipe.alchemyTypes.Any(type =>
            GetNumEmptyTypelessSlots() + Math.Max(GetSlotsOnlyForType(type) - GetNumOfType(type), 0) > 0);
    }

    // Native getters read RebuildCounts' cache; a read never repairs or scans malformed storage.
    public int GetNumOfType(AlchemyTypeSO type) =>
        typeCounts.TryGetValue(type, out var count) ? count : 0;

    public int GetSlotsOnlyForType(AlchemyTypeSO type) =>
        TypeSlots.TryGetValue(type, out var slots) ? slots : 0;

    public int GetNumEmptyTypelessSlots() => numEmptyTypelessSlots;

    public void AddAlchemyInstances(AlchemyRecipeSO recipe, int delta)
    {
        if (SuppressAddMutation || ReferenceEquals(recipe, SuppressAddMutationOf)) return;
        if (AddFailure is { } failure && (AddFailureFor is null || ReferenceEquals(recipe, AddFailureFor)))
            throw failure;
        var instance = value.SingleOrDefault(item => ReferenceEquals(item.get_reference(), recipe));
        if (instance is null)
        {
            instance = new AlchemyInstance(recipe);
            value.Add(instance);
        }
        instance.queuedQuantity += delta;
        RebuildCounts();
        if (BreakAfterMutation) Break();
        if (AddFailureAfterWrite is { } after) throw after;
    }

    private void Break()
    {
        foreach (var instance in value) instance.ThrowOnRead = true;
        throw new InvalidOperationException("injected failure after alchemy write");
    }

    public void RemoveAlchemyInstances(AlchemyRecipeSO recipe, int delta)
    {
        if (SuppressRemoveMutation) return;
        var instance = value.Single(item => ReferenceEquals(item.get_reference(), recipe));
        instance.queuedQuantity = Math.Max(instance.queuedQuantity - delta, 0);
        if (instance.queuedQuantity == 0) value.Remove(instance);
        RebuildCounts();
    }

    public void EngageAlchemy(AlchemyRecipeSO recipe)
    {
        if (!CanAddInstance(recipe)) return;
        var instance = value.SingleOrDefault(item => ReferenceEquals(item.get_reference(), recipe));
        var free = instance?.GetRemainingFreeUsageSlots() ?? recipe.GetFreeUsageSlots();
        var remaining = instance?.GetRemainingMaxUsageSlots() ?? recipe.GetMaxUsageSlots();
        var cost = recipe.GetUsageCost();
        var maximum = cost.IsEmpty()
            ? int.MaxValue
            : (cost.MaximumCostTimes() + new BigDouble(Math.Max(free, 0))).ToInt();
        var amount = Math.Min(GlobalVariables.GetMultiBuy().AsInt(), Math.Min(remaining, maximum));
        if (amount > 0) AddAlchemyInstances(recipe, amount);
    }

    public void DisengageAlchemy(AlchemyRecipeSO recipe) =>
        RemoveAlchemyInstances(recipe, GlobalVariables.GetMultiBuy().AsInt());

    public void RebuildCounts()
    {
        typeCounts.Clear();
        var occupied = 0;
        foreach (var instance in value)
        {
            if (instance.IsEmpty()) continue;
            occupied++;
            foreach (var type in instance.get_reference().alchemyTypes)
            {
                typeCounts.TryGetValue(type, out var count);
                typeCounts[type] = count + 1;
            }
        }
        // Fixtures author a total capacity instead of padding value with native empty placeholders.
        var empty = Math.Max(0, numTypelessSlots + TypeSlots.Values.Sum() - occupied);
        var reservedTyped = TypeSlots.Sum(entry => Math.Max(entry.Value - GetNumOfType(entry.Key), 0));
        numEmptyTypelessSlots = empty - reservedTyped;
    }

    public void SetupMaxSlotsValue()
    {
    }

    public Stacked.StackedIdRecord<AlchemyRecipeSO> CreateStackedRecord()
    {
        var result = new Stacked.StackedIdRecord<AlchemyRecipeSO>();
        foreach (var instance in value)
            if (instance.reference is not null && instance.quantity > 0)
                result.Set(instance.reference, instance.quantity);
        return result;
    }

    public void FromStackedRecord(Stacked.StackedIdRecord<AlchemyRecipeSO> record)
    {
        value.Clear();
        foreach (var entry in record.GetEntries())
        {
            value.Add(new AlchemyInstance(entry.Item1)
            {
                quantity = entry.Item2,
                queuedQuantity = entry.Item2,
            });
        }
        RebuildCounts();
    }
}
