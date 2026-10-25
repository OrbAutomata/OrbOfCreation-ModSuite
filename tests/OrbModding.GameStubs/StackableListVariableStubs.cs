using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class StackableListVariable<T> : GenericListVariable<T>
{
    public bool isStackable = true;
    public Stacked.StackedIdRecord<T> itemStack = new Stacked.StackedIdRecord<T>();

    public int GetStacks(T item) => itemStack.GetQuantity(item);

    public int GetTotalStacks() => itemStack.GetTotalStacks();

    /// <summary>
    /// The multiplicities the game bakes a spell from. It is null on a list the author did not mark
    /// stackable, and plain <c>Add</c> never writes it — the two facts that make an element landing
    /// in <see cref="AbstractListVariable{T}.value"/> no evidence at all that the stack carries it.
    /// </summary>
    public virtual Stacked.StackedIdRecord<T>? GetStackedRecord() => isStackable ? itemStack : null;

    /// <summary>
    /// Whether the stack write returns without writing anything, the way an authored-immutable or
    /// otherwise refusing list does — the branch that makes a landed-looking staging call no
    /// evidence at all that the stack carries what was asked for.
    /// </summary>
    public bool SuppressSetStack { get; set; }

    public int? SuppressSetStackFromCall { get; set; }

    /// <summary>The write, from this call on, throws before it imports anything.</summary>
    public int? ThrowOnSetStackFromCall { get; set; }

    private int _setStackCalls;

    /// <summary>Writes the stack and then the value list from it, the way the game does.</summary>
    public virtual void SetStack(Stacked.StackedIdRecord<T> record)
    {
        _setStackCalls++;
        if (_setStackCalls >= ThrowOnSetStackFromCall)
            throw new InvalidOperationException("injected failure while writing the stack");
        if (SuppressSetStack || _setStackCalls >= SuppressSetStackFromCall) return;
        itemStack.ImportItems(record.ToList());
        SetValue(itemStack.GetItems());
    }

    public override void Empty()
    {
        base.Empty();
        itemStack.Empty();
    }

    public void Stack(T item, int quantity)
    {
        if (!value.Contains(item))
        {
            if (!HasEmptySpot()) return;
            Add(item);
        }
        itemStack.Set(item, itemStack.GetQuantity(item) + quantity);
    }

    public void Unstack(T item, int quantity)
    {
        if (!value.Contains(item)) return;
        itemStack.Set(item, Math.Max(itemStack.GetQuantity(item) - quantity, 0));
        if (itemStack.GetQuantity(item) == 0) value.Remove(item);
    }
}
