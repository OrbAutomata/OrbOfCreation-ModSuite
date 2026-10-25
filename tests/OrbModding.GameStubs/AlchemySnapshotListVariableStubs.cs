using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class AlchemySnapshotListVariable : AbstractListVariable<AlchemySnapshot>
{
    public List<AlchemySnapshot> GetAll() => new List<AlchemySnapshot>();
}
