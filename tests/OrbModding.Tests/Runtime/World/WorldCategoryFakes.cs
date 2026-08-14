using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Stand-ins for the entity categories world collection walks but no test in this file is about.
/// </summary>
/// <remarks>
/// They exist so the collector tests can assert a complete pass without reaching for the shared game
/// stubs, whose registries other test classes populate and which would make a count here depend on
/// what else happens to be running. Each spells out exactly the members its binder requires, so a
/// binder that grows a member fails to bind here before it fails anywhere else.
/// <para>
/// The real member names are checked against the shipped assemblies by the native contract manifest,
/// and the shared stubs cover by-name resolution end to end. These types cover neither; they cover
/// the traversal.
/// </para>
/// </remarks>
internal static class WorldCategoryFakes
{
    /// <summary>Maps each game type name to its stand-in, for a collector test's resolver.</summary>
    internal static IReadOnlyDictionary<string, Type?> ByTypeName { get; } = new Dictionary<string, Type?>(StringComparer.Ordinal)
    {
        ["AlchemyRecipeSO"] = typeof(FakeAlchemyRecipe),
        ["AlchemyTypeSO"] = typeof(FakeAlchemyType),
        ["AlchemyManager"] = typeof(FakeAlchemyManager),
        ["AlchemyInstanceListVariable"] = typeof(FakeAlchemyInstanceList),
        ["AlchemyInstance"] = typeof(FakeAlchemyInstance),
        ["AlchemyRecipeListVariable"] = typeof(FakeAlchemyRecipeList),
        ["InstanceScalingSO"] = typeof(FakeInstanceScaling),
        ["ModifierListRef"] = typeof(FakeModifierListRef),
        ["ModifierListVariable"] = typeof(FakeModifierListVariable),
        ["ValueModifierList"] = typeof(FakeValueModifierList),
        ["ValueModifierRecord"] = typeof(FakeModifierRecord),
        ["GlobalValues"] = typeof(FakeGlobalValues),
        ["SpellRecipeSO"] = typeof(FakeSpellRecipe),
        ["Spell"] = typeof(FakeSpell),
        ["SpellManager"] = typeof(FakeSpellManager),
        ["SpellTypeSO"] = typeof(FakeSpellType),
        ["EquipmentSO"] = typeof(FakeEquipment),
        ["EquipmentTypeSO"] = typeof(FakeEquipmentType),
        ["EquipmentManager"] = typeof(FakeEquipmentManager),
        ["EquipmentListVariable"] = typeof(FakeEquipmentList),
        ["ResourceTypeSO"] = typeof(FakeResourceType),
        ["CraftingRecipeTypeSO"] = typeof(FakeCraftingRecipeType),
        ["CraftingRecipeSO"] = typeof(FakeScribeRecipe),
        ["CraftingStructureSO"] = typeof(FakeCraftingStructure),
        ["CraftingStructure"] = typeof(FakeCraftingStation),
        ["CraftingStructureListVariable"] = typeof(FakeCraftingStationList),
        ["CraftingStructureSO+TypeListElement"] = typeof(FakeCraftingStationElementList),
        ["CraftingStructureSO+TypeElement"] = typeof(FakeCraftingStationElement),
        ["ITooltipable"] = typeof(IFakeTooltipable),
        ["TooltipableObject"] = typeof(FakeCraftingStationTooltipable),
        ["CraftingRecipeListVariable"] = typeof(FakeScribeRecipeList),
        ["CraftingInstanceListVariable"] = typeof(FakeScribeInstanceList),
        ["CraftingInstance"] = typeof(FakeScribeInstance),
        ["ResourceCostList"] = typeof(FakeCraftingResourceCostList),
        ["ResourceTuple"] = typeof(FakeCraftingResourceTuple),
        ["HarvestElementSO"] = typeof(FakeHarvestElement),
        ["HarvestActionSO"] = typeof(FakeHarvestAction),
        ["HarvestActionInstance"] = typeof(FakeHarvestActionInstance),
        ["HarvestElementListVariable"] = typeof(FakeHarvestElementList),
        ["HarvestActionInstanceListVariable"] = typeof(FakeHarvestActionList),
        ["TimeRuneSO"] = typeof(FakeTimeRune),
        ["GlyphSO"] = typeof(FakeGlyph),
        ["GlyphListVariable"] = typeof(FakeGlyphList),
        ["ConsumableSO"] = typeof(FakeConsumable),
        ["EnchantmentSO"] = typeof(FakeScribeEnchantment),
        ["EnchantmentSO+EnchantTable"] = typeof(FakeScribeEnchantTable),
        ["EnchantmentInstance"] = typeof(FakeScribeEnchantmentInstance),
        ["ScalingInfo"] = typeof(FakeScribeScalingInfo),
        ["BigDouble"] = typeof(BigDouble),
        ["InstantEffectBlock"] = typeof(FakeScribeInstantBlock),
        ["EffectBlock"] = typeof(FakeCraftingEffectBlock),
        ["IInstantEffectScript"] = typeof(IFakeScribeInstantScript),
        ["ConsumableSO+ConsumableGainEffect"] = typeof(FakeScribeConsumableGainEffect),
        ["RequestTargetEffectScript"] = typeof(FakeScribeRequestTargetEffect),
        ["Targeting.TargetSelectOptions"] = typeof(FakeScribeTargetOptions),
        ["Targeting.BaseTargetSelection"] = typeof(FakeScribeBaseTargetSelection),
        ["Targeting.TargetStructure"] = typeof(FakeScribeTargetStructure),
        ["Targeting.ITargetable"] = typeof(IFakeScribeTargetable),
        ["EnchantmentSO+EnchantItemScript"] = typeof(FakeScribeEnchantItemScript),
        ["RitualSO"] = typeof(FakeRitual),
        ["RitualManager"] = typeof(FakeRitualManager),
        ["RitualVariable"] = typeof(FakeRitualVariable),
        ["AchievementSO"] = typeof(FakeAchievement),
        ["AdvancementSO"] = typeof(FakeAdvancement),
        ["ChallengeSO"] = typeof(FakeChallenge),
        ["ThoughtStreamSO"] = typeof(FakeThoughtStream),
        ["TutorialSO"] = typeof(FakeTutorial),
        ["ViewSO"] = typeof(FakeView),
        ["PlotNodeActionSO"] = typeof(FakePlotNodeAction),
        ["PassiveAbilitySO"] = typeof(FakePassiveAbility),
        ["CharacterSO"] = typeof(FakeCharacter),
        ["DiscoveryTreeSO"] = typeof(FakeDiscoveryTree),
        ["RecipeBookSO"] = typeof(FakeRecipeBook),
        ["PlotNodeSO"] = typeof(FakePlotNode),
        ["TreasurePoolSO"] = typeof(FakeTreasurePool),
        ["ValueModifierVariable"] = typeof(FakeModifierVariable),
        ["RitualTypeSO"] = typeof(FakeRitualType),
        ["HarvestTypeSO"] = typeof(FakeHarvestType),
        ["PlotNodeTypeSO"] = typeof(FakePlotNodeType),
        ["ConsumableTypeSO"] = typeof(FakeConsumableType),
        ["HarvestActionTypeSO"] = typeof(FakeHarvestActionType),
        ["PassiveAbilityTypeSO"] = typeof(FakePassiveAbilityType),
        ["TimeRuneTypeSO"] = typeof(FakeTimeRuneType),
        ["ChallengeTypeSO"] = typeof(FakeChallengeType),

        // Not a category either: the action queues belong to no per-type registry, so the reader
        // reaches them through the identity registry by uuid.
        ["IdScriptableObject"] = typeof(FakeIdRegistry),
        ["PlotNodeActionInstanceListVariable"] = typeof(FakeActionQueue),
        ["ActionableListVariable"] = typeof(FakeAttributeQueue),

        // Nor is the equipped loadout: Spell belongs to no per-type registry either, so the list
        // holding it is reached by uuid the same way and the slots are read out of that list.
        ["SpellListVariable"] = typeof(FakeSpellLoadout),
        ["LoadoutManager"] = typeof(FakeLoadoutManager),
        ["PlayerLoadoutListVariable"] = typeof(FakePlayerLoadoutListVariable),
        ["PlayerLoadout"] = typeof(FakePlayerLoadout),
        ["PlayerLoadout+LoadoutLabel"] = typeof(FakePlayerLoadout.LoadoutLabel),
        ["AlchemySnapshotListVariable"] = typeof(FakeAlchemySnapshotListVariable),
        ["EquipmentSnapshotListVariable"] = typeof(FakeEquipmentSnapshotListVariable),
        ["AlchemySnapshot"] = typeof(FakeAlchemySnapshot),
        ["EquipmentSnapshot"] = typeof(FakeEquipmentSnapshot),
        ["Stacked.StackedIdRecord`1"] = typeof(FakeLoadoutRecord<>),

        // Not categories either: an effect block's one modifier and one script are read past their
        // counts, and the lists holding them are typed as interfaces, so the two kinds the suite
        // knows how to read are reached by name.
        ["ScalingWeightEffectMod"] = typeof(FakeScalingWeightMod),
        ["TreasurePoolInstantEffect"] = typeof(FakeTreasureEffect),
    };

    /// <summary>Empties every registry, so one test cannot see another's entities.</summary>
    internal static void Clear()
    {
        FakeAlchemyRecipe.All.Clear();
        FakeAlchemyType.All.Clear();
        FakeAlchemyManager.instance = new FakeAlchemyManager();
        FakeSpellRecipe.All.Clear();
        FakeSpellManager.instance = null;
        FakeLoadoutManager.instance = new FakeLoadoutManager();
        FakeSpellType.All.Clear();
        FakeSpellManager.NativeCanCast = true;
        FakeEquipment.All.Clear();
        FakeEquipmentManager.instance = new FakeEquipmentManager();
        FakeEquipmentType.All.Clear();
        FakeResourceType.All.Clear();
        FakeCraftingRecipeType.All.Clear();
        FakeScribeRecipe.All.Clear();
        FakeCraftingStructure.All.Clear();
        FakeHarvestElement.All.Clear();
        FakeHarvestAction.All.Clear();
        FakeTimeRune.All.Clear();
        FakeGlyph.All.Clear();
        FakeConsumable.All.Clear();
        FakeRitual.All.Clear();
        FakeRitualManager.instance = new FakeRitualManager();
        FakeAchievement.All.Clear();
        FakeAdvancement.All.Clear();
        FakeChallenge.All.Clear();
        FakeChallengeType.All.Clear();
        FakeRitualType.All.Clear();
        FakeHarvestType.All.Clear();
        FakePlotNodeType.All.Clear();
        FakeConsumableType.All.Clear();
        FakeHarvestActionType.All.Clear();
        FakePassiveAbilityType.All.Clear();
        FakeTimeRuneType.All.Clear();
        FakeThoughtStream.All.Clear();
        FakeTutorial.All.Clear();
        FakeView.All.Clear();
        FakePlotNodeAction.All.Clear();
        FakePassiveAbility.All.Clear();
        FakeCharacter.All.Clear();
        FakeDiscoveryTree.All.Clear();
        FakeRecipeBook.All.Clear();
        FakePlotNode.All.Clear();
        FakeTreasurePool.All.Clear();
        FakeModifierVariable.All.Clear();
        FakeIdRegistry.RuntimeLookup.Clear();
        UnityEngine.Resources.Objects.Clear();
        SeedScribeRelations();
        SeedHarvestLifecycle();
        SeedGlyphLists();
    }

    private static void SeedScribeRelations()
    {
        FakeIdRegistry.RuntimeLookup[KnownEntities.ScribeCraftingRecipes.Uuid] =
            new FakeScribeRecipeList();
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveScribeInstances.Uuid] =
            new FakeScribeInstanceList();
        FakeIdRegistry.RuntimeLookup[KnownEntities.AutoScribeInstances.Uuid] =
            new FakeScribeInstanceList { isAutoList = true };
        FakeIdRegistry.RuntimeLookup[KnownEntities.ScribeCrafting.Uuid] =
            new FakeCraftingRecipeType
            {
                Identity = KnownEntities.ScribeCrafting.Uuid,
                maxStartingLevel = 1,
            };

