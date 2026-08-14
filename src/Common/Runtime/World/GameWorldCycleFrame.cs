using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One collection cycle's readings: every category's raw samples, reused across cycles, owned by the
/// service runtime.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the collector cannot be the frame. A frame crosses to a worker thread, so the
/// structural validator refuses to let one hold delegates — and the collector is almost entirely
/// delegates, an accessor compiled per member per category. Splitting the readings out gives the
/// cycle something it can own and hand across, while the machinery that fills it stays on the Unity
/// thread where the game is.
/// </para>
/// <para>
/// Buffers are reused and never shrink, so a steady-state cycle allocates nothing. That is sound for
/// the same reason it is sound for every other service frame: one frame belongs to one half-duplex
/// cycle, so capture and derivation are never both touching it.
/// </para>
/// <para>
/// Nothing here is derived. Every value is exactly what the game held when the main thread looked,
/// which is what makes <see cref="GameWorldFrameDeriver"/> free to run anywhere.
/// </para>
/// </remarks>
internal sealed class GameWorldCycleFrame
{
    internal WorldSampleBuffer<RawResourceSample, WorldResource> Resources { get; } = new();
    internal WorldSampleBuffer<RawStructureSample, WorldStructure> Structures { get; } = new();
    internal WorldSampleBuffer<RawUpgradeSample, WorldUpgrade> Upgrades { get; } = new();
    internal WorldSampleBuffer<WorldResearch, WorldResearch> Research { get; } = new();
    internal WorldSampleBuffer<WorldNumberVariable, WorldNumberVariable> DoubleVariables { get; } = new();
    internal WorldSampleBuffer<WorldNumberVariable, WorldNumberVariable> IntVariables { get; } = new();
    internal WorldSampleBuffer<WorldBoolVariable, WorldBoolVariable> BoolVariables { get; } = new();
    internal WorldSampleBuffer<WorldModifierVariable, WorldModifierVariable> ModifierVariables { get; } = new();

    /// <summary>
    /// The authored cost entries, which are one-to-many per entity and so cannot share the
    /// one-row-per-entity buffer every other category uses.
    /// </summary>
    internal WorldPurchaseCostBuffer PurchaseCosts { get; } = new();

    /// <summary>Each upgrade's per-level cost modifiers, split into modifiers and exponents.</summary>
    internal WorldLevelCostModifierBuffer LevelCostModifiers { get; } = new();

    /// <summary>
    /// Every plot-and-action pair. Not one row per entity either: a pair belongs to neither side.
    /// </summary>
    internal WorldPlotActionBuffer PlotActions { get; } = new();

    /// <summary>
    /// Every runtime instance of an action a plot holds. A pair can have several, so they cannot
    /// ride on the pair's own buffer.
    /// </summary>
    internal WorldPlotActionInstanceBuffer PlotActionInstances { get; } = new();

    /// <summary>
    /// The game's action queues, and the slots inside them. A queue is an entity; a slot is a
    /// position in one, so the two cannot share a buffer.
    /// </summary>
    internal WorldSampleBuffer<RawWorldActionQueue, WorldActionQueue> ActionQueues { get; } = new();

    internal WorldActionQueueSlotBuffer ActionQueueSlots { get; } = new();

    /// <summary>
    /// The equipped spell loadout, and what casting out of it costs. One reader fills both, because
    /// a slot's price is only answerable from the same equipped instance the slot was read from.
    /// </summary>
    internal WorldSpellSlotBuffer SpellSlots { get; } = new();

    internal WorldSpellCostBuffer SpellCosts { get; } = new();

    /// <summary>Exact mastery-XP inputs observed since the current lifecycle began.</summary>
    internal WorldMasteryExperienceBuffer MasteryExperience { get; } = new();

    /// <summary>
    /// Concept registry membership, active assignments, and the authored/current drain rows read by
    /// one registry pass.
    /// </summary>
    internal WorldConceptRecipeBuffer ConceptRecipes { get; } = new();

    internal WorldAlchemyInstanceBuffer AlchemyInstances { get; } = new();

    internal WorldAlchemyCostBuffer AlchemyCosts { get; } = new();
    internal RawConceptDrainBasisBuffer ConceptDrainBasis { get; } = new();
    internal WorldModifierProgramBuffer ModifierPrograms { get; } = new();
    internal WorldModifierProgramEntryBuffer ModifierProgramEntries { get; } = new();
    internal RawMasteryCostBuffer MasteryCosts { get; } = new();
    internal Guid MasteryCostStandardId { get; set; }
    internal WorldRelationBuffer<WorldSpellRecipeAuthoring> SpellRecipeAuthoring { get; } = new();
    internal WorldRelationBuffer<WorldSpellAuthoredCost> SpellAuthoredCosts { get; } = new();
    internal WorldRelationBuffer<WorldSpellRelation> SpellRelations { get; } = new();
    internal WorldRelationBuffer<WorldEntityKeyword> EntityKeywords { get; } = new();
    internal WorldRelationBuffer<WorldTypeModifier> TypeModifiers { get; } = new();
    internal WorldRelationBuffer<WorldTypeModifierContribution> TypeModifierContributions { get; } = new();
    internal WorldRelationBuffer<WorldTypeSubtype> TypeSubtypes { get; } = new();
    internal WorldRelationBuffer<WorldChallengeTypeBucket> ChallengeTypes { get; } = new();
    internal WorldRelationBuffer<WorldChallengeTypeMembership> ChallengeTypeMemberships { get; } = new();
    internal WorldRelationBuffer<WorldSpellSlotType> SpellSlotTypes { get; } = new();

