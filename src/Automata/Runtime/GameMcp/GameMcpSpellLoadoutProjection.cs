#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;


using static OrbAutomata.GameMcp.GameMcpDecisionReason;
using static OrbAutomata.GameMcp.GameMcpRowKit;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellLoadoutProjection
{
    internal static GameMcpValue Project(in NativeActionOutcome submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested spell slot state";
        return result.Freeze();
    }

    internal static int OccupiedSpellSlots(GameWorldState world)
    {
        var occupied = 0;
        for (var index = 0; index < world.SpellSlots.Count; index++)
            if (world.SpellSlots[index].Occupied) occupied++;
        return occupied;
    }

    internal static GameMcpValue ProjectSpellLoadoutDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var after = state.World.Snapshot;
        var before = Before(command);
        if (command.Mode is "add" or "create")
        {
            for (var index = 0; index < after.SpellSlots.Count; index++)
            {
                var slot = after.SpellSlots[index];
                if (!slot.Occupied || slot.SpellRecipeId != command.TargetId) continue;
                if (before is not null && ContainsSpellInstance(
                        before.SpellSlots, slot.SpellInstanceId)) continue;
                var loaded = new JObject
                {
                    ["uuid"] = command.TargetId.ToString("D"),
                    ["slot"] = new JObject
                    {
                        ["before"] = GameMcpListColumns.Empty,
                        ["after"] = GameMcpSlotNumbering.Wire(slot.SlotIndex),
                    },
                };
                // What the load actually made. A caller passing an augment layout could read back
                // only the slot number, so the one thing it had asked for — this many of this
                // augment on this spell — was the one thing the answer would not confirm.
                var augments = new JArray();
                for (var augment = 0; augment < slot.AugmentGlyphs.Count; augment++)
                    augments.Add(new JObject
                    {
                        ["glyphId"] = slot.AugmentGlyphs[augment].GlyphId.ToString("D"),
                        ["count"] = slot.AugmentGlyphs[augment].Quantity,
                    });
                if (augments.Count > 0) loaded["augments"] = augments;
                // Both budgets a load is weighed against, settled — the same pair a removal
                // answers with. The usage allocation is re-read after the game's own recompute
                // rather than predicted, because upgrades can move it after the load lands.
                loaded["loadBudget"] = ProjectLoadedBudget(before, after);
                return loaded.Freeze();
            }
        }
        else
        {
            WorldSpellSlot oldSlot = default;
            var hadBefore = before is not null && TryFindSpellInstance(
                before.SpellSlots, command.TargetId, out oldSlot);
            var hasAfter = TryFindSpellInstance(
                after.SpellSlots, command.TargetId, out var newSlot);
            if (command.Mode == "remove" && hadBefore && !hasAfter)
                return new JObject
                {
                    ["uuid"] = oldSlot.SpellRecipeId.ToString("D"),
                    ["slot"] = new JObject
                    {
                        ["before"] = GameMcpSlotNumbering.Wire(oldSlot.SlotIndex),
                        ["after"] = GameMcpListColumns.Empty,
                    },
                    ["loadBudget"] = ProjectRemovalBudget(before, after),
                    ["oneWay"] = "The game destroyed this spell; the way back is another add.",
                }.Freeze();
            if (command.Mode == "move" && hadBefore && hasAfter)
            {
                var moved = new JObject
                {
                    ["uuid"] = newSlot.SpellRecipeId.ToString("D"),
                    ["slot"] = new JObject
                    {
                        ["before"] = GameMcpSlotNumbering.Wire(oldSlot.SlotIndex),
                        ["after"] = GameMcpSlotNumbering.Wire(newSlot.SlotIndex),
                    },
                };
                // A move onto an occupied slot is a swap. Reporting only the half the caller named
                // left the other spell somewhere the caller's model did not have it, and the next
                // cast at the old address refused.
                var displaced = ProjectDisplacedSpell(
                    after, before!, newSlot.SlotIndex, newSlot.SpellInstanceId);
                if (displaced is not null) moved["displaced"] = displaced;
                return moved.Freeze();
            }
        }
        return PostStateUnavailable("requested_state_not_reached",
            "the settled loadout does not show the requested add, remove, or move");
    }

    /// <summary>
    /// The spell the move pushed out of the destination, and where the game put it.
    /// </summary>
    internal static GameMcpValue? ProjectDisplacedSpell(
        GameWorldState after,
        GameWorldState before,
        int destinationIndex,
        Guid movedInstanceId)
    {
        var occupant = default(WorldSpellSlot);
        var occupied = false;
        for (var index = 0; index < before.SpellSlots.Count; index++)
        {
            var slot = before.SpellSlots[index];
            if (slot.SlotIndex != destinationIndex || !slot.Occupied) continue;
            if (slot.SpellInstanceId == movedInstanceId) continue;
            occupant = slot;
            occupied = true;
            break;
        }
        if (!occupied) return null;
        if (!TryFindSpellInstance(after.SpellSlots, occupant.SpellInstanceId, out var settled))
            return null;
        if (settled.SlotIndex == destinationIndex) return null;
        return new JObject
        {
            ["uuid"] = settled.SpellRecipeId.ToString("D"),
            ["slot"] = new JObject
            {
                ["before"] = GameMcpSlotNumbering.Wire(destinationIndex),
                ["after"] = GameMcpSlotNumbering.Wire(settled.SlotIndex),
            },
        }.Freeze();
    }

    internal static bool ContainsSpellInstance(
        PublicationTable<WorldSpellSlot> slots,
        Guid instanceId) => TryFindSpellInstance(slots, instanceId, out _);

    internal static bool TryFindSpellInstance(
        PublicationTable<WorldSpellSlot> slots,
        Guid instanceId,
        out WorldSpellSlot slot)
    {
        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index].SpellInstanceId != instanceId) continue;
            slot = slots[index];
            return true;
        }
        slot = default;
        return false;
    }

    /// <summary>
    /// The spell a caller means when it names a slot on the loadout bar.
    /// </summary>
    /// <remarks>
    /// A runtime spell instance has an id the game never shows and the asset catalog never
    /// publishes, so it has no place on the wire at all. The bar itself is what the player sees and
    /// what the screen numbers, and a refusal names both what was asked for and what is there.
    /// </remarks>
    internal static bool TryEquippedSpellSlot(
        GameWorldState world,
        int slot,
        out Guid spellInstanceId,
        out string reason)
    {
        spellInstanceId = Guid.Empty;
        var slots = world.SpellSlots;
        if (slots.Count == 0)
        {
            reason = "The loadout bar has no slots yet.";
            return false;
        }
        if (slot < 1 || slot > slots.Count)
        {
            reason = "There is no slot " + slot + "; the loadout bar has slots 1 to " +
                slots.Count + ".";
            return false;
        }
        var value = slots[GameMcpSlotNumbering.Index(slot)];
        if (!value.Occupied || value.SpellInstanceId == Guid.Empty)
        {
            reason = "Slot " + slot + " is empty; " + OccupiedSlotsSentence(slots);
            return false;
        }
        spellInstanceId = value.SpellInstanceId;
        reason = string.Empty;
        return true;
    }

    internal static string OccupiedSlotsSentence(PublicationTable<WorldSpellSlot> slots)
    {
        var occupied = new System.Text.StringBuilder();
        var count = 0;
        for (var index = 0; index < slots.Count; index++)
        {
            if (!slots[index].Occupied) continue;
            if (count > 0) occupied.Append(", ");
            occupied.Append(GameMcpSlotNumbering.Wire(slots[index].SlotIndex)
                .ToString(CultureInfo.InvariantCulture));
            count++;
        }
        return count == 0
            ? "no slot on the bar holds a spell."
            : count == 1
                ? "the one spell you have equipped is in slot " + occupied + "."
                : "the spells you have equipped are in slots " + occupied + ".";
    }

    /// <summary>
    /// What a removal moved, in both budgets an add is weighed against.
    /// </summary>
    /// <remarks>
    /// A round removed a spell specifically to free upkeep and got back a slot number with nothing
    /// beside it; when the next add refused for an unrelated reason, the reader could not tell
    /// whether the removal had bought anything at all and read the whole verb as state-sensitive.
    /// Both budgets answer here — the spot count the bar keeps and the usage headroom the add gate
    /// weighs a candidate against — each stated as the move it made rather than as a level.
    /// </remarks>
    internal static JObject ProjectRemovalBudget(GameWorldState? before, GameWorldState after)
    {
        var result = new JObject
        {
            ["used"] = new JObject
            {
                ["before"] = before?.SpellWorkbench.EquippedCount,
                ["after"] = after.SpellWorkbench.EquippedCount,
            },
            ["maximum"] = after.SpellWorkbench.MaximumEquipped,
            ["fitsAnotherSpell"] = after.SpellWorkbench.HasEmptySlot,
        };
        var usage = ProjectFreedUsageBudget(before, after);
        if (usage.Count > 0) result["usageBudget"] = usage;
        return result;
    }

    /// <summary>What a load settled at, in both budgets it is weighed against.</summary>
    /// <remarks>
    /// The usage allocation is the only budget the game gates a load on, and it is settled after
    /// the fact: the game recomputes spell weight once the spell is in a slot, and an upgrade can
    /// move it afterwards. So this is a re-read, never a prediction made before the press.
    /// </remarks>
    internal static JObject ProjectLoadedBudget(GameWorldState? before, GameWorldState after)
    {
        var result = new JObject
        {
            ["used"] = new JObject
            {
                ["before"] = before?.SpellWorkbench.EquippedCount,
                ["after"] = after.SpellWorkbench.EquippedCount,
            },
            ["maximum"] = after.SpellWorkbench.MaximumEquipped,
            ["fitsAnotherSpell"] = after.SpellWorkbench.HasEmptySlot,
        };
        var usage = ProjectFreedUsageBudget(before, after);
        if (usage.Count > 0) result["usageBudget"] = usage;
        return result;
    }

    internal static JArray ProjectFreedUsageBudget(GameWorldState? before, GameWorldState after)
    {
        var rows = new JArray();
        var resourceIds = after.SpellWorkbench.UsageBudgetResourceIds;
        for (var index = 0; index < resourceIds.Count; index++)
        {
            var resourceId = resourceIds[index];
            if (!WorldLookup.TryFind(after.Resources, resourceId, out var resource)) continue;
            var freed = new GameMcpDomainValue(WorldResourceCoordinate.SpendableAmount(in resource));
            object headroom = before is not null &&
                WorldLookup.TryFind(before.Resources, resourceId, out var was)
                ? new JObject
                {
                    ["before"] = new GameMcpDomainValue(
                        WorldResourceCoordinate.SpendableAmount(in was)),
                    ["after"] = freed,
                }
                : freed;
            rows.Add(new JObject
            {
                ["resource"] = EntityReference(after, resourceId),
                ["headroom"] = headroom,
                ["used"] = new GameMcpDomainValue(resource.Reading.Quantity),
                ["maximum"] = new GameMcpDomainValue(resource.Reading.Capacity),
            });
        }
        return rows;
    }

    internal static JArray ProjectSpellUsageBudget(GameWorldState world)
    {
        var rows = new JArray();
        var resourceIds = world.SpellWorkbench.UsageBudgetResourceIds;
        for (var index = 0; index < resourceIds.Count; index++)
        {
            var resourceId = resourceIds[index];
            if (!WorldLookup.TryFind(world.Resources, resourceId, out var resource)) continue;
            rows.Add(new JObject
            {
                ["resource"] = EntityReference(world, resourceId),
                ["headroom"] = new GameMcpDomainValue(
                    WorldResourceCoordinate.SpendableAmount(in resource)),
                ["used"] = new GameMcpDomainValue(resource.Reading.Quantity),
                ["maximum"] = new GameMcpDomainValue(resource.Reading.Capacity),
            });
        }
        return rows;
    }

}
#endif