        foreach (var (scrollId, enchantmentId) in new[]
                 {
                     (KnownEntities.ScrollAdvancement.Uuid, KnownEntities.EnchantAdvancement.Uuid),
                     (KnownEntities.ScrollDevelopment.Uuid, KnownEntities.EnchantDevelopment.Uuid),
                     (KnownEntities.ScrollEcho.Uuid, KnownEntities.EnchantEcho.Uuid),
                     (KnownEntities.ScrollExcellence.Uuid, KnownEntities.EnchantExcellence.Uuid),
                     (KnownEntities.ScrollInvestment.Uuid, KnownEntities.EnchantInvestment.Uuid),
                     (KnownEntities.ScrollLearning.Uuid, KnownEntities.EnchantLearning.Uuid),
                     (KnownEntities.ScrollPower.Uuid, KnownEntities.EnchantPower.Uuid),
                     (KnownEntities.ScrollSpeed.Uuid, KnownEntities.EnchantSpeed.Uuid),
                 })
        {
            var enchantment = new FakeScribeEnchantment { Identity = enchantmentId };
            var block = new FakeScribeInstantBlock();
            block.effectScripts.Add(new FakeScribeRequestTargetEffect());
            block.effectScripts.Add(new FakeScribeEnchantItemScript
            {
                enchantment = enchantment,
            });
            var scroll = new FakeConsumable { Identity = scrollId };
            scroll.onUseEffects.Add(block);
            FakeIdRegistry.RuntimeLookup[enchantmentId] = enchantment;
            FakeIdRegistry.RuntimeLookup[scrollId] = scroll;
        }
    }

    internal static FakeHarvestElementList ActiveHarvestElements { get; private set; } = new();
    internal static FakeHarvestActionList ActiveHarvestActions { get; private set; } = new();

    internal static FakeGlyphList AugmentSpellGlyphs { get; private set; } = new();

    /// <summary>
    /// The four pinned lists always resolve, because the reader publishes membership whole or
    /// withholds it whole and a fake world that omitted one would report the whole category missing.
    /// </summary>
    private static void SeedGlyphLists()
    {
        AugmentSpellGlyphs = new FakeGlyphList { Identity = KnownEntities.GlyphsAugmentSpell.Uuid };
        FakeIdRegistry.RuntimeLookup[KnownEntities.GlyphsAugmentSpell.Uuid] = AugmentSpellGlyphs;
        foreach (var listId in new[]
                 {
                     KnownEntities.GlyphsCoreSpell.Uuid,
                     KnownEntities.GlyphsCoreAlchemy.Uuid,
                     KnownEntities.GlyphsEquipment.Uuid,
                 })
        {
            FakeIdRegistry.RuntimeLookup[listId] = new FakeGlyphList { Identity = listId };
        }
    }

    private static void SeedHarvestLifecycle()
    {
        ActiveHarvestElements = new FakeHarvestElementList();
        ActiveHarvestActions = new FakeHarvestActionList();
        FakeIdRegistry.RuntimeLookup[Guid.Parse("5a9f8001-3ae2-4799-86b6-5198763e0fe2")] =
            ActiveHarvestElements;
        FakeIdRegistry.RuntimeLookup[Guid.Parse("e4a9d4c3-61cc-4f94-bab9-7bc8e841cc32")] =
            ActiveHarvestActions;
    }
}

/// <summary>
/// The game's identity registry, which is how an entity belonging to no per-type <c>All</c> list is
/// reached. Only the action queues need it.
/// </summary>
internal class FakeIdRegistry
{
    public static readonly Dictionary<Guid, object> RuntimeLookup = new();

    public Guid Identity = Guid.NewGuid();

    public Guid GetGuid() => Identity;
}

/// <summary>
/// Stand-ins for the keyword taxonomies the keyword capture reads identity alone from.
/// </summary>
internal sealed class FakePassiveAbilityType : FakeIdRegistry
{
    public static readonly List<FakePassiveAbilityType> All = new();

    public FakeModifierRecord cooldown = new(0d);
    public FakeModifierRecord costMod = new(0d);
    public FakeModifierRecord durationMod = new(0d);
    public FakeModifierRecord maxStacksMod = new(0d);
    public FakeModifierRecord power = new(0d);
}

internal sealed class FakeTimeRuneType : FakeIdRegistry
{
    public static readonly List<FakeTimeRuneType> All = new();

    public bool initialized;
    public FakeModifierRecord freeUsages = new(0d);
    public FakeModifierRecord masteryXpMod = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord powerScalingMod = new(0d);
    public FakeModifierRecord totalLevel = new(0d);
}

internal sealed class FakeGlyphType : FakeIdRegistry
{
}

internal sealed class FakeHarvestActionType : FakeIdRegistry
{
    public static readonly List<FakeHarvestActionType> All = new();

    public FakeModifierRecord costMod = new(0d);
    public FakeModifierRecord growthSizeMod = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord refundRating = new(0d);
    public FakeModifierRecord speed = new(0d);
}

internal sealed class FakeRitualType : FakeIdRegistry
{
    public static readonly List<FakeRitualType> All = new();

    public bool initiated;
    public FakeModifierRecord activeRituals = new(0d);
    public FakeModifierRecord chainLengthBonus = new(0d);
    public FakeModifierRecord chainPower = new(0d);
    public FakeModifierRecord completionCostMod = new(0d);
    public FakeModifierRecord completionRateMod = new(0d);
    public FakeModifierRecord critDurationMod = new(0d);
    public FakeModifierRecord critPower = new(0d);
    public FakeModifierRecord critRating = new(0d);
    public FakeModifierRecord durationMod = new(0d);
    public FakeModifierRecord echoPower = new(0d);
    public FakeModifierRecord echoRating = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord special = new(0d);
    public FakeModifierRecord speed = new(0d);
}

internal sealed class FakeCharacterType : FakeIdRegistry
{
}

internal sealed class FakePlotNodeType : FakeIdRegistry
{
    public static readonly List<FakePlotNodeType> All = new();

    public FakeModifierRecord actionCostMod = new(0d);
    public FakeModifierRecord actionSpeed = new(0d);
    public FakeModifierRecord actionXpRate = new(0d);
    public FakeModifierRecord growingSpeed = new(0d);
    public FakeModifierRecord qualityMod = new(0d);
    public FakeModifierRecord recoverySizeMod = new(0d);
    public FakeModifierRecord restingSpeed = new(0d);
    public FakeModifierRecord sizeMod = new(0d);
    public FakeModifierRecord specialMod = new(0d);
    public FakeModifierRecord totalLevel = new(0d);
    public FakeModifierRecord yieldMod = new(0d);
}

internal sealed class FakeHarvestType : FakeIdRegistry
{
    public static readonly List<FakeHarvestType> All = new();

    public FakeModifierRecord autoGenerationMod = new(0d);
    public FakeModifierRecord drainCostMod = new(0d);
    public FakeModifierRecord experienceRateMod = new(0d);
    public FakeModifierRecord growthSpeedMod = new(0d);
    public FakeModifierRecord harvestSpeedMod = new(0d);
    public FakeModifierRecord level = new(0d);
    public FakeModifierRecord maxQuantity = new(0d);
    public FakeModifierRecord maxRestGrowth = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord qualityMod = new(0d);
    public FakeModifierRecord restingGrowthSpeedMod = new(0d);
}

/// <summary>The draft's weighted buckets. No modifier surface at all, exactly as the game has none.</summary>
internal sealed class FakeChallengeType : FakeIdRegistry
{
    public static readonly List<FakeChallengeType> All = new();

    public double weight;
    public bool restrictedInstances;
    public bool excludeFromRandomSelection;
}

internal class FakeAbstractListVariable : FakeIdRegistry
{
}

/// <summary>One of the four authored glyph populations, reached by the identity it carries.</summary>
internal sealed class FakeGlyphList : FakeIdRegistry
{
    public List<FakeGlyph> value = new();
}

internal class FakeAbstractListVariable<T> : FakeAbstractListVariable
{
    public List<T> value = new();
}

internal sealed class FakeScribeRecipeList
{
    public List<FakeScribeRecipe> value = new();
}

internal sealed class FakeScribeRecipe
{
    public static readonly List<FakeScribeRecipe> All = new();

    public Guid Identity = Guid.NewGuid();
    public List<FakeCraftingRecipeType> craftingTypes = new();
    public FakeCraftingResourceCostList recipeCost = new();
    public FakeCraftingResourceCostList generatedResources = new();
    public List<FakeCraftingEngagementBlock> engagementEffects = new();
    public List<FakeScribeInstantBlock> completeEffects = new();
    public bool useQuantityAsLevel = false;
    public double timeToComplete;
    public bool visible = true;
    public bool canBuy = true;
    public BigDouble startingQuantity = BigDouble.One;
    public FakeCraftingRecipeType MainType = new();

    public Guid GetGuid() => Identity;
    public bool IsVisible() => visible;
    public BigDouble GetStartingQuantity() => startingQuantity;
    public bool CanBuy() => visible && canBuy;
    public bool CanBuyAt(BigDouble quantity) =>
        canBuy && quantity.CompareTo(BigDouble.Zero) > 0;
    public BigDouble GetPurchaseQuantity(BigDouble previousQuantity) =>
        useQuantityAsLevel ? startingQuantity : BigDouble.One;
    public FakeCraftingResourceCostList GetTotalCost(
        BigDouble previousQuantity,
        BigDouble purchasedQuantity) => recipeCost;
    public FakeCraftingRecipeType GetMainType() => MainType;
}

internal sealed class FakeCraftingResourceCostList
{
    public List<FakeCraftingResourceTuple> costs = new();
    public bool withinCapacity = true;
    public bool affordable = true;
    public bool affordabilityUsesResourceAmounts;
    public BigDouble maximumCostTimes = new(int.MaxValue);

    public bool IsWithinCapacity() => withinCapacity;
    public bool HasEnough() => affordable &&
        (!affordabilityUsesResourceAmounts || costs.All(cost =>
            cost.resource is not null &&
            cost.valueBig.CompareTo(cost.resource.GetTrueQuantity()) <= 0));
    public bool AllResourcesVisible() => costs.All(cost => cost.resource?.Visible == true);
    public BigDouble MaximumCostTimes() => maximumCostTimes;
    public bool IsEmpty() => costs.Count == 0;
    public List<FakeCraftingResourceTuple> GetEntries() => costs;

    public FakeCraftingResourceCostList Multiply(BigDouble factor)
    {
        var result = new FakeCraftingResourceCostList
        {
            withinCapacity = withinCapacity,
            affordable = affordable,
            affordabilityUsesResourceAmounts = affordabilityUsesResourceAmounts,
        };
        for (var index = 0; index < costs.Count; index++)
            result.costs.Add(new FakeCraftingResourceTuple
            {
                resource = costs[index].resource,
                valueBig = costs[index].valueBig * factor,
            });
        return result;
    }

    public FakeCraftingResourceCostList Add(FakeCraftingResourceCostList other)
    {
        affordable &= other.affordable;
        affordabilityUsesResourceAmounts |= other.affordabilityUsesResourceAmounts;
        for (var index = 0; index < other.costs.Count; index++)
        {
            var incoming = other.costs[index];
            var existing = costs.FindIndex(row => ReferenceEquals(row.resource, incoming.resource));
            if (existing < 0)
                costs.Add(new FakeCraftingResourceTuple
                    { resource = incoming.resource, valueBig = incoming.valueBig });
            else
                costs[existing].valueBig += incoming.valueBig;
        }
        return this;
    }

    public FakeCraftingResourceCostList Subtract(FakeCraftingResourceCostList other)
    {
        var result = new FakeCraftingResourceCostList
        {
            withinCapacity = withinCapacity,
            affordable = affordable,
            affordabilityUsesResourceAmounts = affordabilityUsesResourceAmounts,
        };
        foreach (var entry in costs)
            result.costs.Add(new FakeCraftingResourceTuple
                { resource = entry.resource, valueBig = entry.valueBig });
        foreach (var entry in other.costs)
        {
            var existing = result.costs.FindIndex(row => ReferenceEquals(row.resource, entry.resource));
            if (existing < 0)
                result.costs.Add(new FakeCraftingResourceTuple
                    { resource = entry.resource, valueBig = -entry.valueBig });
            else
                result.costs[existing].valueBig -= entry.valueBig;
        }
        return result;
    }

    internal FakeCraftingResourceCostList With(FakeResource resource, BigDouble amount)
    {
        costs.Add(new FakeCraftingResourceTuple { resource = resource, valueBig = amount });
        return this;
    }
}

internal sealed class FakeCraftingResourceTuple
{
    public FakeResource? resource;
    public BigDouble valueBig;

    public BigDouble GetValue() => valueBig;
}

internal interface IFakeTooltipable
{
    string GetName();
}

internal sealed class FakeCraftingStationTooltipable : IFakeTooltipable
{
    public Guid Identity = Guid.NewGuid();

    public Guid GetGuid() => Identity;
    public string GetName() => string.Empty;
}

internal sealed class FakeCraftingStationElement
{
    public IFakeTooltipable? tooltipable;
    public bool available = true;

    public IFakeTooltipable? GetTooltipable() => tooltipable;

