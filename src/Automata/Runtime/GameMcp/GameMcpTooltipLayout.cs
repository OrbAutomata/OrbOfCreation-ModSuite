#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

/// <summary>
/// Which optional sections the game's tooltip panels draw: the core panel and the sub-panels beside
/// it are two prefabs, read once per call from <c>GlobalVariables.GetTooltipPrefab()</c>.
/// </summary>
/// <remarks>
/// <c>UITooltip.Render</c> paints a cost, a usage cost, a mastery line or a requirement list only
/// where the prefab holds the element for it, and the mastery line only once its view is unlocked or
/// the item forces it. A prefab the game cannot hand over draws none of them.
/// </remarks>
internal sealed class GameMcpTooltipLayout
{
    internal static readonly GameMcpTooltipLayout Bare = new(default, default, string.Empty);

    private GameMcpTooltipLayout(Panel core, Panel sub, string masteryLabel)
    {
        Core = core;
        Sub = sub;
        MasteryLabel = masteryLabel;
    }

    internal Panel Core { get; }

    internal Panel Sub { get; }

    /// <summary>The word <c>RenderXpBar</c> prints before the level: the mastery level attribute's name.</summary>
    internal string MasteryLabel { get; }

    internal static GameMcpTooltipLayout Read()
    {
        var prefab = GlobalVariables.GetTooltipPrefab();
        if (prefab == null) return Bare;
        return new GameMcpTooltipLayout(
            Panel.Of(prefab.parentTooltip),
            Panel.Of(prefab.childTooltipPrefab),
            GlobalVariables.GetMasteryLevelAttr()?.GetName() ?? string.Empty);
    }

    internal readonly struct Panel
    {
        private Panel(bool resourceCost, bool usageCost, bool mastery, bool masteryUnlocked, bool requirements)
        {
            ResourceCost = resourceCost;
            UsageCost = usageCost;
            Mastery = mastery;
            MasteryUnlocked = masteryUnlocked;
            Requirements = requirements;
        }

        internal bool ResourceCost { get; }

        internal bool UsageCost { get; }

        internal bool Mastery { get; }

        internal bool MasteryUnlocked { get; }

        internal bool Requirements { get; }

        internal static Panel Of(UITooltip? tooltip)
        {
            if (tooltip == null) return default;
            return new Panel(
                tooltip.resourceCostElement != null,
                tooltip.usageCostElement != null,
                tooltip.masteryElement != null,
                tooltip.masteryEnabledView != null && tooltip.masteryEnabledView.IsAvailable(),
                tooltip.requirementNodesElement != null);
        }
    }
}
#endif
