using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Reads the whole game world once per collection, on the Unity thread, into per-category buffers,
/// then derives those readings into one publishable snapshot off-thread.
/// </summary>
/// <remarks>
/// <para>
/// This is the shared pass that replaces per-service collection. Roughly ten services are expected to
/// want resource state every tick; each capturing its own copy costs the main thread once per service
/// for readings that are identical by construction. One pass, published like configuration, makes that
/// cost constant in the number of services instead of linear.
/// </para>
/// <para>
/// <b>Read what the game reads, and never write.</b> Every accessor a binder installs reads state the
/// game stores. Nothing in this type classifies or scales anything either; that all happens in
/// <see cref="Build"/>, off the Unity thread.
/// </para>
/// <para>
/// The read-only requirement is stricter than it sounds, because several of the game's accessors
/// write on read. <c>ValueModifierRecord.GetValue()</c> recalculates and re-stamps its observable
/// when its dirty flag is set, so it is reproduced rather than called — and reproducing it means
/// reproducing the branch, not just the arithmetic. A clean record reads as its
/// <c>calculatedValue</c>, because that memo is the number the game will act on; a dirty one reads as
/// its fold over <c>baseValue</c> and the two modifier sets, because that is what the game would
/// recompute. Each half alone shipped a live failure in the opposite direction. See
/// <see cref="NativeModifierRecordAccess"/>.
/// </para>
/// <para>
/// <b>Composed answers stay exceptional.</b> Resource rate is owned and differentially verified;
/// <c>IsAvailable()</c> still walks a prerequisite graph and reaches <c>GetValue()</c> underneath.
/// That game-only validator remains explicit rather than allowing other composed answers to drift
/// back into capture unnoticed.
/// </para>
/// <para>
/// <b>Accessors bind once.</b> Each category compiles its readers at construction, so the warm path
/// is a direct call returning an unboxed value. This is the technique that removed roughly 15 ms from
/// Auto Buy's 23.7 ms collect; see <see cref="NativeAccessorBinder"/>.
/// </para>
/// <para>
/// <b>Failure is per category, then per entity.</b> A build that renamed one research member still
/// yields resources, structures, and upgrades, and a single unreadable entity costs one row rather
/// than the pass. Every shortfall is reported through <see cref="WorldCollectionReport"/>, because a
/// partial snapshot that reports itself as complete would have consumers read "no research" as a fact
/// about the save rather than a fact about the read.
/// </para>
/// <para>
/// <b>Adding a category is a binder, a row type, and one line here.</b> Traversal, identity claiming,
/// failure capture, buffer growth, sorting, and table construction are all generic; see
/// <see cref="WorldRowBinder{TSample, TRow}"/>. That is deliberate — the game persists per-entity
/// state for roughly thirty categories, and a design that costs a hand-written traversal apiece would
/// stall long before covering them.
/// </para>
/// </remarks>
internal sealed class GameWorldCollector
{
    private readonly WorldCategoryReader<RawResourceSample, WorldResource> _resources;
    private readonly WorldCategoryReader<RawStructureSample, WorldStructure> _structures;
    private readonly WorldCategoryReader<RawUpgradeSample, WorldUpgrade> _upgrades;
    private readonly WorldCategoryReader<WorldResearch, WorldResearch> _research;
    private readonly WorldCategoryReader<WorldNumberVariable, WorldNumberVariable> _doubleVariables;
    private readonly WorldCategoryReader<WorldNumberVariable, WorldNumberVariable> _intVariables;
    private readonly WorldCategoryReader<WorldBoolVariable, WorldBoolVariable> _boolVariables;
    private readonly WorldCategoryReader<WorldModifierVariable, WorldModifierVariable> _modifierVariables;
    private readonly WorldCategoryReader<WorldStatistic, WorldStatistic> _statistics;
    private readonly WorldCategoryReader<WorldAttributeGroup, WorldAttributeGroup> _attributeGroups;
    private readonly WorldAttributeGroupMemberReader _attributeGroupMembers;
    private readonly WorldCategoryReader<WorldStatusEffect, WorldStatusEffect> _statusEffects;

    private readonly WorldCategoryReader<WorldCharacterAttribute, WorldCharacterAttribute>
        _characterAttributes;

    private readonly WorldCategoryReader<WorldDamageType, WorldDamageType> _damageTypes;

    private readonly WorldCategoryReader<WorldCharacterModifier, WorldCharacterModifier>
        _characterModifiers;

    private readonly WorldCategoryReader<WorldCharacterAction, WorldCharacterAction>
        _characterActions;