    public bool IsAvailable() => available;
}

internal sealed class FakeCraftingStationElementList
{
    public List<FakeCraftingStationElement> elements = new();

    public List<FakeCraftingStationElement> GetElements() => elements;
}

internal sealed class FakeCraftingStation
{
    public Guid Identity = Guid.NewGuid();
    public FakeCraftingStructure? reference;
    public FakeCraftingStationGuid recipeId = new(Guid.Empty);
    public FakeCraftingStationElement? firstIngredient;
    public FakeCraftingStationElement? secondIngredient;
    public FakeCraftingStationElement? output;
    public List<FakeCraftingStationElement> outputOptions = new();
    public FakeCraftingResourceCostList drain = new();
    public bool loaded;
    public bool active;
    public int level = 1;
    public int minimumLevel = 1;
    public int maximumLevel = 1;

    public Guid GetGuid() => Identity;

    public FakeCraftingStructure? get_reference() => reference;

    public FakeCraftingStationElement? GetIngredient(int slot) =>
        slot == 0 ? firstIngredient : secondIngredient;

    public FakeCraftingStationElement? GetOutput() => output;

    public List<FakeCraftingStationElement> GetOutputList() => outputOptions;

    public bool IsOutputVisible(FakeCraftingStationElement element) => element.IsAvailable();

    public bool IsLoaded() => loaded;

    public bool IsActive() => active;

    public int GetLevel() => level;

    public int GetMinSelectedLevel() => minimumLevel;

    public int GetMaxSelectedLevel() => maximumLevel;

    public FakeCraftingResourceCostList GetCurrentDrain() => drain;
}

internal sealed class FakeCraftingStationGuid
{
    private readonly Guid _guid;

    internal FakeCraftingStationGuid(Guid guid) => _guid = guid;
}

internal sealed class FakeCraftingStructure
{
    public static readonly List<FakeCraftingStructure> All = new();

    public Guid Identity = Guid.NewGuid();
    public FakeCraftingStationList instances = new();
    public List<FakeCraftingStationElementList> ingredientLists = new();

    public Guid GetGuid() => Identity;
}

internal sealed class FakeCraftingStationList
{
    public List<FakeCraftingStation> value = new();

    public List<FakeCraftingStation> GetAll() => value;
}

internal class FakeCraftingEffectBlock
{
    public BigDouble necessaryDrainRatio = BigDouble.One;

    private BigDouble GetEffectNecessaryDrainRatio() => necessaryDrainRatio;
}

internal sealed class FakeCraftingEngagementBlock : FakeCraftingEffectBlock
{
}

internal sealed class FakeScribeInstanceList
{
    public Guid Identity = Guid.NewGuid();
    public List<FakeScribeInstance> value = new();
    public bool isAutoList;
    public int Maximum = 4;

    public int GetMax() => Maximum;
    public Guid GetGuid() => Identity;
    public bool HasEmptySpot() => value.Count < Maximum;

    public BigDouble GetQuantity(FakeScribeRecipe recipe)
    {
        var quantity = BigDouble.Zero;
        for (var index = 0; index < value.Count; index++)
            if (value[index].RecipeId == recipe.Identity) quantity += value[index].Quantity;
        return quantity;
    }
}

internal sealed class FakeScribeInstance
{
    public Guid RecipeId = Guid.Empty;
    public BigDouble Quantity = BigDouble.Zero;
    public bool Automatic = false;
    public bool Expired = false;
    public int AutomationQuantity = 0;

    public Guid GetGuidReference() => RecipeId;
    public BigDouble GetQuantity() => Quantity;
    public int GetAutomationQuantity() => AutomationQuantity;
    public bool IsAuto() => Automatic;
    public bool IsExpired() => Expired;
}

internal interface IFakeScribeInstantScript
{
}

internal sealed class FakeScribeInstantBlock
{
    public List<IFakeScribeInstantScript> effectScripts = new();
}

internal sealed class FakeScribeConsumableGainEffect : IFakeScribeInstantScript
{
    public FakeConsumable? consumable = null;
}

internal sealed class FakeScribeRequestTargetEffect : IFakeScribeInstantScript
{
    public FakeScribeTargetOptions targetOptions = new();
}

internal sealed class FakeScribeEnchantItemScript : IFakeScribeInstantScript
{
    public FakeScribeEnchantment? enchantment;
}

internal sealed class FakeScribeEnchantment
{
    public Guid Identity = Guid.NewGuid();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeScribeEnchantTable
{
    public List<FakeScribeEnchantmentInstance> enchantments = new();
}

internal sealed class FakeScribeEnchantmentInstance
{
    public Guid EnchantmentId = Guid.Empty;
    public int Level = 0;
    public Guid GetGuidReference() => EnchantmentId;
    public int GetLevel() => Level;
}

internal sealed class FakeScribeScalingInfo
{
    public BigDouble DrainCostMod = new(100);
    public BigDouble GetDrainCostMod() => DrainCostMod;
    public static FakeScribeScalingInfo Basic(BigDouble level) => new();
}

internal class FakeScribeBaseTargetSelection
{
}

internal interface IFakeScribeTargetable
{
}

internal sealed class FakeScribeTargetStructure : FakeScribeBaseTargetSelection
{
    public List<IFakeScribeTargetable> GetRandomList(FakeScribeScalingInfo scaling) => new();
}

internal sealed class FakeScribeTargetOptions
{
    public FakeScribeBaseTargetSelection GetTargeting() => new FakeScribeTargetStructure();
}

/// <summary>
/// An action queue: a list of slots, plus the game's own answers about how many are in use.
/// </summary>
internal sealed class FakeActionQueue
{
    public Guid Identity = Guid.NewGuid();
    public List<FakeQueueSlot?> value = new();

    /// <summary>
    /// What the game answers about occupancy when a test wants it to disagree with the slots. The
    /// two agreeing is what the published reading claims, so a test needs to be able to break it.
    /// </summary>
    public int ReportedUsedSpots = -1;

    public Guid GetGuid() => Identity;

    public int GetUsedSpots()
    {
        if (ReportedUsedSpots >= 0) return ReportedUsedSpots;

        var used = 0;
        foreach (var slot in value)
        {
            if (slot is not null && !slot.IsEmpty()) used++;
        }

        return used;
    }

    public bool HasEmptySpot() => GetUsedSpots() < value.Count;
}

/// <summary>
/// The queue whose occupancy is effectively an integer: entries of every kind the game queues, and a
/// variable saying how many of them it admits.
/// </summary>
internal sealed class FakeAttributeQueue
{
    public Guid Identity = Guid.NewGuid();
    public List<object?> value = new();
    public FakeQueueCapacity? maxQueuedItems;

    public Guid GetGuid() => Identity;

    public int GetUsedSpots()
    {
        var used = 0;
        foreach (var entry in value)
        {
            if (entry is not null) used++;
        }

        return used;
    }

    public bool HasEmptySpot() => maxQueuedItems is null || GetUsedSpots() < maxQueuedItems.Maximum;
}

/// <summary>The global variable a queue's maximum lives in, which is a row of its own elsewhere.</summary>
internal sealed class FakeQueueCapacity
{
    public Guid Identity = Guid.NewGuid();
    public int Maximum;

    public Guid GetGuid() => Identity;
}

internal class FakeQueueSlot
{
    public int quantity;
    public bool engaged;
    public FakePlotNode? plot;
    public FakePlotNodeAction? action;

    public bool IsEmpty() => quantity <= 0;

    public bool IsEngaged() => engaged;

    public int GetActualQuantity() => quantity;

    public FakePlotNode? GetElement() => plot;

    public FakePlotNodeAction? GetAction() => action;
}

/// <summary>
/// Something the list will hold that the reader's accessors were not bound against. The game's own
/// list is typed too, so this is the only shape a foreign entry can take — and the one the action
/// boundary already refuses to read.
/// </summary>
internal sealed class FakeForeignQueueSlot : FakeQueueSlot
{
}

/// <summary>
/// The global modifier registry's stand-in. Its value is an inline struct, which is the shape the
/// binder's nested reads require and the only category that has it.
/// </summary>
internal sealed class FakeModifierVariable
{
    public static readonly List<FakeModifierVariable> All = new();

    public Guid Identity = Guid.NewGuid();
    public FakeValueModifier value;

    public Guid GetGuid() => Identity;
}

internal struct FakeValueModifier
{
    public BigDouble adjustReal;
    public FakeModifierKind type;
    public int order;
    public global::IdScriptableObject? reference;

    internal FakeValueModifier(
        FakeModifierKind type,
        double amount,
        int order,
        global::IdScriptableObject? reference = null)
        : this(type, new BigDouble(amount), order, reference)
    {
    }

    /// <summary>
    /// The same modifier at a magnitude a double cannot hold. The game's own amount is a BigDouble
    /// and this game's modifiers live past 1e308, so a fake that could only be built from a double
    /// could not exercise the range the fold exists for.
    /// </summary>
    internal FakeValueModifier(
        FakeModifierKind type,
        BigDouble amount,
        int order = 0,
        global::IdScriptableObject? reference = null)
    {
        this.type = type;
        adjustReal = amount;
        this.order = order;
        this.reference = reference;
    }
}

/// <summary>Positionally identical to the game's ValueModifierType, which is what travels.</summary>
internal enum FakeModifierKind
{
    Raw,
    MultiDiminishing,
    MultiStacking,
    Reduction,
    Exponent,
}

/// <summary>
/// The game nests each category's state enum inside the category. One stand-in enum serves them all:
/// collection reads the underlying integer and never the member names.
/// </summary>
internal enum FakeState
{
    Idle,
    Active,
    Done,
}

/// <summary>
/// A modifier record shaped as the game shapes it: a base value, two modifier sets, a memo, and the
/// flag that decides which of the last two the game answers with. <see cref="GetValueCalls"/> is how
/// the tests prove the collector never calls the accessor.
/// </summary>
internal sealed class FakeModifierRecord
{
    private BigDouble calculatedValue;

    /// <summary>
    /// Whether the memo is out of date. The game sets it when a modifier is added or removed and
    /// clears it in Calculate(), so a record that never carries a modifier is never dirty — and its
    /// memo is what the game answers with for the rest of the session.
    /// </summary>
    private bool calculationDirty;

    /// <summary>What the collector folds from when the record is dirty. Named as the game names it.</summary>
    public double baseValue;

    public Dictionary<Guid, FakeValueModifier> passiveModifiers = new();

    public Dictionary<Guid, FakeValueModifier> activeModifiers = new();

    internal FakeModifierRecord(double value)
    {
        baseValue = value;
        calculatedValue = value;
    }

    /// <summary>
    /// With <paramref name="activeCount"/> modifiers that are all their type's identity, so the
    /// active-set count the collector publishes moves while the value does not — which is exactly the
    /// pair of facts the count tests want to vary independently.
    /// </summary>
    internal FakeModifierRecord(double value, int activeCount)
        : this(value)
    {
        for (var index = 0; index < activeCount; index++)
        {
            activeModifiers[Guid.NewGuid()] = default;
        }

        if (activeCount > 0) calculationDirty = true;
    }

    internal int GetValueCalls { get; private set; }

    /// <summary>Whether the memo is out of date, so a test can state the shape it built.</summary>
    internal bool IsCalculationDirty => calculationDirty;

    /// <summary>Sets the memo away from a recomputation, the way a save load or a purchase does.</summary>
    internal FakeModifierRecord WithStaleCache(double cached)
    {
        calculatedValue = cached;
        return this;
    }

    /// <summary>
    /// Marks the memo out of date, the way the game does whenever a modifier is added or removed. A
    /// record carrying a modifier without this is a shape the game cannot produce.
    /// </summary>
    internal FakeModifierRecord Dirty()
    {
        calculationDirty = true;
        return this;
    }

    public BigDouble GetValue()
    {
        GetValueCalls++;
        return calculatedValue;
    }

