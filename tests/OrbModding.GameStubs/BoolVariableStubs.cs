using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The game's global flags. Not a NumberVariable — it holds a plain field with nothing to calculate.
/// </summary>
public sealed class BoolVariable
{
    public static List<BoolVariable> All = new List<BoolVariable>();
    public Guid uuid = Guid.NewGuid();
    public bool value;

    public Guid GetGuid() => uuid;
    public bool GetValue() => value;
    public int SetCalls { get; private set; }
    public bool SuppressSet { get; set; }
    public bool ThrowAfterSet { get; set; }
    public void SetValue(bool next)
    {
        SetCalls++;
        if (!SuppressSet) value = next;
        if (ThrowAfterSet) throw new InvalidOperationException("injected failure after bool write");
    }
    public bool initialValue;
    public bool isSaved;
    private int observerId;
}
