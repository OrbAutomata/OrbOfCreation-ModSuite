using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// One authored (thing, modifier) pair. The deprecated container stores its variable effects this
/// way rather than as a script class, so the thing is named <c>item</c> here and nothing else is.
/// </summary>
public sealed class TupleMod<T>
    where T : class
{
    public T? item;
    public ValueModifier modifier;
}