    /// <summary>
    /// Purpose-built support for the research fake's native evaluator. The regression fixture uses
    /// the authored challenge shape: one passive raw adjustment. Other modifier kinds are rejected
    /// instead of turning this test double into a second implementation of native modifier math.
    /// </summary>
    internal int AdjustRawLevel(int value)
    {
        BigDouble adjusted = value;
        foreach (var modifier in passiveModifiers.Values)
        {
            if (modifier.type != FakeModifierKind.Raw)
                throw new InvalidOperationException("The research fake only models raw requirement adjustments.");
            adjusted += modifier.adjustReal;
        }
        foreach (var modifier in activeModifiers.Values)
        {
            if (modifier.type != FakeModifierKind.Raw)
                throw new InvalidOperationException("The research fake only models raw requirement adjustments.");
            adjusted += modifier.adjustReal;
        }
        return adjusted.ToInt();
    }
}

internal sealed class FakeValueModifierList
{
    public List<FakeValueModifier> modifiers = new();
    public List<FakeValueModifier> exponents = new();
}

internal sealed class FakeModifierListVariable
{
    public Guid Identity = Guid.NewGuid();
    public FakeValueModifierList value = new();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeModifierListRef
{
    public FakeModifierListVariable? variable = new();
    public FakeValueModifierList GetValue() => variable?.value ?? new FakeValueModifierList();
}

internal sealed class FakeGlobalValues
{
    public static readonly FakeGlobalValues instance = new();
    public FakeModifierListVariable spellLevelingStandard = new();
}

internal sealed class FakePrerequisiteContainer
{
    public bool result = true;
    public List<object> prerequisites = new();
    public bool Check() => result;
}

internal sealed class FakeScalingConversion
{
    public Dictionary<FakeScalingKind, FakeValueModifierList> values = new();
}

internal enum FakeScalingKind
{
    CostMod = 4,
    Speed = 6,
}

internal sealed class FakeInstanceScaling
{
    public Guid Identity = Guid.NewGuid();
    public bool useRarity;
    public List<FakeScalingKind> rarityAttributeBlacklist = new();
    public FakeScalingConversion instanceScaling = new();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeInstanceScalingRef
{
    public FakeInstanceScaling scaling = new();
}

internal sealed class FakeAlchemyRecipe
    : FakeIdRegistry, global::IDiscoverable
{
    public static readonly List<FakeAlchemyRecipe> All = new();
    public List<FakeAlchemyType> alchemyTypes = new();

    public bool discovered;

    /// <summary>
    /// Which gate this recipe's row is behind. <c>Discover</c> is what all 125 authored recipes on
    /// the pinned build carry, and it is the branch on which <c>discovered</c> is the whole lock.
    /// </summary>
    public FakeVisibilityType visibilityType = FakeVisibilityType.Discover;

    public enum FakeVisibilityType
    {
        Discover = 0,
        Prerequisite = 1,
    }

    public int maxLevel;
    public int advancementLevel;
    public int discRarityLevel;
    public BigDouble masteryXp;
    public int masteryLevel;
    public BigDouble recipeTime;

    public bool isRequiredDiscovery;
    public bool isCompletionRecipe;
    public bool isAdvancementRecipe;
    public double completionTime;
    public bool isDebugAlchemy;
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord speed = new(0d);
    public FakeModifierRecord drainCostMod = new(0d);
    public FakeModifierRecord special = new(0d);
    public FakeModifierRecord timeReqMod = new(0d);
    public FakeModifierRecord timeScalingMod = new(0d);
    public FakeModifierRecord masteryXpRate = new(0d);
    public FakeModifierRecord effectLevels = new(0d);
    public FakeModifierRecord overdrivePower = new(0d);
    public FakeModifierRecord overdriveSpeed = new(0d);
    public FakeModifierRecord overdriveDrainCostMod = new(0d);
    public FakeModifierRecord overdriveXpRate = new(0d);
    public FakeModifierRecord freeUsageSlots = new(0d);
    public FakeModifierRecord maxUsageSlots = new(0d);
    public BigDouble cachedCompletionTime;
    // Deliberately orphaned like the v1.0.5 field. The collector must not read this.
    public BigDouble cachedRequiredXp;
    public FakeExperienceContainer experienceContainer = new();
    public FakeSpellCostList drainCost = new();
    public FakeCraftingResourceCostList usageCost = new();
    public FakeSpellCostList bandwidthCost = new();
    public FakeModifierListRef completionCostAdvanceMod = new();
    public FakeModifierListRef drainCostLevelMod = new();
    public FakePrerequisiteContainer usagePrerequisites = new();
    public FakeInstanceScalingRef instanceScaling = new();
    public global::ResourceCostList genericDiscoveryCost = new();
    public List<global::GlyphSO> genericDiscoveryGlyphs = new();
    public List<global::ResourceSO> genericDiscoveryResources = new();
    public bool NativeDiscoverVisible = true;
    public bool NativeCanDiscover = true;
    public FakeAlchemyType coreType = new();

    public FakeAlchemyType GetCoreType() => coreType;
    public bool IsDiscovered() => discovered;
    public FakeCraftingResourceCostList GetUsageCost() => usageCost;
    public int GetFreeUsageSlots() => freeUsageSlots.GetValue().ToInt();

    public BigDouble GetRequiredExperience() =>
        experienceContainer.GetRequiredExperience();

    global::ResourceCostList global::IDiscoverable.GetDiscoverCost() => genericDiscoveryCost;
    List<global::GlyphSO> global::IDiscoverable.GetGlyphRecipe() => new(genericDiscoveryGlyphs);
    List<global::ResourceSO> global::IDiscoverable.GetResourceRecipe() => new(genericDiscoveryResources);
    bool global::IDiscoverable.IsDiscoverVisible() => NativeDiscoverVisible;
    bool global::IDiscoverable.CanDiscover() => NativeCanDiscover;
    bool global::IDiscoverable.IsDiscovered() => discovered;
    bool global::IDiscoverable.IsDiscoverRequired() => isRequiredDiscovery;
    void global::IDiscoverable.Discover() => discovered = true;
    Guid global::IHasGuid.GetGuid() => GetGuid();

    public int GetMaxUsageSlots()
    {
        if (coreType.maxUsageByMastery) return masteryLevel + 1;
        var maximum = maxUsageSlots.GetValue().ToDouble();
        return maximum < 0 ? int.MaxValue : (int)Math.Floor(maximum);
    }
}

internal sealed class FakeExperienceContainer
{
    public BigDouble cachedRequiredXp;

    public BigDouble GetRequiredExperience() => cachedRequiredXp;
}

internal sealed class FakeAlchemyRecipeList
{
    internal FakeAlchemyRecipeList()
        : this(new List<FakeAlchemyRecipe>())
    {
    }

    internal FakeAlchemyRecipeList(List<FakeAlchemyRecipe> value) => this.value = value;

    public List<FakeAlchemyRecipe> value;
}

internal sealed class FakeAlchemyInstanceList
{
    public List<FakeAlchemyInstance> value = new();
    public int maximum = 8;

    public bool CanAddInstance(FakeAlchemyRecipe recipe) => true;
    public int GetMax() => maximum;

    public FakeLoadoutRecord<FakeAlchemyRecipe> CreateStackedRecord()
    {
        var result = new FakeLoadoutRecord<FakeAlchemyRecipe>();
        foreach (var instance in value)
            if (instance.reference is not null && instance.quantity > 0)
                result.Set(instance.reference, instance.quantity);
        return result;
    }

    public void FromStackedRecord(FakeLoadoutRecord<FakeAlchemyRecipe> record)
    {
        value.Clear();
        foreach (var entry in record.GetEntries())
            value.Add(new FakeAlchemyInstance(entry.Item1) { quantity = entry.Item2 });
    }
}

internal sealed class FakeAlchemyManager
{
    public static FakeAlchemyManager instance = new();
    public FakeAlchemyInstanceList activeAlchemy = new();
    public FakeAlchemyRecipeList allAlchemy = new(FakeAlchemyRecipe.All);
}

internal class FakeAbstractRefInstance<T>
    where T : class
{
    protected FakeAbstractRefInstance(T? recipe)
    {
        reference = recipe;
    }

    public T? reference;

    public T? get_reference() => reference;

    public bool IsEmpty() => reference is null;
}

internal class FakeAlchemyInstance : FakeAbstractRefInstance<FakeAlchemyRecipe>
{
    public FakeAlchemyInstance(FakeAlchemyRecipe? recipe)
        : base(recipe)
    {
    }

    public int quantity;
    public int queuedQuantity;
    public FakeAlchemyDrain resourceDrain = new();

    public int GetQueuedQuantity() => queuedQuantity;
    public int GetRemainingFreeUsageSlots() =>
        Math.Max((reference?.GetFreeUsageSlots() ?? 0) - queuedQuantity, 0);
    public int GetRemainingMaxUsageSlots() =>
        Math.Max((reference?.GetMaxUsageSlots() ?? 0) - queuedQuantity, 0);
    public FakeConceptDrainMultiplier GetDrainCostMod() =>
        new(new BigDouble(quantity));
}

internal readonly struct FakeConceptDrainMultiplier
{
    private readonly BigDouble _percent;

    internal FakeConceptDrainMultiplier(BigDouble multiplier) =>
        _percent = multiplier * new BigDouble(100d);

    public BigDouble AsPercent() => _percent / new BigDouble(100d);
}

internal sealed class FakeUnexpectedAlchemyInstance : FakeAlchemyInstance
{
    internal FakeUnexpectedAlchemyInstance(FakeAlchemyRecipe recipe)
        : base(recipe)
    {
    }
}

internal sealed class FakeAlchemyDrain
{
    public bool isDrainApplied = true;
    public BigDouble currentRatio = new(1d);
    public BigDouble usageRatio = new(1d);
    public FakeSpellCostList current = new();

    public FakeSpellCostList GetCurrentDrain() => current;
}

internal sealed class FakeAlchemyType
{
    public static readonly List<FakeAlchemyType> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool maxUsageByMastery;
    public FakeModifierRecord level = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord speed = new(0d);
    public FakeModifierRecord special = new(0d);
    public FakeModifierRecord drainCostMod = new(0d);
    public FakeModifierRecord experienceRate = new(0d);
    public FakeModifierRecord overdrivePower = new(0d);
    public FakeModifierRecord overdriveSpeed = new(0d);
    public FakeModifierRecord overdriveDrainCostMod = new(0d);
    public FakeModifierRecord overdriveXpRate = new(0d);
    public FakeModifierRecord timeReqMod = new(0d);
    public FakeModifierRecord timeScalingMod = new(0d);
    public FakeModifierRecord freeUsageSlots = new(0d);
    public FakeModifierRecord effectLevels = new(0d);
    public FakeValueModifier reqCostPenalty;
    public FakeValueModifier reqSpeedPenalty;
    public FakeReferencedEntity? selectedLevel;

    public Guid GetGuid() => Identity;
}

/// <summary>
/// Something a reference edge can point at. Answers <c>GetGuid()</c> like every game entity does.
/// </summary>
internal sealed class FakeReferencedEntity
{
    public Guid Identity = Guid.NewGuid();

    public Guid GetGuid() => Identity;
}

/// <summary>The game's other reference shape: an identity already held as one.</summary>
internal sealed class FakeGuidContainer
{
    private Guid _guid;

    internal FakeGuidContainer()
    {
    }

    internal FakeGuidContainer(Guid guid) => _guid = guid;

    public Guid get_guid() => _guid;
}

/// <summary>
/// The equipped loadout, as a list variable with a uuid. Holes are real: the game lets a player leave
/// a position unfilled, and the position still counts toward the index a cast is addressed by.
/// </summary>
internal sealed class FakeSpellLoadout
{
    public Guid Identity = Guid.NewGuid();
    public List<FakeSpell?> value = new();
    public int maximum = 8;

    public Guid GetGuid() => Identity;
    public List<FakeSpell?> GetAll() => value;
    public int GetUsedSpots() => value.Count(spell => spell is not null);
    public int GetMax() => maximum;
    public bool HasEmptySpot() => GetUsedSpots() < maximum;
}

/// <summary>One equipped spell, answering exactly what the loadout reader asks it.</summary>
internal sealed class FakeSpell
{
    public List<FakeSpellType> augmentedSpellTypes = new();
    public FakeReferencedEntity guidContainer = new();
    public FakeSpellRecipe? spellReference;
    public bool empty;
    public bool casting;
    public bool readyingCast;
    public bool attuning;
    public bool channeled;
    public bool toggled;
    public bool chargeable;
    public bool castReady = true;
    public bool chargeAvailable = true;
    public bool resourcesCovered = true;
    public int currentCharges;
    public int maximumCharges;
    public BigDouble cooldownRemaining;
    public FakeSpellCostList cost = new();
    public FakeSpellCostList drainCost = new();
    public int outputLevel = 1;
    public int effectiveLevel = 1;
    public int requiredMasteryLevel;
    public bool durationSpell;
    public bool usageRequirementsMet = true;
    public int CanRemoveCalls { get; private set; }
    public List<FakeGlyph> augmentGlyphs = new();

    /// <summary>The game's own manual-cast counter, read as the field <c>Spell.numCasts</c> is.</summary>
    public int numCasts;

    public FakeSpellRecipe? get_reference() => spellReference;
    public Guid GetId() => guidContainer.Identity;

