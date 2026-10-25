#if SERVICE_CYCLE_PROFILE
using OrbModding.Common;

namespace OrbAutomata.GameMcp;

/// <summary>The one command-to-family map, shared by gameplay admission and command classification.</summary>
internal static class AutomataGameMcpActionFamilies
{
    internal static readonly (GameMcpCommandKind Kind, string? Mode, GameActionFamily Family)[]
        Routes =
        {
            (GameMcpCommandKind.Purchase, "structure", Mcp(AutomationActionFamily.StructurePurchase)),
            (GameMcpCommandKind.Purchase, "upgrade", Mcp(
                AutomationActionFamily.UpgradePurchase,
                AutomationActionFamily.NativeMultiBuyOverride)),
            (GameMcpCommandKind.Cast, null, Mcp(AutomationActionFamily.SpellCast)),
            (GameMcpCommandKind.Concept, null, Mcp(AutomationActionFamily.ConceptAssignment)),
            (GameMcpCommandKind.SpellLevel, null, Mcp(AutomationActionFamily.SpellLevelPurchase)),
            (GameMcpCommandKind.Harvest, null, AutoHarvestFeatureContribution.ActionFamily),
            (GameMcpCommandKind.DiscoveryTreeOffer, null,
                Mcp(AutomationActionFamily.DiscoveryTreeOfferLifecycle)),
            (GameMcpCommandKind.SpellWorkbench, null,
                Mcp(AutomationActionFamily.SpellWorkbenchLifecycle)),
            (GameMcpCommandKind.SpellComposition, null, Mcp(AutomationActionFamily.SpellComposition)),
            (GameMcpCommandKind.SpellLoadout, null, Mcp(AutomationActionFamily.SpellLoadout)),
            (GameMcpCommandKind.Targeting, null, Mcp(AutomationActionFamily.Targeting)),
            (GameMcpCommandKind.Consumable, null, AutoItemsFeatureContribution.ActionFamily),
            (GameMcpCommandKind.Crafting, null, AutoScribeFeatureContribution.ActionFamily),
            (GameMcpCommandKind.EquipmentLoadout, null, Mcp(AutomationActionFamily.EquipmentLoadout)),
            (GameMcpCommandKind.Challenge, null, Mcp(AutomationActionFamily.ChallengeLifecycle)),
            (GameMcpCommandKind.Prestige, null, Mcp(AutomationActionFamily.PrestigeLifecycle)),
            (GameMcpCommandKind.Research, null, Mcp(AutomationActionFamily.ResearchLifecycle)),
            (GameMcpCommandKind.AlchemyLoadout, null, Mcp(AutomationActionFamily.AlchemyLoadout)),
            (GameMcpCommandKind.RitualLifecycle, null, Mcp(AutomationActionFamily.RitualLifecycle)),
            (GameMcpCommandKind.GenericLevel, null, Mcp(AutomationActionFamily.GenericLevel)),
            (GameMcpCommandKind.Loadout, null, Mcp(AutomationActionFamily.PlayerLoadout)),
            (GameMcpCommandKind.HarvestLifecycle, null, Mcp(AutomationActionFamily.HarvestLifecycle)),
            (GameMcpCommandKind.StructureLifecycle, null,
                Mcp(AutomationActionFamily.StructureLifecycle)),
            (GameMcpCommandKind.ReturnToMenu, null, Mcp(AutomationActionFamily.RunTransition)),
        };

    private static GameActionFamily Mcp(params AutomationActionFamily[] members) =>
        new(GameActionFamily.McpOperation, members);
    internal static bool NamesKind(GameMcpCommandKind kind)
    {
        foreach (var route in Routes)
            if (route.Kind == kind) return true;
        return false;
    }
}
#endif
