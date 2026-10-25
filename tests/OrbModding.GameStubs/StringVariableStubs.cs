using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class StringVariable
{
    public static List<StringVariable> All = new List<StringVariable>();
    public string value = string.Empty;

    public string GetValue() => value;
    public int SetCalls { get; private set; }
    public bool SuppressSet { get; set; }
    public void SetValue(string next)
    {
        SetCalls++;
        if (!SuppressSet) value = next;
    }
}
