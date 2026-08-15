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
                name is "suite_config_set" or "suite_breakers" or "suite_emergency_stop")
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
                "game_spell_mastery",
                "game_discover",
                "game_equipment",
                "time_challenge",
                "time_prestige",
                "game_research",
                "game_alchemy",
                "game_ritual",
                "game_level_up",
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

    /// <summary>
    /// A category naming several native types answers for each of them. The catalog called the two
    /// snapshot-list types <c>not-world-projected</c> while <c>world_list snapshot-loadouts</c>
    /// answered for them on the same build, because the type lookup compared the whole descriptor
    /// against one type name and a pipe-joined descriptor equals none.
    /// </summary>
    [Fact]
    public void APipeJoinedCategoryAnswersForEveryTypeItNames()
    {
        Assert.True(GameMcpEntityCapabilityMap.TryCategoryForNativeType(
            "AlchemySnapshotListVariable", out var alchemyList));
        Assert.Equal("snapshot-loadouts", alchemyList);
        Assert.True(GameMcpEntityCapabilityMap.TryCategoryForNativeType(
            "EquipmentSnapshotListVariable", out var equipmentList));
        Assert.Equal("snapshot-loadouts", equipmentList);
        Assert.True(GameMcpEntityCapabilityMap.TryCategoryForNativeType(
            "AlchemySnapshot", out var alchemySlot));
        Assert.Equal("snapshot-slots", alchemySlot);
        Assert.True(GameMcpEntityCapabilityMap.TryCategoryForNativeType(
            "EquipmentSnapshot", out var equipmentSlot));
        Assert.Equal("snapshot-slots", equipmentSlot);
    }

    /// <summary>
    /// One type, one category. A type a single-type descriptor owns keeps that owner even where a
    /// pipe-joined category also lists it, a type several pipe-joined categories list has no single
    /// answer, and a name that is only part of one of those types is not one of them.
    /// </summary>
    [Fact]
    public void TheSingleTypeOwnerWinsAndAPartialNameIsNeverAType()
    {
        Assert.True(GameMcpEntityCapabilityMap.TryCategoryForNativeType("UpgradeSO", out var upgrade));
        Assert.Equal("upgrades", upgrade);
        Assert.True(GameMcpEntityCapabilityMap.TryCategoryForNativeType(
            "AlchemyRecipeSO", out var recipe));
        Assert.Equal("alchemy-recipes", recipe);
        Assert.False(GameMcpEntityCapabilityMap.TryCategoryForNativeType(
            "EquipmentSnapshotList", out var partial));
        Assert.Equal(string.Empty, partial);
        Assert.False(GameMcpEntityCapabilityMap.TryCategoryForNativeType(
            "SnapshotListVariable", out var suffix));
        Assert.Equal(string.Empty, suffix);
    }
}