    internal WorldRelationBuffer<WorldAlchemyLoadoutDecision> AlchemyLoadout { get; } = new();

    internal WorldRelationBuffer<WorldAlchemyUsageCost> AlchemyUsageCosts { get; } = new();

    /// <summary>
    /// What each plot's author decided, and the phases it authors. Keyed by the plot rather than
    /// carrying its identity, so the plot is claimed once.
    /// </summary>
    internal WorldPlotAuthoringBuffer PlotAuthoring { get; } = new();

    internal WorldPlotPhaseDescriptorBuffer PlotPhaseDescriptors { get; } = new();

    /// <summary>Every authored effect block, keyed by the entity whose completion applies it.</summary>
    internal WorldEffectBlockBuffer EffectBlocks { get; } = new();

    /// <summary>
    /// Every authored condition on an entity's next level, keyed by the entity it gates. One-to-many
    /// and not one row per entity, so it cannot ride on the category buffers.
    /// </summary>
    internal WorldEntityRequirementBuffer EntityRequirements { get; } = new();

    /// <summary>
    /// The membership of every list variable a requirement compares against. Filled by the same
    /// structural reader, because a list is only reachable through the condition that names it.
    /// </summary>
    internal WorldRequirementListBuffer RequirementLists { get; } = new();

    /// <summary>
    /// Exact candidate-to-list/view routes for Auto Buy. Authored structure, so the collector keeps
    /// the rows for the whole lifecycle and only the ordinary <c>views</c> category refreshes each
    /// view's live availability.
    /// </summary>
    internal WorldRelationBuffer<WorldPurchaseViewRelation> PurchaseViewRelations { get; } = new();
    internal WorldRelationBuffer<WorldPurchaseViewRoute> PurchaseViewRoutes { get; } = new();

    /// <summary>
    /// Which authored list each upgrade sits on — the game's own grouping axis, and the only fact
    /// that says which screen shows a row. Filled by the same authored walk, and equally structural.
    /// </summary>
    internal WorldRelationBuffer<WorldUpgradeListMembership> UpgradeListMemberships { get; } = new();

    /// <summary>
    /// Which authored <c>GlyphListVariable</c> each glyph sits on — the same grouping axis one level
    /// over, and equally structural. A glyph may sit on several at once, which an upgrade never does.
    /// </summary>
    internal WorldRelationBuffer<WorldGlyphListMembership> GlyphListMemberships { get; } = new();

