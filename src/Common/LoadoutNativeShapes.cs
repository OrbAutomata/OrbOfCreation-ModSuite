using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace OrbModding.Common;

/// <summary>
/// Pure readings of loadout native shapes, shared by the world loadout reader and the loadout
/// action. Neither reflects: each works over values and delegates its caller already bound.
/// </summary>
internal static class LoadoutNativeShapes
{
    /// <summary>
    /// One <c>StackedIdRecord.GetEntries()</c> element, an <c>(item, quantity)</c> tuple whose item
    /// must be exactly <paramref name="expectedItemType"/>.
    /// </summary>
    internal static bool TryReadEntry(object entry, Type expectedItemType,
        out object? item, out int quantity)
    {
        item = null;
        quantity = 0;
        if (entry is not ITuple tuple || tuple.Length != 2 ||
            tuple[0] is not { } candidate || candidate.GetType() != expectedItemType ||
            tuple[1] is not int count)
        {
            return false;
        }
        item = candidate;
        quantity = count;
        return true;
    }

    /// <summary>
    /// <c>LoadoutManager.CanSwapLoadouts()</c>'s own question, asked without calling it: on a
    /// refusal the game plays its error sound and raises a popup, so a read through it is audible
    /// on every collection pass while a spell casts.
    /// </summary>
    internal static Func<object, bool> SpellsAllowSwap(Func<object, object?> activeSpells,
        Func<object, IList?> values, Func<object, bool> casting, Func<object, bool> readying) =>
        manager =>
        {
            var spells = activeSpells(manager) is { } list ? values(list) : null;
            if (spells is null)
                throw new InvalidOperationException("LoadoutManager.activeSpells was unavailable");
            foreach (var spell in spells)
            {
                if (spell is null)
                    throw new InvalidOperationException("an active spell slot was null");
                if (casting(spell) || readying(spell)) return false;
            }
            return true;
        };
}
