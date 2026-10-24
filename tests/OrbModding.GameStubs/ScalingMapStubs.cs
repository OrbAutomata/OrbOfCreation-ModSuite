using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class ScalingMap<T>
{
    protected Dictionary<ScalingType, T> values = new Dictionary<ScalingType, T>();

    public void Set(ScalingType kind, T value) => values[kind] = value;
}
