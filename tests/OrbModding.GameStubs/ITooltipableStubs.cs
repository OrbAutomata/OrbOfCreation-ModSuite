using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public interface ITooltipable
{
    string GetName();
    string GetDisplayType();
    UnityEngine.Sprite GetIcon();
    UnityEngine.Color GetColor();
    bool IsColoredIcon();
    bool HasAltTooltips();
    string GetDescription();
    List<TooltipNode> GetTooltipNodes();
    List<TooltipNode> GetAltTooltipNodes();
}
