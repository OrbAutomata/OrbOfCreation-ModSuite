using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class AlchemyInstance : AbstractRefInstance<AlchemyRecipeSO>
{
    public AlchemyInstance()
    {
    }

    public AlchemyInstance(AlchemyRecipeSO reference)
    {
        this.reference = reference;
    }

    public int quantity;
    public int queuedQuantity;
    public ResourceDrain resourceDrain = new ResourceDrain();

    public bool ThrowOnRead;

    public BigDouble GetDrainCostMod() => new BigDouble(quantity * 100d);
    public int GetQueuedQuantity() =>
        ThrowOnRead ? throw new InvalidOperationException("injected failure reading alchemy uses") : queuedQuantity;
    public int GetRemainingFreeUsageSlots() =>
        Math.Max(reference.GetFreeUsageSlots() - queuedQuantity, 0);
    public int GetRemainingMaxUsageSlots() =>
        Math.Max(reference.GetMaxUsageSlots() - queuedQuantity, 0);
}