    public bool IsEmpty() => empty;
    public bool IsCasting() => casting;
    public bool IsReadyingCast() => readyingCast;
    public bool IsAttuning() => attuning;
    public bool IsChanneled() => channeled;
    public bool IsToggledSpell() => toggled;
    public bool CanCharge() => chargeable;
    public bool CanCast() => castReady;
    public bool IsChargeAvailable() => chargeAvailable;
    public bool CanRemove()
    {
        CanRemoveCalls++;
        return IsChargeAvailable() && !IsCasting();
    }
    public bool HasEnoughResources() => resourcesCovered;
    public int GetCurrSpellCharges() => currentCharges;
    public int GetMaxSpellCharges() => maximumCharges;
    public BigDouble GetCooldownTimeRemaining() => cooldownRemaining;
    public FakeSpellCostList GetCost() => cost;
    public FakeSpellCostList GetDrainCost() => drainCost;
    public int GetOutputLevel() => outputLevel;
    public int GetLevel() => effectiveLevel;
    public int GetRequiredLevel() => requiredMasteryLevel;
    public int GetRecipeMasteryLevel() => spellReference?.masteryLevel ?? 0;
    public bool IsDurationSpell() => durationSpell;
    public bool HasMetUsageRequirements() => usageRequirementsMet;
    public List<FakeGlyph> GetAugmentGlyphs() => new(augmentGlyphs);
    public int GetQuantityOfGlyph(FakeGlyph glyph) =>
        augmentGlyphs.Count(candidate => ReferenceEquals(candidate, glyph));
}

/// <summary>What a spell answers when asked its price, in the game's own cost-list shape.</summary>
internal sealed class FakeSpellCostList
{
    public List<FakeSpellCostEntry> costs = new();

    internal FakeSpellCostList With(Guid resource, double amount)
    {
        costs.Add(new FakeSpellCostEntry(resource, amount));
        return this;
    }

    public FakeSpellCostList Multiply(BigDouble multiplier)
    {
        var result = new FakeSpellCostList();
        foreach (var entry in costs)
            result.costs.Add(new FakeSpellCostEntry(
                entry.resource.GetGuid(),
                (entry.valueBig * multiplier).ToDouble()));
        return result;
    }
}

/// <summary>
/// One priced resource. The magnitude is the BigDouble field, not the serialized double beside it.
/// </summary>
internal struct FakeSpellCostEntry
{
    public FakeReferencedEntity resource;
    public BigDouble valueBig;

    internal FakeSpellCostEntry(Guid resourceId, double amount)
    {
        resource = new FakeReferencedEntity { Identity = resourceId };
        valueBig = new BigDouble(amount, 0);
    }
}

internal sealed class FakeSpellRecipe : FakeIdRegistry, global::IDiscoverable
{
    private List<FakeSpellType> notSpellTypes = new();
    public static readonly List<FakeSpellRecipe> All = new();

    public bool discovered;
    public int discRarityLevel;
    public BigDouble masteryExperience;
    public int masteryLevel;
    public bool readyToLevel;
    public FakeExperienceContainer masteryXpContainer = new();
    public FakeSpellLevelCost levelCost = new();
    public FakeSpellCostList baseLevelingCost = new();
    public FakeSpellCostList baseResourceCost = new();
    public FakeSpellCostList baseUsageCost = new();
    public FakeSpellCostList holdDrain = new();
    public List<FakeReferencedEntity> spellTypes = new();
    public FakeRecipeBookList recipeBookList = new();
    public FakeSpellCastType castType;
    public FakeDurationEntry baseRecharge = new();
    public FakeScalingValue maxChannel = new();
    public FakeScalingValue repeatInstantEffectRate = new();
    public bool hiddenDiscovery;
    public bool isRequiredDiscovery;
    public int penaltyUsageCost;
    public double castSpeed;
    public int baseCharges;
    public bool repeatInstantEffects;
    public FakeModifierRecord spellPowerMod = new(0d);
    public FakeModifierRecord spellCostMod = new(0d);
    public FakeModifierRecord spellCdSpeedMod = new(0d);
    public FakeModifierRecord spellDurationMod = new(0d);
    public FakeModifierRecord spellSpecialMod = new(0d);
    public FakeModifierRecord spellXpMod = new(0d);
    public bool hasAlertedThisMastery;
    public List<FakeGlyph> coreRecipe = new();
    public FakeSpellWorkbenchCostList discoveryCost = new();
    public global::ResourceCostList genericDiscoveryCost = new();
    public List<global::ResourceSO> genericDiscoveryResources = new();
    public bool NativeDiscoverVisible = true;
    public bool NativeCanDiscover = true;


    public bool IsReadyToLevelMastery() => readyToLevel;
    public List<FakeGlyph> GetGlyphRecipe() => new(coreRecipe);
    List<global::GlyphSO> global::IDiscoverable.GetGlyphRecipe()
    {
        var result = new List<global::GlyphSO>();
        foreach (var component in coreRecipe)
        {
            var glyph = new global::GlyphSO();
            glyph.SetGuid(component.Identity);
            result.Add(glyph);
        }
        return result;
    }
    List<global::ResourceSO> global::IDiscoverable.GetResourceRecipe() =>
        new(genericDiscoveryResources);
    public FakeSpellWorkbenchCostList GetDiscoverCost() => discoveryCost;
    global::ResourceCostList global::IDiscoverable.GetDiscoverCost()
    {
        if (genericDiscoveryCost.costs.Count > 0 || discoveryCost.costs.Count == 0)
            return genericDiscoveryCost;
        var translated = new global::ResourceCostList { affordable = discoveryCost.affordable };
        foreach (var cost in discoveryCost.costs)
        {
            var resource = new global::ResourceSO { quantity = cost.resource.amount };
            resource.SetGuid(cost.resource.Identity);
            translated.costs.Add(new global::ResourceTuple(resource, cost.amount));
        }
        return translated;
    }
    bool global::IDiscoverable.IsDiscoverVisible() => NativeDiscoverVisible;
    bool global::IDiscoverable.CanDiscover() => NativeCanDiscover;
    bool global::IDiscoverable.IsDiscovered() => discovered;
    bool global::IDiscoverable.IsDiscoverRequired() => isRequiredDiscovery;
    void global::IDiscoverable.Discover() => discovered = true;
    Guid global::IHasGuid.GetGuid() => GetGuid();
    public FakeSpellLevelCost GetLevelCost() => levelCost;
}

internal sealed class FakeSpellManager
{
    public static FakeSpellManager? instance;
    internal static bool NativeCanCast = true;

    public FakeSpellLoadout activeSpells = new();
    public static bool CanCastASpell() => NativeCanCast;
}

internal sealed class FakeSpellWorkbenchCostList
{
    public List<FakeSpellWorkbenchCost> costs = new();
    public bool affordable = true;

    public bool HasEnough() => affordable;
    public List<FakeSpellWorkbenchCost> GetEntries() => costs;
}

internal sealed class FakeSpellWorkbenchCost
{
    public FakeSpellWorkbenchResource resource = new();
    public BigDouble amount;

    public BigDouble GetValue() => amount;
}

internal sealed class FakeSpellWorkbenchResource
{
    public Guid Identity = Guid.NewGuid();
    public BigDouble amount;

    public Guid GetGuid() => Identity;
    public BigDouble GetQuantity() => amount;
    public BigDouble GetTrueQuantity() => amount;
}

internal sealed class FakeRecipeBookList
{
    public List<FakeReferencedEntity> recipeBooks = new();
}

internal sealed class FakeDurationEntry
{
    public double duration;
    public double mult;
    public FakeDurationProcessorType type;
}

internal enum FakeSpellCastType { Instant }
internal enum FakeDurationProcessorType { Fixed }

internal sealed class FakeScalingValue
{
    public double baseValue;
}

internal sealed class FakeSpellLevelCost
{
    public bool affordable = true;
    public bool HasEnough() => affordable;
}

internal sealed class FakeSpellType
{
    public static readonly List<FakeSpellType> All = new();

    public Guid Identity = Guid.NewGuid();
    public int typeLevel;
    public BigDouble typeXp;
    public double typeXpRequiredBase;
    public double augmentPowerMod;
    public bool hasNoLevels;
    public bool isElemental;
    public bool isLoadoutUnique;
    public bool hasNotTypeSignificance;
    public bool isVisible;
    public bool debugMode;
    public FakeModifierRecord typeXpMod = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord cooldownSpeed = new(0d);
    public FakeModifierRecord cooldownTime = new(0d);
    public FakeModifierRecord costMod = new(0d);
    public FakeModifierRecord drainCostMod = new(0d);
    public FakeModifierRecord durationMod = new(0d);
    public FakeModifierRecord elementalResonance = new(0d);
    public FakeModifierRecord augmentResonance = new(0d);
    public FakeModifierRecord maxStacksMod = new(0d);
    public FakeModifierRecord scalingMod = new(0d);
    public FakeModifierRecord usageCostReduction = new(0d);
    public FakeModifierRecord bonusCritRate = new(0d);
    public FakeModifierRecord critEffectMod = new(0d);
    public FakeModifierRecord critDurationMod = new(0d);
    public FakeModifierRecord bonusDoubleCastRate = new(0d);
    public FakeModifierRecord doubleCastEffectMod = new(0d);
    public FakeModifierRecord chargeTimeMod = new(0d);
    public FakeModifierRecord chargeEffectMod = new(0d);
    public FakeModifierRecord chargeSpecialMod = new(0d);
    public FakeModifierRecord bonusFlashRate = new(0d);
    public FakeModifierRecord flashEffectMod = new(0d);

    public Guid GetGuid() => Identity;
}

internal sealed class FakeEquipment : FakeIdRegistry, global::IDiscoverable
{
    public static readonly List<FakeEquipment> All = new();
    public List<FakeEquipmentType> subEquipmentTypes = new();

    public bool isCreated;
    public int discRarityLevel;
    public FakeEquipmentType equipmentType = new();
    public BigDouble masteryXp;
    public int masteryLevel;
    public bool isRequiredDiscovery;
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord baseLevel = new(0d);
    public FakeModifierRecord experienceRateMod = new(0d);
    public int equippedLevel;
    public int attuningLevel;
    public double attunementTimeLeft;
    public BigDouble baseXpRate;
    public global::ResourceCostList genericDiscoveryCost = new();
    public List<global::GlyphSO> genericDiscoveryGlyphs = new();
    public List<global::ResourceSO> genericDiscoveryResources = new();
    public FakeCraftingResourceCostList usageCost = new();
    public int maximumStacks = 4;
    public bool NativeDiscoverVisible = true;
    public bool NativeCanDiscover = true;

    public int GetMaxLevel() => maximumStacks;
    public FakeCraftingResourceCostList GetUsageCost() => usageCost;
    global::ResourceCostList global::IDiscoverable.GetDiscoverCost() => genericDiscoveryCost;
    List<global::GlyphSO> global::IDiscoverable.GetGlyphRecipe() => new(genericDiscoveryGlyphs);
    List<global::ResourceSO> global::IDiscoverable.GetResourceRecipe() => new(genericDiscoveryResources);
    bool global::IDiscoverable.IsDiscoverVisible() => NativeDiscoverVisible;
    bool global::IDiscoverable.CanDiscover() => NativeCanDiscover;
    bool global::IDiscoverable.IsDiscovered() => isCreated;
    bool global::IDiscoverable.IsDiscoverRequired() => isRequiredDiscovery;
    void global::IDiscoverable.Discover() => isCreated = true;
    Guid global::IHasGuid.GetGuid() => GetGuid();
}

internal sealed class FakeEquipmentType
{
    public static readonly List<FakeEquipmentType> All = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public int freeLevels;
    public int baseUsage;
    public FakeModifierRecord masteryLevel = new(0d);
    public FakeModifierRecord maxTypeSlots = new(0d);
    public FakeModifierRecord powerMod = new(0d);
    public FakeModifierRecord experienceRateMod = new(0d);
    public bool NativeCanLevel = true;
    public FakeCraftingResourceCostList LevelCost = new();
    public FakeCraftingResourceCostList BonusLevelCost = new();

    public Guid GetGuid() => Identity;
    public int GetMaxTypeSlots() => (int)maxTypeSlots.GetValue().ToDouble();
    public int GetLevel() => level + freeLevels;
    public int GetFreeLevels() => freeLevels;
    public bool CanLevel() => NativeCanLevel;
    public FakeCraftingResourceCostList GetLevelCost() => LevelCost;
    public FakeCraftingResourceCostList GetFreeLevelCost() => BonusLevelCost;
    public void PurchaseLevel() => level++;
    public void PurchaseFreeLevel() => freeLevels++;
}

internal sealed class FakeEquipmentList
{
    private readonly Dictionary<FakeEquipment, int> stacks = new();

    public List<FakeEquipment> value = new();
    public int maximum = 4;

