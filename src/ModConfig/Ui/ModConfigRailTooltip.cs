using System.Collections.Generic;
using UnityEngine;

namespace OrbModConfig;

internal sealed class ModConfigRailTooltip : ITooltipable
{
    private readonly ModConfigTopPage _page;
    private readonly Sprite _icon;
    public ModConfigRailTooltip(ModConfigTopPage page, Sprite icon) { _page = page; _icon = icon; }
    public string GetName() => _page.Label;
    public string GetDisplayType() => "MODSUITE";
    public Sprite GetIcon() => _icon;
    public Color GetColor() => Color.white;
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => $"Open the {_page.Label} page.";
    public List<TooltipNode> GetTooltipNodes() => new()
    {
        new TooltipNode(_page.Kind switch
        {
            ModConfigTopPageKind.Runtime => "Health updates live and diagnostic actions run immediately.",
            ModConfigTopPageKind.Strategist => "Read the current progression advice. This page performs no gameplay actions.",
            _ => "Settings changes remain staged until Apply; controls marked immediate apply at once.",
        }),
    };
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
