using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class GenericListVariable<T> : AbstractListVariable<T>
{
    public bool SuppressAdd;
    public bool ThrowAfterAdd;
    public Exception? AfterAddFault;
    public int AddCalls;

    public int GetUsedSpots()
    {
        var used = 0;
        foreach (var element in value)
        {
            if (IsFilledElement(element)) used++;
        }

        return used;
    }

    /// <summary>
    /// Whether one more element fits. A filled-type list answers the game's own question — is there
    /// a spot holding nothing — which is the branch that makes <c>Add</c> a permanent no-op on a
    /// list that was never padded out to its maximum.
    /// </summary>
    /// <remarks>
    /// The non-filled arm counts used spots rather than the game's <c>!IsAtMax()</c> because the
    /// concrete stub lists that keep an emptied placeholder — the spell loadout above all — model
    /// fixed slots without declaring themselves filled, and re-modelling them is a change to every
    /// loadout test rather than to the staging defect this shape exists for.
    /// </remarks>
    public bool HasEmptySpot() =>
        IsFilledType() ? FindEmptyIndex() >= 0 : GetUsedSpots() < GetMax();

    public List<T> GetFilledElements() => value.FindAll(IsFilledElement);

    public int FindEmptyIndex() => value.FindIndex(element => !IsFilledElement(element));

    /// <summary>
    /// The game's own <c>Add</c>, branch for branch: three of its paths return without writing and
    /// without saying so, which is why a caller that does not read the list back cannot know
    /// whether its element landed.
    /// </summary>
    public virtual void Add(T element)
    {
        AddCalls++;
        if (SuppressAdd)
        {
            if (ThrowAfterAdd) throw new InvalidOperationException("injected failure after admission");
            return;
        }
        if (IsImmutable()) return;
        if (!HasEmptySpot()) return;
        if (IsFilledType())
        {
            EnsureCorrectSpots();
            var index = FindEmptyIndex();
            if (index < 0) return;
            value[index] = element;
        }
        else value.Add(element);
        UpdateObservable();
        if (ThrowAfterAdd) throw new InvalidOperationException("injected failure after admission");
        if (AfterAddFault is { } fault) throw fault;
    }

    /// <summary>
    /// The setter the game itself uses to stage a recipe's core, gated only on immutability and on
    /// being handed the list it already holds.
    /// </summary>
    public virtual void SetValue(List<T> next)
    {
        if (IsImmutable() || ReferenceEquals(value, next)) return;
        if (HasMax()) next = next.Take(GetMax()).ToList();
        if (IsFilledType())
        {
            EnsureCorrectSpots();
            value = new List<T>(next);
            FillSpots();
        }
        else value = new List<T>(next);
        UpdateObservable();
    }

    public bool SuppressToggle { get; set; }

    /// <summary>
    /// How many presses land before the list starts ignoring them, as the game's own <c>Add</c>
    /// silently does once the list turns immutable or full.
    /// </summary>
    public int TogglesThatLand { get; set; } = int.MaxValue;
    public bool ThrowAfterToggle { get; set; }
    public int ToggleCalls { get; private set; }
    public void Toggle(T element)
    {
        ToggleCalls++;
        if (!SuppressToggle && ToggleCalls <= TogglesThatLand)
        {
            if (value.Contains(element)) value.Remove(element);
            else value.Add(element);
        }
        if (ThrowAfterToggle) throw new InvalidOperationException("injected failure after list toggle");
    }

    protected virtual bool IsFilledElement(T element) => element is not null;

    public class AdditionTuple<TList>
        where TList : GenericListVariable<T>
    {
        public TList list = null!;
        public T element = default!;
        public void Add() => list.Add(element);
        public void Remove() => list.value.Remove(element);
    }
}