    public int GetMax() => maximum;
    public bool IsAtMax() => value.Count >= maximum;
    public int GetStacks(FakeEquipment equipment) =>
        stacks.TryGetValue(equipment, out var quantity) ? quantity : 0;
    public int GetTypesEquipped(FakeEquipmentType equipmentType) =>
        value.Count(equipment => ReferenceEquals(equipment.equipmentType, equipmentType));

    internal void SetStacks(FakeEquipment equipment, int quantity)
    {
        if (quantity <= 0)
        {
            stacks.Remove(equipment);
            value.Remove(equipment);
            return;
        }
        stacks[equipment] = quantity;
        if (!value.Contains(equipment)) value.Add(equipment);
    }

    public FakeLoadoutRecord<FakeEquipment> GetStackedRecord()
    {
        var result = new FakeLoadoutRecord<FakeEquipment>();
        foreach (var pair in stacks) result.Set(pair.Key, pair.Value);
        return result;
    }

    public void SetStack(FakeLoadoutRecord<FakeEquipment> record)
    {
        stacks.Clear();
        value.Clear();
        foreach (var entry in record.GetEntries()) SetStacks(entry.Item1, entry.Item2);
    }
}

internal sealed class FakeEquipmentManager
{
    public static FakeEquipmentManager instance = new();
    public FakeEquipmentList equippedEquipment = new();
}

internal sealed class FakeResourceType
{
    public static readonly List<FakeResourceType> All = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public int freeLevels;
    public bool specialHidden;
    public bool ignoreAudit;
    public bool ignoreEffects;
    public bool auditHasMaxQuantity;
    public FakeModifierRecord rateMod = new(0d);
    public FakeModifierRecord maxQuantityMod = new(0d);
    public FakeModifierRecord maxQuantityRateMod = new(0d);
    public FakeModifierRecord qualityMod = new(0d);
    public FakeModifierRecord gainRateMod = new(0d);
    public FakeModifierRecord drainMod = new(0d);
    public FakeModifierRecord lossPercentMod = new(0d);
    public FakeModifierRecord restMod = new(0d);
    public FakeModifierRecord splashRate = new(0d);
    public FakeModifierRecord splashRateMaxPercent = new(0d);
    public FakeModifierRecord splashRateInterest = new(0d);
    public FakeModifierRecord splashRateMissing = new(0d);
    public FakeModifierRecord splashRateLifetime = new(0d);
    public FakeModifierRecord rawMaxQuantity = new(0d);

    public Guid GetGuid() => Identity;
    public FakeModifierRecord attributeCostMod = new(0d);
    public FakeModifierRecord reservationMod = new(0d);
    public FakeModifierRecord reverberateMod = new(0d);
    public FakeModifierRecord reverberateTimeMod = new(0d);
    public FakeModifierRecord replenishRatio = new(0d);
    public FakeModifierRecord replenishTimeMod = new(0d);
    public FakeModifierRecord decayRatio = new(0d);
    public FakeModifierRecord decayTimeMod = new(0d);
    public bool NativeCanLevel = true;
    public FakeCraftingResourceCostList LevelCost = new();
    public FakeCraftingResourceCostList BonusLevelCost = new();

    public int GetLevel() => level + freeLevels;
    public int GetFreeLevels() => freeLevels;
    public bool CanLevel() => NativeCanLevel;
    public FakeCraftingResourceCostList GetLevelCost() => LevelCost;
    public FakeCraftingResourceCostList GetFreeLevelCost() => BonusLevelCost;
    public void PurchaseLevel() => level++;
    public void PurchaseFreeLevel() => freeLevels++;
}

internal sealed class FakeCraftingRecipeType
{
    public static readonly List<FakeCraftingRecipeType> All = new();

    public Guid Identity = Guid.NewGuid();
    public int startingLevel;
    public int maxStartingLevel;
    public string craftVerb;
    public bool isLevelType;
    public bool initiated;
    public double magnitudeLoss;
    public double magnitudeTime;
    public FakeModifierRecord magnitudeIncrement = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord speed = new(0d);
    public FakeModifierRecord costMod = new(0d);
    public FakeModifierRecord costIncrementMod = new(0d);
    public FakeModifierRecord efficiencyMod = new(0d);
    public FakeModifierRecord autoPenaltyMod = new(0d);
    public FakeModifierRecord multiPenaltyMod = new(0d);

    public Guid GetGuid() => Identity;
}

/// <summary>
/// A resource. Shared rather than nested in one test class because the harvest element owns one, and
/// the collector reads that one with exactly the member list it reads every other resource with.
/// </summary>
internal sealed class FakeResource
{
    public static readonly List<FakeResource> All = new();
    public List<FakeResourceType> resourceTypes = new();

    public Guid Identity = Guid.NewGuid();
    public BigDouble Quantity;
    public BigDouble Rate;
    public bool Visible = true;
    public bool ThrowOnRate;
    public FakeModifierRecord maxQuantity = new(-1d);
    public BigDouble lifetimeQuantity;
    public BigDouble discoveryTime;
    public FakeModifierRecord quality = new(100d);
    public FakeModifierRecord gainRate = new(100d);
    public FakeModifierRecord drain = new(0d);
    public FakeModifierRecord reservationMod = new(100d);
    public FakeModifierRecord usage = new(0d);
    public FakeModifierRecord rate = new(0d);
    public FakeModifierRecord rateSplash = new(0d);
    public FakeModifierRecord rateMaxPercent = new(0d);
    public FakeModifierRecord rateInterestPercent = new(0d);
    public FakeModifierRecord rateMissingPercent = new(0d);
    public FakeModifierRecord rateLifetimePercent = new(0d);
    public FakeModifierRecord lossPercent = new(0d);
    public FakeModifierRecord displayRate = new(0d);
    public BigDouble calcRarityValue;
    public double baseLoss = 0.5d;
    public long appliedLevels;
    public FakeReferencedEntity? levelVariable;
    public double rarityValue;
    public double rarityValueEnd;
    public double restEngageTime;
    public bool pauseLossOnChange;
    public bool canOverflow;
    public bool noOverflowRubberBand;
    public bool bandwidthResource;
    public bool invertedResource;
    public bool excludeFromGlobals;
    public bool startVisible;
    public BigDouble appliedMaxQuantity;
    public int quantitySoftCapOrder;
    public int quantitySoftCapMagnitude;
    public double quantitySoftCapRatio;
    public bool debugResource;
    public double currentLossRate;
    public BigDouble lastReservation;
    public BigDouble debouncedReplenish;
    public BigDouble debouncedReverberate;
    public BigDouble debouncedDecay;
    public bool firstIncrement;
    public FakeModifierRecord maxQuantityRate = new(0d);
    public FakeModifierRecord maxQuantityFunctional = new(0d);
    public FakeModifierRecord restingRateMod = new(0d);
    public FakeModifierRecord attributeCostMod = new(100d);
    public FakeModifierRecord decayRatio = new(0d);
    public FakeModifierRecord decayTimeMod = new(0d);
    public FakeModifierRecord replenishRatio = new(0d);
    public FakeModifierRecord replenishTimeMod = new(0d);
    public FakeModifierRecord reverberateMod = new(0d);
    public FakeModifierRecord reverberateTimeMod = new(0d);
    public FakeModifierRecord rallyThreshold = new(0d);
    public FakeModifierRecord rallyMod = new(0d);
    public FakeModifierRecord usageDrainPenalty = new(0d);
    public bool inLossMode;
    public bool inRestMode;
    public bool inRallyMode;

    public Guid GetGuid() => Identity;

    public BigDouble GetQuantity() => Quantity;

    public BigDouble GetTrueQuantity() => Quantity;

    public BigDouble GetTrueRate() =>
        ThrowOnRate ? throw new InvalidOperationException("rate unavailable") : Rate;

    public bool IsVisible() => Visible;
}

internal sealed class FakeHarvestElement
{
    public static readonly List<FakeHarvestElement> All = new();
    public List<FakeHarvestType> harvestTypes = new();

    public Guid Identity = Guid.NewGuid();
    public BigDouble masteryXp;
    public int masteryLevel;
    public double harvestTime;
    public double growthTime;
    public double rarityValue;
    public double initialMaxQuantity;
    public double requiredXpToLevel;
    public FakeModifierRecord instances = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord harvestSpeedMod = new(0d);
    public FakeModifierRecord drainCostMod = new(0d);
    public FakeModifierRecord autoGenerationMod = new(0d);
    public FakeModifierRecord experienceRateMod = new(0d);
    public FakeModifierRecord actionXpRate = new(0d);
    public FakeModifierRecord actionPower = new(0d);
    public FakeModifierRecord actionSpeed = new(0d);
    public FakeModifierRecord actionCostMod = new(0d);
    public BigDouble harvestRate;
    public BigDouble lastOutputRate;
    public bool Visible = true;
    public bool Available = true;
    public int MaximumAdditional = 8;
    public FakeCraftingResourceCostList usageCost = new();
    public List<FakeHarvestActionInstance> ActionInstances = new();
    /// <summary>
    /// Private, created by the element rather than registered — the shape that keeps it out of the
    /// resource registry and makes reading it through its owner the only path.
    /// </summary>
    private FakeResource harvestResource = new();

    internal FakeResource Resource => harvestResource;


    public Guid GetGuid() => Identity;
    public bool IsVisible() => Visible;
    public bool IsAvailable() => Available;
    public BigDouble MaximumNumberInstances() => new(MaximumAdditional);
    public List<FakeHarvestActionInstance> GetActionInstances() => ActionInstances;
}

internal sealed class FakeHarvestAction
{
    public static readonly List<FakeHarvestAction> All = new();

    public List<FakeHarvestActionType> actionTypes = new();
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord speed = new(0d);
    public FakeModifierRecord costMod = new(0d);
    public Guid Identity = Guid.NewGuid();
    public bool Visible = true;
    public FakeCraftingResourceCostList DrainCost = new();
    public BigDouble NextDrainPercent = new(100);
    public Guid GetGuid() => Identity;
}

internal sealed class FakeHarvestActionInstance
{
    public FakeHarvestElement Element = null!;
    public FakeHarvestAction Action = null!;
    public int instances;
    public int Maximum = 1;

    public FakeHarvestAction GetAction() => Action;
    public FakeHarvestElement GetElement() => Element;
    public bool IsVisible() => Action.Visible;
    public int GetMaximumInstances() => Maximum;
    public FakeScribeScalingInfo GetScalingInfo(int count) => new()
    {
        DrainCostMod = Action.NextDrainPercent,
    };
    private FakeCraftingResourceCostList ComputeResourceCost() => Action.DrainCost;
}

internal sealed class FakeHarvestElementList
{
    private readonly Dictionary<FakeHarvestElement, int> _stacks = new();
    public bool HasSpace = true;
    public bool HasEmptySpot() => HasSpace;
    public int GetStacks(FakeHarvestElement element) =>
        _stacks.TryGetValue(element, out var count) ? count : 0;
    public void SetStacks(FakeHarvestElement element, int count) => _stacks[element] = count;
}

internal sealed class FakeHarvestActionList
{
    public List<FakeHarvestActionInstance> value = new();
    public bool HasSpace = true;
    public bool HasEmptySpot() => HasSpace;
}

internal sealed class FakeTimeRune : global::IDiscoverable
{
    public static readonly List<FakeTimeRune> All = new();
    public List<FakeTimeRuneType> timeRuneTypes = new();

    public Guid Identity = Guid.NewGuid();
    public bool discovered;
    public int level;
    public int discRarityLevel;
    public BigDouble masteryXp;
    public int masteryLevel;
    public bool isDiscoverRequired;
    public bool seen;
    public FakeModifierRecord freeUsages = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord powerScalingMod = new(0d);
    public FakeModifierRecord masteryXpMod = new(0d);
    public global::ResourceCostList genericDiscoveryCost = new();
    public List<global::GlyphSO> genericDiscoveryGlyphs = new();
    public List<global::ResourceSO> genericDiscoveryResources = new();
    public bool NativeDiscoverVisible = true;
    public bool NativeCanDiscover = true;
    public bool NativeCanLevel = true;
    public FakeCraftingResourceCostList LevelCost = new();

