using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class ResourceFillList
{
    public List<ResourceFillEntry> entries = new List<ResourceFillEntry>();

    public BigDouble GetAverageRatio() => entries.Count == 0
        ? BigDouble.Zero
        : entries.Aggregate(BigDouble.Zero, (sum, entry) => sum + entry.FillPercent()) /
          new BigDouble(entries.Count);
    public BigDouble GetLowestRatio() => entries.Count == 0
        ? BigDouble.Zero
        : entries.Min(entry => entry.FillPercent());
    public ResourceFillList ClearInvestment()
    {
        foreach (var entry in entries) entry.Clear();
        return this;
    }

    public sealed class ResourceFillEntry
    {
        public ResourceFillEntry(ResourceSO resource, BigDouble quantity, BigDouble capacity)
        {
            this.resource = resource;
            Quantity = quantity;
            Capacity = capacity;
        }

        private readonly ResourceSO resource;
        public BigDouble Quantity { get; private set; }
        public BigDouble Capacity { get; }
        public ResourceSO get_resource() => resource;
        public BigDouble GetQuantity() => Quantity;
        public BigDouble GetCapacity() => Capacity;
        public BigDouble GetRemaining() => BigDouble.Max(Capacity - Quantity, BigDouble.Zero);
        public BigDouble FillPercent() => Capacity <= BigDouble.Zero ? BigDouble.Zero : Quantity / Capacity;
        public ResourceFillEntry Clear() { Quantity = BigDouble.Zero; return this; }
    }
}
