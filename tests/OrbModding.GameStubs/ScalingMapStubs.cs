using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class ScalingMap<T>
{
    protected Dictionary<ScalingType, T> values = new Dictionary<ScalingType, T>();

    public sealed class Entry
    {
        public ScalingType t;
        public T v = default!;
    }
    public List<Entry> valueList = new List<Entry>();
    public void Set(ScalingType kind, T value)
    {
        values[kind] = value;
        var entry = valueList.Find(row => row.t == kind);
        if (entry is null) valueList.Add(new Entry { t = kind, v = value });
        else entry.v = value;
    }
}