    public Guid GetGuid() => Identity;
    public int GetLevel() => level;
    public bool CanLevel() => NativeCanLevel;
    public FakeCraftingResourceCostList GetLevelCost() => LevelCost;
    public void PurchaseLevel() => level++;
    global::ResourceCostList global::IDiscoverable.GetDiscoverCost() => genericDiscoveryCost;
    List<global::GlyphSO> global::IDiscoverable.GetGlyphRecipe() => new(genericDiscoveryGlyphs);
    List<global::ResourceSO> global::IDiscoverable.GetResourceRecipe() => new(genericDiscoveryResources);
    bool global::IDiscoverable.IsDiscoverVisible() => NativeDiscoverVisible;
    bool global::IDiscoverable.CanDiscover() => NativeCanDiscover;
    bool global::IDiscoverable.IsDiscovered() => discovered;
    bool global::IDiscoverable.IsDiscoverRequired() => isDiscoverRequired;
    void global::IDiscoverable.Discover() => discovered = true;
    Guid global::IHasGuid.GetGuid() => GetGuid();
}

internal sealed class FakeGlyph : global::IDiscoverable
{
    public static readonly List<FakeGlyph> All = new();
    public List<FakeGlyphType> glyphTypes = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public int freeLevels;
    public int discRarityLevel;
    public bool discovered;

    public Guid GetGuid() => Identity;
    public bool discoverable;
    public bool discoveryRequired;
    public bool augmentsSpells;
    public bool requiresDuration;
    public bool requiresToggleable;

    // The one authored condition an unlocker carries, in the container type the requirement reader
    // walks for every other owner — a second container type at the same name would not be readable
    // by the accessor bound against the first.
    public FakePrerequisites prerequisites = new();
    public int masteryReqCount;
    public FakeModifierRecord freeUsages = new(0d);
    public FakeModifierRecord freeLoadoutUsages = new(0d);
    public FakeModifierRecord maxUsages = new(0d);
    public global::ResourceCostList genericDiscoveryCost = new();
    public List<global::GlyphSO> genericDiscoveryGlyphs = new();
    public List<global::ResourceSO> genericDiscoveryResources = new();
    public bool NativeDiscoverVisible = true;
    public bool NativeCanDiscover = true;
    public bool NativeCanLevel = true;
    public FakeCraftingResourceCostList LevelCost = new();
    public FakeCraftingResourceCostList BonusLevelCost = new();

    public bool NativeAvailable = true;
    public bool IsAvailable() => NativeAvailable;
    public int GetMaxUsages() => (int)maxUsages.GetValue().ToDouble();
    public int GetLevel() => level + freeLevels;
    public int GetFreeLevels() => freeLevels;
    public bool CanLevel() => NativeCanLevel;
    public FakeCraftingResourceCostList GetLevelCost() => LevelCost;
    public FakeCraftingResourceCostList GetFreeLevelCost() => BonusLevelCost;
    public void PurchaseLevel() => level++;
    public void PurchaseFreeLevel() => freeLevels++;
    global::ResourceCostList global::IDiscoverable.GetDiscoverCost() => genericDiscoveryCost;
    List<global::GlyphSO> global::IDiscoverable.GetGlyphRecipe() => new(genericDiscoveryGlyphs);
    List<global::ResourceSO> global::IDiscoverable.GetResourceRecipe() => new(genericDiscoveryResources);
    bool global::IDiscoverable.IsDiscoverVisible() => NativeDiscoverVisible;
    bool global::IDiscoverable.CanDiscover() => NativeCanDiscover;
    bool global::IDiscoverable.IsDiscovered() => discovered;
    bool global::IDiscoverable.IsDiscoverRequired() => discoveryRequired;
    void global::IDiscoverable.Discover() => discovered = true;
    Guid global::IHasGuid.GetGuid() => GetGuid();
}

internal sealed class FakeConsumable
{
    public static readonly List<FakeConsumable> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool visible;
    public bool randomized;
    public int quantity;
    public int queuedQuantity;
    public int gainedSince;
    public int maxCreatedLv;
    public BigDouble currentPrepTime;
    public BigDouble currentCooldown;
    public BigDouble currentCooldownTime;
    public ValueModifierRecord power = new(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new(new BigDouble(0.0, 0));
    public ValueModifierRecord prepSpeed = new(new BigDouble(0.0, 0));
    public ValueModifierRecord bonusLevels = new(new BigDouble(0.0, 0));
    public List<FakeConsumableType> consumableTypes = new();
    public List<FakeConsumableUsage> consumableUsages = new();
    public List<FakeConsumableCount> consumableCounts = new();
    public List<FakeScribeInstantBlock> onUseEffects = new();
    public FakeConsumableCostList consumeCost = new();
    public FakeConsumableCostList usageCost = new();
    public int maximumCarryLoad = 100;

    public Guid GetGuid() => Identity;
    public int GetMaximumCarryLoad() => maximumCarryLoad;
    public double preparationTime;
    public bool canBeRandomized;
    public bool hasDuration;
    public double durationBase;
    public bool queueOnStart;
}

internal sealed class FakeConsumableType
{
    public static readonly List<FakeConsumableType> All = new();

