using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The global modifier-list registry, the counterpart to <see cref="ValueModifierVariable"/> for
/// whole lists.
/// </summary>
public sealed class ModifierListVariable
{
    public static List<ModifierListVariable> All = new List<ModifierListVariable>();

    public Guid uuid = Guid.NewGuid();
    public ValueModifierList value = new ValueModifierList();

    public Guid GetGuid() => uuid;

    public ValueModifierList GetValue() => value;
    public Guid Identity { get => GetGuid(); set => uuid = value; }
}
