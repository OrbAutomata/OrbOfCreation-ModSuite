using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>The game's global scalars. Same shape as <see cref="IntVariable"/>; separate registry.</summary>
public sealed class DoubleVariable : NumberVariable
{
    public static List<DoubleVariable> All = new List<DoubleVariable>();

    // The two flags that decide the value is a count of seconds, and how finely the game draws it.

    public BigDouble AsPercent() => value.AsPercent();

    public BigDouble AsBigDouble() => value.AsBigDouble();

}
