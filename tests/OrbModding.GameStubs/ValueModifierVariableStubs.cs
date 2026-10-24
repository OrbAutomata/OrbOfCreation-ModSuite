using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The global modifier registry. Entities hold a reference to one of these rather than owning a
/// modifier, which is why the suite collects the registry and carries identities.
/// </summary>
public sealed class ValueModifierVariable
{
    public static List<ValueModifierVariable> All = new List<ValueModifierVariable>();

    public Guid uuid = Guid.NewGuid();
    public ValueModifier value;

    public Guid GetGuid() => uuid;

    public ValueModifier GetValue() => value;
    public Guid Identity { get => GetGuid(); set => uuid = value; }
}