    private readonly WorldCategoryReader<WorldCharacterType, WorldCharacterType> _characterTypes;
    private readonly WorldCategoryReader<WorldEnchantment, WorldEnchantment> _enchantments;
    private readonly WorldCategoryReader<WorldGlyphType, WorldGlyphType> _glyphTypes;
    private readonly WorldCategoryReader<WorldRuneStone, WorldRuneStone> _runeStones;
    private readonly WorldCategoryReader<WorldDisplayType, WorldDisplayType> _displayTypes;
    private readonly WorldPurchaseCostReader _purchaseCosts;
    private readonly WorldUpgradeCostReader _upgradeCosts;
    private readonly WorldPlotActionReader _plotActions;
    private readonly WorldActionQueueReader _actionQueues;
    private readonly WorldSpellSlotReader _spellSlots;
    private readonly WorldSpellWorkbenchReader _spellWorkbench;
    private readonly WorldTargetingReader _targeting;
    private readonly WorldAlchemyInstanceReader _alchemyInstances;
    private readonly WorldAlchemyLoadoutReader _alchemyLoadout;
    private readonly WorldPlotAuthoringReader _plotAuthoring;
    private readonly WorldEffectBlockReader _effectBlocks;
    private readonly WorldEntityRequirementReader _entityRequirements;
    private readonly WorldRecipeBookGlyphReader _recipeBookGlyphs;
    private readonly WorldEffectFactorReader _effectFactors;
    private readonly WorldPurchaseViewRelationReader _purchaseViewRelations;
    private readonly WorldPrerequisiteLinkTierReader _prerequisiteLinkTiers;
    private readonly IWorldMasteryExperienceSource _masteryExperience;
    private readonly WorldCategoryReader<WorldAlchemyRecipe, WorldAlchemyRecipe> _alchemyRecipes;
    private readonly WorldCategoryReader<WorldAlchemyType, WorldAlchemyType> _alchemyTypes;
    private readonly WorldCategoryReader<RawSpellRecipeSample, WorldSpellRecipe> _spellRecipes;
    private readonly WorldMasteryCostReader _spellLevelCosts;
    private readonly WorldSpellGraphReader _spellGraph;
    private readonly WorldEntityKeywordReader _entityKeywords;
    private readonly WorldTypeModifierReader _typeModifiers;
    private readonly WorldTypeModifierContributionReader _typeModifierContributions;
    private readonly WorldCategoryReader<WorldSpellType, WorldSpellType> _spellTypes;
    private readonly WorldCategoryReader<WorldEquipment, WorldEquipment> _equipment;
    private readonly WorldCategoryReader<WorldEquipmentType, WorldEquipmentType> _equipmentTypes;
    private readonly WorldCategoryReader<WorldResourceType, WorldResourceType> _resourceTypes;
    private readonly WorldCategoryReader<WorldCraftingRecipeType, WorldCraftingRecipeType> _craftingRecipeTypes;
    private readonly WorldCategoryReader<WorldStructureType, WorldStructureType> _structureTypes;
    private readonly WorldCategoryReader<WorldRitualType, WorldRitualType> _ritualTypes;
    private readonly WorldCategoryReader<WorldHarvestType, WorldHarvestType> _harvestTypes;
    private readonly WorldCategoryReader<WorldPlotNodeType, WorldPlotNodeType> _plotNodeTypes;
    private readonly WorldCategoryReader<WorldResearchType, WorldResearchType> _researchTypes;
    private readonly WorldCategoryReader<WorldConsumableFamily, WorldConsumableFamily> _consumableFamilies;
    private readonly WorldCategoryReader<WorldHarvestActionType, WorldHarvestActionType> _harvestActionTypes;
    private readonly WorldCategoryReader<WorldPassiveAbilityType, WorldPassiveAbilityType> _passiveAbilityTypes;
    private readonly WorldCategoryReader<WorldTimeRuneType, WorldTimeRuneType> _timeRuneTypes;
    private readonly WorldCraftingRecipeAuthoringReader _craftingRecipeAuthoring;
    private readonly WorldCraftingRecipeReader _craftingRecipes;
    private readonly WorldCraftingDecisionReader _craftingDecisions;
    private readonly WorldCraftingStationReader _craftingStations;
    private readonly WorldLoadoutReader _loadouts;
    private readonly WorldCategoryReader<WorldHarvestElement, WorldHarvestElement> _harvestElements;
    private readonly WorldCategoryReader<WorldHarvestAction, WorldHarvestAction> _harvestActions;
    private readonly WorldCategoryReader<RawHarvestResourceSample, WorldHarvestResource> _harvestResources;
    private readonly WorldHarvestLifecycleReader _harvestLifecycle;
    private readonly WorldCategoryReader<WorldTimeRune, WorldTimeRune> _timeRunes;
    private readonly WorldCategoryReader<WorldGlyph, WorldGlyph> _glyphs;
    private readonly WorldConsumableReader _consumables;
    private readonly WorldConsumableInventoryReader _consumableInventory;
    private readonly WorldScribeRelationReader _scribeRelations;
    private readonly WorldCategoryReader<WorldRitual, WorldRitual> _rituals;
    private readonly WorldCategoryReader<WorldAchievement, WorldAchievement> _achievements;
    private readonly WorldCategoryReader<WorldAdvancement, WorldAdvancement> _advancements;
    private readonly WorldCategoryReader<WorldChallenge, WorldChallenge> _challenges;
    private readonly WorldChallengeContextReader _challengeContext;
    private readonly WorldCategoryReader<WorldThoughtStream, WorldThoughtStream> _thoughtStreams;
    private readonly WorldCategoryReader<WorldTutorial, WorldTutorial> _tutorials;
    private readonly WorldCategoryReader<WorldView, WorldView> _views;
    private readonly WorldCategoryReader<WorldPlotNodeAction, WorldPlotNodeAction> _plotNodeActions;
    private readonly WorldCategoryReader<WorldPassiveAbility, WorldPassiveAbility> _passiveAbilities;
    private readonly WorldCategoryReader<WorldCharacter, WorldCharacter> _characters;
    private readonly WorldCategoryReader<WorldDiscoveryTree, WorldDiscoveryTree> _discoveryTrees;
    private readonly WorldCategoryReader<WorldRecipeBook, WorldRecipeBook> _recipeBooks;
    private readonly WorldCategoryReader<RawPlotNodeSample, WorldPlotNode> _plotNodes;
    private readonly WorldCategoryReader<WorldTreasurePool, WorldTreasurePool> _treasurePools;

