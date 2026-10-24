using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class AbstractListVariable<T> : AbstractListVariable
{
    public static List<AbstractListVariable<T>> All = new List<AbstractListVariable<T>>();
    public List<T> value = new List<T>();

    /// <summary>Whether the list is authored rather than played into, as the game marks it.</summary>
    public bool isStatic;

    /// <summary>
    /// Whether the list keeps a fixed run of spots rather than growing, as the game marks it. A
    /// filled list writes into an empty index instead of appending, and every mutation the game
    /// performs on it goes through that index.
    /// </summary>
    public bool isFilled;
    public int Maximum = 4;
    public IntVariable? maxSizeVariable;
    public int GetMax() => maxSizeVariable?.AsInt() ?? Maximum;
    public bool HasMax() => maxSizeVariable is not null;
    public bool IsImmutable() => isStatic;
    public virtual bool IsFilledType() => isFilled;
    public List<T> ToList() => new List<T>(value);
    public int Count => value.Count;
    public int GetCount() => value.Count;
    public virtual void Empty()
    {
        if (IsImmutable()) return;
        value = new List<T>();
        if (IsFilledType()) FillSpots();
        UpdateObservable();
    }

    public bool IsAtMax() => value.Count >= GetMax();

    protected virtual T GetEmptyElement() => default!;

    protected void FillSpots()
    {
        if (!HasMax() || !IsFilledType() || GetCount() >= GetMax()) return;
        while (value.Count < GetMax()) value.Add(GetEmptyElement());
        UpdateObservable();
    }

    protected void EnsureCorrectSpots()
    {
        if (!HasMax() || !IsFilledType() || GetMax() == GetCount()) return;
        FillSpots();
        value = value.Take(GetMax()).ToList();
        UpdateObservable();
    }

    public bool Contains(T element) => value.Contains(element);
    public virtual void Remove(T element) => value.Remove(element);
    public bool SuppressSwap { get; set; }
    public bool ThrowBeforeSwap { get; set; }
    public bool ThrowAfterSwap { get; set; }
    public bool SuppressSetAt { get; set; }
    public bool ThrowAfterSetAt { get; set; }
    public int SwapCalls { get; private set; }
    public int SetAtCalls { get; private set; }
    public int UpdateObservableCalls { get; private set; }

    public void SwapPositions(int first, int second)
    {
        SwapCalls++;
        if (ThrowBeforeSwap) throw new InvalidOperationException("injected failure before slot swap");
        if (!SuppressSwap)
            (value[first], value[second]) = (value[second], value[first]);
        if (ThrowAfterSwap) throw new InvalidOperationException("injected failure after slot swap");
    }

    public void SetAt(int index, T valueN)
    {
        SetAtCalls++;
        if (!SuppressSetAt) value[index] = valueN;
        if (ThrowAfterSetAt) throw new InvalidOperationException("injected failure after list set");
    }

    public void UpdateObservable() => UpdateObservableCalls++;
}
