using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>The plot-action queue: one instance, reached by uuid rather than through a registry.</summary>
public sealed class PlotNodeActionInstanceListVariable : EmptyTypeListVariable<PlotNodeActionInstance>
{
    public bool SuppressMutation { get; set; }

    /// <summary>Adds of this one action do nothing; every other action still lands.</summary>
    public PlotNodeActionSO? SuppressMutationOf { get; set; }

    /// <summary>The add lands, then the game throws and the list stops reading.</summary>
    public bool BreakAfterMutation { get; set; }

    /// <summary>Adds of this one action throw an exception the suite does not expect, and nothing lands.</summary>
    public PlotNodeActionSO? EscapeFromAddOf { get; set; }
    private bool brokenReads;

    protected override bool IsFilledElement(PlotNodeActionInstance element) =>
        element is not null && !element.IsEmpty();

    public PlotNodeActionInstance? FindInstance(PlotNodeActionInstance prototype)
    {
        if (brokenReads) throw new InvalidOperationException("injected failure reading the plot actions");
        foreach (var candidate in value)
        {
            if (candidate is not null &&
                ReferenceEquals(candidate.GetElement(), prototype.GetElement()) &&
                ReferenceEquals(candidate.GetAction(), prototype.GetAction()))
                return candidate;
        }
        return null;
    }

    public bool HasInstance(PlotNodeActionInstance prototype) => FindInstance(prototype) is not null;

    public void AddInstance(PlotNodeActionInstance prototype, int quantity)
    {
        if (EscapeFromAddOf is not null && ReferenceEquals(prototype.GetAction(), EscapeFromAddOf))
            throw new NotSupportedException("injected unexpected failure adding the plot action");
        Add(prototype, quantity);
        if (!BreakAfterMutation) return;
        brokenReads = true;
        throw new InvalidOperationException("injected failure after the plot action add");
    }

    private void Add(PlotNodeActionInstance prototype, int quantity)
    {
        if (SuppressMutation || SuppressMutationOf is not null &&
            ReferenceEquals(prototype.GetAction(), SuppressMutationOf)) return;
        var existing = FindInstance(prototype);
        if (existing is not null && !existing.IsEmpty())
        {
            existing.PlayerChangeInstanceQuantity(quantity);
            return;
        }
        if (!HasEmptySpot()) return;

        prototype.PlayerChangeInstanceQuantity(quantity);
        prototype.Engage();
        for (var index = 0; index < value.Count; index++)
        {
            if (value[index] is null || value[index].IsEmpty())
            {
                value[index] = prototype;
                return;
            }
        }
        value.Add(prototype);
    }

    public void RemoveInstance(PlotNodeActionInstance prototype, int quantity)
    {
        if (SuppressMutation) return;
        var existing = FindInstance(prototype);
        if (existing is null || existing.IsEmpty()) return;
        existing.PlayerChangeInstanceQuantity(-quantity);
    }
    public int? ReportedUsedSpots;
    public new int GetUsedSpots() => ReportedUsedSpots ?? base.GetUsedSpots();
}