    /// <summary>Every reader in traversal order, so the pass itself is category-blind.</summary>
    private readonly IWorldCategoryReader[] _readers;

    /// <summary>The pseudo-category a pass appends when the modifier fold had to reconstruct an input.</summary>
    private const string ModifierFoldingCategory = "modifier folding";

    /// <summary>
    /// Identities already seen this pass, reused across collections. Claiming spans every category at
    /// once: the game keys all entities in one UUID space, so a collision between categories is as
    /// much an authoring error as one within a category — and a duplicate reaching table construction
    /// would cost the whole snapshot rather than one row.
    /// </summary>
    private readonly HashSet<Guid> _claimed = new();

    /// <summary>
    /// Reads Unity's fixed timestep. Injected rather than called inline so the collector stays
    /// constructible without a Unity player loop, which is the whole reason its category types are
    /// injected too.
    /// </summary>
    private readonly Func<double> _readFixedDeltaTime;

    /// <summary>
    /// The frame used by the parameterless <see cref="Collect()"/>, for callers that are not a
    /// service cycle and so have nowhere else to keep one.
    /// </summary>
    private readonly GameWorldCycleFrame _ownFrame = new();

    /// <summary>Reads the frame-wide rate terms that belong to no category.</summary>
    private readonly WorldFrameGlobalsReader _rateGlobals;

    /// <summary>
    /// Which entries of <see cref="_readers"/> read authored content rather than played state, and so
    /// are read once per lifecycle epoch instead of once per pass.
    /// </summary>
    /// <remarks>
    /// A parallel flag rather than a separate list, because the report is built in traversal order and
    /// a skipped category still owes the operator its row. Marked by identity at construction, so
    /// reordering <see cref="_readers"/> cannot silently re-classify one.
    /// </remarks>
    private readonly bool[] _isStructural;

    /// <summary>
    /// What the last epoch-scoped read of each structural category found, kept so a skipped pass can
    /// report what is in the buffer rather than reporting nothing.
    /// </summary>
    private readonly WorldCategoryReport[] _structuralReports;

    private GameWorldCycleFrame? _structuralFrame;
    private long _structuralEpoch;

    /// <summary>
    /// The collector the session composes: the one that publishes the process-wide owning-view
    /// admission snapshot the purchase boundary reads from.
    /// </summary>
    /// <remarks>
    /// Production purchase topology is an explicit, named opt-in and nothing else selects it. A
    /// collector built any other way binds its own resolver, so a diagnostic, a verifier or a test
    /// cannot restamp the live snapshot no matter how it is constructed. The inverse default cost a
    /// live save twenty-seven minutes of refused purchases: a verification pass allocated four
    /// throwaway <c>new GameWorldCollector()</c> collectors, each took the shared resolver, and each
    /// stamped it at the epoch its own frame carried — zero.
    /// </remarks>
    internal static GameWorldCollector ForSession() =>
        ForSession(EmptyWorldMasteryExperienceSource.Instance);

