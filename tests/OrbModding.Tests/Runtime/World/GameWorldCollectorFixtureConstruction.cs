using System;
using System.Linq;

namespace OrbModding.Tests.Runtime.World;

/// <summary>Authors native fixture data; collection and every native answer use the shared stubs.</summary>
internal static class GameWorldCollectorFixtureConstruction
{
    internal static ResourceCostList With(this ResourceCostList list, ResourceSO resource, BigDouble amount)
    {
        list.costs.Add(new ResourceTuple(resource, amount));
        return list;
    }

    internal static ResourceCostList With(this ResourceCostList list, Guid identity, double amount) =>
        list.With(ResourceSO.All.FirstOrDefault(resource => resource.GetGuid() == identity)
            ?? new ResourceSO { Identity = identity }, new BigDouble(amount));

    internal static PlotNodeSO With(this PlotNodeSO plot, PlotNodePhases phase, int quantity)
    {
        if (plot.phaseInfos.All(info => info.phase != phase))
            plot.phaseInfos.Add(new PlotNodePhaseInfo { phase = phase });
        plot.phaseInstances.Add(new PlotNodePhaseInstance { phase = phase, timers = new TimerList { q = quantity } });
        return plot;
    }

    internal static PlotNodeSO Offering(this PlotNodeSO plot, PlotNodeActionSO action)
    {
        plot.availableActions.Add(action);
        return plot;
    }

    internal static PlotNodeSO Running(this PlotNodeSO plot, PlotNodeActionSO action, int quantity = 1, bool engaged = false)
    {
        var instance = new PlotNodeActionInstance(plot, action) { quantity = quantity };
        if (engaged) instance.Engage();
        plot.GetActionInstances().Add(instance);
        return plot;
    }

    internal static PlotNodeSO RunningSomethingUnknown(this PlotNodeSO plot)
    {
        plot.GetActionInstances().Add(new PlotNodeActionInstance { quantity = 1 });
        return plot;
    }

    internal static PlotNodeSO RunningUnresolved(this PlotNodeSO plot, PlotNodeActionSO action)
    {
        var instance = new PlotNodeActionInstance(plot, action)
        {
            refObj = IdObjectRef.Serialized(action.GetGuid()),
        };
        plot.GetActionInstances().Add(instance);
        return plot;
    }
}
