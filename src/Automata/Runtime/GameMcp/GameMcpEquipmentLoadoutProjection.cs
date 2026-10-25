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
using static OrbAutomata.GameMcp.GameMcpDiscoveryTreeOfferProjection;
using static OrbAutomata.GameMcp.GameMcpGenericLevelProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpEquipmentLoadoutProjection
{
    internal static GameMcpValue Project(in EquipmentLoadoutSubmission submission)
    {
        if (submission.Outcome.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested equipped stack count";

        // A refusal whose sentence names a ceiling carries that same ceiling as a number, read from
        // the admission capture the sentence was written from.
        if (submission.MaximumAmount >= 0) result["maximumAmount"] = submission.MaximumAmount;
        return result.Freeze();
    }

    internal static GameMcpValue ProjectEquipmentDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null ||
            !WorldLookup.TryFind(state.World.Snapshot.Equipment, command.TargetId, out var current))
            return PostStateNotPublished("this artifact");
        var oldWorld = Before(command);
        var before = oldWorld is not null && WorldLookup.TryFind(
            oldWorld.Equipment, command.TargetId, out var previous)
            ? previous.EquippedLevel
            : (int?)null;
        return Change(command.TargetId, before, current.EquippedLevel, "equippedCount");
    }

    internal static GameMcpValue ProjectEquipment(GameWorldState world, in WorldEquipment equipment)
    {
        var result = new JObject
        {
            ["entityId"] = equipment.EntityId.ToString("D"),
            ["category"] = "equipment",
            ["created"] = equipment.IsCreated,
            ["masteryLevel"] = equipment.MasteryLevel,
            ["attuningLevel"] = equipment.AttuningLevel,
        };
        if (equipment.AttunementTimeLeft > 0d)
            result["attunementTimeLeft"] = equipment.AttunementTimeLeft;
        var decision = equipment.Loadout;
        if (decision.Available)
        {
            result["equipmentTypeId"] = decision.EquipmentTypeId.ToString("D");
            result["equippedStacks"] = decision.EquippedStacks;
            result["maximumStacks"] = decision.MaximumStacks;
            result["loadout"] = new JObject
            {
                ["usedSlots"] = decision.UsedSlots,
                ["maximumSlots"] = decision.MaximumSlots,
                ["typeUsedSlots"] = decision.TypeUsedSlots,
                ["typeMaximumSlots"] = decision.TypeMaximumSlots,
            };
            var equip = new JObject { ["available"] = equipment.IsCreated && decision.MaximumEquipAmount > 0 };
            if (equipment.IsCreated && decision.MaximumEquipAmount > 0)
                equip["maximumAmount"] = decision.MaximumEquipAmount;
            else
                equip["reasonCode"] = !equipment.IsCreated
                    ? "not_created"
                    : decision.EquippedStacks >= decision.MaximumStacks
                        ? "maximum_stacks"
                        : decision.EquippedStacks == 0 && decision.UsedSlots >= decision.MaximumSlots
                            ? "loadout_full"
                            : decision.EquippedStacks == 0 && decision.TypeUsedSlots >= decision.TypeMaximumSlots
                                ? "equipment_type_full"
                                : !decision.UsageAffordable
                                    ? "usage_unaffordable"
                                    : "usage_unavailable";
            if (decision.Costs.Count > 0)
            {
                var costs = new JArray();
                for (var index = 0; index < decision.Costs.Count; index++)
                {
                    var cost = decision.Costs[index];
                    var playerCost = PlayerFacingCost(world, cost.ResourceId, cost.Cost);
                    var costRow = new JObject
                    {
                        ["resourceId"] = cost.ResourceId.ToString("D"),
                        ["cost"] = new GameMcpDomainValue(playerCost),
                    };
                    if (WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
                    {
                        var amount = SpendableAmount(world, cost.ResourceId, resource.Reading.Quantity);
                        costRow["spendableAmount"] = new GameMcpDomainValue(amount);
                        var affordable = CanAfford(
                            world, cost.ResourceId, cost.Cost, resource.Reading.Quantity);
                        costRow["affordable"] = affordable;
                        if (resource.Reading.Traits.BandwidthResource)
                        {
                            // The ceiling follows the value it caps: this one bounds a carried
                            // weight, so it is the magnitude-shaped `maximumCarry`, never the
                            // `maximum` every argument bound on the surface ships as a number.
                            result["weightBudget"] = new JObject
                            {
                                ["used"] = new GameMcpDomainValue(resource.Reading.Quantity),
                                ["maximumCarry"] = new GameMcpDomainValue(resource.Reading.Capacity),
                                ["itemWeight"] = new GameMcpDomainValue(playerCost),
                                ["fits"] = affordable,
                            };
                        }
                    }
                    else costRow["affordable"] = false;
                    costs.Add(costRow);
                }
                equip["usageCosts"] = costs;
            }
            result["equip"] = equip;
            result["unequip"] = decision.MaximumUnequipAmount > 0
                ? new JObject
                {
                    ["available"] = true,
                    ["maximumAmount"] = decision.MaximumUnequipAmount,
                }
                : new JObject { ["available"] = false, ["reasonCode"] = "not_equipped" };
        }
        else
        {
            result["loadoutUnavailable"] = new JObject
            {
                ["reasonCode"] = decision.UnavailableReason.Length == 0
                    ? "loadout_unavailable"
                    : decision.UnavailableReason,
            };
        }
        AddDiscoveryDecision(
            world,
            result,
            equipment.Discovery,
            KnownEntities.WorkshopArtifactCreate.Uuid,
            equipment.EntityId);
        return result.Freeze();
    }

    internal static GameMcpValue ProjectEquipmentType(
        GameWorldState world,
        in WorldEquipmentType equipmentType)
    {
        var result = new JObject
        {
            ["entityId"] = equipmentType.EntityId.ToString("D"),
            ["category"] = "equipment-types",
            ["baseUsage"] = equipmentType.BaseUsage,
            ["masteryLevel"] = new GameMcpDomainValue(equipmentType.MasteryLevel),

            // The game's own answer for this quantity is an integer: GetMaxTypeSlots() is
            // maxTypeSlots.AsInt(), which is GetValue().ToInt() over the very record this row
            // holds. Publishing the raw record put one key on the wire in two JSON types, because
            // the loadout block's typeMaximumSlots reads that method.
            ["maximumSlots"] = Number(equipmentType.MaxTypeSlots.ToInt()),
        };
        AddLevelDecision(world, result, equipmentType.LevelDecision);
        return result.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldEquipment equipment,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = equipment.EntityId.ToString("D"),
                ["created"] = equipment.IsCreated,
                ["equippedCount"] = equipment.EquippedLevel,
            }.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldEquipmentType equipmentTypeRow,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = equipmentTypeRow.EntityId.ToString("D"),
                ["totalLevel"] = equipmentTypeRow.LevelDecision.TotalLevel,
            }.Freeze();
    }

}
#endif