    /// <inheritdoc cref="ForSession()"/>
    internal static GameWorldCollector ForSession(IWorldMasteryExperienceSource masteryExperience) =>
        new(
            WorldNativeTypes.Resolve,
            static () => UnityEngine.Time.fixedDeltaTime,
            masteryExperience,
            productionPurchaseTopology: true);

    internal GameWorldCollector()
        : this(
            WorldNativeTypes.Resolve,
            static () => UnityEngine.Time.fixedDeltaTime,
            EmptyWorldMasteryExperienceSource.Instance,
            productionPurchaseTopology: false)
    {
    }

    internal GameWorldCollector(IWorldMasteryExperienceSource masteryExperience)
        : this(
            WorldNativeTypes.Resolve,
            static () => UnityEngine.Time.fixedDeltaTime,
            masteryExperience,
            productionPurchaseTopology: false)
    {
    }

    /// <summary>
    /// Binds every category through <paramref name="resolveType"/>, which maps a game type name to the
    /// loaded type or null. Taking the resolver rather than the types keeps this constructor from
    /// growing a parameter per category, and lets tests answer for exactly the names they stub.
    /// </summary>
    internal GameWorldCollector(Func<string, Type?> resolveType)
        : this(
            resolveType,
            static () => UnityEngine.Time.fixedDeltaTime,
            EmptyWorldMasteryExperienceSource.Instance)
    {
    }

    /// <summary>As above, with the tick clock supplied too.</summary>
    internal GameWorldCollector(
        Func<string, Type?> resolveType,
        Func<double> readFixedDeltaTime,
        IWorldMasteryExperienceSource? masteryExperience = null)
        : this(resolveType, readFixedDeltaTime, masteryExperience, productionPurchaseTopology: false)
    {
    }

    private GameWorldCollector(
        Func<string, Type?> resolveType,
        Func<double> readFixedDeltaTime,
        IWorldMasteryExperienceSource? masteryExperience,
        bool productionPurchaseTopology)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        if (readFixedDeltaTime is null) throw new ArgumentNullException(nameof(readFixedDeltaTime));

        _readFixedDeltaTime = readFixedDeltaTime;
        _masteryExperience =
            masteryExperience ?? EmptyWorldMasteryExperienceSource.Instance;
        _rateGlobals = new WorldFrameGlobalsReader(resolveType);

