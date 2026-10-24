using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The three words every statistic row is tagged with. A reference holds one, and the reference of
/// the one record that names none holds nothing — which is the shape the glossary binder reads.
/// </summary>
public sealed class DisplayTypeSO : TooltipableObject
{
    public static List<DisplayTypeSO> All = new List<DisplayTypeSO>();

    public sealed class Reference
    {
        public DisplayTypeSO displayType;
    }
}
