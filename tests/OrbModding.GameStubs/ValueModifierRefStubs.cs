using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// A reference to a global modifier. A structure holds one of these rather than a modifier, which is
/// why world collection carries the referenced variable's identity.
/// </summary>
public sealed class ValueModifierRef
{
    public ValueModifierVariable? variable;

    public ValueModifier GetMod() => variable is null ? default : variable.GetValue();

    public ValueModifier GetModifier() => GetMod();
}