    /// <summary>The volatile active/passive gates around the structural prerequisite-link graph.</summary>
    internal WorldPrerequisiteLinkTierBuffer PrerequisiteLinkTiers { get; } = new();
    internal WorldSampleBuffer<WorldAlchemyRecipe, WorldAlchemyRecipe> AlchemyRecipes { get; } = new();
    internal WorldSampleBuffer<WorldAlchemyType, WorldAlchemyType> AlchemyTypes { get; } = new();
    internal WorldSampleBuffer<RawSpellRecipeSample, WorldSpellRecipe> SpellRecipes { get; } = new();
    internal WorldSpellWorkbenchBuffer SpellWorkbench { get; } = new();
    internal WorldTargetingBuffer Targeting { get; } = new();
    internal WorldSampleBuffer<WorldSpellType, WorldSpellType> SpellTypes { get; } = new();
    internal WorldSampleBuffer<WorldEquipment, WorldEquipment> Equipment { get; } = new();
    internal WorldSampleBuffer<WorldEquipmentType, WorldEquipmentType> EquipmentTypes { get; } = new();
    internal WorldSampleBuffer<WorldResourceType, WorldResourceType> ResourceTypes { get; } = new();
    internal WorldSampleBuffer<WorldCraftingRecipeType, WorldCraftingRecipeType> CraftingRecipeTypes { get; } = new();
    internal WorldSampleBuffer<WorldStructureType, WorldStructureType> StructureTypes { get; } = new();
    internal WorldSampleBuffer<WorldRitualType, WorldRitualType> RitualTypes { get; } = new();
    internal WorldSampleBuffer<WorldHarvestType, WorldHarvestType> HarvestTypes { get; } = new();
    internal WorldSampleBuffer<WorldPlotNodeType, WorldPlotNodeType> PlotNodeTypes { get; } = new();
    internal WorldSampleBuffer<WorldResearchType, WorldResearchType> ResearchTypes { get; } = new();
    internal WorldSampleBuffer<WorldConsumableFamily, WorldConsumableFamily> ConsumableFamilies { get; } = new();
    internal WorldSampleBuffer<WorldHarvestActionType, WorldHarvestActionType> HarvestActionTypes { get; } = new();
    internal WorldSampleBuffer<WorldPassiveAbilityType, WorldPassiveAbilityType> PassiveAbilityTypes { get; } = new();
    internal WorldSampleBuffer<WorldTimeRuneType, WorldTimeRuneType> TimeRuneTypes { get; } = new();
    internal WorldRelationBuffer<RawCraftingRecipeSample> CraftingRecipes { get; } = new();
    internal WorldRelationBuffer<WorldCraftingRecipeTypeLink> CraftingRecipeTypeLinks { get; } = new();
    internal WorldRelationBuffer<RawCraftingRecipeResource> CraftingRecipeResources { get; } = new();
    internal WorldRelationBuffer<WorldCraftingRecipeConsumableOutput> CraftingRecipeConsumableOutputs { get; } = new();
    internal WorldRelationBuffer<WorldCraftingRecipeDrainBlock> CraftingRecipeDrainBlocks { get; } = new();
    internal WorldRelationBuffer<WorldCraftingDecision> CraftingDecisions { get; } = new();
    internal WorldRelationBuffer<WorldCraftingDecisionCost> CraftingDecisionCosts { get; } = new();
    internal WorldRelationBuffer<WorldCraftingQueueEntry> CraftingQueueEntries { get; } = new();
    internal WorldSampleBuffer<WorldCraftingStation, WorldCraftingStation> CraftingStations { get; } = new();
    internal WorldRelationBuffer<WorldCraftingStationOption> CraftingStationOptions { get; } = new();
    internal WorldRelationBuffer<WorldCraftingStationDrain> CraftingStationDrains { get; } = new();
    internal WorldSampleBuffer<WorldPlayerLoadout, WorldPlayerLoadout> PlayerLoadouts { get; } = new();
    internal WorldRelationBuffer<WorldLoadoutEntry> PlayerLoadoutEntries { get; } = new();
    internal WorldSampleBuffer<WorldSnapshotLoadout, WorldSnapshotLoadout> SnapshotLoadouts { get; } = new();
    internal WorldRelationBuffer<WorldSnapshotSlot> SnapshotSlots { get; } = new();
    internal WorldRelationBuffer<WorldSnapshotEntry> SnapshotEntries { get; } = new();
    internal WorldSampleBuffer<WorldHarvestElement, WorldHarvestElement> HarvestElements { get; } = new();
    internal WorldSampleBuffer<WorldHarvestAction, WorldHarvestAction> HarvestActions { get; } = new();
    internal WorldSampleBuffer<RawHarvestResourceSample, WorldHarvestResource> HarvestResources { get; } = new();
    internal WorldRelationBuffer<WorldHarvestElementControl> HarvestElementControls { get; } = new();
    internal WorldRelationBuffer<WorldHarvestActionControl> HarvestActionControls { get; } = new();
    internal WorldRelationBuffer<WorldHarvestLifecycleCost> HarvestLifecycleCosts { get; } = new();
    internal WorldSampleBuffer<WorldTimeRune, WorldTimeRune> TimeRunes { get; } = new();
    internal WorldSampleBuffer<WorldGlyph, WorldGlyph> Glyphs { get; } = new();
    internal WorldSampleBuffer<RawConsumableSample, WorldConsumable> Consumables { get; } = new();
    internal WorldConsumableInventoryBuffer ConsumableInventory { get; } = new();
    internal Guid ConsumableMaximumCarryLoadVariableId { get; set; }
    internal WorldConsumableTypeBuffer ConsumableTypes { get; } = new();
    internal WorldConsumableCostBuffer ConsumableCosts { get; } = new();
    internal WorldConsumableUsageBuffer ConsumableUsages { get; } = new();
    internal WorldConsumableCountBuffer ConsumableCounts { get; } = new();
    internal WorldRelationBuffer<WorldScribeRecipe> ScribeRecipes { get; } = new();
    internal WorldRelationBuffer<WorldScribeQueue> ScribeQueues { get; } = new();
    internal WorldRelationBuffer<WorldScribeWork> ScribeWork { get; } = new();
    internal WorldRelationBuffer<WorldStructureEnchantment> StructureEnchantments { get; } = new();
    internal WorldRelationBuffer<WorldScrollTarget> ScrollTargets { get; } = new();
    internal WorldRelationBuffer<WorldScrollTargetEvidence> ScrollTargetEvidence { get; } = new();
    internal WorldSampleBuffer<WorldRitual, WorldRitual> Rituals { get; } = new();
    internal WorldSampleBuffer<WorldAchievement, WorldAchievement> Achievements { get; } = new();
    internal WorldSampleBuffer<WorldAdvancement, WorldAdvancement> Advancements { get; } = new();
    internal WorldSampleBuffer<WorldChallenge, WorldChallenge> Challenges { get; } = new();

