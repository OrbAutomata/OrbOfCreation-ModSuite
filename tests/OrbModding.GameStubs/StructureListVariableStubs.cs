using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class StructureListVariable : GenericListVariable<StructureSO>
{
    public List<StructureSO> GetAll() => StructureSO.All;
}
