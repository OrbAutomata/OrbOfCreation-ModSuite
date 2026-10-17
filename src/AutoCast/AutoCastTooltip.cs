using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoCastTooltip : ITooltipable
{
    private readonly AutoCastToggleControl _control;

    public AutoCastTooltip(AutoCastToggleControl control) { _control = control; }
    public string GetName() => "Auto Cast";
    public string GetDisplayType() =>
        ConfiguredIntentIconButtonVisual.TooltipLabelFor(_control.Status);
    public Sprite GetIcon() => null!;
    public Color GetColor() =>
        ConfiguredIntentIconButtonVisual.FromFeatureStatus(_control.Status).Color;
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Automatically casts eligible equipped spells through the native spell manager.";
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _control.Status, GetColor(), lineWidth: 42);
        nodes.AddRange(new[]
        {
            new TooltipNode($"Minimum resource fullness: {_control.Config.AutoCast.StartResourcePercent:0.##}%"),
            new TooltipNode($"Charged spells: {(_control.Config.AutoCast.FullCharge ? "Full charge" : "Fire immediately")}"),
            new TooltipNode($"Attack spells: {(_control.Config.AutoCast.RitualAware ? "Ritual battles only" : "Always")}"),
            new TooltipNode("Evaluation cadence: each world update"),
            new TooltipNode($"Pause after manual cast: {_control.Config.AutoCast.ManualPauseSeconds:0.##} seconds"),
            new TooltipNode("Click or press F8 to toggle."),
        });
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
