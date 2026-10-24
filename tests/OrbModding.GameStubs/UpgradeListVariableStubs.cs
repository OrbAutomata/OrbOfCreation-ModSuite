using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class UpgradeListVariable : GenericListVariable<UpgradeSO>
{
    public List<UpgradeSO> GetAll() => UpgradeSO.All;
}
