using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class AttributeSO : TooltipableObject
{
    public static List<AttributeSO> All = new List<AttributeSO>();
    public DisplayTypeSO.Reference displayTypeRef = new DisplayTypeSO.Reference();
    public string globalDefinition = string.Empty;
    public bool isPercent;
    public bool useBigTooltip;
}
