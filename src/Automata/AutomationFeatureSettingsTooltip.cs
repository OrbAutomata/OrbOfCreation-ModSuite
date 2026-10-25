using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using UnityEngine;

namespace OrbAutomata;

/// <summary>The common quick-control presentation for a feature's registered settings.</summary>
internal sealed class AutomationFeatureSettingsTooltip : ITooltipable
{
    private readonly AutomataFeatureContribution _feature;
    private readonly Func<SuiteRuntimeConfiguration> _configuration;
    private readonly Func<FeatureStatusSnapshot> _status;

    internal AutomationFeatureSettingsTooltip(AutomataFeatureContribution feature,
        Func<SuiteRuntimeConfiguration> configuration, Func<FeatureStatusSnapshot> status)
    {
        _feature = feature;
        _configuration = configuration;
        _status = status;
    }

    public string GetName() => _feature.DisplayName;
    public string GetDisplayType() => ConfiguredIntentIconButtonVisual.TooltipLabelFor(_status());
    public Sprite GetIcon() => null!;
    public Color GetColor() => ConfiguredIntentIconButtonVisual.FromFeatureStatus(_status()).Color;
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => _feature.Settings!.Description;
    public List<TooltipNode> GetTooltipNodes()
    {
        var nodes = new List<TooltipNode>();
        TooltipNodeLayout.AddFeatureStatus(nodes, _status(), GetColor(), lineWidth: 58);
        var snapshot = _configuration();
        foreach (var setting in _feature.Settings!.Settings)
        {
            if (setting.Key == "Mode") continue;
            nodes.Add(new TooltipNode(setting.Label + ": " + setting.ReadPublishedValue(snapshot)));
        }
        nodes.Add(new TooltipNode("Click to toggle " + _feature.DisplayName + "."));
        return nodes;
    }
    public List<TooltipNode> GetAltTooltipNodes() => new();
}
