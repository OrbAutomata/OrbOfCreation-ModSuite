using System;
using System.Linq;
using OrbAutomata.GameMcp;
using Xunit;
using Newtonsoft.Json.Linq;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.ProfileTests;

internal static class GameMcpEntityDetailFixture
{
    internal static readonly Guid ReadySpellId =
        Guid.Parse("10000000-0000-4000-8000-000000000001");
    internal static readonly Guid WaitingSpellId =
        Guid.Parse("10000000-0000-4000-8000-000000000002");
    internal static readonly Guid ReadyResearchId =
        Guid.Parse("20000000-0000-4000-8000-000000000001");
    internal static readonly Guid BlockedResearchId =
        Guid.Parse("20000000-0000-4000-8000-000000000002");
    internal static readonly Guid ReadyCraftingId =
        Guid.Parse("30000000-0000-4000-8000-000000000001");
    internal static readonly Guid BlockedCraftingId =
        Guid.Parse("30000000-0000-4000-8000-000000000002");

    internal static bool Predicate(JObject explanation, string name) =>
        (bool)explanation["predicates"]![name]!["available"]!;

    internal static JObject Explain(GameWorldState world, Guid id, ulong generation) =>
        GameMcpTestHarness.Detail(GameMcpTestHarness.Context(world, generation), id);

