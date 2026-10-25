using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class ViewListVariable : GenericListVariable<ViewSO>
{
    public List<ViewSO> GetAll() => value;

    public sealed class ListTuple : AdditionTuple<ViewListVariable>
    {
    }
}
