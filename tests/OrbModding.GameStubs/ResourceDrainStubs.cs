using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class ResourceDrain
{
    public bool isDrainApplied = true;
    public BigDouble currentRatio = new BigDouble(1.0, 0);
    public BigDouble usageRatio = new BigDouble(1.0, 0);
    public ResourceCostList drainCostMult = new ResourceCostList();
    public BigDouble GetRatio() => new BigDouble(1.0, 0);
    public ResourceCostList GetCurrentDrain() => drainCostMult;
}
