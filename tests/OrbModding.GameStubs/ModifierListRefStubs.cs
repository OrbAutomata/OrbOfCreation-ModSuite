using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// A reference to a modifier list. The game's Standard subclass resolves nine shared lists off
/// GlobalValues instead of the named variable, which is why the suite reads the resolved list's
/// contents through the virtual accessor rather than carrying an identity.
/// </summary>
public class ModifierListRef
{
    private static readonly ValueModifierList EmptyList = new ValueModifierList();

    public ModifierListVariable? variable;

    public virtual ValueModifierList GetValue() => variable is null ? EmptyList : variable.GetValue();
}
