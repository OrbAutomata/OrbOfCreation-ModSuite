using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The list-variable hierarchy, shaped as the shipped assembly declares it: the value list and the
/// per-element-type registry sit on the generic base, occupancy on the generic list, and a concrete
/// list variable adds only its own members.
/// </summary>
/// <remarks>
/// The shape is why <c>All</c> is no use for reaching a queue. It is declared on the generic base, so
/// it holds every list variable over one element type rather than every instance of one concrete
/// type — and reflection does not find a base type's static on a derived one at all.
/// </remarks>
public abstract class AbstractListVariable : IdScriptableObject
{
}
