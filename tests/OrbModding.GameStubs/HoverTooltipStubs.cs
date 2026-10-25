using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class HoverTooltip : UnityEngine.MonoBehaviour
{
    public ITooltipable? tooltipItem;
    public TooltipableObject? setupObject;
    private List<ITooltipable> subTooltips = new();
    public void Setup(ITooltipable item, List<ITooltipable>? subTooltips = null)
    {
        tooltipItem = item;
        this.subTooltips = subTooltips ?? new List<ITooltipable>();
    }
    public void OpenTooltip() { }
}
