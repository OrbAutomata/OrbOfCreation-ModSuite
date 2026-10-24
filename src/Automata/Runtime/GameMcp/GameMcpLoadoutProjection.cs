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


using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpSpellLoadoutProjection;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpLoadoutProjection
{
    internal static GameMcpValue Project(in LoadoutSubmission submission)
    {
        if (submission.Outcome.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested loadout transition";

        // A refusal whose sentence names a slot range carries both bounds as numbers, read from
        // the live snapshot list the sentence was written from.
        if (submission.MinimumSlot >= 0) result["minimumSlot"] = submission.MinimumSlot;
        if (submission.MaximumSlot >= 0) result["maximumSlot"] = submission.MaximumSlot;
        return result.Freeze();
    }

    internal static GameMcpValue ProjectSnapshotSlotRow(in WorldSnapshotSlot slot) =>
        new JObject
        {
            ["ownerId"] = Named(slot.OwnerId),
            ["slot"] = GameMcpSlotNumbering.Wire(slot.Slot),
            ["populated"] = slot.Populated,
        }.Freeze();

    internal static GameMcpValue ProjectSnapshotEntryRow(in WorldSnapshotEntry entry) =>
        new JObject
        {
            ["ownerId"] = Named(entry.OwnerId),
            ["slot"] = GameMcpSlotNumbering.Wire(entry.Slot),
            ["entryId"] = Named(entry.EntryId),
            ["quantity"] = entry.Quantity,
        }.Freeze();

    internal static GameMcpValue ProjectLoadoutDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var before = Before(command);
        if (command.DerivedNativeType == GameMcpLoadoutCommand.PlayerLoadoutType)
        {
            if (!WorldLoadoutLookup.TryFindPlayer(world.PlayerLoadouts,
                    command.TargetId, out var current))
                return PostStateNotPublished("this loadout");
            WorldPlayerLoadout previous = default;
            var hadBefore = before is not null && WorldLoadoutLookup.TryFindPlayer(
                before.PlayerLoadouts, command.TargetId, out previous);
            // The loadout is a live object the asset catalog never publishes, so its id resolves for
            // nobody and every tool refuses it; the read rows dropped it for the same reason, and a
            // mutation answering with one would be the one place a caller could still find it.
            var result = new JObject { ["name"] = current.Name };
            switch (command.Mode)
            {
                case "select":
                    result["selected"] = new JObject
                    {
                        ["before"] = hadBefore && previous.Selected,
                        ["after"] = current.Selected,
                    };
                    // Swapping loadouts re-equips the spell bar, and the bar belongs to the player
                    // rather than to the loadout being described. Selecting an empty loadout
                    // unequipped five spells and the only trace was `spells: none` inside the
                    // loadout's own contents, which reads as what that loadout stores.
                    var bar = ProjectSpellBarEffect(world, before);
                    if (bar is not null) result["spellBar"] = bar;
                    result["loadout"] = ProjectPlayerLoadout(world, in current);
                    break;
                case "set_equipment":
                    result["equipment"] = new JObject
                    {
                        ["before"] = hadBefore && previous.SavesEquipment,
                        ["after"] = current.SavesEquipment,
                    };
                    break;
                case "set_alchemy":
                    result["alchemy"] = new JObject
                    {
                        ["before"] = hadBefore && previous.SavesAlchemy,
                        ["after"] = current.SavesAlchemy,
                    };
                    break;
                case "rename":
                    result["label"] = new JObject
                    {
                        ["before"] = hadBefore ? previous.Name : null,
                        ["after"] = current.Name,
                    };
                    break;
                case "next_icon":
                    result["icon"] = new JObject
                    {
                        ["before"] = hadBefore ? previous.Icon : (int?)null,
                        ["after"] = current.Icon,
                    };
                    break;
                case "next_color":
                    result["color"] = new JObject
                    {
                        ["before"] = hadBefore ? previous.Color : (int?)null,
                        ["after"] = current.Color,
                    };
                    break;
            }
            return result.Freeze();
        }

        if (!WorldLoadoutLookup.TryFindSnapshot(world.SnapshotLoadouts,
                command.TargetId, out var owner))
            return PostStateNotPublished("this saved-layout list");
        var slot = command.Amount - 1;
        var snapshot = ProjectSnapshotSlot(world, in owner, slot);
        if (snapshot is null)
            return PostStateNotPublished("that saved-layout slot");
        var response = new JObject
        {
            ["name"] = SnapshotOwnerName(world, in owner),
            ["kind"] = SnapshotKind(owner.Kind),
            ["snapshot"] = snapshot,
        };
        if (command.Mode == "snapshot_load")
            response["active"] = ProjectActiveLoadoutSection(world, owner.Kind);
        return response.Freeze();
    }

    /// <summary>
    /// What a loadout swap did to the spell bar, or nothing when the bar did not move.
    /// </summary>
    /// <remarks>
    /// The bar is player state, not loadout contents, so it is named at the top of the answer with
    /// the spells that left and the spells that arrived. Recovery needs the names that went away,
    /// and the settled world no longer holds them.
    /// </remarks>
    internal static GameMcpValue? ProjectSpellBarEffect(
        GameWorldState world,
        GameWorldState? before)
    {
        if (before is null) return null;
        var removed = new JArray();
        var added = new JArray();
        for (var index = 0; index < before.SpellSlots.Count; index++)
        {
            var slot = before.SpellSlots[index];
            if (!slot.Occupied) continue;
            if (ContainsSpellInstance(world.SpellSlots, slot.SpellInstanceId)) continue;
            removed.Add(EntityReference(before, slot.SpellRecipeId));
        }
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied) continue;
            if (ContainsSpellInstance(before.SpellSlots, slot.SpellInstanceId)) continue;
            added.Add(EntityReference(world, slot.SpellRecipeId));
        }
        if (removed.Count == 0 && added.Count == 0) return null;
        var result = new JObject
        {
            ["equipped"] = new JObject
            {
                ["before"] = OccupiedSpellSlots(before),
                ["after"] = OccupiedSpellSlots(world),
            },
        };
        if (removed.Count > 0) result["unequipped"] = removed;
        if (added.Count > 0) result["equippedNow"] = added;
        return result.Freeze();
    }

    internal static GameMcpValue ProjectPlayerLoadout(
        GameWorldState world,
        in WorldPlayerLoadout loadout)
    {
        var spells = new JArray();
        var equipment = new JArray();
        var alchemy = new JArray();
        for (var index = 0; index < world.PlayerLoadoutEntries.Count; index++)
        {
            var entry = world.PlayerLoadoutEntries[index];
            if (entry.OwnerId != loadout.EntityId) continue;
            if (entry.Kind == WorldLoadoutEntryKind.Spell)
            {
                // The saved spell is a runtime instance, so its id is in no catalog and resolves for
                // nobody. Its recipe does, and it is the only identity here a caller can look up.
                spells.Add(new JObject { ["spell"] = EntityReference(world, entry.ReferenceId) });
            }
            else
            {
                var row = EntityReference(world, entry.EntryId, entry.Quantity);
                (entry.Kind == WorldLoadoutEntryKind.Equipment ? equipment : alchemy).Add(row);
            }
        }
        // Every section is always present with its own list, empty or not: a loadout that saves
        // nothing is a fact the caller needs, and dropping the key made it read the same as a
        // section nobody projected.
        var sections = new JObject { ["spells"] = spells };
        sections["equipment"] = new JObject
        {
            ["saved"] = loadout.SavesEquipment,
            ["entries"] = equipment,
        };
        sections["alchemy"] = new JObject
        {
            ["saved"] = loadout.SavesAlchemy,
            ["entries"] = alchemy,
        };
        var result = new JObject
        {
            ["name"] = loadout.Name,
            ["category"] = "player-loadouts",
            ["selected"] = loadout.Selected,
            ["sections"] = sections,
            ["label"] = new JObject
            {
                ["icon"] = loadout.Icon,
                ["color"] = loadout.Color,
            },
        };

        // One predicate serves the read and the mutation: CanSwapLoadouts() is the game's whole
        // admission for a swap, so a false read carries the code select itself would return.
        if (!loadout.Selected)
        {
            result["canSelect"] = loadout.CanSwitchNow;
            if (!loadout.CanSwitchNow) result["reasonCode"] = "switch_blocked";
        }
        return result.Freeze();
    }

    internal static GameMcpValue ProjectSnapshotLoadout(
        GameWorldState world,
        in WorldSnapshotLoadout owner)
    {
        var slots = new JArray();
        for (var slot = 0; slot < owner.Slots; slot++)
        {
            var row = ProjectSnapshotSlot(world, in owner, slot);
            if (row is not null) slots.Add(row);
        }
        var result = new JObject
        {
            ["name"] = SnapshotOwnerName(world, in owner),
            ["category"] = "snapshot-loadouts",
            ["kind"] = SnapshotKind(owner.Kind),
            ["slots"] = slots,
        };
        return result.Freeze();
    }

    internal static GameMcpValue? ProjectSnapshotSlot(
        GameWorldState world,
        in WorldSnapshotLoadout owner,
        int slot)
    {
        WorldSnapshotSlot value = default;
        var found = false;
        for (var index = 0; index < world.SnapshotSlots.Count; index++)
        {
            var candidate = world.SnapshotSlots[index];
            if (candidate.OwnerId != owner.EntityId || candidate.Slot != slot) continue;
            value = candidate;
            found = true;
            break;
        }
        if (!found) return null;
        var result = new JObject
        {
            ["slot"] = GameMcpSlotNumbering.Wire(slot),
            ["populated"] = value.Populated,
        };
        if (value.Populated)
        {
            var entries = new JArray();
            for (var index = 0; index < world.SnapshotEntries.Count; index++)
            {
                var entry = world.SnapshotEntries[index];
                if (entry.OwnerId == owner.EntityId && entry.Slot == slot)
                    entries.Add(EntityReference(world, entry.EntryId, entry.Quantity));
            }
            result["entries"] = entries;
        }
        return result.Freeze();
    }

    internal static GameMcpValue ProjectActiveLoadoutSection(
        GameWorldState world,
        WorldSnapshotLoadoutKind kind)
    {
        var entries = new JArray();
        if (kind == WorldSnapshotLoadoutKind.Equipment)
        {
            for (var index = 0; index < world.Equipment.Count; index++)
            {
                var equipment = world.Equipment[index];
                if (equipment.EquippedLevel > 0)
                    entries.Add(EntityReference(world, equipment.EntityId,
                        equipment.EquippedLevel));
            }
        }
        else
        {
            for (var index = 0; index < world.AlchemyLoadout.Count; index++)
            {
                var alchemy = world.AlchemyLoadout[index];
                if (alchemy.IsActive && alchemy.Amount > 0)
                    entries.Add(EntityReference(world, alchemy.RecipeId, alchemy.Amount));
            }
        }
        return entries.Freeze();
    }

    internal static string SnapshotOwnerName(
        GameWorldState world,
        in WorldSnapshotLoadout owner)
    {
        var identity = EntityIdentityFormatter.Describe(owner.EntityId, world.EntityIdentities);
        return identity.Source is EntityIdentityNameSource.LiveDisplayName or
            EntityIdentityNameSource.KnownEntityBootstrap
            ? identity.Name
            : SnapshotKind(owner.Kind) + " snapshots";
    }

    internal static string SnapshotKind(WorldSnapshotLoadoutKind kind) =>
        kind == WorldSnapshotLoadoutKind.Alchemy ? "alchemy" : "equipment";

    /// <summary>
    /// The player loadout a caller means when it names a position on the loadout bar.
    /// </summary>
    internal static bool TryPlayerLoadout(
        GameWorldState world,
        int position,
        out Guid loadoutId,
        out string reason)
    {
        loadoutId = Guid.Empty;
        var loadouts = world.PlayerLoadouts;
        if (loadouts.Count == 0)
        {
            reason = "This save has no player loadouts.";
            return false;
        }
        if (position < 1 || position > loadouts.Count)
        {
            reason = "There is no loadout " + position + "; you have loadouts 1 to " +
                loadouts.Count + ".";
            return false;
        }
        loadoutId = loadouts[GameMcpSlotNumbering.Index(position)].EntityId;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The snapshot list a caller means when it names the Equipment or Alchemy section.
    /// </summary>
    internal static bool TrySnapshotList(
        GameWorldState world,
        string section,
        out Guid listId,
        out string reason)
    {
        listId = Guid.Empty;
        var wanted = string.Equals(section, "alchemy", StringComparison.Ordinal)
            ? WorldSnapshotLoadoutKind.Alchemy
            : WorldSnapshotLoadoutKind.Equipment;
        for (var index = 0; index < world.SnapshotLoadouts.Count; index++)
        {
            if (world.SnapshotLoadouts[index].Kind != wanted) continue;
            listId = world.SnapshotLoadouts[index].EntityId;
            reason = string.Empty;
            return true;
        }
        reason = "The game is not showing the " +
            (wanted == WorldSnapshotLoadoutKind.Alchemy ? "Alchemy" : "Equipment") +
            " snapshots right now.";
        return false;
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldPlayerLoadout playerLoadout,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["name"] = playerLoadout.Name,
                ["selected"] = playerLoadout.Selected,
            }.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldSnapshotLoadout snapshotLoadout,
        int nestedOffset,
        int nestedLimit)
    {
        var identity = EntityIdentityFormatter.Describe(
            snapshotLoadout.EntityId, world.EntityIdentities);
        return new JObject
        {
            ["name"] = identity.HasName
                ? identity.Name
                : SnapshotKind(snapshotLoadout.Kind) + " snapshots",
            ["kind"] = SnapshotKind(snapshotLoadout.Kind),
            ["slots"] = snapshotLoadout.Slots,
        }.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldSnapshotSlot snapshotSlot,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectSnapshotSlotRow(in snapshotSlot);
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldSnapshotEntry snapshotEntry,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectSnapshotEntryRow(in snapshotEntry);
    }

}
#endif
