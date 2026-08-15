#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModConfig;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// One element of one tooltip panel, in the columns every panel on the page is read in.
/// </summary>
/// <remarks>
/// <para>
/// A panel row says what the thing is (<c>uuid</c>, <c>name</c>), where the screen keeps it
/// (<c>path</c>), and — for a spell the player has equipped — which slot the cast verbs address it
/// by. Before this, the two panels holding what a player actually casts said only path and name,
/// and reaching an id cost a search while the bracket index in the path read as a slot number one
/// too small.
/// </para>
/// <para>
/// The slot is a join, never the path. The bracket index is a Unity sibling ordinal and matched
/// <c>slot − 1</c> only because nothing above it had been removed that session; deriving a
/// gameplay number from it is precisely the inference the boundary doctrine forbids. What answers
/// instead is the published loadout: the recipe the panel is about, matched against the recipe each
/// occupied slot holds.
/// </para>
/// </remarks>
internal static class GameMcpTooltipPanelRow
{
    internal static GameMcpObjectBuilder Project(
        string path,
        string name,
        Guid entityId,
        GameWorldState? world)
    {
        var row = new GameMcpObjectBuilder
        {
            ["path"] = path,
            ["name"] = name,
        };
        if (entityId == Guid.Empty) return row;
        row["uuid"] = entityId.ToString("D");
        if (world is not null && TrySoleSpellSlot(world, entityId, out var slotIndex))
            row["slot"] = GameMcpSlotNumbering.Wire(slotIndex);
        return row;
    }

    /// <summary>
    /// The address a panel of one element hands out: its own segment where that segment resolves on
    /// its own and the row carries a uuid, and the whole tail otherwise.
    /// </summary>
    /// <remarks>
    /// Two identities for one row. A row carrying a uuid already has the handle the rest of this
    /// surface addresses things by, and nine such rows of one round spent ~190 bytes each on an
    /// absolute path the caller never once quoted back. A row with no uuid keeps the full tail —
    /// that address is its only handle — and so does one whose segment two elements answer to: a
    /// short handle that does not resolve is worse than a long one that does.
    /// </remarks>
    internal static string Address(string tail, bool identified, IReadOnlyList<string> livePaths)
    {
        if (tail is null) throw new ArgumentNullException(nameof(tail));
        if (livePaths is null) throw new ArgumentNullException(nameof(livePaths));
        if (!identified) return tail;
        var cut = tail.LastIndexOf('/');
        if (cut < 0) return tail;
        var segment = tail.Substring(cut + 1);
        var found = 0;
        for (var index = 0; index < livePaths.Count && found < 2; index++)
            if (NativeObjectPath.Addresses(livePaths[index], segment)) found++;
        return found == 1 ? segment : tail;
    }

    /// <summary>
    /// The component every element of one panel is an indexed instance of, when there is one and
    /// saying it once is shorter than saying it on every row.
    /// </summary>
    /// <remarks>
    /// Only a whole component counts. A character-wise prefix would cut
    /// <c>PlotNodeItem(Clone)[1]</c> and <c>PlotNodeItem(Clone)[11]</c> mid-index and name a
    /// component that does not exist, so a segment qualifies only as text followed by a bracketed
    /// run of digits, and every element of the panel must carry the same text before it. The line
    /// costs its own label and one copy of the component; every row after the first is what it buys
    /// back, so a short component on a two-row panel does not pay for the line.
    /// </remarks>
    internal static bool TrySharedComponent(IReadOnlyList<string> segments, out string component)
    {
        if (segments is null) throw new ArgumentNullException(nameof(segments));
        component = string.Empty;
        if (segments.Count < 2) return false;
        for (var index = 0; index < segments.Count; index++)
        {
            if (!TryIndexedComponent(segments[index], out var candidate)) return false;
            if (index == 0) component = candidate;
            else if (!string.Equals(component, candidate, StringComparison.Ordinal)) return false;
        }
        return component.Length > 0 &&
            (segments.Count - 1) * component.Length > ComponentLine.Length;
    }

    private const string ComponentLine = "pathComponent: ";

    /// <summary>The text a segment's trailing <c>[index]</c> hangs off, when it has one.</summary>
    private static bool TryIndexedComponent(string segment, out string component)
    {
        component = string.Empty;
        if (segment is null || segment.Length < 3 || segment[segment.Length - 1] != ']')
            return false;
        var open = segment.LastIndexOf('[');
        if (open <= 0 || open + 1 == segment.Length - 1) return false;
        for (var index = open + 1; index < segment.Length - 1; index++)
            if (segment[index] is < '0' or > '9') return false;
        component = segment.Substring(0, open);
        return true;
    }

    /// <summary>
    /// The one occupied slot holding this recipe, when exactly one does.
    /// </summary>
    /// <remarks>
    /// Two answers mean no answer. A loadout may hold the same recipe in two slots, and then every
    /// button showing it joins to both — so naming either would be a guess about which button the
    /// reader is looking at, and the row says nothing rather than sending a cast to the wrong
    /// position. A recipe no slot holds — every spell shown outside the casting bar — is the same
    /// silence for the same reason.
    /// </remarks>
    internal static bool TrySoleSpellSlot(GameWorldState world, Guid spellRecipeId, out int slotIndex)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        slotIndex = 0;
        var found = false;
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied || slot.SpellRecipeId != spellRecipeId) continue;
            if (found)
            {
                slotIndex = 0;
                return false;
            }
            slotIndex = slot.SlotIndex;
            found = true;
        }
        return found;
    }
}
#endif
