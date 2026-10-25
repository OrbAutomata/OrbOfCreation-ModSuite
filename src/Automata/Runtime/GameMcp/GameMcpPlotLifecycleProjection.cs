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


using static OrbAutomata.GameMcp.GameMcpChallengeProjection;
using static OrbAutomata.GameMcp.GameMcpDecisionReason;
using static OrbAutomata.GameMcp.GameMcpHarvestLifecycleProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpPlotLifecycleProjection
{

    internal static GameMcpValue ProjectHarvestDelta(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed)
    {
        if (state.World is null ||
            !WorldPlotActionLookup.TryFind(state.World.Snapshot.PlotActions,
                command.TargetId, command.SecondaryId, out var current))
            return PostStateNotPublished("this plot action");
        var world = state.World.Snapshot;
        var oldWorld = Before(command);
        var before = oldWorld is null ? 0 : PlotActionQuantity(
            oldWorld.ActionQueueSlots, command.TargetId, command.SecondaryId);
        var after = PlotActionQuantity(
            world.ActionQueueSlots, command.TargetId, command.SecondaryId);
        if (command.Mode == "add_plot_action" ? after <= before : after >= before)
        {
            var observed = Property(committed.Details, "active");
            if (observed is null)
                return PostStateUnavailable(
                    "post_state_not_published",
                    "The press went through, but the plot action changed again before the game " +
                    "redrew it, so its new state cannot be read back.");
            return WithAddedDrain(world, command, new JObject
            {
                ["plot"] = EntityReference(world, command.TargetId),
                ["action"] = EntityReference(world, command.SecondaryId),
                ["active"] = observed,
            });
        }
        return WithAddedDrain(world, command, new JObject
        {
            ["plot"] = EntityReference(world, command.TargetId),
            ["action"] = EntityReference(world, command.SecondaryId),
            ["active"] = new JObject
            {
                ["before"] = before,
                ["after"] = after,
            },
        });
    }

    /// <summary>
    /// An add also says what the action now drains each second, which is the running cost the plot
    /// quantity it paid does not say.
    /// </summary>
    internal static GameMcpValue WithAddedDrain(
        GameWorldState world,
        GameMcpCommand command,
        JObject delta)
    {
        if (command.Mode == "add_plot_action")
        {
            var drains = ProjectPlotActionDrains(world, command.TargetId, command.SecondaryId);
            if (drains.Count > 0) delta["drainCostsPerSecond"] = drains;
        }
        return delta.Freeze();
    }

    internal static int PlotActionQuantity(
        PublicationTable<WorldActionQueueSlot> instances,
        Guid plotId,
        Guid actionId)
    {
        var quantity = 0;
        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            if (!instance.Empty && instance.PlotNodeId == plotId &&
                instance.PlotNodeActionId == actionId)
                quantity += Math.Max(instance.Quantity, 0);
        }
        return quantity;
    }

    /// <summary>
    /// What the action drains each second on every plot that offers it, which is what its diamond's
    /// hover prints. A row names its plot only when the action drains on more than one, because the
    /// plot's own scaling is a factor of the number.
    /// </summary>
    internal static GameMcpValue WithPlotNodeActionDrains(
        GameWorldState world,
        in WorldPlotNodeAction action,
        GameMcpProjectedDomainValue reflected)
    {
        var plots = new List<Guid>();
        foreach (var drain in world.PlotActionDrains.AsSpan())
        {
            if (drain.PlotNodeActionId == action.PlotNodeActionId && !plots.Contains(drain.PlotNodeId))
                plots.Add(drain.PlotNodeId);
        }
        if (plots.Count == 0) return reflected;

        var drains = new JArray();
        foreach (var plotId in plots)
            AppendPlotActionDrains(world, plotId, action.PlotNodeActionId, plots.Count > 1, drains);
        return reflected.With(new JObject { ["drainCostsPerSecond"] = drains }.Freeze());
    }

    /// <summary>
    /// One plot action's drain on one plot, per second, as the diamond's hover prints it. The game
    /// takes it every second the action runs, so it is a running cost beside the one-off plot
    /// quantity the add decision prices.
    /// </summary>
    internal static JArray ProjectPlotActionDrains(
        GameWorldState world,
        Guid plotNodeId,
        Guid plotNodeActionId)
    {
        var result = new JArray();
        AppendPlotActionDrains(world, plotNodeId, plotNodeActionId, namePlot: false, result);
        return result;
    }

    internal static void AppendPlotActionDrains(
        GameWorldState world,
        Guid plotNodeId,
        Guid plotNodeActionId,
        bool namePlot,
        JArray result)
    {
        foreach (var drain in WorldPlotActionDrainLookup.For(
                     world.PlotActionDrains, plotNodeId, plotNodeActionId))
        {
            var price = PlayerFacingCost(world, drain.ResourceId, drain.Amount);
            var row = new JObject
            {
                ["resourceId"] = drain.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(price),
            };
            if (TryHarvestSpendableAmount(world, drain.ResourceId, out var amount))
            {
                (row["cost"], row["spendableAmount"]) = GameMcpNumberFormatter.CellsApart(price, amount);
                row["affordable"] = CanAfford(world, drain.ResourceId, drain.Amount, amount);
            }
            if (namePlot) row["plot"] = EntityReference(world, plotNodeId);
            result.Add(row);
        }
    }
    internal static GameMcpValue List(
        GameWorldState world,
        in WorldPlotAction plotAction,
        int nestedOffset,
        int nestedLimit)
    {
        return ProjectPlotAction(world, in plotAction, asRow: true);
    }

}
#endif
