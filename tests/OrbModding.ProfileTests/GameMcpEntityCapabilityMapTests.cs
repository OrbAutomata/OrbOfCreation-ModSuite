using System;
using System.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpEntityCapabilityMapTests
{
    [Fact]
    public void EveryReadCategoryAndGameplayCapabilityHasExactlyOneAuthoritativeDescriptor()
    {
        var readCategories = GameMcpWorldQuery.RegisteredCategoryNames()
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var descriptors = GameMcpEntityCapabilityMap.Entries;
        var mappedCategories = descriptors
            .Select(static descriptor => descriptor.Category)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(readCategories, mappedCategories);
        Assert.Equal(mappedCategories.Length, mappedCategories.Distinct(StringComparer.Ordinal).Count());
        Assert.All(descriptors, descriptor =>
            Assert.False(string.IsNullOrWhiteSpace(descriptor.ExpectedNativeType)));

        var mappedGameplay = descriptors
            .SelectMany(static descriptor => descriptor.Capabilities)
            .Distinct()
            .OrderBy(static kind => kind)
            .ToArray();
        var declaredGameplay = Enum.GetValues<GameMcpCommandKind>()
            .Where(GameMcpCommandKinds.IsEntityGameplayAction)
            .OrderBy(static kind => kind)
            .ToArray();
        Assert.Equal(declaredGameplay, mappedGameplay);
    }

    [Fact]
    public void AdvertisedToolsHaveCanonicalKindsAndInternalDiscoveryKindsShareOneNamespace()
    {
        var commandTools = GameMcpAcceptanceFixture.Tools()
            .Select(tool => (string)tool["name"]!)
            .Where(name =>
                name.StartsWith("game_", StringComparison.Ordinal) ||
                name.StartsWith("time_", StringComparison.Ordinal) ||
                name is "suite_config_set" or "suite_automation" or "suite_emergency_stop")
            .ToArray();
        var mappings = commandTools
            .Select(name => (Name: name, Kind: GameMcpCommandKinds.FromToolName(name)))
            .ToArray();

        Assert.Equal(32, mappings.Length);
        Assert.Equal(32, mappings.Select(mapping => mapping.Kind).Distinct().Count());
        Assert.Equal(
            new[]
            {
                "game_purchase",
                "game_cast",
                "game_concept",
                "game_agromancy",
                "game_structure",
                "game_spell_level",
                "game_discover",
                "game_equipment",
                "time_challenge",
                "time_prestige",
                "game_research",
                "game_alchemy",
                "game_ritual",
                "game_level",
                "game_loadout",
                "game_casting_dial",
                "game_spell_loadout",
                "game_targeting",
                "game_consumable",
                "game_craft",
            }.OrderBy(name => name, StringComparer.Ordinal),
            mappings
                .Where(mapping => GameMcpCommandKinds.IsEntityGameplayAction(mapping.Kind))
                .Select(mapping => mapping.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(
            mappings.Where(mapping => GameMcpCommandKinds.IsEntityGameplayAction(mapping.Kind)),
            mapping => Assert.Contains(
                GameMcpEntityCapabilityMap.Entries,
                descriptor => descriptor.Capabilities.Contains(mapping.Kind)));
        Assert.Equal("game_discover",
            GameMcpCommandKinds.ToolName(GameMcpCommandKind.DiscoveryTreeOffer));
        Assert.Equal("game_discover",
            GameMcpCommandKinds.ToolName(GameMcpCommandKind.SpellWorkbench));
        Assert.Equal(GameMcpCommandKind.Harvest,
            GameMcpCommandKinds.FromRequest(
                "game_agromancy", "add_plot_action", string.Empty));
        Assert.Equal(GameMcpCommandKind.HarvestLifecycle,
            GameMcpCommandKinds.FromRequest(
                "game_agromancy", "add_element", string.Empty));
        Assert.Throws<ArgumentException>(() =>
            GameMcpCommandKinds.FromToolName("game_arbitrary_reflection"));
    }
}
