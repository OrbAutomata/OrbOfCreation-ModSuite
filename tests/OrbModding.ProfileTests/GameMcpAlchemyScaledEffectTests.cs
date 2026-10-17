using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// An alchemy recipe's <c>levelEffects</c> amount is authored, and the card prints it scaled.
/// </summary>
/// <remarks>
/// A round ran four Extract Thaumic Scrolls for the +8 Alchemical Capacity its row printed per copy
/// and gained +1.6. The card read +2: the block scales with the instance's power and speed, and the
/// recipe's usage requirement, Expert Alchemy 7, was unmet, so the Alchemy type's
/// <c>reqSpeedPenalty</c> ran it at a quarter speed. The row now says both.
/// </remarks>
[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpAlchemyScaledEffectTests : IDisposable
{
    private static readonly Guid ExtractThaumicScrolls =
        Guid.Parse("6628c5b7-f83b-40fe-92c9-631105d7f3fe");
    private static readonly Guid Alchemy = Guid.Parse("f9c93e42-e9e8-4fe3-a1f3-5aec5430b5c2");
    private static readonly Guid ExpertAlchemy = Guid.Parse("064d395a-4667-4bbb-b3b4-91bb03f67ba3");
    private static readonly Guid AlchemicalCapacity =
        Guid.Parse("e30d8b62-288d-458a-8eac-8ba00ee4fbc4");

    public GameMcpAlchemyScaledEffectTests() => global::IdScriptableObject.RuntimeLookup.Clear();

    public void Dispose() => global::IdScriptableObject.RuntimeLookup.Clear();

    [Fact]
    public void An_alchemy_recipes_level_effects_say_where_the_amount_holds()
    {
        var row = Row(World(expertAlchemyLevel: 3, penalty: QuarterSpeed));

        Assert.Equal("+8", (string?)Assert.Single(row["levelEffects"]!)["amount"]);
        Assert.Equal(
            "amounts are authored at Lv 1, 100 power and 100% speed; game_tooltip shows the " +
            "card's scaled number",
            (string?)row["levelEffectsAt"]);
    }

    [Fact]
    public void An_unmet_usage_requirement_names_itself_and_its_speed_penalty()
    {
        var row = Row(World(expertAlchemyLevel: 3, penalty: QuarterSpeed, costPenalty: SixteenCost));

        Assert.Equal(
            "Expert Alchemy at level 7 (at 3) not met: speed x0.250, drain cost x16 until it is",
            (string?)row["usageRequirement"]);
    }

    [Fact]
    public void A_met_requirement_or_a_type_without_a_penalty_says_nothing()
    {
        Assert.Null(Row(World(expertAlchemyLevel: 7, penalty: QuarterSpeed))["usageRequirement"]);
        Assert.Null(Row(World(expertAlchemyLevel: 3, penalty: default, costPenalty: default))
            ["usageRequirement"]);
    }

    private static readonly GameValueModifier QuarterSpeed =
        new(GameValueModifierType.MultiStacking, new BigDouble(0.25));

    private static readonly GameValueModifier SixteenCost =
        new(GameValueModifierType.MultiStacking, new BigDouble(16));

    private static JObject Row(GameWorldState world)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(983));
        var page = GameMcpWorldQuery.GetRows(
            GameMcpTestHarness.Context(publisher.ReadLatest()),
            "alchemy-recipes",
            new[] { ExtractThaumicScrolls.ToString("D") });
        var json = Assert.IsType<JObject>(
            GameMcpDocumentJsonEncoder.Encode(page.Freeze(), world.EntityIdentities));
        return (JObject)Assert.Single(json["results"]!.Values<JObject>())!["row"]!;
    }

    private static GameWorldState World(
        int expertAlchemyLevel,
        GameValueModifier penalty,
        GameValueModifier costPenalty = default)
    {
        var recipe = new WorldAlchemyRecipe(
            ExtractThaumicScrolls, Alchemy, discovered: true, maxLevel: 1, advancementLevel: 0,
            discoveryRarityLevel: 0, masteryXp: BigDouble.Zero, masteryLevel: 0,
            recipeTime: BigDouble.One, isRequiredDiscovery: false,
            isCompletionRecipe: false, isAdvancementRecipe: false, completionTime: 0,
            isDebugAlchemy: false, power: BigDouble.Zero, speed: BigDouble.Zero,
            drainCostMod: BigDouble.Zero, special: BigDouble.Zero,
            timeReqMod: BigDouble.Zero, timeScalingMod: BigDouble.Zero,
            masteryXpRate: BigDouble.Zero, effectLevels: BigDouble.Zero,
            overdrivePower: BigDouble.Zero, overdriveSpeed: BigDouble.Zero,
            overdriveDrainCostMod: BigDouble.Zero, overdriveXpRate: BigDouble.Zero,
            freeUsageSlots: BigDouble.One, maxUsageSlots: new BigDouble(8),
            cachedCompletionTime: BigDouble.Zero, requiredExperience: BigDouble.One);
        var modifiers = new RawResearchModifiers(BigDouble.Zero, BigDouble.Zero,
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero);
        var research = new WorldResearch(ExpertAlchemy, expertAlchemyLevel, 0, 0, 0, 10, 60,
            false, false, false, true, true, false, true, true,
            true, true, true, true,
            expertAlchemyLevel, expertAlchemyLevel, 0, expertAlchemyLevel, 10, false, 2, 1,
            new BigDouble(60), 1, 1,
            PublicationTable<WorldResearchRequirementAdjustment>.Empty, in modifiers);
        var usage = new WorldEntityRequirement(
            ExtractThaumicScrolls,
            WorldRequirementOwnerKind.AlchemyRecipe,
            0,
            WorldRequirementConditionKind.Research,
            "ResearchRequirement",
            ExpertAlchemy,
            2,
            7,
            default,
            default,
            WorldRequirementProgramKind.Usage);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(
                ExtractThaumicScrolls, "AlchemyRecipeSO", "Extract Thaumic Scrolls",
                "DismantleThaumicScrolls"),
            new EntityIdentityName(Alchemy, "AlchemyTypeSO", "Alchemy", "Alchemy"),
            new EntityIdentityName(ExpertAlchemy, "ResearchSO", "Expert Alchemy", "ExpertAlchemy"),
            new EntityIdentityName(
                AlchemicalCapacity, "ResourceSO", "Alchemical Capacity", "BandwidthAlch"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 91,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(new[] { recipe }),
            AlchemyTypes = PublicationTable<WorldAlchemyType>.Create(new[]
            {
                new WorldAlchemyType(Alchemy, Guid.Empty, false, BigDouble.One, penalty, costPenalty),
            }),
            Research = PublicationTable<WorldResearch>.Create(new[] { research }),
            EntityRequirements = PublicationTable<WorldEntityRequirement>.Create(new[] { usage }),
            LevelEffects = PublicationTable<WorldLevelEffect>.Create(new[]
            {
                new WorldLevelEffect(
                    ExtractThaumicScrolls, 0, "MaxQuantityFunctional", AlchemicalCapacity,
                    (int)GameValueModifierType.Raw, new BigDouble(8), 0),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                GameMcpWorldQuery.RegisteredCategoryNames()
                    .Select(name => new WorldCollectionCategoryStatus(
                        name, WorldCategoryOutcome.Collected, 0, 0, string.Empty))
                    .ToArray()),
        };
    }
}