        _resources = Reader(new WorldResourceBinder(), resolveType, static frame => frame.Resources);
        _structures = Reader(new WorldStructureBinder(), resolveType, static frame => frame.Structures);
        _upgrades = Reader(new WorldUpgradeBinder(), resolveType, static frame => frame.Upgrades);
        _research = Reader(new WorldResearchBinder(resolveType), resolveType, static frame => frame.Research);
        _doubleVariables = Reader(new WorldDoubleVariableBinder(), resolveType, static frame => frame.DoubleVariables);
        _intVariables = Reader(new WorldIntVariableBinder(), resolveType, static frame => frame.IntVariables);
        _boolVariables = Reader(new WorldBoolVariableBinder(), resolveType, static frame => frame.BoolVariables);
        _modifierVariables = Reader(new WorldModifierVariableBinder(), resolveType, static frame => frame.ModifierVariables);
        _statistics = Reader(new WorldStatisticBinder(), resolveType, static frame => frame.Statistics);
        _attributeGroups = Reader(new WorldAttributeGroupBinder(), resolveType, static frame => frame.AttributeGroups);
        _attributeGroupMembers = new WorldAttributeGroupMemberReader(resolveType);
        _statusEffects = Reader(new WorldStatusEffectBinder(), resolveType, static frame => frame.StatusEffects);
        _characterAttributes = Reader(new WorldCharacterAttributeBinder(), resolveType, static frame => frame.CharacterAttributes);
        _damageTypes = Reader(new WorldDamageTypeBinder(), resolveType, static frame => frame.DamageTypes);
        _characterModifiers = Reader(new WorldCharacterModifierBinder(), resolveType, static frame => frame.CharacterModifiers);
        _characterActions = Reader(new WorldCharacterActionBinder(), resolveType, static frame => frame.CharacterActions);
        _characterTypes = Reader(new WorldCharacterTypeBinder(), resolveType, static frame => frame.CharacterTypes);
        _enchantments = Reader(new WorldEnchantmentBinder(), resolveType, static frame => frame.Enchantments);
        _glyphTypes = Reader(new WorldGlyphTypeBinder(), resolveType, static frame => frame.GlyphTypes);
        _runeStones = Reader(new WorldRuneStoneBinder(), resolveType, static frame => frame.RuneStones);
        _displayTypes = Reader(new WorldDisplayTypeBinder(), resolveType, static frame => frame.DisplayTypes);
        _alchemyRecipes = Reader(new WorldAlchemyRecipeBinder(), resolveType, static frame => frame.AlchemyRecipes);
        _alchemyTypes = Reader(new WorldAlchemyTypeBinder(), resolveType, static frame => frame.AlchemyTypes);
        _spellRecipes = Reader(new WorldSpellRecipeBinder(), resolveType, static frame => frame.SpellRecipes);
        _spellLevelCosts = new WorldMasteryCostReader(resolveType);
        _spellGraph = new WorldSpellGraphReader(resolveType("SpellRecipeSO"));
        _entityKeywords = new WorldEntityKeywordReader(resolveType);
        var typeModifierBindings = new WorldTypeModifierBindings(resolveType);
        _typeModifiers = new WorldTypeModifierReader(typeModifierBindings);
        _typeModifierContributions = new WorldTypeModifierContributionReader(typeModifierBindings);
        _spellTypes = Reader(new WorldSpellTypeBinder(), resolveType, static frame => frame.SpellTypes);
        _equipment = Reader(new WorldEquipmentBinder(resolveType), resolveType, static frame => frame.Equipment);
        _equipmentTypes = Reader(new WorldEquipmentTypeBinder(resolveType), resolveType, static frame => frame.EquipmentTypes);
        _resourceTypes = Reader(new WorldResourceTypeBinder(resolveType), resolveType, static frame => frame.ResourceTypes);
        _craftingRecipeTypes = Reader(new WorldCraftingRecipeTypeBinder(), resolveType, static frame => frame.CraftingRecipeTypes);
        _structureTypes = Reader(new WorldStructureTypeBinder(), resolveType, static frame => frame.StructureTypes);
        _ritualTypes = Reader(new WorldRitualTypeBinder(), resolveType, static frame => frame.RitualTypes);
        _harvestTypes = Reader(new WorldHarvestTypeBinder(), resolveType, static frame => frame.HarvestTypes);
        _plotNodeTypes = Reader(new WorldPlotNodeTypeBinder(), resolveType, static frame => frame.PlotNodeTypes);
        _researchTypes = Reader(new WorldResearchTypeBinder(), resolveType, static frame => frame.ResearchTypes);
        _consumableFamilies = Reader(new WorldConsumableFamilyBinder(), resolveType, static frame => frame.ConsumableFamilies);
        _harvestActionTypes = Reader(new WorldHarvestActionTypeBinder(), resolveType, static frame => frame.HarvestActionTypes);
        _passiveAbilityTypes = Reader(new WorldPassiveAbilityTypeBinder(), resolveType, static frame => frame.PassiveAbilityTypes);
        _timeRuneTypes = Reader(new WorldTimeRuneTypeBinder(), resolveType, static frame => frame.TimeRuneTypes);
        _craftingRecipes = new WorldCraftingRecipeReader(resolveType);
        _craftingDecisions = new WorldCraftingDecisionReader(resolveType);
        _craftingStations = new WorldCraftingStationReader(resolveType);
        _loadouts = new WorldLoadoutReader(resolveType);
        _craftingRecipeAuthoring = new WorldCraftingRecipeAuthoringReader(_craftingRecipes);
        _harvestElements = Reader(new WorldHarvestElementBinder(), resolveType, static frame => frame.HarvestElements);
        _harvestActions = Reader(new WorldHarvestActionBinder(), resolveType, static frame => frame.HarvestActions);
        _harvestResources = Reader(new WorldHarvestResourceBinder(), resolveType, static frame => frame.HarvestResources);
        _harvestLifecycle = new WorldHarvestLifecycleReader(resolveType);
        _timeRunes = Reader(new WorldTimeRuneBinder(resolveType), resolveType, static frame => frame.TimeRunes);
        _glyphs = Reader(new WorldGlyphBinder(resolveType), resolveType, static frame => frame.AugmentGlyphs);
        _consumables = new WorldConsumableReader(
            resolveType("ConsumableSO"),
            resolveType("IdScriptableObject"));
        _consumableInventory = new WorldConsumableInventoryReader(resolveType);
        _scribeRelations = new WorldScribeRelationReader(resolveType);
        _rituals = Reader(new WorldRitualBinder(resolveType), resolveType, static frame => frame.Rituals);
        _achievements = Reader(new WorldAchievementBinder(), resolveType, static frame => frame.Achievements);
        _advancements = Reader(new WorldAdvancementBinder(), resolveType, static frame => frame.Advancements);
        _challenges = Reader(new WorldChallengeBinder(), resolveType, static frame => frame.Challenges);
        _challengeContext = new WorldChallengeContextReader(resolveType);
        _thoughtStreams = Reader(new WorldThoughtStreamBinder(), resolveType, static frame => frame.ThoughtStreams);
        _tutorials = Reader(new WorldTutorialBinder(), resolveType, static frame => frame.Tutorials);
        _views = Reader(new WorldViewBinder(), resolveType, static frame => frame.Views);
        _plotNodeActions = Reader(new WorldPlotNodeActionBinder(), resolveType, static frame => frame.PlotNodeActions);
        _passiveAbilities = Reader(new WorldPassiveAbilityBinder(), resolveType, static frame => frame.PassiveAbilities);
        _characters = Reader(new WorldCharacterBinder(), resolveType, static frame => frame.Characters);
        _discoveryTrees = Reader(new WorldDiscoveryTreeBinder(), resolveType, static frame => frame.DiscoveryTrees);
        _recipeBooks = Reader(new WorldRecipeBookBinder(), resolveType, static frame => frame.RecipeBooks);
        _plotNodes = Reader(new WorldPlotNodeBinder(), resolveType, static frame => frame.PlotNodes);
        _treasurePools = Reader(new WorldTreasurePoolBinder(), resolveType, static frame => frame.TreasurePools);
        _purchaseCosts = new WorldPurchaseCostReader(resolveType("StructureSO"), resolveType);
        _upgradeCosts = new WorldUpgradeCostReader(resolveType("UpgradeSO"), resolveType);
        _plotActions = new WorldPlotActionReader(resolveType("PlotNodeSO"));
        _actionQueues = new WorldActionQueueReader(
            resolveType("IdScriptableObject"),
            resolveType("PlotNodeActionInstanceListVariable"),
            resolveType("ActionableListVariable"));
        _spellSlots = new WorldSpellSlotReader(
            resolveType("IdScriptableObject"),
            resolveType("SpellListVariable"),
            resolveType);
        _spellWorkbench = new WorldSpellWorkbenchReader(resolveType);
        _targeting = new WorldTargetingReader(resolveType);
        _alchemyInstances = new WorldAlchemyInstanceReader(
            resolveType("IdScriptableObject"),
            resolveType(KnownEntities.ActiveConcepts.ManagedTypeName),
            resolveType(KnownEntities.ConceptRecipes.ManagedTypeName),
            resolveType);
        _alchemyLoadout = new WorldAlchemyLoadoutReader(resolveType);
        _plotAuthoring = new WorldPlotAuthoringReader(resolveType("PlotNodeSO"));
        _effectBlocks = new WorldEffectBlockReader(resolveType("PlotNodeActionSO"), resolveType);
        _entityRequirements = new WorldEntityRequirementReader(
            resolveType("UpgradeSO"),
            resolveType("StructureSO"),
            resolveType("ResearchSO"),
            resolveType("PrerequisiteLinkSO"),
            resolveType("AlchemyRecipeSO"),
            resolveType("GlyphSO"));
        _recipeBookGlyphs = new WorldRecipeBookGlyphReader(resolveType);
        _effectFactors = new WorldEffectFactorReader(resolveType);
        _purchaseViewRelations = new WorldPurchaseViewRelationReader(
            resolveType,
            productionPurchaseTopology);
        _prerequisiteLinkTiers = new WorldPrerequisiteLinkTierReader(
            resolveType("PrerequisiteLinkSO"),
            resolveType("GameManager"));

