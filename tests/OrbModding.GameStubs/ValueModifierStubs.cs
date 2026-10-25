using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// One modifier. Named and shaped as the game shapes it — a struct whose three arithmetic fields
/// world collection reads directly, plus enough identity to be countable inside a record's set.
/// </summary>
public struct ValueModifier
{
    public enum ValueModifierType
    {
        Raw,
        MultiDiminishing,
        MultiStacking,
        Reduction,
        Exponent,
    }

    public GuidContainer gc;
    public BigDouble adjustReal;
    public ValueModifierType type;
    public int order;
#pragma warning disable CS0414 // Native private field read by the collector through a bound accessor.
    private IdScriptableObject? reference;
#pragma warning restore CS0414

    public ValueModifier(ValueModifierType type, BigDouble amount, int order = 0, IdScriptableObject? reference = null)
    {
        gc = new GuidContainer(Guid.NewGuid());
        this.type = type;
        adjustReal = amount;
        this.order = order;
        this.reference = reference;
    }

    public BigDouble Adjust(BigDouble value) => type switch
    {
        ValueModifierType.Raw => value + adjustReal,
        ValueModifierType.MultiDiminishing => value * (BigDouble.One + adjustReal),
        ValueModifierType.MultiStacking => value * adjustReal,
        ValueModifierType.Reduction => value / (BigDouble.One + adjustReal),
        ValueModifierType.Exponent => BigDouble.Pow(
            value,
            value > BigDouble.Zero && value < BigDouble.One
                ? BigDouble.One / adjustReal
                : adjustReal),
        _ => value,
    };
}
