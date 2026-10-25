using System;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameActionFamilyTableTests
{
    [Fact]
    public void AConsumableOperationDoesNotSupplyTheBuyAutomationUpgradeScope()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        using var ownership = new AutomataActionFamilyOwnership(registry);
        using var external = registry.RegisterKnownExternal(
            new ActionFamilyOwner(new FeatureStatusKey("tests", "multi-buy"), "External multi-buy"),
            new[] { AutomationActionFamily.NativeMultiBuyOverride });
        ownership.Refresh(config.Current, lifecycleReady: true);
        Assert.True(ownership.OwnsFamily(AutomationActionFamily.UpgradePurchase));
        Assert.False(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade));
        external.Dispose();

        Assert.True(ownership.TryBeginGameMcpOperation(GameMcpCommandKind.Consumable, "use",
            out var scope, out var reason), reason);
        using (scope)
        {
            Assert.True(ownership.OwnsFamily(AutomationActionFamily.NativeMultiBuyOverride));
            Assert.False(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade));
            Assert.True(ownership.Family(GameMcpCommandKind.Consumable).TryCapture());
        }
    }

    [Theory]
    [InlineData(nameof(GameMcpCommandKind.DiscoveryTreeOffer), "The current MCP operation does not hold DiscoveryTreeOfferLifecycle.")]
    [InlineData(nameof(GameMcpCommandKind.SpellWorkbench), "The current MCP operation does not hold SpellWorkbenchLifecycle.")]
    [InlineData(nameof(GameMcpCommandKind.SpellComposition), "The current MCP operation does not hold SpellComposition.")]
    [InlineData(nameof(GameMcpCommandKind.SpellLoadout), "The current MCP operation does not hold SpellLoadout.")]
    [InlineData(nameof(GameMcpCommandKind.Targeting), "The current MCP operation does not hold Targeting.")]
    [InlineData(nameof(GameMcpCommandKind.EquipmentLoadout), "The current MCP operation does not hold EquipmentLoadout.")]
    [InlineData(nameof(GameMcpCommandKind.Challenge), "The current MCP operation does not hold ChallengeLifecycle.")]
    [InlineData(nameof(GameMcpCommandKind.Prestige), "The current MCP operation does not hold PrestigeLifecycle.")]
    [InlineData(nameof(GameMcpCommandKind.Research), "The current MCP operation does not hold ResearchLifecycle.")]
    [InlineData(nameof(GameMcpCommandKind.AlchemyLoadout), "The current MCP operation does not hold AlchemyLoadout.")]
    [InlineData(nameof(GameMcpCommandKind.RitualLifecycle), "The current MCP operation does not hold RitualLifecycle.")]
    [InlineData(nameof(GameMcpCommandKind.GenericLevel), "The current MCP operation does not hold GenericLevel.")]
    [InlineData(nameof(GameMcpCommandKind.Loadout), "The current MCP operation does not hold PlayerLoadout.")]
    [InlineData(nameof(GameMcpCommandKind.HarvestLifecycle), "The current MCP operation does not hold HarvestLifecycle.")]
    [InlineData(nameof(GameMcpCommandKind.StructureLifecycle), "The current MCP operation does not hold StructureLifecycle.")]
    [InlineData(nameof(GameMcpCommandKind.ReturnToMenu), "The current MCP operation does not hold RunTransition.")]
    public void An_mcp_only_family_refuses_outside_its_operation_with_its_exact_sentence(
        string kindName,
        string sentence)
    {
        var kind = Enum.Parse<GameMcpCommandKind>(kindName);
        using var ownership = new AutomataActionFamilyOwnership(new ActionFamilyOwnershipRegistry());
        var family = ownership.Family(kind);

        Assert.False(family.TryCapture());
        Assert.Equal(sentence, family.Failure());
        Assert.True(ownership.TryBeginGameMcpOperation(kind, "any", out var scope, out var reason), reason);
        using (scope) Assert.True(family.TryCapture());
        Assert.False(family.TryCapture());
    }

    [Fact]
    public void Each_kind_and_mode_has_one_family_and_an_unnamed_kind_has_no_second_home()
    {
        var keys = AutomataGameMcpActionFamilies.Routes
            .Select(route => (route.Kind, route.Mode))
            .ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
        foreach (var kind in Enum.GetValues(typeof(GameMcpCommandKind)).Cast<GameMcpCommandKind>())
        {
            var modes = keys.Where(key => key.Kind == kind).Select(key => key.Mode).ToArray();
            var named = modes.Length != 0;
            Assert.False(
                modes.Contains(null) && modes.Length > 1,
                kind + " is named both for every mode and for one");
            if (named) continue;
            using var ownership = new AutomataActionFamilyOwnership(new ActionFamilyOwnershipRegistry());
            Assert.False(ownership.TryBeginGameMcpOperation(kind, "any", out _, out var reason));
            Assert.Equal("command " + kind + " has no gameplay action family", reason);
            Assert.Throws<InvalidOperationException>(() => ownership.Family(kind));
        }
    }

    [Theory]
    [InlineData(nameof(GameMcpCommandKind.Purchase), "structure", new[] { AutomationActionFamily.StructurePurchase })]
    [InlineData(nameof(GameMcpCommandKind.Purchase), "upgrade", new[] { AutomationActionFamily.UpgradePurchase, AutomationActionFamily.NativeMultiBuyOverride })]
    [InlineData(nameof(GameMcpCommandKind.Cast), "any", new[] { AutomationActionFamily.SpellCast })]
    [InlineData(nameof(GameMcpCommandKind.Concept), "any", new[] { AutomationActionFamily.ConceptAssignment })]
    [InlineData(nameof(GameMcpCommandKind.SpellLevel), "any", new[] { AutomationActionFamily.SpellLevelPurchase })]
    [InlineData(nameof(GameMcpCommandKind.Harvest), "any", new[] { AutomationActionFamily.HarvestAction })]
    [InlineData(nameof(GameMcpCommandKind.DiscoveryTreeOffer), "any", new[] { AutomationActionFamily.DiscoveryTreeOfferLifecycle })]
    [InlineData(nameof(GameMcpCommandKind.SpellWorkbench), "any", new[] { AutomationActionFamily.SpellWorkbenchLifecycle })]
    [InlineData(nameof(GameMcpCommandKind.SpellComposition), "any", new[] { AutomationActionFamily.SpellComposition })]
    [InlineData(nameof(GameMcpCommandKind.SpellLoadout), "any", new[] { AutomationActionFamily.SpellLoadout })]
    [InlineData(nameof(GameMcpCommandKind.Targeting), "any", new[] { AutomationActionFamily.Targeting })]
    [InlineData(nameof(GameMcpCommandKind.Consumable), "any", new[] { AutomationActionFamily.ConsumableUse, AutomationActionFamily.NativeMultiBuyOverride })]
    [InlineData(nameof(GameMcpCommandKind.Crafting), "any", new[] { AutomationActionFamily.CraftingQueueSubmission })]
    [InlineData(nameof(GameMcpCommandKind.EquipmentLoadout), "any", new[] { AutomationActionFamily.EquipmentLoadout })]
    [InlineData(nameof(GameMcpCommandKind.Challenge), "any", new[] { AutomationActionFamily.ChallengeLifecycle })]
    [InlineData(nameof(GameMcpCommandKind.Prestige), "any", new[] { AutomationActionFamily.PrestigeLifecycle })]
    [InlineData(nameof(GameMcpCommandKind.Research), "any", new[] { AutomationActionFamily.ResearchLifecycle })]
    [InlineData(nameof(GameMcpCommandKind.AlchemyLoadout), "any", new[] { AutomationActionFamily.AlchemyLoadout })]
    [InlineData(nameof(GameMcpCommandKind.RitualLifecycle), "any", new[] { AutomationActionFamily.RitualLifecycle })]
    [InlineData(nameof(GameMcpCommandKind.GenericLevel), "any", new[] { AutomationActionFamily.GenericLevel })]
    [InlineData(nameof(GameMcpCommandKind.Loadout), "any", new[] { AutomationActionFamily.PlayerLoadout })]
    [InlineData(nameof(GameMcpCommandKind.HarvestLifecycle), "any", new[] { AutomationActionFamily.HarvestLifecycle })]
    [InlineData(nameof(GameMcpCommandKind.StructureLifecycle), "any", new[] { AutomationActionFamily.StructureLifecycle })]
    [InlineData(nameof(GameMcpCommandKind.ReturnToMenu), "any", new[] { AutomationActionFamily.RunTransition })]
    public void A_gameplay_operation_claims_exactly_its_families(
        string kindName,
        string mode,
        AutomationActionFamily[] families)
    {
        var kind = Enum.Parse<GameMcpCommandKind>(kindName);
        var registry = new ActionFamilyOwnershipRegistry();
        using var ownership = new AutomataActionFamilyOwnership(registry);

        Assert.True(ownership.TryBeginGameMcpOperation(kind, mode, out var scope, out var reason), reason);
        using (scope)
        {
            var held = Enum.GetValues(typeof(AutomationActionFamily))
                .Cast<AutomationActionFamily>()
                .Where(family => !CanClaim(registry, family))
                .ToArray();
            Assert.Equal(families.OrderBy(family => family), held.OrderBy(family => family));
        }
    }

    private static bool CanClaim(ActionFamilyOwnershipRegistry registry, AutomationActionFamily family)
    {
        if (!registry.TryClaimSet(
                new ActionFamilyOwner(new FeatureStatusKey("tests", "probe"), "Probe"),
                new[] { family },
                out var lease,
                out _))
            return false;
        lease!.Dispose();
        return true;
    }
}
