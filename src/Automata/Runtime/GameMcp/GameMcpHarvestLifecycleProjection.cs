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
using static OrbAutomata.GameMcp.GameMcpPlotLifecycleProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpHarvestLifecycleProjection
{
    internal static GameMcpValue Project(in HarvestLifecycleSubmission submission)
    {
        if (submission.Outcome.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested harvest-list transition";

        // A refusal whose sentence names a ceiling carries that same ceiling as a number, read from
        // the admission capture the sentence was written from.
        if (submission.MaximumAmount >= 0) result["maximumAmount"] = submission.MaximumAmount;
        return result.Freeze();
    }

    /// <summary>
    /// Whether the harvest screen lists this node. <c>UIPlotNode.IsVisible()</c> is
    /// <c>PlotNodeSO.IsVisible()</c>, which is the <c>visible</c> field the game latches from the
    /// node's own visibility prerequisite. Mastery grows without a ceiling, so there is no third
    /// word.
    /// </summary>
    internal static string PlotNodeState(in WorldPlotNode plot) =>
        plot.Reading.Visible ? GameMcpListColumns.Available : GameMcpListColumns.Locked;

    internal static GameMcpValue ProjectHarvestLifecycleDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var oldWorld = Before(command);
        if (command.Mode is "add_element" or "remove_element")
        {
            if (!TryFindHarvestElementControl(
                    world.HarvestElementControls, command.TargetId, out var current))
                return PostStateNotPublished("this harvest element");
            WorldHarvestElementControl previous = default;
            var hadBefore = oldWorld is not null && TryFindHarvestElementControl(
                oldWorld.HarvestElementControls, command.TargetId, out previous);
            var result = new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                ["active"] = new JObject
                {
                    ["before"] = hadBefore ? previous.Active : (int?)null,
                    ["after"] = current.Active,
                },
            };
            return result.Freeze();
        }

        if (!TryFindHarvestActionControl(world.HarvestActionControls,
                command.TargetId, command.SecondaryId, out var action))
            return PostStateNotPublished("this harvest element's action");
        WorldHarvestActionControl previousAction = default;
        var hadActionBefore = oldWorld is not null && TryFindHarvestActionControl(
            oldWorld.HarvestActionControls, command.TargetId, command.SecondaryId,
            out previousAction);

        // The count belongs to the action, so it is said under the action — the same place the
        // element's own row says it, in `actions[]`. Flat beside the element handle it read as the
        // element's count: `add_element` and `add_element_action` both answered `active: 0 -> 1`,
        // and neither response said which of the two things on it had moved.
        return new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["action"] = new JObject
            {
                ["uuid"] = command.SecondaryId.ToString("D"),
                ["active"] = new JObject
                {
                    ["before"] = hadActionBefore ? previousAction.Active : (int?)null,
                    ["after"] = action.Active,
                },
            },
        }.Freeze();
    }

    /// <summary>
    /// One harvest node's own page, in the words every other category's page uses.
    /// </summary>
    /// <remarks>
    /// Every fact is the one the list row already computes; only the words are new here, and
    /// <c>visible</c> is folded into <c>state</c> exactly as it is on that row, because
    /// <c>PlotNodeSO.IsVisible()</c> is what both are read from.
    /// </remarks>
    internal static GameMcpValue ProjectPlotNode(in WorldPlotNode plot) =>
        new JObject
        {
            ["entityId"] = plot.EntityId.ToString("D"),
            ["category"] = "plot-nodes",
            ["state"] = PlotNodeState(in plot),
            ["masteryLevel"] = plot.Reading.MasteryLevel,
            ["masteryXp"] = new GameMcpDomainValue(plot.Reading.MasteryXp),

            // Three counts the harvest screen shows side by side: how many of the node exist, how
            // many are sitting idle right now, and how many an action may still be started on.
            ["quantity"] = plot.Reading.TotalQuantity,
            ["idleQuantity"] = plot.Reading.IdleQuantity,
            ["availableQuantity"] = plot.RemainingTotalQuantity,
            ["availableIdleQuantity"] = plot.RemainingQuantity,
            ["currentTime"] = new GameMcpDomainValue(plot.Reading.CurrentTime),
        }.Freeze();

    internal static GameMcpValue ProjectHarvestElement(
        GameWorldState world,
        in WorldHarvestElement element)
    {
        var result = new JObject
        {
            ["entityId"] = element.EntityId.ToString("D"),
            ["category"] = "agromancy-elements",
            ["masteryLevel"] = element.MasteryLevel,
            ["masteryXp"] = new GameMcpDomainValue(element.MasteryXp),
        };
        for (var index = 0; index < world.HarvestResources.Count; index++)
        {
            var harvestResource = world.HarvestResources[index];
            if (harvestResource.ElementId != element.EntityId) continue;
            var resource = harvestResource.Resource;
            var output = new JObject
            {
                ["amount"] = new GameMcpDomainValue(
                    WorldResourceCoordinate.DisplayAmount(in resource)),
                ["netRatePerSecond"] = new GameMcpDomainValue(resource.TrueRate),
            };
            if (resource.IsCapped)
            {
                output["capacity"] = new GameMcpDomainValue(resource.Reading.Capacity);
                output["atCapacity"] = resource.IsAtCapacity;
            }
            result["output"] = output;
            break;
        }
        if (!TryFindHarvestElementControl(
                world.HarvestElementControls, element.EntityId, out var control))
            return result.Freeze();
        result["active"] = control.Active;
        result["addElement"] = ProjectHarvestElementDecision(world, in control);
        result["removeElement"] = RemoveElementDecision(in control);

        var actions = new JArray();
        for (var index = 0; index < world.HarvestActionControls.Count; index++)
        {
            var action = world.HarvestActionControls[index];
            if (action.ElementId != element.EntityId || !action.Visible) continue;
            var row = new JObject
            {
                ["uuid"] = action.ActionId.ToString("D"),
                ["active"] = action.Active,
                ["maximumAmount"] = action.Maximum,
                ["add"] = HarvestActionAddDecision(world, in action),
                ["remove"] = HarvestActionRemoveDecision(in action),
            };
            actions.Add(row);
        }
        if (actions.Count > 0) result["actions"] = actions;
        return result.Freeze();
    }

    internal static GameMcpValue ProjectPlotAction(
        GameWorldState world,
        in WorldPlotAction action) =>
        ProjectPlotAction(world, in action, asRow: false);

    /// <summary>
    /// One plot-and-action pair, as a detail block or as a row of the table.
    /// </summary>
    /// <remarks>
    /// The two decisions are the same decisions either way; what differs is how much room the
    /// answer has. A detail reader asked about this pair and gets the whole verdict — the sentence,
    /// the repair to call, the remaining-instance count. A row of twenty gets the word, because the
    /// alternative is the same paragraph twenty times in a column three characters wide.
    /// </remarks>
    internal static GameMcpValue ProjectPlotAction(
        GameWorldState world,
        in WorldPlotAction action,
        bool asRow)
    {
        var active = PlotActionQuantity(
            world.ActionQueueSlots, action.PlotNodeId, action.PlotNodeActionId);
        var add = ProjectPlotActionDecision(world, in action, active);
        var remove = PlotActionRemoveDecision(active);
        var result = new JObject
        {
            ["plot"] = EntityReference(world, action.PlotNodeId),
            ["action"] = EntityReference(world, action.PlotNodeActionId),
            ["active"] = active,
            ["add"] = asRow ? DecisionWord(add) : (object)add,
            ["remove"] = asRow ? DecisionWord(remove) : (object)remove,
        };
        if (!asRow)
        {
            var drains = ProjectPlotActionDrains(world, action.PlotNodeId, action.PlotNodeActionId);
            if (drains.Count > 0) result["drainCostsPerSecond"] = drains;
        }
        return result.Freeze();
    }

    /// <summary>
    /// A decision block as the one word a cell has room for: <c>yes</c>, or what stands in the way.
    /// </summary>
    internal static object DecisionWord(JObject decision)
    {
        if (decision["available"] is true) return GameMcpListColumns.Yes;
        var code = (decision["reasonCode"] ?? decision["code"]) as string;
        return string.IsNullOrEmpty(code)
            ? GameMcpListColumns.No
            : GameMcpListColumns.Word(code!);
    }

    /// <summary>
    /// Nothing running is not a refusal. Left bare, the no-bare-no backstop stamped this
    /// <c>native_rejected</c> and told a caller the game had refused something it never asked for;
    /// every sibling names its own axis, and this is the same axis they name.
    /// </summary>
    internal static JObject PlotActionRemoveDecision(int active)
    {
        var result = new JObject { ["available"] = active > 0 };
        if (active <= 0) result["reasonCode"] = "not_active";
        return result;
    }

    internal static GameMcpValue ProjectAgromancyProcessing(
        GameWorldState world,
        in WorldActionQueueSlot slot)
    {
        // What fills the slot answers it. Whether the occupant is under way rather than merely
        // present — the game's `IsEngaged()` — is not a column. It is the same fact as a spell's
        // `casting`: a flag the game turns over on its own between two reads of one scan, so a page
        // of it plans nothing. What the slot holds and how much of it are the standing facts, and
        // they are here.
        var queueFound = WorldLookup.TryFind(world.ActionQueues, slot.QueueId, out var queue);
        var result = new JObject
        {
            ["slot"] = GameMcpSlotNumbering.Wire(slot.Index),
            ["capacity"] = queueFound ? queue.SlotCount : (object)GameMcpListColumns.Unreadable,
            ["used"] = queueFound ? queue.UsedSlots : (object)GameMcpListColumns.Unreadable,
            ["plot"] = slot.Empty
                ? GameMcpListColumns.Empty
                : EntityReference(world, slot.PlotNodeId),
            ["action"] = slot.Empty
                ? GameMcpListColumns.Empty
                : EntityReference(world, slot.PlotNodeActionId),
            ["amount"] = slot.Empty ? (object)GameMcpListColumns.Empty : slot.Quantity,
        };
        return result.Freeze();
    }

    internal static JObject ProjectPlotActionDecision(
        GameWorldState world,
        in WorldPlotAction action,
        int active)
    {
        var result = new JObject();
        var reason = string.Empty;
        var available = true;
        if (action.Reading.OfferedCount != 1)
        {
            available = false;
            reason = action.Reading.OfferedCount == 0
                ? "not_offered"
                : "ambiguous_offer";
        }
        else if (action.Reading.PrerequisiteEvidence !=
                 PlotActionPrerequisiteEvidence.NativeLatchedTrue)
        {
            // Not "unavailable": the game latches this prerequisite only when the action is
            // attempted, so the read itself is what is missing. Said with the suite's one word for
            // a fact it cannot supply, next to the one word every other decision uses for the fact
            // it can.
            result["status"] = "not_available";
            result["code"] = "prerequisite_unverified";
            result["reason"] =
                "The game only checks this action's prerequisite when the action is started, " +
                "so whether it can be queued cannot be read ahead of time.";
            result["checkWith"] = "game_agromancy add_plot_action";
            return result;
        }
        else if (!action.ElementCostKnown)
        {
            available = false;
            reason = "cost_unavailable";
        }
        else if (!action.HasEnoughForOneInstance || action.MaximumRemainingInstances <= 0)
        {
            available = false;
            reason = "plot_quantity_insufficient";
        }
        else if (active == 0)
        {
            reason = !WorldLookup.TryFind(world.ActionQueues,
                    KnownEntities.ActivePlotNodeActions.Uuid, out var queue)
                ? "queue_not_published"
                : !queue.Consistent
                    ? "queue_reading_inconsistent"
                    : !queue.HasEmptySlot ? "plot_action_list_full" : string.Empty;
            available = reason.Length == 0;
        }
        result["available"] = available;
        if (!available)
        {
            result["reasonCode"] = reason;
            return result;
        }
        // The game's own remaining-instance count, and nothing else. Folding the tool schema's
        // per-call ceiling in here produced a third number that was neither bound: with the list
        // nearly full it under-reported what the game admits, and the schema's ceiling is a
        // per-call limit rather than a running budget in the first place.
        result["maximumAmount"] = action.MaximumRemainingInstances;
        result["plotQuantityCost"] = action.ElementCost;
        return result;
    }

    internal static JObject ProjectHarvestElementDecision(
        GameWorldState world,
        in WorldHarvestElementControl control)
    {
        var result = new JObject
        {
            ["available"] = control.AddAvailable,
        };
        if (!control.AddAvailable)
        {
            result["reasonCode"] = !control.Visible
                ? "element_unavailable"
                : control.MaximumAdditional <= 0
                    ? "capacity_exhausted"
                    : !control.ListSpaceAvailable
                        ? "harvest_list_full"
                        : "unaffordable";
            return result;
        }
        result["maximumAmount"] = control.MaximumAdditional;
        result["affordable"] = true;
        var costs = ProjectHarvestLifecycleCosts(
            world, control.ElementId, Guid.Empty,
            WorldHarvestLifecycleCostKind.ElementUsage);
        if (costs.Count > 0) result["costs"] = costs;
        return result;
    }

    /// <summary>
    /// A false availability is a decision, not a flag. `addAvailable: false` beside nothing left a
    /// caller with no axis to branch on and no way to tell a temporary no from a permanent one,
    /// which is the stall the sentence rule exists to prevent.
    /// </summary>
    internal static JObject HarvestActionAddDecision(
        GameWorldState world,
        in WorldHarvestActionControl action)
    {
        var result = new JObject { ["available"] = action.AddAvailable };
        if (!action.AddAvailable)
        {
            result["reasonCode"] = action.Active >= action.Maximum
                ? "capacity_exhausted"
                : "harvest_list_full";
            return result;
        }
        var costs = ProjectHarvestLifecycleCosts(
            world, action.ElementId, action.ActionId,
            WorldHarvestLifecycleCostKind.NextActionDrain);
        if (costs.Count > 0) result["nextDrain"] = costs;
        return result;
    }

    internal static JObject HarvestActionRemoveDecision(in WorldHarvestActionControl action)
    {
        var result = new JObject { ["available"] = action.RemoveAvailable };
        if (!action.RemoveAvailable) result["reasonCode"] = "not_active";
        return result;
    }

    internal static JObject RemoveElementDecision(in WorldHarvestElementControl control)
    {
        var result = new JObject { ["available"] = control.RemoveAvailable };
        if (!control.RemoveAvailable)
            result["reasonCode"] = control.Active > 0 ? "not_available" : "not_active";
        return result;
    }

    internal static JArray ProjectHarvestLifecycleCosts(
        GameWorldState world,
        Guid elementId,
        Guid actionId,
        WorldHarvestLifecycleCostKind kind)
    {
        var result = new JArray();
        for (var index = 0; index < world.HarvestLifecycleCosts.Count; index++)
        {
            var cost = world.HarvestLifecycleCosts[index];
            if (cost.ElementId != elementId || cost.ActionId != actionId || cost.Kind != kind)
                continue;
            var price = PlayerFacingCost(world, cost.ResourceId, cost.Amount);
            var row = new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(price),
            };
            if (TryHarvestSpendableAmount(world, cost.ResourceId, out var amount))
            {
                (row["cost"], row["spendableAmount"]) = GameMcpNumberFormatter.CellsApart(price, amount);
                row["affordable"] = CanAfford(
                    world, cost.ResourceId, cost.Amount, amount);
            }
            result.Add(row);
        }
        return result;
    }

    internal static bool TryHarvestSpendableAmount(
        GameWorldState world,
        Guid resourceId,
        out BigDouble amount)
    {
        if (WorldLookup.TryFind(world.Resources, resourceId, out var resource))
        {
            amount = SpendableAmount(world, resourceId, resource.Reading.Quantity);
            return true;
        }
        if (WorldLookup.TryFind(world.HarvestResources, resourceId, out var harvestResource))
        {
            var value = harvestResource.Resource;
            amount = WorldResourceCoordinate.SpendableAmount(in value);
            return true;
        }
        amount = BigDouble.Zero;
        return false;
    }

    internal static bool TryFindHarvestElementControl(
        PublicationTable<WorldHarvestElementControl> values,
        Guid elementId,
        out WorldHarvestElementControl result)
    {
        for (var index = 0; index < values.Count; index++)
            if (values[index].ElementId == elementId)
            {
                result = values[index];
                return true;
            }
        result = default;
        return false;
    }

    internal static bool TryFindHarvestActionControl(
        PublicationTable<WorldHarvestActionControl> values,
        Guid elementId,
        Guid actionId,
        out WorldHarvestActionControl result)
    {
        for (var index = 0; index < values.Count; index++)
            if (values[index].ElementId == elementId && values[index].ActionId == actionId)
            {
                result = values[index];
                return true;
            }
        result = default;
        return false;
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldPlotNode plot,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = plot.EntityId.ToString("D"),
                ["state"] = PlotNodeState(in plot),
                ["masteryLevel"] = plot.Reading.MasteryLevel,
                ["quantity"] = plot.Reading.TotalQuantity,
                ["availableQuantity"] = plot.RemainingTotalQuantity,
            }.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldActionQueueSlot processingSlot,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectAgromancyProcessing(world, in processingSlot);
    }

}
#endif
