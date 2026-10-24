using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class EquipmentSnapshotListVariable : AbstractListVariable<EquipmentSnapshot>
{
    public List<EquipmentSnapshot> GetAll() => new List<EquipmentSnapshot>();
}
