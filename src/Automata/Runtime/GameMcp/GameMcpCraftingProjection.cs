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

internal static class GameMcpCraftingProjection
{
    /// <summary>
    /// Queues <c>amount</c> crafts of the recipe, or with <c>max</c> what the screen's Mx press
    /// would, through the one craft GameAction and answers with how many crafts its own sentinel
    /// verified. A refusal after crafts landed keeps them and names why the rest stopped; a
    /// submission that faulted answers as the fault.
    /// </summary>
    internal static GameMcpCommandResult Press(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration,
        bool max,
        Func<string> shortfall,
        Func<int, CraftingPlayerSubmission> press)
    {
        var asked = new BigDouble(command.Amount, 0);
        var landed = BigDouble.Zero;
        var completed = false;
        ServiceActionResult verified = default;
        string? remainder = null;
        while (max ? landed <= BigDouble.Zero : landed < asked)
        {
            var submission = press(command.Amount - (int)landed.ToDouble());
            if (!submission.Outcome.Verified)
            {
                if (landed <= BigDouble.Zero ||
                    submission.Outcome.CallOutcome.MutationAttempts > 0)
                {
                    var stopped = submission.Outcome.ToResult();
                    return GameMcpCommandResult.FromAction(
                        stopped.WithReason(landed <= BigDouble.Zero
                            ? FirstRefusal(in submission, shortfall)
                            : GameMcpNumberFormatter.Format(landed) + " of " +
                              GameMcpNumberFormatter.Format(asked) + " asked went through " +
                              "before this craft. " + submission.Outcome.Reason),
                        lifecycle,
                        configurationGeneration,
                        Project(in submission));
                }
                remainder = submission.Outcome.Reason;
                break;
            }
            verified = submission.Outcome.ToResult();
            completed = submission.Postcondition is
                CraftingPlayerPostcondition.DirectEffectAdvanced or
                CraftingPlayerPostcondition.InstantCompleted;
            landed += submission.Crafted;
        }
        return GameMcpCommandResult.FromAction(
            in verified,
            lifecycle,
            configurationGeneration,
            details: new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                [completed ? "completed" : "queued"] = remainder is null
                    ? (object)new GameMcpDomainValue(landed)
                    : GameMcpNumberFormatter.Format(landed) + " of " +
                      GameMcpNumberFormatter.Format(asked) + " asked; the rest stopped: " +
                      remainder,
            }.Freeze());
    }

    private static string FirstRefusal(
        in CraftingPlayerSubmission submission,
        Func<string> shortfall)
    {
        if (submission.Outcome.Code != CraftingPlayerActionResultCodes.Unaffordable)
            return submission.Outcome.Reason;
        var named = shortfall();
        return named.Length == 0 ? submission.Outcome.Reason : named;
    }

    internal static GameMcpValue Project(in CraftingPlayerSubmission submission)
    {
        if (submission.Outcome.Verified || submission.Outcome.CallOutcome.MutationAttempts == 0)
            return new JObject().Freeze();
        return new JObject
        {
            ["missingOutcome"] = "requested craft completion",
        }.Freeze();
    }

    internal static GameMcpValue Project(
        in CraftingInstanceLifecycleSubmission submission)
    {
        if (submission.Outcome.Verified || submission.Outcome.CallOutcome.MutationAttempts == 0)
            return new JObject().Freeze();
        var failure = new JObject
        {
            ["missingOutcome"] = "requested crafting-instance transition",
        };
        if (submission.SideEffect.Observed)
        {
            // The action observes GetAutomationQuantity(), which counts repetitions rather than
            // the badge's amount, so it is published under that name.
            failure["observed"] = new JObject
            {
                ["repetitions"] = new JObject
                {
                    ["before"] = submission.SideEffect.AutomationBefore,
                    ["after"] = submission.SideEffect.AutomationAfter,
                },
            };
        }
        return failure.Freeze();
    }

    internal static GameMcpValue ProjectCraftingQueueEntry(
        in WorldCraftingQueueEntry entry)
    {
        // Only an automated entry repeats, so the repetition count is where "this one is manual"
        // belongs; a separate `automatic` flag said the same bit a second time.
        var result = new JObject
        {
            ["queueId"] = Named(entry.QueueId),
            ["slot"] = GameMcpSlotNumbering.Wire(entry.Slot),
            ["recipeId"] = Named(entry.RecipeId),
            ["amount"] = new GameMcpDomainValue(entry.Amount),
            ["repetitions"] = entry.Automatic
                ? entry.Repetitions
                : (object)GameMcpListColumns.Manual,
        };
        return result.Freeze();
    }

    internal static GameMcpValue ProjectCraftingDelta(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed)
    {
        if (command.Mode == "craft")
            return committed.Details ?? throw new InvalidOperationException(
                "A committed craft carries the count its presses verified.");
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var oldWorld = Before(command);
        WorldCraftingDecision previous = default;
        var hasBefore = oldWorld is not null && WorldCraftingDecisionLookup.TryFind(
            oldWorld.CraftingDecisions, command.TargetId, out previous);
        if (!WorldCraftingDecisionLookup.TryFind(
                state.World.Snapshot.CraftingDecisions,
                command.TargetId,
                out var current))
            return PostStateNotPublished(
                command.TargetId, command.Amount, "this crafting recipe");
        if (command.Mode is "automate" or "cancel_automation")
        {
            // The screen's number, not the repetition count behind it: one cancel on a doubled
            // entry moves the badge 8 to 4 while the repetitions move 4 to 3. A side whose queue
            // entry was not collected while the recipe still repeats says so rather than reporting
            // the zero the lookup returned.
            var result = new JObject { ["uuid"] = command.TargetId.ToString("D") };
            var priorBadge = BigDouble.Zero;
            var hasAfter = TryAutomationBadge(state.World.Snapshot, current.AutomationQueueId,
                command.TargetId, current.AutomationRepetitions, out var afterBadge);
            var hasPrior = !hasBefore || oldWorld is null ||
                TryAutomationBadge(oldWorld, previous.AutomationQueueId, command.TargetId,
                    previous.AutomationRepetitions, out priorBadge);
            if (hasAfter && hasPrior)
                result["amount"] = new JObject
                {
                    ["before"] = hasBefore && oldWorld is not null
                        ? new GameMcpDomainValue(priorBadge)
                        : null,
                    ["after"] = new GameMcpDomainValue(afterBadge),
                };
            else
                result["amountUnavailable"] = AutomationAmountUnavailable();
            return result.Freeze();
        }
        return Change(
            command.TargetId,
            hasBefore ? new GameMcpDomainValue(previous.QueuedAmount) : null,
            new GameMcpDomainValue(current.QueuedAmount),
            "queued");
    }

    internal static string CraftShortfallReason(GameWorldState world, Guid recipeId)
    {
        if (!WorldCraftingDecisionLookup.TryFindCostRange(
                world.CraftingDecisionCosts, recipeId, out var start, out var count))
            return string.Empty;
        var rows = new List<(string, BigDouble, BigDouble)>();
        for (var index = start; index < start + count; index++)
        {
            var value = world.CraftingDecisionCosts[index].Cost;
            if (CanAfford(world, value.ResourceId, value.Price, value.Held)) continue;
            var identity = EntityIdentityFormatter.Describe(
                value.ResourceId, world.EntityIdentities);
            rows.Add((
                identity.HasName ? identity.Name : value.ResourceId.ToString("D"),
                PlayerFacingCost(world, value.ResourceId, value.Price),
                SpendableAmount(world, value.ResourceId, value.Held)));
        }
        return GameMcpDecisionReason.Shortfall(rows);
    }

    /// <summary>
    /// What occupies a queue right now, so a row that reports a queue is full also reports what is
    /// filling it. The collection is always present; a queue that was read and is empty is empty.
    /// </summary>
    internal static GameMcpValue QueueSlots(GameWorldState world, Guid queueId)
    {
        var slots = new JArray();
        if (WorldCraftingDecisionLookup.TryFindQueueRange(
                world.CraftingQueueEntries, queueId, out var start, out var count))
            for (var index = 0; index < count; index++)
                slots.Add(ProjectCraftingQueueEntry(world.CraftingQueueEntries[start + index]));
        return slots.Freeze();
    }

    /// <summary>
    /// What the automation strip's badge shows for one recipe. <c>UICraftingInstance</c> draws the
    /// queued instance's own <c>quantity</c>, while the entry's repetition count is the exponent
    /// behind it — <c>CraftingInstance.SetAutomationQuantity(n)</c> stores
    /// <c>quantity = 2^(n-1)</c>. The two coincide at 1 and 2 and diverge exponentially after that,
    /// so only the badge's number is published as <c>amount</c>.
    /// </summary>
    /// <remarks>
    /// A miss is only "nothing is automated" while the repetition count agrees. A recipe the game
    /// says is repeating some number of times has an entry drawing that badge, so failing to find
    /// it means the queue-entry collection did not arrive — a different answer from zero, and the
    /// caller is told which one it got.
    /// </remarks>
    internal static bool TryAutomationAmount(
        GameWorldState world,
        Guid automationQueueId,
        Guid recipeId,
        out BigDouble amount)
    {
        amount = BigDouble.Zero;
        if (!WorldCraftingDecisionLookup.TryFindQueueRange(
                world.CraftingQueueEntries, automationQueueId, out var start, out var count))
            return false;
        for (var index = 0; index < count; index++)
        {
            var entry = world.CraftingQueueEntries[start + index];
            if (entry.RecipeId == recipeId)
            {
                amount = entry.Amount;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The badge amount, or false when the entry that draws it is missing from a recipe the game
    /// says is repeating. Zero repetitions need no entry, and zero is that recipe's honest badge.
    /// </summary>
    internal static bool TryAutomationBadge(
        GameWorldState world,
        Guid automationQueueId,
        Guid recipeId,
        int repetitions,
        out BigDouble amount) =>
        TryAutomationAmount(world, automationQueueId, recipeId, out amount) || repetitions <= 0;

    internal static JObject AutomationAmountUnavailable() => new()
    {
        ["reasonCode"] = "automation_entry_not_published",
        ["reason"] = "the recipe repeats but no queue entry for it was collected, " +
            "so the badge amount behind those repetitions is unknown",
    };

    internal static GameMcpValue ProjectCraftingRecipe(
        GameWorldState world,
        in WorldCraftingRecipe recipe)
    {
        var reading = recipe.Reading;
        var hasDecision = WorldCraftingDecisionLookup.TryFind(
            world.CraftingDecisions,
            recipe.EntityId,
            out var decision);
        var canStart = reading.Visible && reading.OutputWithinCapacity &&
            (hasDecision ? decision.CanStart : reading.CanBuyAtStartingQuantity);
        var result = new JObject
        {
            ["entityId"] = recipe.EntityId.ToString("D"),
            ["category"] = "crafting-recipes",
            ["visible"] = reading.Visible,
            ["startingAmount"] = new GameMcpDomainValue(reading.StartingQuantity),
            ["craftTimeSeconds"] = reading.TimeToComplete,
            ["canStart"] = canStart,
        };
        if (reading.UseQuantityAsLevel) result["amountActsAsLevel"] = true;
        var blockers = new JArray();
        if (!reading.Visible) blockers.Add("hidden_or_undiscovered");
        if (hasDecision)
        {
            result["execution"] = CraftingPipeline(decision.Pipeline);
            result["purchaseAmount"] = new GameMcpDomainValue(decision.PurchaseAmount);
            if (decision.Pipeline is WorldCraftingPipeline.QueueStack or
                WorldCraftingPipeline.QueueNew)
            {
                result["queuedAmount"] = new GameMcpDomainValue(decision.QueuedAmount);
                result["queue"] = new JObject
                {
                    ["queueId"] = decision.QueueId.ToString("D"),
                    ["used"] = decision.QueueUsed,
                    ["maximum"] = decision.QueueMaximum,
                    ["slots"] = QueueSlots(world, decision.QueueId),
                };
                if (decision.CanCancelManual)
                    result["cancelManual"] = new JObject { ["available"] = true };
                var automation = new JObject
                {
                    ["queueId"] = decision.AutomationQueueId.ToString("D"),
                };
                if (TryAutomationBadge(world, decision.AutomationQueueId, recipe.EntityId,
                        decision.AutomationRepetitions, out var badge))
                    automation["amount"] = new GameMcpDomainValue(badge);
                else
                    automation["amountUnavailable"] = AutomationAmountUnavailable();
                automation["repetitions"] = decision.AutomationRepetitions;
                automation["available"] = decision.CanAutomate;
                automation["slots"] = QueueSlots(world, decision.AutomationQueueId);
                if (decision.CanCancelAutomation) automation["canCancel"] = true;
                if (decision.AutomationMaximum > 0)
                {
                    automation["used"] = decision.AutomationUsed;
                    automation["maximum"] = decision.AutomationMaximum;
                }
                if (!decision.CanAutomate)
                {
                    automation["reasonCode"] = decision.AutomationReasonCode;
                    automation["reason"] = GameMcpDecisionReason.For(decision.AutomationReasonCode);
                }
                result["automation"] = automation;
            }
            if (!decision.CanStart && decision.ReasonCode.Length > 0 &&
                decision.ReasonCode != "hidden_or_undiscovered" &&
                decision.ReasonCode != "output_capacity_blocked")
                blockers.Add(decision.ReasonCode);
            if (WorldCraftingDecisionLookup.TryFindCostRange(
                    world.CraftingDecisionCosts,
                    recipe.EntityId,
                    out var costStart,
                    out var costCount))
            {
                var exactCosts = new JArray();
                for (var index = 0; index < costCount; index++)
                {
                    var cost = world.CraftingDecisionCosts[costStart + index].Cost;
                    var playerCost = PlayerFacingCost(world, cost.ResourceId, cost.Price);
                    var spendable = SpendableAmount(world, cost.ResourceId, cost.Held);
                    exactCosts.Add(new JObject
                    {
                        ["resourceId"] = cost.ResourceId.ToString("D"),
                        ["cost"] = new GameMcpDomainValue(playerCost),
                        ["spendableAmount"] = new GameMcpDomainValue(spendable),
                        ["affordable"] = CanAfford(
                            world, cost.ResourceId, cost.Price, cost.Held),
                    });
                }
                result["nextCosts"] = exactCosts;
            }
        }
        else if (!reading.CanBuyAtStartingQuantity) blockers.Add("native_purchase_refused");
        if (!reading.OutputWithinCapacity) blockers.Add("output_capacity_blocked");
        if (blockers.Count > 0) result["blockers"] = blockers;
        if (recipe.Types.Count > 0)
        {
            var types = new JArray();
            for (var index = 0; index < recipe.Types.Count; index++)
                types.Add(recipe.Types[index].TypeId.ToString("D"));
            result["types"] = types;
        }
        if (recipe.Resources.Count > 0)
        {
            var inputs = new JArray();
            var outputs = new JArray();
            for (var index = 0; index < recipe.Resources.Count; index++)
            {
                var resource = recipe.Resources[index];
                var projected = new JObject
                {
                    ["resourceId"] = resource.ResourceId.ToString("D"),
                };
                if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    projected["cost"] = new GameMcpDomainValue(
                        PlayerFacingCost(world, resource.ResourceId, resource.Amount));
                else
                    projected["yield"] = new GameMcpDomainValue(resource.Amount);
                if (resource.ResourceStateAvailable)
                {
                    var spendable = SpendableAmount(
                        world, resource.ResourceId, resource.TrueQuantity);
                    projected["spendableAmount"] = new GameMcpDomainValue(spendable);
                    if (resource.IsCapped)
                        projected["capacity"] = new GameMcpDomainValue(resource.Capacity);
                    if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    {
                        (projected["cost"], projected["spendableAmount"]) = GameMcpNumberFormatter.CellsApart(
                            PlayerFacingCost(world, resource.ResourceId, resource.Amount),
                            spendable);
                        projected["affordable"] = CanAfford(
                            world, resource.ResourceId, resource.Amount, resource.TrueQuantity);
                    }
                }
                else if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    projected["affordable"] = false;
                if (resource.BandwidthResource) projected["bandwidth"] = true;
                if (!resource.Visible) projected["hidden"] = true;
                if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    inputs.Add(projected);
                else
                    outputs.Add(projected);
            }
            if (inputs.Count > 0) result["inputs"] = inputs;
            if (outputs.Count > 0) result["outputs"] = outputs;
        }
        if (recipe.ConsumableOutputs.Count > 0)
        {
            var consumables = new JArray();
            for (var index = 0; index < recipe.ConsumableOutputs.Count; index++)
                consumables.Add(recipe.ConsumableOutputs[index].ConsumableId.ToString("D"));
            result["consumableOutputs"] = consumables;
        }
        var drainBlockers = new JArray();
        for (var index = 0; index < recipe.DrainBlocks.Count; index++)
        {
            var drain = recipe.DrainBlocks[index];
            if (!drain.Blocked) continue;
            drainBlockers.Add(new JObject
            {
                ["reasonCode"] = "engagement_drain_limited",
                ["availableRatio"] = new GameMcpDomainValue(drain.NecessaryRatio),
            });
        }
        if (drainBlockers.Count > 0) result["drainBlockers"] = drainBlockers;
        return result.Freeze();
    }

    internal static string CraftingPipeline(WorldCraftingPipeline pipeline) => pipeline switch
    {
        WorldCraftingPipeline.Direct => "direct",
        WorldCraftingPipeline.QueueStack => "queue_stack",
        WorldCraftingPipeline.QueueNew => "queue_new",
        _ => "unknown",
    };

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldCraftingQueueEntry queueEntry,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectCraftingQueueEntry(in queueEntry);
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldCraftingRecipe listedRecipe,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = listedRecipe.EntityId.ToString("D"),
                ["startingAmount"] =
                    new GameMcpDomainValue(listedRecipe.Reading.StartingQuantity),
            }.Freeze();
    }

}
#endif
