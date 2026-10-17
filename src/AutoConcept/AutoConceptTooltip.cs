using System.Collections.Generic;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

internal sealed class AutoConceptTooltip : ITooltipable
{
    private readonly AutoConceptToggleControl _control;
    private SuiteRuntimeConfiguration Config => _control.Config;

    public AutoConceptTooltip(AutoConceptToggleControl control)
    {
        _control = control;
    }

    public string GetName() => "Auto Concept";

    public string GetDisplayType() =>
        ConfiguredIntentIconButtonVisual.TooltipLabelFor(_control.Status);

    public Sprite GetIcon() => null!;

    public Color GetColor() =>
        ConfiguredIntentIconButtonVisual.FromFeatureStatus(_control.Status).Color;

    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => "Balances discovered Scholar Concept mastery through the native Active Concepts list.";

    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _control.Status, GetColor(), lineWidth: 42);
        nodes.AddRange(new TooltipNode[]
        {
            new($"Slot management: {Config.AutoConcept.SlotManagement}"),
            new($"Training period: {Config.AutoConcept.TrainingPeriodSeconds} seconds"),
            new($"Rate reserve: {Config.AutoConcept.RateReservePercent:0.##}%"),
            new("Click to toggle Auto Concept."),
        });
        return nodes;
    }

    public List<TooltipNode> GetAltTooltipNodes() => new();
}
