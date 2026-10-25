using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public struct ResourceTuple
{
    // Both magnitudes, named as the game names them. `value` is the serialized double Unity writes to
    // disk; `valueBig` is the one the arithmetic uses and the one world collection reads.
    private double value;
    private BigDouble valueBig;

    public ResourceTuple(ResourceSO resource, BigDouble value)
    {
        this.resource = resource;
        valueBig = value;
        this.value = value.ToDouble();
    }

    public ResourceSO resource;

    public BigDouble GetValue() => valueBig;
    public ResourceTuple(Guid identity, double amount) : this(new ResourceSO { Identity = identity }, new BigDouble(amount)) { }
    public BigDouble Amount { get => valueBig; set { valueBig = value; this.value = value.ToDouble(); } }

}