    internal WorldChallengeContextBuffer ChallengeContext { get; } = new();
    internal WorldSampleBuffer<WorldThoughtStream, WorldThoughtStream> ThoughtStreams { get; } = new();
    internal WorldSampleBuffer<WorldTutorial, WorldTutorial> Tutorials { get; } = new();
    internal WorldSampleBuffer<WorldView, WorldView> Views { get; } = new();
    internal WorldSampleBuffer<WorldPlotNodeAction, WorldPlotNodeAction> PlotNodeActions { get; } = new();
    internal WorldSampleBuffer<WorldPassiveAbility, WorldPassiveAbility> PassiveAbilities { get; } = new();
    internal WorldSampleBuffer<WorldCharacter, WorldCharacter> Characters { get; } = new();
    internal WorldSampleBuffer<WorldDiscoveryTree, WorldDiscoveryTree> DiscoveryTrees { get; } = new();
    internal WorldSampleBuffer<WorldRecipeBook, WorldRecipeBook> RecipeBooks { get; } = new();
    internal WorldSampleBuffer<RawPlotNodeSample, WorldPlotNode> PlotNodes { get; } = new();
    internal WorldSampleBuffer<WorldTreasurePool, WorldTreasurePool> TreasurePools { get; } = new();

    /// <summary>
    /// The once-per-lifecycle identity catalog captured on this same Unity-thread boundary. Every
    /// later frame in the lifecycle carries the same immutable reference.
    /// </summary>
    internal EntityIdentityCatalogSnapshot EntityIdentities { get; set; } =
        EntityIdentityCatalogSnapshot.Unbound(0);

    /// <summary>
    /// Unity's fixed timestep as of this capture. A Unity static that may only be read on the main
    /// thread, so capture is the only place a worker can be given it.
    /// </summary>
    internal double FixedDeltaTime { get; set; }

    /// <summary>
    /// The frame-wide terms the resource rate chain needs, read on the main thread with everything
    /// else. Carries <see cref="FixedDeltaTime"/> too, so the deriver takes one argument rather than
    /// two that must agree.
    /// </summary>
    internal WorldFrameGlobals FrameGlobals { get; set; }

    /// <summary>
    /// The pump frame these readings were true for, which becomes the published snapshot's
    /// generation.
    /// </summary>
    /// <remarks>
    /// Stamped when the game was read rather than when derivation finished, because those are
    /// different frames and only the first answers the question consumers ask. A service that acted
    /// on frame N must not act again until the world has been re-read <em>after</em> N; a snapshot
    /// stamped at publish time would claim to be newer than the action it is missing, which is
    /// exactly the double-act this generation exists to prevent.
    /// </remarks>
    internal long CollectedAtFrame { get; set; }

    /// <summary>
    /// The lifecycle epoch these readings were true for: which run of the game they describe, as
    /// opposed to which pump frame they were taken on.
    /// </summary>
    /// <remarks>
    /// A frame answers "how new is this"; an epoch answers "is this still the same game". A save load
    /// or a reset can leave every entity's identity intact while replacing what stands behind it, and
    /// a consumer holding a reference from before that is holding a stale object no frame comparison
    /// can catch. Stamped by the capture port at the moment the game is read, beside
    /// <see cref="CollectedAtFrame"/> and for the same reason. A frame nobody stamped reads zero,
    /// which is the epoch no lifecycle ever has.
    /// </remarks>
    internal long CollectedAtEpoch { get; set; }

    /// <summary>
    /// UTC ticks from the same capture boundary as <see cref="CollectedAt"/>. Kept as a scalar so
    /// the immutable publication contains no runtime clock or mutable date object.
    /// </summary>
    internal long CollectedAtUtcTicks { get; set; }

    /// <summary>
    /// Monotonic time at which this collection began, carried to diagnostics so a native refusal can
    /// quantify how long its resource quantities had to move before admission.
    /// </summary>
    internal MonotonicTimestamp CollectedAt { get; set; }

    /// <summary>
    /// What the capture could and could not read. Carried on the frame so the worker can project it
    /// into the service's diagnostics without asking the Unity thread a second time.
    /// </summary>
    internal WorldCollectionReport Report { get; set; } = new();
}

