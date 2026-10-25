using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The game's tooltip decorator: a plain wrapper that forwards to the entity it holds and replaces
/// whichever parts the caller overwrote. The panels that build a tooltip out of several facts hand
/// the screen one of these rather than the entity itself.
/// </summary>
public sealed class OverwriteTooltip :
    ITooltipable, IHasRequirements, IHasResourceCost, IHasUsageCost, IHasExperienceElement
{
    public readonly ITooltipable tooltipable;
    public Func<List<TooltipNode>>? overwriteRequirements;
    public Func<List<TooltipNode>>? overwriteTooltipNodes;
    public List<TooltipNode>? tooltipNodes;

    public OverwriteTooltip(ITooltipable tooltipable) => this.tooltipable = tooltipable;

    public OverwriteTooltip SetTooltipNodes(Func<List<TooltipNode>> nodes)
    {
        overwriteTooltipNodes = nodes;
        tooltipNodes = null;
        return this;
    }

    public OverwriteTooltip SetTooltipNodes(List<TooltipNode> nodes)
    {
        tooltipNodes = nodes;
        return this;
    }

    public OverwriteTooltip SetRequirements(Func<List<TooltipNode>> requirements)
    {
        overwriteRequirements = requirements;
        return this;
    }

    public List<TooltipNode> GetRequirements() =>
        overwriteRequirements?.Invoke() ??
        (tooltipable as IHasRequirements)?.GetRequirements() ??
        new List<TooltipNode>();
    public ResourceCostList GetResourceCost() =>
        (tooltipable as IHasResourceCost)?.GetResourceCost() ?? new ResourceCostList();
    public ResourceCostList GetUsageCost() =>
        (tooltipable as IHasUsageCost)?.GetUsageCost() ?? new ResourceCostList();
    public IExperienceElement? GetExperienceElement() =>
        (tooltipable as IHasExperienceElement)?.GetExperienceElement() ??
        tooltipable as IExperienceElement;

    public string GetName() => tooltipable?.GetName() ?? string.Empty;
    public string GetDisplayType() => tooltipable?.GetDisplayType() ?? string.Empty;
    public UnityEngine.Sprite GetIcon() => tooltipable?.GetIcon() ?? new UnityEngine.Sprite();
    public UnityEngine.Color GetColor() => tooltipable?.GetColor() ?? UnityEngine.Color.white;
    public bool IsColoredIcon() => tooltipable?.IsColoredIcon() == true;
    public bool HasAltTooltips() => tooltipable?.HasAltTooltips() == true;
    public string GetDescription() => tooltipable?.GetDescription() ?? string.Empty;
    public List<TooltipNode> GetTooltipNodes() =>
        tooltipNodes ?? overwriteTooltipNodes?.Invoke() ?? tooltipable?.GetTooltipNodes() ?? new();
    public List<TooltipNode> GetAltTooltipNodes() =>
        tooltipNodes ?? tooltipable?.GetAltTooltipNodes() ?? new();
}