    public bool hidden;
    public int sortOrder;
    public FakeModifierRecord bonusLevels = new(0d);
    public FakeModifierRecord durationMod = new(0d);
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord prepSpeed = new(0d);
    public FakeModifierRecord special = new(0d);
    public Guid Identity = Guid.NewGuid();
    public FakeConsumableVariable maximumCarryLoad = new();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeConsumableVariable
{
    public Guid Identity = Guid.NewGuid();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeConsumableCostList
{
    public List<FakeConsumableCost> costs = new();
}

internal sealed class FakeConsumableCost
{
    internal FakeConsumableCost(Guid resourceId, double amount)
    {
        resource = new FakeConsumableResource { Identity = resourceId };
        valueBig = new BigDouble(amount);
    }

    public FakeConsumableResource resource;
    public BigDouble valueBig;
    public BigDouble GetValue() => valueBig;
}

internal sealed class FakeConsumableResource
{
    public Guid Identity = Guid.NewGuid();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeConsumableUsage
{
    public Guid Identity = Guid.NewGuid();
    public bool en;
    public BigDouble dr;
    public BigDouble maxDr;
    public FakeConsumableScalingInfo baseSi = new();
    public Guid GetGuid() => Identity;
}

internal sealed class FakeConsumableScalingInfo
{
    public int Level = 1;
    public int GetLevelInt() => Level;
}

internal sealed class FakeConsumableCount
{
    public int Level = 1;
    public int Quantity;
    public int fr;
    public int GetLevel() => Level;
    public int GetQuantity() => Quantity;
}

internal sealed class FakeRitual : global::IDiscoverable
{
    public static readonly List<FakeRitual> All = new();
    public List<FakeRitualType> ritualTypes = new();

    public Guid Identity = Guid.NewGuid();
    public bool discovered;
    public bool inBattle;
    public int reachedLevel;
    public int lastReachedLevel;
    public int selectedLevel;
    public int wavesCompleted;
    public int discRarityLevel;
    public int critLevel;
    public int echoLevel;
    public int chainLevel;
    public global::ResourceCostList genericDiscoveryCost = new();
    public List<global::GlyphSO> genericDiscoveryGlyphs = new();
    public List<global::ResourceSO> genericDiscoveryResources = new();
    public bool NativeDiscoverVisible = true;
    public bool NativeCanDiscover = true;

    /// <summary>Left null unless a test fills it, so the null-reads-as-zero path stays exercised.</summary>
    public List<object>? ritualInstances;

    public List<object> durationRewardBlocks = new();
    public BigDouble battleTotalWeight;
    public ValueModifierRecord power = new(new BigDouble(0.0, 0));
    public ValueModifierRecord speed = new(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new(new BigDouble(0.0, 0));
    public ValueModifierRecord durationMod = new(new BigDouble(0.0, 0));
    public ValueModifierRecord echoRating = new(new BigDouble(0.0, 0));
    public ValueModifierRecord echoPower = new(new BigDouble(0.0, 0));
    public ValueModifierRecord critRating = new(new BigDouble(0.0, 0));
    public ValueModifierRecord critPower = new(new BigDouble(0.0, 0));
    public ValueModifierRecord critDurationMod = new(new BigDouble(0.0, 0));
    public ValueModifierRecord chainLengthBonus = new(new BigDouble(0.0, 0));
    public ValueModifierRecord chainPower = new(new BigDouble(0.0, 0));
    public ValueModifierRecord completionCostMod = new(new BigDouble(0.0, 0));
    public ValueModifierRecord completionRateMod = new(new BigDouble(0.0, 0));

    public Guid GetGuid() => Identity;
    global::ResourceCostList global::IDiscoverable.GetDiscoverCost() => genericDiscoveryCost;
    List<global::GlyphSO> global::IDiscoverable.GetGlyphRecipe() => new(genericDiscoveryGlyphs);
    List<global::ResourceSO> global::IDiscoverable.GetResourceRecipe() => new(genericDiscoveryResources);
    bool global::IDiscoverable.IsDiscoverVisible() => NativeDiscoverVisible;
    bool global::IDiscoverable.CanDiscover() => NativeCanDiscover;
    bool global::IDiscoverable.IsDiscovered() => discovered;
    bool global::IDiscoverable.IsDiscoverRequired() => isDiscoverRequired;
    void global::IDiscoverable.Discover() => discovered = true;
    Guid global::IHasGuid.GetGuid() => GetGuid();
    public bool hideEndScreenResults;
    public bool isDiscoverRequired;
    public bool forceLevel;
    public int forceLevelValue;
    public int baseWaves;
    public int maxWaves;
    public int wavesPerLevel;
    public double baseWeight;
    public int minimumEffectLevel;
    public int maximumSelectedLevel = 1;
    public bool usageRequirementsMet = true;
    public FakeCraftingResourceCostList activationCost = new();
    public FakeCraftingResourceCostList completionCost = new();
    public FakeModifierListRef completionCostPerLevel = new();
    public GameWorldCollectorTests.FakeResearchFillList resourceFillList = new();

    public int GetMaxSelectedLevel() => maximumSelectedLevel;
    public bool HasMetUsageRequirements() => usageRequirementsMet;
    public FakeCraftingResourceCostList GetActivationCost() => activationCost;
    public FakeCraftingResourceCostList GetSelectedCompletionCost() => completionCost;

    /// <summary>The results screen's own record, and the verdict End() reads before showing it.</summary>
    public List<FakeSpoilsRecordEntry> currentSpoils = new();

    public bool IsFailedRun() => wavesCompleted < 5;
    public int GetCurrentLevel() => forceLevel ? forceLevelValue : selectedLevel;
    public int GetRequiredWaves() => Math.Min(
        baseWaves + (wavesPerLevel * GetCurrentLevel()),
        maxWaves > 0 ? maxWaves : int.MaxValue);
}

internal sealed class FakeSpoilsRecordEntry
{
    public FakeSpoilsRecordEntry(FakeResource resource, BigDouble quantity)
    {
        this.resource = resource;
        this.quantity = quantity;
    }

    public BigDouble quantity;
    public FakeResource resource { get; }
}

internal sealed class FakeRitualVariable
{
    public FakeRitual? value;
    public bool IsItem(FakeRitual ritual) => ReferenceEquals(value, ritual);
}

internal sealed class FakeRitualManager
{
    public static FakeRitualManager instance = new();
    public FakeRitualVariable selectedRitual = new();
}

internal sealed class FakeAchievement
{
    public static readonly List<FakeAchievement> All = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public bool seen;
    public bool logProgress;
    public string steamApiName;
    public int maxLevels;
    public int achievementStrength;

    public Guid GetGuid() => Identity;
}

internal sealed class FakeAdvancement
{
    public static readonly List<FakeAdvancement> All = new();

    public Guid Identity = Guid.NewGuid();
    public BigDouble levels;
    public BigDouble xp;
    public bool isPersistent;
    public double baseRequiredXp;
    public FakeModifierRecord power = new(0d);

    public Guid GetGuid() => Identity;
}

internal sealed class FakeChallenge
{
    public List<FakeChallengeType> challengeTypes = new();
    public static readonly List<FakeChallenge> All = new();

    public Guid Identity = Guid.NewGuid();
    public int level;
    public FakeState state;
    public bool hasBeenSeen;
    public bool rewardQueued;
    public int maxLevel;
    public int weight;
    public double difficulty;
    public double baseReward;

    public Guid GetGuid() => Identity;
    public bool IsAvailableToRun() => maxLevel < 0 || level < maxLevel;
    public bool IsCompletedOnce() => level > 0 || (int)state == 3;
    public bool IsMaxLevel() => maxLevel >= 0 && level >= maxLevel;
    public BigDouble GetDifficulty() => new(difficulty);
    public BigDouble GetNextInstanceBaseReward() => new(baseReward);
}

internal sealed class FakeThoughtStream
{
    public static readonly List<FakeThoughtStream> All = new();

    public Guid Identity = Guid.NewGuid();
    public FakeState state;

    public Guid GetGuid() => Identity;
}

internal sealed class FakeTutorial
{
    public static readonly List<FakeTutorial> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool isCompleted;

    public Guid GetGuid() => Identity;
}

internal sealed class FakeView : FakeIdRegistry
{
    public static readonly List<FakeView> All = new();

    public bool active;
    public bool alwaysActive;
    public List<FakeAbstractListVariable> relevantLists = new();
    public List<FakeAbstractListVariable> availableLists = new();

    public bool IsAvailable() => active || alwaysActive;
}

internal sealed class FakePlotNodeAction
{
    public static readonly List<FakePlotNodeAction> All = new();
    public List<FakeHarvestActionType> actionTypes = new();

    public Guid Identity = Guid.NewGuid();
    public bool hasBeenUsed;
    public bool isGrowingAction;
    public bool showExitTooltip;
    public int elementCost;
    public bool useSizeModForCost;
    public bool useAnyStateForCost;
    public bool parallelAction;
    public double baseTime;
    public bool useSpaceUsageForTimeMult;
    public bool ignoreNodeYield;
    public bool useParentSize;
    public bool useParentQuality;
    public FakeModifierRecord power = new(0d);
    public FakeModifierRecord speed = new(0d);
    public FakeModifierRecord costMod = new(0d);
    public FakeModifierRecord growthSizeMod = new(0d);
    public FakeModifierRecord refundRating = new(0d);
    public List<FakePlotNode> sizeModNodes = new();
    public FakePrerequisites prerequisites = new();
    public FakeCostType elementCostType;
    public FakePlotPhase elementCostExitPhase;
    public FakeResourceCostList actionDrain = new();
    public List<object> actionEffects = new();
    public List<FakeEffectBlock> completeEffects = new();

    public Guid GetGuid() => Identity;
}

/// <summary>Positionally identical to the game's PlotNodeActionSO.CostType.</summary>
internal enum FakeCostType
{
    OnStart,
    OnExitPhase,
}

internal sealed class FakeResourceCostList
{
    public List<object> costs = new();
}

/// <summary>An authored effect block, read for its shape rather than its contents.</summary>
internal sealed class FakeEffectBlock
{
    public FakePrerequisites prerequisites = new();
    public List<object> effectMods = new();
    public List<object> effectScripts = new();
}

/// <summary>An effect modifier that scales what it applies by an authored weight.</summary>
internal sealed class FakeScalingWeightMod
{
    public FakeScalingWeightRef scalingWeightRef = new();
}

internal sealed class FakeScalingWeightRef
{
    public FakeScalingWeight? scalingWeight;
}

internal sealed class FakeScalingWeight
{
    public Guid Identity = Guid.NewGuid();

    public Guid GetGuid() => Identity;
}

/// <summary>An effect script that pays a treasure out of a pool.</summary>
internal sealed class FakeTreasureEffect
{
    public FakeTreasurePool? treasurePool;
    public string effectType = string.Empty;
    public double effectValue;
    public FakeFilterMod filterScaling = new();
}

internal sealed class FakeFilterMod
{
    public FakeFilterType listType;
    public List<object> listContents = new();
}

/// <summary>Positionally identical to the game's FilterEffectMod.FilterType.</summary>
internal enum FakeFilterType
{
    BlackList,
    WhiteList,
}

internal sealed class FakePrerequisites
{
    public bool available;
    public List<object> prerequisites = new();

    /// <summary>
    /// The read-only parameterized overload world collection binds as a differential oracle. This
    /// traversal fake intentionally answers only the unconditional case; requirement arithmetic is
    /// covered by the native-shaped shared stubs and evaluator tests.
    /// </summary>
    public bool Check(Requirements.ConditionInfo conditionInfo) => prerequisites.Count == 0;
}

internal sealed class FakePassiveAbility
{
    public static readonly List<FakePassiveAbility> All = new();
    public List<FakePassiveAbilityType> passiveTypes = new();

    public Guid Identity = Guid.NewGuid();
    public bool muted;
    public bool touched;
    public bool hidden;
    public bool silent;
    public bool global;
    public bool startOnCooldown;
    public bool ignoreReactionCooldown;
    public double reactionTokenCost;
    public double maxTokens;
    public double minTokenForEffect;
    public bool tokenIndividuateDuration;
    public bool applyWhileRecharging;
    public bool expireAttachedStatusEffect;
    public FakeModifierRecord tokenRate = new(0d);

    public Guid GetGuid() => Identity;
}

internal sealed class FakeCharacter
{
    public static readonly List<FakeCharacter> All = new();
    public List<FakeCharacterType> characterTypes = new();

    public Guid Identity = Guid.NewGuid();
    public bool discovered;
    public double numberSlain;

    public Guid GetGuid() => Identity;
    public bool floats;
}

internal sealed class FakeDiscoveryTree
{
    public static readonly List<FakeDiscoveryTree> All = new();

    public Guid Identity = Guid.NewGuid();
    public BigDouble actionTime;
    public int rerollsLeft;
    public bool usedRerollsLastDiscover;
    public FakeState actionMode;
    public FakeGuidContainer selectedChoiceId = new();
    public List<FakeGuidContainer> currentChoiceIds = new();
    public FakeReferencedEntity? overrideDiscoveryRerolls;
    public FakeReferencedEntity? overrideDiscoveryChoices;
    public bool visible = true;
    public bool immediateRequired;
    public FakeDiscoveryCostList nextItemCost = new();

    public Guid GetGuid() => Identity;
    public bool IsVisible() => visible;
    public bool HasImmediateRequiredDiscover() => immediateRequired;
    public FakeDiscoveryCostList GetNextItemCost() => nextItemCost;
    public int additionalDiscoveryChoices;
    public int discoveryBonusLevelCost;
    public bool debugMode;
    public int totalDiscoveredCount;
    public int poolDiscoveredCount;

    // The two lists the game counts discovered items out of. Their sizes are the denominators, so a
    // fake that omitted them modelled a tree whose progress nobody could read.
    public List<object> allDiscoverableItems = new();
    public List<object> mainDiscoverableItemPool = new();
    public bool hasRequiredDiscovery;
    public bool hasRemainingDiscovery;
    public bool hasCompletedAllDiscoveries;
}

internal sealed class FakeDiscoveryCostList
{
    public bool affordable = true;
    public List<FakeDiscoveryCost> costs = new();

    public bool HasEnough() => affordable;
    public List<FakeDiscoveryCost> GetEntries() => costs;
}

internal sealed class FakeDiscoveryCost
{
    public FakeDiscoveryCost(FakeResource resource, BigDouble amount)
    {
        this.resource = resource;
        Amount = amount;
    }

    public FakeResource resource;
    internal BigDouble Amount { get; }
    public BigDouble GetValue() => Amount;
}

internal sealed class FakeRecipeBook
{
    public static readonly List<FakeRecipeBook> All = new();

    public Guid Identity = Guid.NewGuid();
    public bool Available;

    public Guid GetGuid() => Identity;
    public bool IsAvailable() => Available;
}

internal sealed class FakePlotNode
{
    public static readonly List<FakePlotNode> All = new();
    public List<FakePlotNodeType> nodeTypes = new();

    public Guid Identity = Guid.NewGuid();
    public bool visible;
    public BigDouble currentTime;
    public BigDouble nextErraticTime;
    public BigDouble sizeLevel;
    public BigDouble masteryXp;
    public int masteryLevel;

    public Guid GetGuid() => Identity;
    public bool noMastery;
    public bool noSizeDisplay;
    public bool useVisibilityPrereq;
    public bool hasErraticGrowth;
    public bool debugMode;
    public int erraticQuantity;
    public FakeModifierRecord actionQuantityUsageMain = new(0d);
    public FakeModifierRecord actionQuantityUsageAny = new(0d);
    public FakeModifierRecord actionXpRate = new(0d);
    public FakeModifierRecord yieldMod = new(0d);
    public FakeModifierRecord specialMod = new(0d);
    public FakeModifierRecord actionSpeed = new(0d);
    public FakeModifierRecord actionCostMod = new(0d);
    public FakeModifierRecord growingSpeed = new(0d);
    public FakeModifierRecord restingSpeed = new(0d);
    public FakeModifierRecord sizeMod = new(0d);
    public FakeModifierRecord qualityMod = new(0d);
    public FakeModifierRecord recoverySizeMod = new(0d);
    public FakeModifierRecord naturalGrowth = new(0d);
    public FakeModifierRecord naturalGrowthPower = new(0d);
    public int lastQuantity;

    public FakePlotNodeAction? autoAction;

    public List<FakePhaseInfo> phaseInfos = new() { new FakePhaseInfo { phase = FakePlotPhase.Idle } };
    public List<FakePhaseInstance> phaseInstances = new();

    public List<FakePlotNodeAction> availableActions = new();
    private List<FakePlotActionInstance> actionInstances = new();

    public List<FakePlotActionInstance> GetActionInstances() => actionInstances;

    /// <summary>Puts <paramref name="quantity"/> of the node into <paramref name="phase"/>.</summary>
    internal FakePlotNode With(FakePlotPhase phase, int quantity)
    {
        if (phaseInfos.TrueForAll(info => info.phase != phase))
            phaseInfos.Add(new FakePhaseInfo { phase = phase });
        phaseInstances.Add(new FakePhaseInstance { phase = phase, timers = new FakeTimerList { q = quantity } });
        return this;
    }

    internal FakePlotNode Offering(FakePlotNodeAction action)
    {
        availableActions.Add(action);
        return this;
    }

    /// <summary>
    /// Adds a running instance whose reference has already been resolved, which is the shape the game
    /// holds after anything has asked the reference for its guid.
    /// </summary>
    internal FakePlotNode Running(FakePlotNodeAction action, int quantity = 1, bool engaged = false)
    {
        actionInstances.Add(new FakePlotActionInstance
        {
            refObj = FakeIdObjectRef.Memoised(action.Identity),
            quantity = quantity,
            engaged = engaged,
        });
        return this;
    }

    /// <summary>
    /// Adds an instance whose reference names nothing that resolves, which is what a plot holding an
    /// action from a build that no longer ships it looks like.
    /// </summary>
    internal FakePlotNode RunningSomethingUnknown()
    {
        actionInstances.Add(new FakePlotActionInstance { refObj = FakeIdObjectRef.Unresolvable(), quantity = 1 });
        return this;
    }

    /// <summary>
    /// Adds a running instance whose reference is still only the serialized string, which is the
    /// shape it has straight off a save load.
    /// </summary>
    internal FakePlotNode RunningUnresolved(FakePlotNodeAction action)
    {
        actionInstances.Add(new FakePlotActionInstance
        {
            refObj = FakeIdObjectRef.Serialized(action.Identity),
        });
        return this;
    }
}

internal sealed class FakePlotActionInstance
{
    public FakeIdObjectRef refObj;
    public int quantity;
    public bool engaged;

    public bool IsEmpty() => quantity <= 0;

    public bool IsEngaged() => engaged;

    public int GetActualQuantity() => quantity;
}

/// <summary>
/// Shaped like the game's IdObjectRef: a serialized string, and a guid memoised from it the first
/// time anything asks. Nothing in this assembly reads <c>_guid</c> by name — the collector does, by
/// reflection, which is exactly what the two factories below are here to exercise.
/// </summary>
internal struct FakeIdObjectRef
{
    public string idStr;
#pragma warning disable CS0414
    private Guid _guid;
#pragma warning restore CS0414

    internal static FakeIdObjectRef Memoised(Guid identity) =>
        new() { idStr = identity.ToString(), _guid = identity };

    internal static FakeIdObjectRef Serialized(Guid identity) =>
        new() { idStr = identity.ToString(), _guid = Guid.Empty };

    internal static FakeIdObjectRef Unresolvable() =>
        new() { idStr = string.Empty, _guid = Guid.Empty };
}

/// <summary>Positionally identical to the game's PlotNodeSO.PlotNodePhases.</summary>
internal enum FakePlotPhase
{
    Idle,
    Growing,
    Resting,
}

internal sealed class FakePhaseInfo
{
    public FakePlotPhase phase;
    public double phaseTime;
    public FakeTimerType processType;
    public FakePlotPhase exitPhase;
}

/// <summary>Positionally identical to the game's TimerList.TimerType.</summary>
internal enum FakeTimerType
{
    Single,
    Parallel,
}

internal sealed class FakePhaseInstance
{
    public FakePlotPhase phase;
    public FakeTimerList timers = new();
}

internal sealed class FakeTimerList
{
    public int q;
    public List<BigDouble> ds = new();
}

internal sealed class FakeTreasurePool
{
    public static readonly List<FakeTreasurePool> All = new();

    public Guid Identity = Guid.NewGuid();
    public int treasuresFound;
    public BigDouble partialTreasureReward;

    public Guid GetGuid() => Identity;
    public bool forceLevel;
    public int treasureLevel;
    public bool calculatedTreasureLevel;
}
