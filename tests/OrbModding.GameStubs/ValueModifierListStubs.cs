using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Two modifier lists, not one. The exponents strengthen the modifiers before any of them touches a
/// value, so the suite reads both and keeps them apart.
/// </summary>
public sealed class ValueModifierList
{
    public List<ValueModifier> modifiers = new List<ValueModifier>();
    public List<ValueModifier> exponents = new List<ValueModifier>();
}