        _readers = new IWorldCategoryReader[]
        {
            _resources, _structures, _upgrades, _research,
            _doubleVariables, _intVariables, _boolVariables, _modifierVariables, _statistics,
            _attributeGroups, _attributeGroupMembers,
            _statusEffects, _characterAttributes, _damageTypes, _characterModifiers,
            _characterActions, _characterTypes,
            _enchantments, _glyphTypes, _runeStones, _displayTypes,
            _alchemyRecipes, _alchemyTypes, _spellRecipes, _spellLevelCosts, _spellGraph, _spellTypes,
            _equipment, _equipmentTypes, _resourceTypes, _craftingRecipeTypes,
            _structureTypes, _ritualTypes, _harvestTypes, _plotNodeTypes, _researchTypes,
            _consumableFamilies, _harvestActionTypes, _passiveAbilityTypes, _timeRuneTypes,
            _craftingRecipeAuthoring, _craftingRecipes,
            _craftingDecisions,
            _craftingStations,
            _loadouts,
            _harvestElements, _harvestActions, _harvestResources, _harvestLifecycle, _timeRunes,
            _glyphs, _consumables,
            _consumableInventory,
            _scribeRelations,
            _rituals, _achievements, _advancements, _challenges, _challengeContext,
            _thoughtStreams, _tutorials, _views, _plotNodeActions,
            _passiveAbilities, _characters, _discoveryTrees, _plotNodes,
            _recipeBooks, _treasurePools, _purchaseCosts, _upgradeCosts, _plotActions,
            _spellWorkbench,
            _targeting,
            _actionQueues, _spellSlots, _alchemyInstances, _alchemyLoadout,
            _plotAuthoring, _effectBlocks,
            _entityRequirements, _purchaseViewRelations, _recipeBookGlyphs, _effectFactors,
            _prerequisiteLinkTiers,
            _entityKeywords,
            _typeModifiers, _typeModifierContributions,
        };

