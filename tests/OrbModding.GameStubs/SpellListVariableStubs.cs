using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The equipped loadout. A list variable like every other, which is what lets world collection reach
/// it by uuid through the identity registry rather than through the spell manager singleton.
/// </summary>
public sealed class SpellListVariable : GenericListVariable<Spell>, IEnumerable
{
    public bool PreserveSlotsOnRemove { get; set; } = true;

    public Spell this[int index] => value[index];

    protected override bool IsFilledElement(Spell element) =>
        element is not null && !element.IsEmpty();

    public override void Remove(Spell element)
    {
        var index = value.IndexOf(element);
        if (index < 0) return;
        if (PreserveSlotsOnRemove)
        {
            value[index] = new Spell
            {
                NativeEmpty = true,
                guidContainer = new GuidContainer(Guid.Empty),
            };
            return;
        }
        value.RemoveAt(index);
    }

    public IEnumerator GetEnumerator() => value.GetEnumerator();

    public List<Spell> GetAll() => new List<Spell>(value);
}