    /// <summary>Reads a runtime spell through the recipe detail that owns its equipped slots.</summary>
    internal static JObject EquippedSpell(GameWorldState world, in WorldSpellSlot slot)
    {
        world = world with { CollectedAtUtcTicks = DateTime.UtcNow.Ticks };
        var position = GameMcpSlotNumbering.Wire(slot.SlotIndex);
        if (!slot.Occupied)
        {
            var context = GameMcpTestHarness.Context(world);
            var publication = Assert.IsType<WorldPublication<GameWorldState>>(context.World);
            var page = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
                context, "spell-slots", 0, 200).Freeze(), publication.Snapshot);
            var rows = Assert.IsType<JArray>(page["rows"]);
            return Assert.Single(rows.Select(node => Assert.IsType<JObject>(node)),
                row => row.Value<int>("slot") == position);
        }

        // These fixtures model runtime slots. Supply their owning recipe so the same public read
        // a caller uses can reach them, retaining every existing row and report in the fixture.
        if (!WorldLookup.TryFind(world.SpellRecipes, slot.SpellRecipeId, out _))
        {
            world = world with
            {
                SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(
                    world.SpellRecipes.AsSpan().ToArray()
                        .Append(Spell(slot.SpellRecipeId, discovered: false, hidden: false, masteryLevel: 0))
                        .OrderBy(row => row.EntityId).ToArray()),
            };
        }
        if (!world.CollectionCategories.AsSpan().ToArray().Any(report =>
                GameMcpWorldCategories.Normalize(report.Category) == "spell-recipes"))
        {
            world = world with
            {
                CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                    world.CollectionCategories.AsSpan().ToArray().Append(
                        new WorldCollectionCategoryStatus("spell-recipes", WorldCategoryOutcome.Collected,
                            world.SpellRecipes.Count, 0, string.Empty)).ToArray()),
            };
        }
        var pinned = GameMcpTestHarness.Context(world);
        var pinnedPublication = Assert.IsType<WorldPublication<GameWorldState>>(pinned.World);
        var read = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            pinned, "spell-recipes", new[] { slot.SpellRecipeId.ToString("D") }).Freeze(),
            pinnedPublication.Snapshot);
        var results = Assert.IsType<JArray>(read["results"]);
        var block = Assert.IsType<JObject>(Assert.Single(results));
        var recipe = Assert.IsType<JObject>(block["row"]);
        var equipped = Assert.IsType<JArray>(recipe["equipped"]);
        return Assert.Single(equipped.Select(node => Assert.IsType<JObject>(node)),
            row => row.Value<int>("slot") == position);
    }

    internal static WorldSpellRecipe Spell(
        Guid id,
        bool discovered,
        bool hidden,
        int masteryLevel) => new(
            id,
            discovered,
            discRarityLevel: 0,
            masteryXp: BigDouble.Zero,
            masteryLevel,
            masteryLevelReady: false,
            hiddenDiscovery: hidden,
            isRequiredDiscovery: true,
            penaltyUsageCost: 1,
            castSpeed: 1,
            baseCharges: 1,
            repeatInstantEffects: false,
            spellPowerMod: BigDouble.One,
            spellCostMod: BigDouble.One,
            spellCdSpeedMod: BigDouble.One,
            spellDurationMod: BigDouble.One,
            spellSpecialMod: BigDouble.One,
            spellXpMod: BigDouble.One,
            hasAlertedThisMastery: false);

    internal static WorldResearch Research(
        Guid id,
        bool available,
        int level,
        int maxLevel,
        int baseRequirement,
        int effectiveRequirement,
        int leeway,
        PublicationTable<WorldResearchRequirementAdjustment>? adjustments = null,
        bool stillHasLeeway = true) => new(
            id,
            level,
            queuedLevels: 0,
            researchStage: 0,
            selfBonusLevels: 0,
            maxLevel,
            researchTime: 60,
            isDeveloping: false,
            isActive: false,
            flagged: false,
            available,
            visible: available,
            complete: maxLevel > 0 && level >= maxLevel,
            canDevelop: available && (maxLevel <= 0 || level < maxLevel),
            withinDevelopRange: available && (maxLevel <= 0 || level < maxLevel),
            meetsLevelRequirements: level + leeway >= effectiveRequirement,
            stillHasLeeway,
            belowArtificialMaxLevel: true,
            belowMaxInvestmentLevel: maxLevel <= 0 || level < maxLevel,
            purchasedLevels: level,
            baseLevel: level,
            bonusLevel: 0,
            totalLevel: level,
            artificialMaxLevel: 0,
            hiddenLevel: false,
            levelVisibilityRange: 2,
            requiredStagesCached: 0,
            requiredTimeCached: BigDouble.Zero,
            baseRequirement,
            effectiveRequirement,
            adjustments ?? PublicationTable<WorldResearchRequirementAdjustment>.Empty,
            new RawResearchModifiers(
                bonusLevels: BigDouble.Zero,
                baseLevels: BigDouble.Zero,
                power: new BigDouble(100d),
                maxLevelCap: BigDouble.Zero,
                leewayPoints: new BigDouble(leeway)));

    internal static WorldCraftingRecipe Crafting(
        Guid id,
        bool visible,
        bool canBuy,
        PublicationTable<WorldCraftingRecipeResource>? resources = null,
        PublicationTable<WorldCraftingRecipeDrainBlock>? drains = null)
    {
        var reading = new RawCraftingRecipeSample(
            id,
            visible,
            canBuy,
            startingQuantity: BigDouble.One,
            useQuantityAsLevel: false,
            timeToComplete: 1,
            outputWithinCapacity: true,
            completionEffectCount: 0);
        return new WorldCraftingRecipe(
            in reading,
            PublicationTable<WorldCraftingRecipeTypeLink>.Empty,
            resources ?? PublicationTable<WorldCraftingRecipeResource>.Empty,
            PublicationTable<WorldCraftingRecipeConsumableOutput>.Empty,
            drains ?? PublicationTable<WorldCraftingRecipeDrainBlock>.Empty);
    }

    internal static global::UpgradeSO Upgrade()
    {
        var upgrade = new global::UpgradeSO { maxLevel = -1 };
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }

    internal static global::ResourceSO Resource(bool inLedger)
    {
        var resource = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString("D"),
            visible = inLedger,
        };
        global::ResourceSO.All.Add(resource);
        return resource;
    }

    internal static global::StructureSO Structure()
    {
        var structure = new global::StructureSO();
        global::StructureSO.All.Add(structure);
        return structure;
    }

    internal static global::ResearchSO ResearchStub(
        int level,
        Guid? id = null,
        int maxLevel = 20)
    {
        var research = new global::ResearchSO
        {
            uuid = (id ?? Guid.NewGuid()).ToString("D"),
            level = level,
            maxLevel = maxLevel,
        };
        global::ResearchSO.All.Add(research);
        return research;
    }

    internal static Requirements.ResearchRequirement Require(
        global::ResearchSO target,
        double value) => new()
        {
            item = target,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = value },
        };

    internal static global::PrerequisiteLinkSO.LinkDefinition Tier(
        params Requirements.IRequirementCondition[] requirements)
    {
        var tier = new global::PrerequisiteLinkSO.LinkDefinition();
        foreach (var requirement in requirements)
            tier.prerequisites.prerequisites.Add(requirement);
        return tier;
    }

    internal static GameWorldState Collect()
    {
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        collector.Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }


}
