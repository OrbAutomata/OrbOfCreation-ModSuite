using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class UITooltipContainer : UnityEngine.MonoBehaviour
{
    public UITooltip? parentTooltip;
    public UITooltip? childTooltipPrefab;

    /// <summary>
    /// The game's own answer to how deep stacked tooltips go: past five panels
    /// <c>PauseTooltip</c> simply refuses to open another.
    /// </summary>
    public const int MaxTooltipDepth = 5;

    public static List<UITooltipContainer> globalTooltips = new();

    /// <summary>
    /// The more-info key, which the game reads as <c>InputManager.instance.ShowMoreInfo()</c> —
    /// a live key bind rather than anything the panel stores.
    /// </summary>
    public static bool showMoreInfo;

    public ITooltipable? item;
    public List<ITooltipable> subTooltips = new();

    public bool IsUsingAltTooltip() => item is not null && item.HasAltTooltips() && showMoreInfo;
    public bool IsEmpty() => item is null && (subTooltips is null || subTooltips.Count == 0);
}
