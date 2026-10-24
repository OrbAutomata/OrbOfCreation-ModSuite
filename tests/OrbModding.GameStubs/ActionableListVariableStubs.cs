using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class ActionableListVariable : StackableListVariable<IActionable>
{
    public IntVariable maxQueuedItems = new IntVariable();
}
