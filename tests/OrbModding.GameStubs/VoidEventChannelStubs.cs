using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class VoidEventChannel : UnityEngine.ScriptableObject
{
    public int RaiseCalls { get; private set; }

    public void Raise() => RaiseCalls++;
}
