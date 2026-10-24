using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class ResourceCostList
{
    public List<ResourceTuple> costs = new List<ResourceTuple>();
    public bool WithinCapacity = true;
    public int CostPrintReads { get; private set; }
    private string _costPrint
    {
        get
        {
            CostPrintReads++;
            return "diagnostic-only";
        }
    }
    public bool affordable = true;
    public bool ResourcesVisible = true;

    public bool IsWithinCapacity() => WithinCapacity;
    public bool IsEmpty() => costs.Count == 0;
    public bool AllResourcesVisible() => ResourcesVisible;

    // How many more purchases the holdings cover. Spending is what makes a price unaffordable in the
    // game, so a fixture that wants a purchase to stop being possible partway says how far the
    // holdings go rather than reaching in and flipping a flag mid-call.
    public int AffordableLevels = int.MaxValue;
    public int PerformCalls { get; private set; }
    public int? ThrowAfterCostRows { get; set; }
    public Action? WhileAffordabilityIsRead { get; set; }
    public bool HasEnough()
    {
        WhileAffordabilityIsRead?.Invoke();
        if (!affordable || AffordableLevels <= 0) return false;
        var totals = new Dictionary<ResourceSO, BigDouble>();
        for (var index = 0; index < costs.Count; index++)
        {
            var row = costs[index];
            if (row.resource is null) return false;
            totals.TryGetValue(row.resource, out var current);
            totals[row.resource] = current + row.GetValue();
        }
        foreach (var pair in totals)
            if (!pair.Key.HasAmount(pair.Value)) return false;
        return true;
    }
    public Exception? EntriesFault;
    public List<ResourceTuple> GetEntries() => EntriesFault is { } fault ? throw fault : costs;
    public BigDouble MaximumCostTimes()
    {
        if (maximumCostTimes is { } maximum) return maximum;
        if (costs.Count == 0) return new BigDouble(int.MaxValue);
        var result = new BigDouble(int.MaxValue);
        foreach (var row in costs)
        {
            if (row.resource is null || row.GetValue() <= BigDouble.Zero) return BigDouble.Zero;
            var available = row.resource.bandwidthResource
                ? row.resource.GetMissing()
                : row.resource.GetTrueQuantity();
            result = BigDouble.Min(result, BigDouble.Floor(available / row.GetValue()));
        }
        return BigDouble.Max(result, BigDouble.Zero);
    }
    public ResourceCostList Multiply(BigDouble factor)
    {
        var result = new ResourceCostList
        {
            WithinCapacity = WithinCapacity,
            affordable = affordable,
            AffordableLevels = AffordableLevels,
        };
        for (var index = 0; index < costs.Count; index++)
            result.costs.Add(new ResourceTuple(
                costs[index].resource,
                costs[index].GetValue() * factor));
        return result;
    }
    public ResourceCostList Add(ResourceCostList other)
    {
        foreach (var row in other.costs)
        {
            var index = costs.FindIndex(existing => ReferenceEquals(existing.resource, row.resource));
            if (index < 0) costs.Add(row);
            else costs[index] = new ResourceTuple(row.resource,
                costs[index].GetValue() + row.GetValue());
        }
        affordable &= other.affordable;
        return this;
    }
    public ResourceCostList Subtract(ResourceCostList other)
    {
        var result = new ResourceCostList();
        foreach (var row in costs)
        {
            var previous = other.costs.FirstOrDefault(
                candidate => ReferenceEquals(candidate.resource, row.resource));
            result.costs.Add(new ResourceTuple(
                row.resource,
                row.GetValue() - (previous.resource is null
                    ? BigDouble.Zero
                    : previous.GetValue())));
        }
        return result;
    }
    public ResourceCostList Apply(ValueModifier modifier)
    {
        var result = new ResourceCostList
        {
            WithinCapacity = WithinCapacity,
            affordable = affordable,
            AffordableLevels = AffordableLevels,
        };
        for (var index = 0; index < costs.Count; index++)
            result.costs.Add(new ResourceTuple(
                costs[index].resource,
                modifier.Adjust(costs[index].GetValue())));
        return result;
    }
    public void PerformCost()
    {
        PerformCalls++;
        for (var index = 0; index < costs.Count; index++)
        {
            var row = costs[index];
            row.resource?.Spend(row.GetValue());
            if (ThrowAfterCostRows == index + 1)
                throw new InvalidOperationException($"injected failure after {index + 1} cost rows");
        }
        if (AffordableLevels is > 0 and < int.MaxValue) AffordableLevels--;
    }

    public void PerformUsage(Guid guid)
    {
        foreach (var row in costs) row.resource?.AddUsage(guid, row.GetValue());
    }

    public void RemoveUsage(Guid guid)
    {
        foreach (var row in costs) row.resource?.RemoveUsage(guid);
    }
    public bool withinCapacity { get => WithinCapacity; set => WithinCapacity = value; }
    public BigDouble? maximumCostTimes;

}
