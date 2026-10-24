using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The base every modifier record derives from in the game. Modelled only as far as the active set
/// world collection counts, since a plain ModifierRecord has no cached value of its own.
/// </summary>
public class ModifierRecord
{
    public Dictionary<Guid, ValueModifier> passiveModifiers = new Dictionary<Guid, ValueModifier>();

    public Dictionary<Guid, ValueModifier> activeModifiers = new Dictionary<Guid, ValueModifier>();

    public bool HasActiveElements() => activeModifiers.Count > 0;

    public BigDouble Adjust(BigDouble value)
    {
        foreach (var modifier in passiveModifiers.Values.Concat(activeModifiers.Values)
                     .OrderBy(static modifier => modifier.order))
        {
            if (modifier.type != ValueModifier.ValueModifierType.Raw)
                throw new InvalidOperationException("The portable research stub models raw requirement adjustments only.");
            value += modifier.adjustReal;
        }
        return value;
    }
}
