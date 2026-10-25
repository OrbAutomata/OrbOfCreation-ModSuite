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
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpConsumableProjection
{
    internal static GameMcpValue Project(in NativeActionOutcome submission)
    {
        if (submission.Verified) return new JObject().Freeze();
        var result = new JObject();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested consumable transition";
        return result.Freeze();
    }

    internal static GameMcpValue ProjectConsumableDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null ||
            !WorldLookup.TryFind(state.World.Snapshot.Consumables, command.TargetId, out var current))
            return PostStateNotPublished("this item");
        var oldWorld = Before(command);
        WorldConsumable previous = default;
        var hasPrevious = oldWorld is not null && WorldLookup.TryFind(
            oldWorld.Consumables, command.TargetId, out previous);
        return command.Mode switch
        {
            "set_randomization" => Change(
                command.TargetId,
                hasPrevious ? previous.Randomized : (bool?)null,
                current.Randomized,
                "randomized"),
            // A use takes the item into the game's preparation queue and it is consumed later, so
            // the press answers that it queued. A cancel takes entries back out, which is a settled
            // count moving now.
            "use" => QueuedMutation(
                command.TargetId,
                command.Amount,
                hasPrevious ? current.QueuedQuantity - previous.QueuedQuantity : null),
            "cancel" => Change(
                command.TargetId,
                hasPrevious ? previous.QueuedQuantity : (int?)null,
                current.QueuedQuantity,
                "queued"),
            "move" => ProjectConsumableMoveDelta(state, command),
            _ => Change(
                command.TargetId,
                hasPrevious ? previous.Quantity : (int?)null,
                current.Quantity,
                "amount"),
        };
    }

    internal static GameMcpValue ProjectConsumableMoveDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        var oldWorld = Before(command);
        var list = string.Equals(command.PayloadKey, "hotbar", StringComparison.Ordinal)
            ? WorldConsumableListKind.Hotbar
            : WorldConsumableListKind.Inventory;
        var before = oldWorld is not null
            ? FindConsumablePosition(oldWorld.ConsumableInventory.Slots, command.TargetId, list)
            : null;
        var after = FindConsumablePosition(
            state.World!.Snapshot.ConsumableInventory.Slots, command.TargetId, list);
        return Change(command.TargetId, before, after, "slot");
    }

    internal static int? FindConsumablePosition(
        PublicationTable<WorldConsumableSlot> slots,
        Guid consumableId,
        WorldConsumableListKind list)
    {
        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (slot.List == list && slot.ConsumableId == consumableId)
                return GameMcpSlotNumbering.Wire(slot.Position);
        }
        return null;
    }

    internal static GameMcpValue ProjectConsumable(
        GameWorldState world,
        in WorldConsumable consumable)
    {
        var result = new JObject
        {
            ["entityId"] = consumable.EntityId.ToString("D"),
            ["category"] = "consumables",
            ["visible"] = consumable.Visible,
            ["amount"] = consumable.Quantity,
            ["queued"] = consumable.QueuedQuantity,
            ["maximumCarry"] = consumable.MaximumCarryLoad,
        };
        if (consumable.CurrentPrepTime > BigDouble.Zero)
            result["preparationRemaining"] = new GameMcpDomainValue(consumable.CurrentPrepTime);
        if (consumable.CurrentCooldownTime > BigDouble.Zero)
            result["cooldownRemaining"] = new GameMcpDomainValue(
                BigDouble.Max(consumable.CurrentCooldownTime, BigDouble.Zero));

        var types = new JArray();
        if (WorldConsumableTypeLookup.TryFindRange(
                world.ConsumableTypes,
                consumable.EntityId,
                out var typeStart,
                out var typeCount))
        {
            for (var index = typeStart; index < typeStart + typeCount; index++)
                types.Add(new JObject
                {
                    ["typeId"] = world.ConsumableTypes[index].TypeId.ToString("D"),
                });
        }
        if (types.Count > 0) result["types"] = types;

        var levels = new JArray();
        if (WorldConsumableCountLookup.TryFindRange(
                world.ConsumableCounts,
                consumable.EntityId,
                out var countStart,
                out var countCount))
        {
            for (var index = countStart; index < countStart + countCount; index++)
            {
                var value = world.ConsumableCounts[index];
                levels.Add(new JObject
                {
                    ["level"] = value.Level,
                    ["amount"] = value.Quantity,
                    ["freeAmount"] = value.FreeQuantity,
                });
            }
        }
        if (levels.Count > 0) result["levels"] = levels;

        if (consumable.Visible && consumable.Quantity > 0)
        {
            var immediate = ProjectConsumableCosts(
                world,
                consumable.EntityId,
                WorldConsumableCostKind.Consume);
            var held = ProjectConsumableCosts(
                world,
                consumable.EntityId,
                WorldConsumableCostKind.Usage);
            if (immediate.Count > 0) result["useCosts"] = immediate;
            if (held.Count > 0) result["heldCostsPerSecond"] = held;
        }

        var useAvailable = consumable.Visible && consumable.Quantity > 0 && consumable.CanFire &&
            world.ConsumableInventory.CanUse;
        var use = new JObject { ["available"] = useAvailable };
        if (!useAvailable)
        {
            use["reasonCode"] = !consumable.Visible
                ? "not_visible"
                : consumable.Quantity <= 0
                    ? "none_owned"
                    : !world.ConsumableInventory.CanUse
                        ? "inventory_busy"
                        : !consumable.ImmediateCostsAffordable
                            ? "unaffordable"
                            : consumable.CurrentCooldownTime > BigDouble.Zero
                                ? "cooldown_active"
                                : "native_use_refused";
        }
        result["use"] = use;

        var usages = ProjectConsumableUsages(world, consumable.EntityId);
        if (usages.Count > 0) result["usages"] = usages;
        result["cancel"] = consumable.QueuedQuantity > 0 && usages.Count > 0
            ? new JObject { ["available"] = true }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "no_cancellable_usage",
            };
        result["discard"] = consumable.Quantity > 0
            ? new JObject
            {
                ["available"] = true,
                ["maximumAmount"] = consumable.Quantity,
            }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "none_owned",
            };
        if (consumable.CanBeRandomized)
        {
            result["randomization"] = new JObject
            {
                ["available"] = true,
                ["enabled"] = consumable.Randomized,
            };
        }

        var placements = ProjectConsumablePlacements(world, consumable.EntityId);
        if (placements.Count > 0) result["placements"] = placements;
        return result.Freeze();
    }

    internal static JArray ProjectConsumableCosts(
        GameWorldState world,
        Guid consumableId,
        WorldConsumableCostKind kind)
    {
        var result = new JArray();
        if (!WorldConsumableCostLookup.TryFindRange(
                world.ConsumableCosts,
                consumableId,
                kind,
                out var start,
                out var count))
            return result;
        for (var index = start; index < start + count; index++)
        {
            var value = world.ConsumableCosts[index];
            var playerCost = PlayerFacingCost(world, value.ResourceId, value.Amount);
            var cost = new JObject
            {
                ["resourceId"] = value.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(playerCost),
            };
            if (WorldLookup.TryFind(world.Resources, value.ResourceId, out var resource))
            {
                var amount = SpendableAmount(world, value.ResourceId, resource.Reading.Quantity);
                cost["spendableAmount"] = new GameMcpDomainValue(amount);
                cost["affordable"] = CanAfford(
                    world, value.ResourceId, value.Amount, resource.Reading.Quantity);
            }
            else cost["affordable"] = false;
            result.Add(cost);
        }
        return result;
    }

    internal static JArray ProjectConsumableUsages(GameWorldState world, Guid consumableId)
    {
        var result = new JArray();
        if (!WorldConsumableUsageLookup.TryFindRange(
                world.ConsumableUsages,
                consumableId,
                out var start,
                out var count))
            return result;
        for (var index = start; index < start + count; index++)
        {
            var usage = world.ConsumableUsages[index];
            var value = new JObject
            {
                ["usageId"] = usage.UsageId.ToString("D"),
                ["level"] = usage.Level,
                ["state"] = usage.Engaged ? "active" : "pending",
            };
            if (usage.Engaged)
            {
                value["remainingDuration"] = new GameMcpDomainValue(usage.RemainingDuration);
                value["maximumDuration"] = new GameMcpDomainValue(usage.MaximumDuration);
            }
            result.Add(value);
        }
        return result;
    }

    internal static JArray ProjectConsumablePlacements(GameWorldState world, Guid consumableId)
    {
        var result = new JArray();
        var slots = world.ConsumableInventory.Slots;
        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (slot.ConsumableId != consumableId) continue;
            var placement = new JObject
            {
                ["list"] = ConsumableListName(slot.List),
                ["position"] = GameMcpSlotNumbering.Wire(slot.Position),
            };
            result.Add(placement);
        }
        return result;
    }

    internal static string ConsumableListName(WorldConsumableListKind kind) => kind switch
    {
        WorldConsumableListKind.Inventory => "inventory",
        WorldConsumableListKind.Hotbar => "hotbar",
        _ => "unknown",
    };

}
#endif