        _isStructural = new bool[_readers.Length];
        _structuralReports = new WorldCategoryReport[_readers.Length];
        for (var index = 0; index < _readers.Length; index++)
        {
            _isStructural[index] =
                ReferenceEquals(_readers[index], _plotAuthoring) ||
                ReferenceEquals(_readers[index], _effectBlocks) ||
                ReferenceEquals(_readers[index], _spellGraph) ||
                ReferenceEquals(_readers[index], _entityKeywords) ||
                ReferenceEquals(_readers[index], _entityRequirements) ||
                ReferenceEquals(_readers[index], _purchaseViewRelations) ||
                ReferenceEquals(_readers[index], _recipeBookGlyphs) ||
                ReferenceEquals(_readers[index], _craftingRecipeTypes) ||
                ReferenceEquals(_readers[index], _craftingRecipeAuthoring) ||
                ReferenceEquals(_readers[index], _purchaseCosts) ||
                ReferenceEquals(_readers[index], _upgradeCosts);
        }
    }

    /// <summary>
    /// Whether any category resolved at all. False means collection has nothing to offer on this
    /// build and the service should not run; a partial collector still publishes what it can, with
    /// the shortfall named in the report.
    /// </summary>
    internal bool IsAnyCategoryAvailable
    {
        get
        {
            foreach (var reader in _readers)
            {
                if (reader.IsAvailable) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// What this collector calls its categories, in the traversal order a report is built in, so
    /// entry <c>index</c> names the report at <c>index</c>.
    /// </summary>
    /// <remarks>
    /// The modifier-folding pseudo-category is last, because that is where a degraded pass appends it.
    /// It is named here rather than only when it appears: a reader that meets it for the first time
    /// mid-session should find it already named, not discover an unnamed identity.
    /// </remarks>
    internal string[] CategoryNames()
    {
        var names = new string[_readers.Length + 1];
        for (var index = 0; index < _readers.Length; index++) names[index] = _readers[index].Category;
        names[_readers.Length] = ModifierFoldingCategory;
        return names;
    }

    /// <summary>Whether every category resolved. False means the snapshot will be partial by design.</summary>
    /// <remarks>
    /// The frame globals are not a category and never appear in a report row, but they bind against
    /// the game by name exactly as a category does, so a build that renamed one of their accessors is
    /// the same kind of shortfall. Leaving them out let the whole set degrade unnoticed: the reader
    /// yields neutral terms rather than throwing, so nothing else says a word about it.
    /// </remarks>
    internal bool IsFullyAvailable
    {
        get
        {
            if (!_rateGlobals.IsAvailable) return false;

            foreach (var reader in _readers)
            {
                if (!reader.IsAvailable) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Reads every category into <paramref name="frame"/>, replacing whatever the previous collection
    /// left there. Unity thread only.
    /// </summary>
    /// <remarks>
    /// The structural categories are the exception: they describe what the game's authors wrote rather
    /// than what the player has done, so they are re-read only when the frame arrives under a lifecycle
    /// epoch this collector has not already read for. See <see cref="IsStructuralReadingCurrent"/>.
    /// </remarks>
    internal WorldCollectionReport Collect(GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));

        _claimed.Clear();
        var structuralIsCurrent = IsStructuralReadingCurrent(frame);
        frame.FixedDeltaTime = _readFixedDeltaTime();
        frame.FrameGlobals = _rateGlobals.Read(frame.FixedDeltaTime);
        frame.MasteryExperience.Reset();
        _masteryExperience.CopyTo(frame.CollectedAtEpoch, frame.MasteryExperience);

        // Two readers append here, so neither may reset it: whichever ran second would discard the
        // other's rows, and which that is depends on traversal order rather than on anything stated.
        if (!structuralIsCurrent) frame.PurchaseCosts.Reset();
        frame.ModifierPrograms.Reset();
        frame.ModifierProgramEntries.Reset();

        // One extra row when the modifier fold had to reconstruct an input. It is reported as an
        // unavailable pseudo-category rather than logged, because it makes every folded number in the
        // pass suspect and a report that still called itself complete would be lying about all of
        // them. When nothing degraded the row is absent and the report is exactly as it was.
        var degradation = _rateGlobals.Degradation;
        var reports = new WorldCategoryReport[_readers.Length + (degradation.Length == 0 ? 0 : 1)];
        if (degradation.Length > 0)
        {
            reports[_readers.Length] = WorldCategoryReport.Missing(ModifierFoldingCategory, degradation);
        }

        var structuralWasRead = true;
        for (var index = 0; index < _readers.Length; index++)
        {
            if (structuralIsCurrent && _isStructural[index])
            {
                // Not re-read and not reset, so the rows the last epoch's read left are still there.
                // The report is the one that read them, because that is what is true of the buffer —
                // charged at nothing, because that is what this pass spent getting them.
                reports[index] = _structuralReports[index].WithElapsedTicks(0);
                continue;
            }

            // Two clock reads per category, about sixty per pass. Collection is the suite's largest
            // main-thread cost and this is the only place that can say which category it went on: an
            // outer span can time the pass, and nothing outside the loop can attribute it.
            var startedAtTicks = Stopwatch.GetTimestamp();
            reports[index] = _readers[index]
                .Collect(_claimed, frame)
                .WithElapsedTicks(Stopwatch.GetTimestamp() - startedAtTicks);
            if (!_isStructural[index]) continue;
            _structuralReports[index] = reports[index];
            if (_readers[index].IsAvailable &&
                reports[index].Outcome != WorldCategoryOutcome.Collected)
            {
                structuralWasRead = false;
            }
        }

        // The gate says the structural rows in this frame describe this run of the game, so it may
        // only close over rows a read actually delivered. A bound reader that threw left its buffer
        // reset, and calling that "already read for this epoch" is how one transient native
        // exception used to disable a whole lifecycle's worth of admissions — recovery needed a
        // prestige. A reader that did not bind at all is a different fact: nothing on this build
        // will ever make it deliver, and holding the gate open for it would re-walk every other
        // structural category four times a second for an answer that cannot arrive.
        if (!structuralIsCurrent && structuralWasRead)
        {
            _structuralFrame = frame;
            _structuralEpoch = frame.CollectedAtEpoch;
        }

        var report = new WorldCollectionReport(reports);
        frame.Report = report;
        return report;
    }

    /// <summary>
    /// Whether the structural rows already in <paramref name="frame"/> describe the run of the game
    /// this pass is reading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authored content changes at a lifecycle boundary and nowhere else, so reading it four times a
    /// second is four hundred plot and action walks a minute for an answer that cannot have moved. The
    /// epoch is exactly the fact that says when it can have: an unchanged epoch is the same run of the
    /// game, and the same run has the same authors' work in it.
    /// </para>
    /// <para>
    /// Skipping means skipping the <em>native reads</em>, not the derivation. The buffers are left
    /// alone rather than reset, so the tables are rebuilt each cycle from unchanged samples — a few
    /// hundred rows of arithmetic off the Unity thread, which is where the whole design wants that
    /// work to be.
    /// </para>
    /// <para>
    /// The frame is compared by identity as well as by epoch because a collector may be handed more
    /// than one. Two frames under one epoch are two sets of buffers, and only one of them can be the
    /// one that was filled; a skip decided on the epoch alone would hand the other an empty table and
    /// call it collected.
    /// </para>
    /// </remarks>
    private bool IsStructuralReadingCurrent(GameWorldCycleFrame frame) =>
        ReferenceEquals(frame, _structuralFrame) && frame.CollectedAtEpoch == _structuralEpoch;

    /// <summary>
    /// Collects into a frame this collector owns, for callers outside a service cycle.
    /// </summary>
    /// <remarks>
    /// Diagnostics and tests want a collection without standing up a runtime to own the frame for
    /// them. The service path passes its own frame and never touches this one, so the two cannot
    /// interfere.
    /// </remarks>
    internal WorldCollectionReport Collect() => Collect(_ownFrame);

    /// <summary>Derives the collection made into this collector's own frame.</summary>
    internal GameWorldState Build() => GameWorldFrameDeriver.Build(_ownFrame);

    private static WorldCategoryReader<TSample, TRow> Reader<TSample, TRow>(
        WorldRowBinder<TSample, TRow> binder,
        Func<string, Type?> resolveType,
        Func<GameWorldCycleFrame, WorldSampleBuffer<TSample, TRow>> buffer)
        where TSample : struct, IWorldEntity
        where TRow : struct, IWorldEntity =>
        new(binder, resolveType(binder.TypeName), buffer);
}