/// <summary>
/// Turns one captured frame into one publishable snapshot. The worker half of collection: arithmetic
/// over values already in hand, with no game access, so it may run anywhere.
/// </summary>
/// <remarks>
/// Static and stateless on purpose. Derivation reaching for anything that is not on the frame would
/// be derivation that cannot run off the Unity thread, and the surest way to keep that from happening
/// quietly is to leave it nothing else to reach for.
/// </remarks>
internal static class GameWorldFrameDeriver
{
    internal static GameWorldState Build(GameWorldCycleFrame frame)
    {
        var resources = frame.Resources.Build(new WorldResourceDeriver(frame.FrameGlobals));
        var structures = frame.Structures.Build(WorldStructureDeriver.Shared);
        var modifierVariables =
            frame.ModifierVariables.Build(WorldIdentityDeriver<WorldModifierVariable>.Shared);
        var intVariables =
            frame.IntVariables.Build(WorldIdentityDeriver<WorldNumberVariable>.Shared);
        var alchemyTypes = frame.AlchemyTypes.Build(WorldIdentityDeriver<WorldAlchemyType>.Shared);
        var alchemyCosts = WorldAlchemyCostDeriver.Build(frame.AlchemyCosts);
        var modifierPrograms = WorldModifierProgramDeriver.Build(frame.ModifierPrograms);
        var modifierProgramEntries = WorldModifierProgramDeriver.Build(frame.ModifierProgramEntries);
        var conceptDrainBasis = WorldConceptDrainBasisDeriver.Build(
            frame.ConceptDrainBasis, alchemyTypes, intVariables, alchemyCosts, resources);
        var consumableCosts = WorldConsumableRelationDeriver.Build(frame.ConsumableCosts);
        var spellLevelCosts = OwnedMasteryCostMath.Build(
            frame.MasteryCosts,
            frame.SpellRecipes,
            frame.MasteryCostStandardId,
            modifierPrograms,
            modifierProgramEntries,
            resources);

        // Built after the three tables it reads, because a cost is the one derived fact that is not a
        // function of its own category: it needs the entity's modifiers, each resource's attribute
        // modifier, the modifier the entity points at, and the already-collected grouping counts that
        // bound its exact rising-curve total.
        var upgrades = frame.Upgrades.Build(WorldUpgradeDeriver.Shared);
        var purchaseCosts = new WorldPurchaseCostDeriver(
                structures, upgrades, resources, modifierVariables, frame.LevelCostModifiers,
                frame.FrameGlobals,
                WorldPurchaseGrouping.Read(intVariables, KnownEntities.BulkDevelopment.Uuid))
            .Build(frame.PurchaseCosts);

        // Same shape, same reason: what one run of an action costs a plot is a function of both, so
        // the pair table is built after the two categories it reads.
        var plotNodes = frame.PlotNodes.Build(WorldPlotNodeDeriver.Shared);
        var plotNodeActions = frame.PlotNodeActions.Build(WorldIdentityDeriver<WorldPlotNodeAction>.Shared);
        var plotActions = new WorldPlotActionDeriver(plotNodes, plotNodeActions).Build(frame.PlotActions);
        var purchaseViews = WorldPurchaseViewRelationDeriver.Build(
            frame.PurchaseViewRelations,
            frame.PurchaseViewRoutes);

        // The modifier tables are built here rather than in the initializer because three derived
        // tables read them and one reads another. A type's own total is arithmetic over the captured
        // entries, what a keyword is worth joins that total to the membership and the subtype chain,
        // and the spell type layer is a product over an effective set that spans three tables. All of
        // it is the fold the capture path deliberately left undone.
        var spellRelations = WorldRelationTableDeriver.Build(
            frame.SpellRelations,
            static (left, right) =>
            {
                var recipe = left.RecipeId.CompareTo(right.RecipeId);
                if (recipe != 0) return recipe;
                var kind = ((int)left.Kind).CompareTo((int)right.Kind);
                return kind != 0 ? kind : left.Ordinal.CompareTo(right.Ordinal);
            });
        var entityKeywords = WorldRelationTableDeriver.Build(
            frame.EntityKeywords,
            static (left, right) =>
            {
                var owner = left.OwnerId.CompareTo(right.OwnerId);
                if (owner != 0) return owner;
                var source = ((int)left.Source).CompareTo((int)right.Source);
                return source != 0 ? source : left.Ordinal.CompareTo(right.Ordinal);
            });
        var typeModifiers = WorldRelationTableDeriver.Build(
            frame.TypeModifiers,
            static (left, right) =>
            {
                var type = left.TypeId.CompareTo(right.TypeId);
                return type != 0
                    ? type
                    : string.CompareOrdinal(left.Property, right.Property);
            });
        var typeModifierContributions = WorldRelationTableDeriver.Build(
            frame.TypeModifierContributions,
            static (left, right) =>
            {
                var type = left.TypeId.CompareTo(right.TypeId);
                if (type != 0) return type;
                var property = string.CompareOrdinal(left.Property, right.Property);
                return property != 0
                    ? property
                    : left.Contribution.ModifierId.CompareTo(right.Contribution.ModifierId);
            });
        var typeSubtypes = WorldRelationTableDeriver.Build(
            frame.TypeSubtypes,
            static (left, right) =>
            {
                var type = left.TypeId.CompareTo(right.TypeId);
                return type != 0 ? type : left.Ordinal.CompareTo(right.Ordinal);
            });
        var spellSlotTypes = WorldRelationTableDeriver.Build(
            frame.SpellSlotTypes,
            static (left, right) =>
            {
                var slot = left.SlotIndex.CompareTo(right.SlotIndex);
                return slot != 0 ? slot : left.Ordinal.CompareTo(right.Ordinal);
            });
        var spellSlots = WorldSpellSlotDeriver.Build(frame.SpellSlots);
        var spellTypes = frame.SpellTypes.Build(WorldIdentityDeriver<WorldSpellType>.Shared);
        var research = frame.Research.Build(WorldIdentityDeriver<WorldResearch>.Shared);
        var consumableTypes = WorldConsumableRelationDeriver.Build(frame.ConsumableTypes);
        var typeModifierTotals =
            WorldTypeModifierTotalDeriver.Build(typeModifiers, typeModifierContributions);
        var keywordModifiers = WorldKeywordModifierDeriver.Build(
            typeModifierTotals, entityKeywords, research, consumableTypes, typeSubtypes);
        var spellTypeResonance = WorldSpellTypeResonanceDeriver.Build(
            spellSlots, spellSlotTypes, spellRelations, spellTypes);

        return new GameWorldState
        {
            EntityIdentities = frame.EntityIdentities,
            CollectionCategories = WorldCollectionCategoryStatus.Build(frame.Report),
            FixedDeltaTime = frame.FixedDeltaTime,
            CollectedAtFrame = frame.CollectedAtFrame,
            CollectedAtEpoch = frame.CollectedAtEpoch,
            CollectedAtUtcTicks = frame.CollectedAtUtcTicks,
            CollectedAt = frame.CollectedAt,
            Resources = resources,
            Structures = structures,
            PurchaseCosts = purchaseCosts,
            Upgrades = upgrades,
            Research = research,
            DoubleVariables = frame.DoubleVariables.Build(WorldIdentityDeriver<WorldNumberVariable>.Shared),
            IntVariables = intVariables,
            BoolVariables = frame.BoolVariables.Build(WorldIdentityDeriver<WorldBoolVariable>.Shared),
            ModifierVariables = modifierVariables,
            AlchemyRecipes = WorldAlchemyRecipeDeriver.Build(
                frame.AlchemyRecipes, alchemyTypes, intVariables),
            AlchemyTypes = alchemyTypes,
            SpellRecipes = frame.SpellRecipes.Build(new WorldSpellRecipeDeriver(spellLevelCosts)),
            SpellWorkbench = frame.SpellWorkbench.Build(),
            Targeting = frame.Targeting.Build(),
            SpellRecipeAuthoring = WorldRelationTableDeriver.Build(
                frame.SpellRecipeAuthoring,
                static (left, right) => left.RecipeId.CompareTo(right.RecipeId)),
            SpellAuthoredCosts = WorldRelationTableDeriver.Build(
                frame.SpellAuthoredCosts,
                static (left, right) =>
                {
                    var recipe = left.RecipeId.CompareTo(right.RecipeId);
                    if (recipe != 0) return recipe;
                    var kind = ((int)left.Kind).CompareTo((int)right.Kind);
                    return kind != 0 ? kind : left.Ordinal.CompareTo(right.Ordinal);
                }),
            SpellRelations = spellRelations,
            EntityKeywords = entityKeywords,
            TypeModifiers = typeModifiers,
            TypeModifierContributions = typeModifierContributions,
            TypeSubtypes = typeSubtypes,
            TypeModifierTotals = typeModifierTotals,
            KeywordModifiers = keywordModifiers,
            SpellTypeResonance = spellTypeResonance,
            ChallengeTypes = WorldRelationTableDeriver.Build(
                frame.ChallengeTypes,
                static (left, right) => left.ChallengeTypeId.CompareTo(right.ChallengeTypeId)),
            ChallengeTypeMemberships = WorldRelationTableDeriver.Build(
                frame.ChallengeTypeMemberships,
                static (left, right) =>
                {
                    var challenge = left.ChallengeId.CompareTo(right.ChallengeId);
                    return challenge != 0 ? challenge : left.Ordinal.CompareTo(right.Ordinal);
                }),
            SpellSlotTypes = spellSlotTypes,
            MasteryCosts = spellLevelCosts,
            ModifierPrograms = modifierPrograms,
            ModifierProgramEntries = modifierProgramEntries,
            SpellTypes = spellTypes,
            Equipment = frame.Equipment.Build(WorldIdentityDeriver<WorldEquipment>.Shared),
            EquipmentTypes = frame.EquipmentTypes.Build(WorldIdentityDeriver<WorldEquipmentType>.Shared),
            ResourceTypes = frame.ResourceTypes.Build(WorldIdentityDeriver<WorldResourceType>.Shared),
            CraftingRecipeTypes = frame.CraftingRecipeTypes.Build(WorldIdentityDeriver<WorldCraftingRecipeType>.Shared),
            StructureTypes = frame.StructureTypes.Build(WorldIdentityDeriver<WorldStructureType>.Shared),
            RitualTypes = frame.RitualTypes.Build(WorldIdentityDeriver<WorldRitualType>.Shared),
            HarvestTypes = frame.HarvestTypes.Build(WorldIdentityDeriver<WorldHarvestType>.Shared),
            PlotNodeTypes = frame.PlotNodeTypes.Build(WorldIdentityDeriver<WorldPlotNodeType>.Shared),
            ResearchTypes = frame.ResearchTypes.Build(WorldIdentityDeriver<WorldResearchType>.Shared),
            ConsumableFamilies = frame.ConsumableFamilies.Build(WorldIdentityDeriver<WorldConsumableFamily>.Shared),
            HarvestActionTypes =
                frame.HarvestActionTypes.Build(WorldIdentityDeriver<WorldHarvestActionType>.Shared),
            PassiveAbilityTypes =
                frame.PassiveAbilityTypes.Build(WorldIdentityDeriver<WorldPassiveAbilityType>.Shared),
            TimeRuneTypes = frame.TimeRuneTypes.Build(WorldIdentityDeriver<WorldTimeRuneType>.Shared),
            CraftingRecipes = WorldCraftingRecipeDeriver.Build(
                frame.CraftingRecipes,
                frame.CraftingRecipeTypeLinks,
                frame.CraftingRecipeResources,
                frame.CraftingRecipeConsumableOutputs,
                frame.CraftingRecipeDrainBlocks,
                resources),
            CraftingDecisions = WorldScribeRelationDeriver.Build(
                frame.CraftingDecisions,
                static (left, right) => left.RecipeId.CompareTo(right.RecipeId)),
            CraftingDecisionCosts = WorldScribeRelationDeriver.Build(
                frame.CraftingDecisionCosts,
                static (left, right) =>
                {
                    var recipe = left.RecipeId.CompareTo(right.RecipeId);
                    return recipe != 0 ? recipe : left.ResourceId.CompareTo(right.ResourceId);
                }),
            CraftingQueueEntries = WorldScribeRelationDeriver.Build(
                frame.CraftingQueueEntries,
                static (left, right) =>
                {
                    var queue = left.QueueId.CompareTo(right.QueueId);
                    return queue != 0 ? queue : left.Slot.CompareTo(right.Slot);
                }),
            CraftingStations = frame.CraftingStations.Build(
                WorldIdentityDeriver<WorldCraftingStation>.Shared),
            CraftingStationOptions = WorldCraftingStationDeriver.BuildOptions(
                frame.CraftingStationOptions),
            CraftingStationDrains = WorldCraftingStationDeriver.BuildDrains(
                frame.CraftingStationDrains),
            PlayerLoadouts = frame.PlayerLoadouts.Build(
                WorldIdentityDeriver<WorldPlayerLoadout>.Shared),
            PlayerLoadoutEntries = WorldLoadoutDeriver.BuildEntries(frame.PlayerLoadoutEntries),
            SnapshotLoadouts = frame.SnapshotLoadouts.Build(
                WorldIdentityDeriver<WorldSnapshotLoadout>.Shared),
            SnapshotSlots = WorldLoadoutDeriver.BuildSlots(frame.SnapshotSlots),
            SnapshotEntries = WorldLoadoutDeriver.BuildSnapshotEntries(frame.SnapshotEntries),
            HarvestElements = frame.HarvestElements.Build(WorldIdentityDeriver<WorldHarvestElement>.Shared),
            HarvestActions = frame.HarvestActions.Build(WorldIdentityDeriver<WorldHarvestAction>.Shared),
            HarvestResources = frame.HarvestResources.Build(new WorldHarvestResourceDeriver(frame.FrameGlobals)),
            HarvestElementControls = WorldHarvestLifecycleDeriver.BuildElements(
                frame.HarvestElementControls),
            HarvestActionControls = WorldHarvestLifecycleDeriver.BuildActions(
                frame.HarvestActionControls),
            HarvestLifecycleCosts = WorldHarvestLifecycleDeriver.BuildCosts(
                frame.HarvestLifecycleCosts),
            TimeRunes = frame.TimeRunes.Build(WorldIdentityDeriver<WorldTimeRune>.Shared),
            Glyphs = frame.Glyphs.Build(WorldIdentityDeriver<WorldGlyph>.Shared),
            Consumables = frame.Consumables.Build(new WorldConsumableDeriver(
                WorldLookup.TryFind(
                    intVariables,
                    frame.ConsumableMaximumCarryLoadVariableId,
                    out var maximumCarryLoad)
                    ? maximumCarryLoad.Value.ToInt()
                    : 0,
                consumableCosts,
                resources)),
            ConsumableTypes = consumableTypes,
            ConsumableCosts = consumableCosts,
            ConsumableUsages = WorldConsumableRelationDeriver.Build(frame.ConsumableUsages),
            ConsumableCounts = WorldConsumableRelationDeriver.Build(frame.ConsumableCounts),
            ConsumableInventory = frame.ConsumableInventory.Build(),
            ScribeRecipes = WorldScribeRelationDeriver.Build(
                frame.ScribeRecipes,
                static (left, right) => left.RecipeId.CompareTo(right.RecipeId)),
            ScribeQueues = WorldScribeRelationDeriver.Build(
                frame.ScribeQueues,
                static (left, right) => left.QueueId.CompareTo(right.QueueId)),
            ScribeWork = WorldScribeRelationDeriver.Build(
                frame.ScribeWork,
                static (left, right) =>
                {
                    var queue = left.QueueId.CompareTo(right.QueueId);
                    if (queue != 0) return queue;
                    var recipe = left.RecipeId.CompareTo(right.RecipeId);
                    return recipe != 0 ? recipe : left.Level.CompareTo(right.Level);
                }),
            StructureEnchantments = WorldScribeRelationDeriver.Build(
                frame.StructureEnchantments,
                static (left, right) =>
                {
                    var structure = left.StructureId.CompareTo(right.StructureId);
                    return structure != 0
                        ? structure
                        : left.EnchantmentId.CompareTo(right.EnchantmentId);
                }),
            ScrollTargets = WorldScribeRelationDeriver.Build(
                frame.ScrollTargets,
                static (left, right) =>
                {
                    var item = left.ConsumableId.CompareTo(right.ConsumableId);
                    if (item != 0) return item;
                    var enchantment = left.EnchantmentId.CompareTo(right.EnchantmentId);
                    return enchantment != 0
                        ? enchantment
                        : left.StructureId.CompareTo(right.StructureId);
                }),
            ScrollTargetEvidence = WorldScribeRelationDeriver.Build(
                frame.ScrollTargetEvidence,
                static (left, right) =>
                {
                    var item = left.ConsumableId.CompareTo(right.ConsumableId);
                    return item != 0
                        ? item
                        : left.EnchantmentId.CompareTo(right.EnchantmentId);
                }),
            Rituals = WorldRitualDeriver.Build(frame.Rituals),
            Achievements = frame.Achievements.Build(WorldIdentityDeriver<WorldAchievement>.Shared),
            Advancements = frame.Advancements.Build(WorldIdentityDeriver<WorldAdvancement>.Shared),
            Challenges = frame.Challenges.Build(WorldIdentityDeriver<WorldChallenge>.Shared),
            ChallengeContext = frame.ChallengeContext.Build(),
            ThoughtStreams = frame.ThoughtStreams.Build(WorldIdentityDeriver<WorldThoughtStream>.Shared),
            Tutorials = frame.Tutorials.Build(WorldIdentityDeriver<WorldTutorial>.Shared),
            Views = frame.Views.Build(WorldIdentityDeriver<WorldView>.Shared),
            PurchaseViewRelations = purchaseViews.Relations,
            PurchaseViewRoutes = purchaseViews.Routes,
            UpgradeListMemberships =
                WorldUpgradeListMembershipDeriver.Build(frame.UpgradeListMemberships),
            GlyphListMemberships =
                WorldGlyphListMembershipDeriver.Build(frame.GlyphListMemberships),
            PlotNodeActions = plotNodeActions,
            PassiveAbilities = frame.PassiveAbilities.Build(WorldIdentityDeriver<WorldPassiveAbility>.Shared),
            Characters = frame.Characters.Build(WorldIdentityDeriver<WorldCharacter>.Shared),
            DiscoveryTrees = frame.DiscoveryTrees.Build(WorldIdentityDeriver<WorldDiscoveryTree>.Shared),
            RecipeBooks = frame.RecipeBooks.Build(WorldIdentityDeriver<WorldRecipeBook>.Shared),
            PlotNodes = plotNodes,
            PlotActions = plotActions,
            PlotActionInstances = WorldPlotActionInstanceDeriver.Build(frame.PlotActionInstances),
            ActionQueues = frame.ActionQueues.Build(new WorldActionQueueDeriver(intVariables)),
            ActionQueueSlots = WorldActionQueueSlotDeriver.Build(frame.ActionQueueSlots),
            SpellSlots = spellSlots,
            SpellCosts = WorldSpellCostDeriver.Build(frame.SpellCosts),
            MasteryExperience = WorldMasteryExperienceDeriver.Build(frame.MasteryExperience),
            ConceptRecipes = WorldAlchemyRowDeriver.Build(frame.ConceptRecipes),
            AlchemyInstances = WorldAlchemyRowDeriver.Build(frame.AlchemyInstances),
            ConceptDrainBasis = conceptDrainBasis,
            AlchemyCosts = alchemyCosts,
            AlchemyLoadout = WorldScribeRelationDeriver.Build(
                frame.AlchemyLoadout,
                static (left, right) => left.RecipeId.CompareTo(right.RecipeId)),
            AlchemyUsageCosts = WorldScribeRelationDeriver.Build(
                frame.AlchemyUsageCosts,
                static (left, right) =>
                {
                    var recipe = left.RecipeId.CompareTo(right.RecipeId);
                    return recipe != 0 ? recipe : left.ResourceId.CompareTo(right.ResourceId);
                }),
            PlotAuthoring = WorldPlotAuthoringDeriver.Build(frame.PlotAuthoring),
            PlotPhaseDescriptors =
                WorldPlotPhaseDescriptorDeriver.Build(frame.PlotPhaseDescriptors),
            EffectBlocks = WorldEffectBlockDeriver.Build(frame.EffectBlocks),
            EntityRequirements = WorldEntityRequirementDeriver.Build(frame.EntityRequirements),
            RequirementListMembers = WorldRequirementListDeriver.Build(frame.RequirementLists),
            PrerequisiteLinkTiers =
                WorldPrerequisiteLinkTierDeriver.Build(frame.PrerequisiteLinkTiers),
            TreasurePools = frame.TreasurePools.Build(WorldIdentityDeriver<WorldTreasurePool>.Shared),
        };
    }
}
