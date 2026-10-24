using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// One panel of a tooltip container. Which of its optional sections a prefab carries is a serialized
/// reference the game leaves empty on a panel that never draws that section.
/// </summary>
public class UITooltip : UnityEngine.MonoBehaviour
{
    public UIResourceCostList? resourceCostElement;
    public UIResourceCostList? usageCostElement;
    public UnityEngine.RectTransform? masteryElement;
    public ViewSO? masteryEnabledView;
    public UITooltipNodeList? requirementNodesElement;
}
