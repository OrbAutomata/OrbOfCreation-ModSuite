#if SERVICE_CYCLE_PROFILE
using System;
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
