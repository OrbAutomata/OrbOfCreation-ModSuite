using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class TooltipableObject : IdScriptableObject, ITooltipable
{
    public string displayName = string.Empty;

    /// <summary>
    /// The authored tooltip text, under the field name and behind the virtual the game gives it.
    /// The stub answered every description with an empty string, so nothing here could model an
    /// entity the game authors one for.
    /// </summary>
    public string description = string.Empty;

    public UnityEngine.Sprite Icon { get; set; } = new UnityEngine.Sprite();
    public virtual string GetName() => displayName;
    public UnityEngine.Sprite GetIcon() => Icon;
    public virtual string GetDisplayType() => GetType().Name;
    public virtual UnityEngine.Color GetColor() => UnityEngine.Color.white;
    public virtual bool IsColoredIcon() => false;
    public virtual bool HasAltTooltips() => false;
    public virtual string GetDescription() => description;
    public virtual List<TooltipNode> GetTooltipNodes() => new();
    public virtual List<TooltipNode> GetAltTooltipNodes() => new();
}
