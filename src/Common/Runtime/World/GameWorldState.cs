using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One immutable snapshot of everything the suite knows about the game world, published once per
/// collection interval and consumed by every service the same way configuration is.
/// </summary>
/// <remarks>
/// <para>
/// The shape is category-generic on purpose: one bounded table per entity category, each keyed by
/// stable UUID. Adding a category is a row type and a table, not a redesign — which is why the four
/// categories this started with became every category the game keeps per-entity state for, without
/// the shape changing.
/// </para>
/// <para>
/// This exists because roughly ten services are expected to need resource state every tick, and
/// having each capture its own copy costs the main thread once per service. One publication that
/// every worker pins is the same latest-wins, generation-stamped bargain configuration already
/// makes: a consumer that runs before a newer snapshot lands uses the previous one and picks the new
/// one up next cycle, so no cross-service scheduling is introduced.
/// </para>
/// <para>
/// Tables are sorted by identity at build time, so lookups are a binary search rather than a scan —
/// with 80 resources and 222 upgrades in a real save, a linear probe per candidate per cycle is the
/// kind of cost this whole design exists to remove.
/// </para>
/// <para>
/// Public because the worker contract names it: the runtime hands every service the snapshot as an
/// argument, and an internal parameter type cannot appear on a public interface. Its members stay
/// internal, so the suite reads the world and anything outside it sees only an opaque handle.
/// </para>
/// </remarks>
public sealed record GameWorldState
{
    /// <summary>
    /// Names and runtime types from the stable game registry, captured once for this lifecycle. This
    /// is the exact Common latest-wins catalog reference, not a per-world copy or category join.
    /// </summary>
    internal EntityIdentityCatalogSnapshot EntityIdentities { get; init; } =
        EntityIdentityCatalogSnapshot.Unbound(0);

    /// <summary>
    /// The result of attempting each world category in this exact collection. A zero-row category is
    /// only a fact about the save when its matching row says it was collected cleanly.
    /// </summary>
    internal PublicationTable<WorldCollectionCategoryStatus> CollectionCategories { get; init; } =
        PublicationTable<WorldCollectionCategoryStatus>.Empty;

    internal PublicationTable<WorldResource> Resources { get; init; } =
        PublicationTable<WorldResource>.Empty;

    internal PublicationTable<WorldStructure> Structures { get; init; } =
        PublicationTable<WorldStructure>.Empty;

    internal PublicationTable<WorldUpgrade> Upgrades { get; init; } =
        PublicationTable<WorldUpgrade>.Empty;

    internal PublicationTable<WorldResearch> Research { get; init; } =
        PublicationTable<WorldResearch>.Empty;

    /// <summary>
    /// The game's global variables, by registry. Every <c>Player</c> and <c>GlobalVariables</c>
    /// accessor is a lookup into one of these three, so they are collected as registries rather than
    /// as a hundred separately declared accessors.
    /// </summary>
    internal PublicationTable<WorldNumberVariable> DoubleVariables { get; init; } =
        PublicationTable<WorldNumberVariable>.Empty;

    internal PublicationTable<WorldNumberVariable> IntVariables { get; init; } =
        PublicationTable<WorldNumberVariable>.Empty;

    internal PublicationTable<WorldBoolVariable> BoolVariables { get; init; } =
        PublicationTable<WorldBoolVariable>.Empty;

    /// <summary>
    /// The global modifier registry. Entities point into it by identity rather than owning a copy,
    /// so a modifier shared by twenty structures is collected once.
    /// </summary>
    internal PublicationTable<WorldModifierVariable> ModifierVariables { get; init; } =
        PublicationTable<WorldModifierVariable>.Empty;

    /// <summary>
    /// The game's own statistic glossary — the words it prints above its numbers and the sentences
    /// it prints under them. Authored text that never moves, collected beside the registries for
    /// the same reason they are: every screen in the game reads out of it.
    /// </summary>
    internal PublicationTable<WorldStatistic> Statistics { get; init; } =
        PublicationTable<WorldStatistic>.Empty;

    /// <summary>
    /// The Statistics tab's other half: the headings its numbers are grouped under, and — as
    /// <see cref="AttributeGroupMembers"/> — what each heading's bonus is distributed into.
    /// </summary>
    internal PublicationTable<WorldAttributeGroup> AttributeGroups { get; init; } =
        PublicationTable<WorldAttributeGroup>.Empty;

    /// <summary>
    /// Every stat group's authored distribution, sorted by group and then by authored position, so
    /// one group's members are a contiguous range. Read it through
    /// <see cref="WorldAttributeGroupMemberLookup"/>.
    /// </summary>
    internal PublicationTable<WorldAttributeGroupMember> AttributeGroupMembers { get; init; } =
        PublicationTable<WorldAttributeGroupMember>.Empty;

    /// <summary>
    /// The ritual layer's own vocabulary — the words a battle log, a portrait and an enemy card are
    /// written in. Six authored glossaries the game ships and never changes, published under the
    /// game's own word for each class rather than folded into one table: they carry different facts,
    /// and two of them name each other.
    /// </summary>
    internal PublicationTable<WorldStatusEffect> StatusEffects { get; init; } =
        PublicationTable<WorldStatusEffect>.Empty;

    internal PublicationTable<WorldCharacterAttribute> CharacterAttributes { get; init; } =
        PublicationTable<WorldCharacterAttribute>.Empty;

    internal PublicationTable<WorldDamageType> DamageTypes { get; init; } =
        PublicationTable<WorldDamageType>.Empty;

    internal PublicationTable<WorldCharacterModifier> CharacterModifiers { get; init; } =
        PublicationTable<WorldCharacterModifier>.Empty;

    internal PublicationTable<WorldCharacterAction> CharacterActions { get; init; } =
        PublicationTable<WorldCharacterAction>.Empty;

    internal PublicationTable<WorldCharacterType> CharacterTypes { get; init; } =
        PublicationTable<WorldCharacterType>.Empty;

    /// <summary>
    /// The four authored vocabularies that close edges the world already publishes: what scribing
    /// applies, which page a glyph sits on, the time layer's named stones, and the three words a
    /// tooltip splits its sections under.
    /// </summary>
    internal PublicationTable<WorldEnchantment> Enchantments { get; init; } =
        PublicationTable<WorldEnchantment>.Empty;

    internal PublicationTable<WorldGlyphType> GlyphTypes { get; init; } =
        PublicationTable<WorldGlyphType>.Empty;

    internal PublicationTable<WorldRuneStone> RuneStones { get; init; } =
        PublicationTable<WorldRuneStone>.Empty;

    internal PublicationTable<WorldDisplayType> DisplayTypes { get; init; } =
        PublicationTable<WorldDisplayType>.Empty;

    /// <summary>
    /// What one more level of each entity costs, computed here rather than asked of the game. Keyed by
    /// entity and then resource, with several rows per entity; read it through
    /// <see cref="WorldPurchaseCostLookup"/> rather than <see cref="WorldLookup"/>.
    /// </summary>
    internal PublicationTable<WorldPurchaseCost> PurchaseCosts { get; init; } =
        PublicationTable<WorldPurchaseCost>.Empty;

    internal PublicationTable<WorldAlchemyRecipe> AlchemyRecipes { get; init; } =
        PublicationTable<WorldAlchemyRecipe>.Empty;

    internal PublicationTable<WorldAlchemyType> AlchemyTypes { get; init; } =
        PublicationTable<WorldAlchemyType>.Empty;

    internal PublicationTable<WorldSpellRecipe> SpellRecipes { get; init; } =
        PublicationTable<WorldSpellRecipe>.Empty;

    /// <summary>The native equipped-loadout capacity and global casting dials for this world.</summary>
    internal WorldSpellWorkbench SpellWorkbench { get; init; } = new(
        0,
        0,
        false,
        0,
        0,
        0,
        0);

    /// <summary>The native current target request, empty when the game is not targeting.</summary>
    internal PublicationTable<WorldTargetingRequest> Targeting { get; init; } =
        PublicationTable<WorldTargetingRequest>.Empty;

    /// <summary>Authored cast, recharge, channel, and repeated-effect scalars for every spell.</summary>
    internal PublicationTable<WorldSpellRecipeAuthoring> SpellRecipeAuthoring { get; init; } =
        PublicationTable<WorldSpellRecipeAuthoring>.Empty;

    /// <summary>All authored immediate, usage, and held-drain tuples for every spell.</summary>
    internal PublicationTable<WorldSpellAuthoredCost> SpellAuthoredCosts { get; init; } =
        PublicationTable<WorldSpellAuthoredCost>.Empty;

    /// <summary>Spell-to-type, core-glyph, and recipe-book edges in authored order.</summary>
    internal PublicationTable<WorldSpellRelation> SpellRelations { get; init; } =
        PublicationTable<WorldSpellRelation>.Empty;

    /// <summary>
    /// Every authored keyword edge: the entity, and the type asset whose display name the game prints
    /// in its tooltip word line.
    /// </summary>
    internal PublicationTable<WorldEntityKeyword> EntityKeywords { get; init; } =
        PublicationTable<WorldEntityKeyword>.Empty;

    /// <summary>
    /// Every modifier record on every type asset, with how many modifiers currently sit on it.
    /// </summary>
    /// <remarks>
    /// A total derived from these rows and a member value the suite already publishes are the same
    /// bonus for every distributor-fed record; see <see cref="WorldTypeModifier"/> for which they are
    /// and why <c>SpellTypeSO</c> is the exception.
    /// </remarks>
    internal PublicationTable<WorldTypeModifier> TypeModifiers { get; init; } =
        PublicationTable<WorldTypeModifier>.Empty;

    /// <summary>Every modifier entry behind those counts, with the source that put it there.</summary>
    internal PublicationTable<WorldTypeModifierContribution> TypeModifierContributions { get; init; } =
        PublicationTable<WorldTypeModifierContribution>.Empty;

    /// <summary>Parent-to-child structure type edges, along which a parent's records are conferred.</summary>
    internal PublicationTable<WorldTypeSubtype> TypeSubtypes { get; init; } =
        PublicationTable<WorldTypeSubtype>.Empty;

    /// <summary>The weighted buckets the challenge draft picks from, before it picks a challenge.</summary>
    internal PublicationTable<WorldChallengeTypeBucket> ChallengeTypes { get; init; } =
        PublicationTable<WorldChallengeTypeBucket>.Empty;

    /// <summary>Which bucket each challenge is drafted out of.</summary>
    internal PublicationTable<WorldChallengeTypeMembership> ChallengeTypeMemberships { get; init; } =
        PublicationTable<WorldChallengeTypeMembership>.Empty;

    /// <summary>
    /// The spell types each equipped spell currently resonates with, after glyphs have rewritten the
    /// authored set.
    /// </summary>
    internal PublicationTable<WorldSpellSlotType> SpellSlotTypes { get; init; } =
        PublicationTable<WorldSpellSlotType>.Empty;

    /// <summary>
    /// What each type-level record that holds no value of its own currently adds up to.
    /// </summary>
    /// <remarks>
    /// Derived, never captured: the fold is arithmetic, and arithmetic belongs off the Unity thread.
    /// The magnitudes are named <c>DistributedTotal…</c> while the member value they already sit
    /// inside keeps the plain property name — see <see cref="WorldTypeModifierTotal"/> for why
    /// multiplying the two is the mistake this shape exists to make hard.
    /// </remarks>
    internal PublicationTable<WorldTypeModifierTotal> TypeModifierTotals { get; init; } =
        PublicationTable<WorldTypeModifierTotal>.Empty;

    /// <summary>
    /// What each keyword is currently worth, and how many members of each kind it reaches once the
    /// structure subtype chain is followed.
    /// </summary>
    internal PublicationTable<WorldKeywordModifier> KeywordModifiers { get; init; } =
        PublicationTable<WorldKeywordModifier>.Empty;

    /// <summary>
    /// The spell type layer of each equipped spell — the one type total the game itself multiplies
    /// into a member's number.
    /// </summary>
    internal PublicationTable<WorldSpellTypeResonance> SpellTypeResonance { get; init; } =
        PublicationTable<WorldSpellTypeResonance>.Empty;

    /// <summary>Derived next-mastery-level cost rows, preserved in authored row order.</summary>
    internal PublicationTable<WorldMasteryCost> MasteryCosts { get; init; } =
        PublicationTable<WorldMasteryCost>.Empty;

    /// <summary>Narrow raw modifier programs used by the two owned Phase-B formula families.</summary>
    internal PublicationTable<WorldModifierProgram> ModifierPrograms { get; init; } =
        PublicationTable<WorldModifierProgram>.Empty;

    internal PublicationTable<WorldModifierProgramEntry> ModifierProgramEntries { get; init; } =
        PublicationTable<WorldModifierProgramEntry>.Empty;

    internal PublicationTable<WorldSpellType> SpellTypes { get; init; } =
        PublicationTable<WorldSpellType>.Empty;

    internal PublicationTable<WorldEquipment> Equipment { get; init; } =
        PublicationTable<WorldEquipment>.Empty;

    internal PublicationTable<WorldEquipmentType> EquipmentTypes { get; init; } =
        PublicationTable<WorldEquipmentType>.Empty;

    internal PublicationTable<WorldResourceType> ResourceTypes { get; init; } =
        PublicationTable<WorldResourceType>.Empty;

    internal PublicationTable<WorldCraftingRecipeType> CraftingRecipeTypes { get; init; } =
        PublicationTable<WorldCraftingRecipeType>.Empty;

    /// <summary>
    /// The nine type taxonomies that carry modifier records and, until now, no row of their own. See
    /// <see cref="WorldStructureType"/> for why a row here holds identity and value records only.
    /// </summary>
    internal PublicationTable<WorldStructureType> StructureTypes { get; init; } =
        PublicationTable<WorldStructureType>.Empty;

    internal PublicationTable<WorldRitualType> RitualTypes { get; init; } =
        PublicationTable<WorldRitualType>.Empty;

    internal PublicationTable<WorldHarvestType> HarvestTypes { get; init; } =
        PublicationTable<WorldHarvestType>.Empty;

    internal PublicationTable<WorldPlotNodeType> PlotNodeTypes { get; init; } =
        PublicationTable<WorldPlotNodeType>.Empty;

    internal PublicationTable<WorldResearchType> ResearchTypes { get; init; } =
        PublicationTable<WorldResearchType>.Empty;

    internal PublicationTable<WorldConsumableFamily> ConsumableFamilies { get; init; } =
        PublicationTable<WorldConsumableFamily>.Empty;

    internal PublicationTable<WorldHarvestActionType> HarvestActionTypes { get; init; } =
        PublicationTable<WorldHarvestActionType>.Empty;

    internal PublicationTable<WorldPassiveAbilityType> PassiveAbilityTypes { get; init; } =
        PublicationTable<WorldPassiveAbilityType>.Empty;

    internal PublicationTable<WorldTimeRuneType> TimeRuneTypes { get; init; } =
        PublicationTable<WorldTimeRuneType>.Empty;

    /// <summary>
    /// Every concrete crafting recipe with authored inputs/outputs and current native visibility,
    /// purchase, output-capacity, bandwidth, and engagement-drain evidence.
    /// </summary>
    internal PublicationTable<WorldCraftingRecipe> CraftingRecipes { get; init; } =
        PublicationTable<WorldCraftingRecipe>.Empty;

    /// <summary>Exact next manual-craft decision and authored queue routing per recipe.</summary>
    internal PublicationTable<WorldCraftingDecision> CraftingDecisions { get; init; } =
        PublicationTable<WorldCraftingDecision>.Empty;

    internal PublicationTable<WorldCraftingDecisionCost> CraftingDecisionCosts { get; init; } =
        PublicationTable<WorldCraftingDecisionCost>.Empty;

    /// <summary>Ordered visible manual and automatic crafting queue contents.</summary>
    internal PublicationTable<WorldCraftingQueueEntry> CraftingQueueEntries { get; init; } =
        PublicationTable<WorldCraftingQueueEntry>.Empty;

    /// <summary>Runtime Brewing Stations and the choices exposed by their owning screen.</summary>
    internal PublicationTable<WorldCraftingStation> CraftingStations { get; init; } =
        PublicationTable<WorldCraftingStation>.Empty;

    internal PublicationTable<WorldCraftingStationOption> CraftingStationOptions { get; init; } =
        PublicationTable<WorldCraftingStationOption>.Empty;

    internal PublicationTable<WorldCraftingStationDrain> CraftingStationDrains { get; init; } =
        PublicationTable<WorldCraftingStationDrain>.Empty;

    internal PublicationTable<WorldPlayerLoadout> PlayerLoadouts { get; init; } =
        PublicationTable<WorldPlayerLoadout>.Empty;
    internal PublicationTable<WorldLoadoutEntry> PlayerLoadoutEntries { get; init; } =
        PublicationTable<WorldLoadoutEntry>.Empty;
    internal PublicationTable<WorldSnapshotLoadout> SnapshotLoadouts { get; init; } =
        PublicationTable<WorldSnapshotLoadout>.Empty;
    internal PublicationTable<WorldSnapshotSlot> SnapshotSlots { get; init; } =
        PublicationTable<WorldSnapshotSlot>.Empty;
    internal PublicationTable<WorldSnapshotEntry> SnapshotEntries { get; init; } =
        PublicationTable<WorldSnapshotEntry>.Empty;

    internal PublicationTable<WorldHarvestElement> HarvestElements { get; init; } =
        PublicationTable<WorldHarvestElement>.Empty;

    /// <summary>
    /// The six base agromancy verbs an element offers. Their type linkage is a keyword row rather
    /// than a column here; see <see cref="WorldHarvestAction"/>.
    /// </summary>
    internal PublicationTable<WorldHarvestAction> HarvestActions { get; init; } =
        PublicationTable<WorldHarvestAction>.Empty;

    /// <summary>
    /// The resource each harvest element owns. Separate from <see cref="Resources"/> because the game
    /// keeps it out of <c>ResourceSO.All</c> and out of every global aggregate; see
    /// <see cref="RawHarvestResourceSample"/>.
    /// </summary>
    internal PublicationTable<WorldHarvestResource> HarvestResources { get; init; } =
        PublicationTable<WorldHarvestResource>.Empty;

    /// <summary>Active-count and admission facts for the player's harvest element list.</summary>
    internal PublicationTable<WorldHarvestElementControl> HarvestElementControls { get; init; } =
        PublicationTable<WorldHarvestElementControl>.Empty;

    /// <summary>Active-count and admission facts for every offered element/action pair.</summary>
    internal PublicationTable<WorldHarvestActionControl> HarvestActionControls { get; init; } =
        PublicationTable<WorldHarvestActionControl>.Empty;

    /// <summary>Only the costs that can affect the player's next harvest-list decision.</summary>
    internal PublicationTable<WorldHarvestLifecycleCost> HarvestLifecycleCosts { get; init; } =
        PublicationTable<WorldHarvestLifecycleCost>.Empty;

    internal PublicationTable<WorldTimeRune> TimeRunes { get; init; } =
        PublicationTable<WorldTimeRune>.Empty;

    internal PublicationTable<WorldGlyph> Glyphs { get; init; } =
        PublicationTable<WorldGlyph>.Empty;

    internal PublicationTable<WorldConsumable> Consumables { get; init; } =
        PublicationTable<WorldConsumable>.Empty;

    /// <summary>Every native family assigned to each consumable.</summary>
    internal PublicationTable<WorldConsumableType> ConsumableTypes { get; init; } =
        PublicationTable<WorldConsumableType>.Empty;

    /// <summary>Every immediate and held resource cost authored on each consumable.</summary>
    internal PublicationTable<WorldConsumableCost> ConsumableCosts { get; init; } =
        PublicationTable<WorldConsumableCost>.Empty;

    /// <summary>Every pending or engaged native usage owned by each consumable.</summary>
    internal PublicationTable<WorldConsumableUsage> ConsumableUsages { get; init; } =
        PublicationTable<WorldConsumableUsage>.Empty;

    /// <summary>Every levelled inventory bucket owned by each consumable.</summary>
    internal PublicationTable<WorldConsumableCount> ConsumableCounts { get; init; } =
        PublicationTable<WorldConsumableCount>.Empty;

    /// <summary>The player's ordered consumable inventory and hotbar plus live use admission.</summary>
    internal WorldConsumableInventory ConsumableInventory { get; init; } =
        WorldConsumableInventory.Empty;

    /// <summary>The complete contents of the audited Scribe recipe registry.</summary>
    internal PublicationTable<WorldScribeRecipe> ScribeRecipes { get; init; } =
        PublicationTable<WorldScribeRecipe>.Empty;

    /// <summary>Occupancy of the player and game-owned Scribe production lists.</summary>
    internal PublicationTable<WorldScribeQueue> ScribeQueues { get; init; } =
        PublicationTable<WorldScribeQueue>.Empty;

    /// <summary>
    /// Existing Scribe work, including player-owned AutoScribeInstances as observed external
    /// production pressure. The suite never edits that persistent automation list.
    /// </summary>
    internal PublicationTable<WorldScribeWork> ScribeWork { get; init; } =
        PublicationTable<WorldScribeWork>.Empty;

    internal PublicationTable<WorldStructureEnchantment> StructureEnchantments { get; init; } =
        PublicationTable<WorldStructureEnchantment>.Empty;

    internal PublicationTable<WorldScrollTarget> ScrollTargets { get; init; } =
        PublicationTable<WorldScrollTarget>.Empty;

    internal PublicationTable<WorldScrollTargetEvidence> ScrollTargetEvidence { get; init; } =
        PublicationTable<WorldScrollTargetEvidence>.Empty;

    internal PublicationTable<WorldRitual> Rituals { get; init; } =
        PublicationTable<WorldRitual>.Empty;

    internal PublicationTable<WorldAchievement> Achievements { get; init; } =
        PublicationTable<WorldAchievement>.Empty;

    internal PublicationTable<WorldAdvancement> Advancements { get; init; } =
        PublicationTable<WorldAdvancement>.Empty;

    internal PublicationTable<WorldChallenge> Challenges { get; init; } =
        PublicationTable<WorldChallenge>.Empty;

    /// <summary>Ordered challenge selections/offers and the next fetch decision from the same frame.</summary>
    internal WorldChallengeContext ChallengeContext { get; init; }

    internal PublicationTable<WorldThoughtStream> ThoughtStreams { get; init; } =
        PublicationTable<WorldThoughtStream>.Empty;

    internal PublicationTable<WorldTutorial> Tutorials { get; init; } =
        PublicationTable<WorldTutorial>.Empty;

    internal PublicationTable<WorldView> Views { get; init; } =
        PublicationTable<WorldView>.Empty;

    /// <summary>
    /// The exact authored list and owning view for each native Auto Buy candidate. Non-resolved rows
    /// are published too, so a planner can fail closed with the relation's named status.
    /// </summary>
    internal PublicationTable<WorldPurchaseViewRelation> PurchaseViewRelations { get; init; } =
        PublicationTable<WorldPurchaseViewRelation>.Empty;

    internal PublicationTable<WorldPurchaseViewRoute> PurchaseViewRoutes { get; init; } =
        PublicationTable<WorldPurchaseViewRoute>.Empty;

    /// <summary>
    /// Which authored <c>UpgradeListVariable</c> each upgrade sits on. Empty means the read did not
    /// land, never that the upgrades sit on nothing: membership is published whole or withheld
    /// whole, so no row can be quietly demoted to "on no screen".
    /// </summary>
    internal PublicationTable<WorldUpgradeListMembership> UpgradeListMemberships { get; init; } =
        PublicationTable<WorldUpgradeListMembership>.Empty;

    /// <summary>
    /// Which authored <c>GlyphListVariable</c> each glyph sits on, for the four lists that are a
    /// population rather than a runtime selection or the whole registry. Withheld whole for the same
    /// reason as the upgrade table above: a partial one reads exactly like a glyph on no list.
    /// </summary>
    internal PublicationTable<WorldGlyphListMembership> GlyphListMemberships { get; init; } =
        PublicationTable<WorldGlyphListMembership>.Empty;

    /// <summary>
    /// What each glyph does, as the authored factors it applies. Sorted by glyph and then by the
    /// slot order the game declares them in, so one glyph's factors are a contiguous range.
    /// </summary>
    internal PublicationTable<WorldGlyphFactor> GlyphEffects { get; init; } =
        PublicationTable<WorldGlyphFactor>.Empty;

    /// <summary>
    /// What one more level of a levelable entity buys, as the authored modifier tuples it applies.
    /// Sorted by owner and then by authored position, so one entity's tuples are a contiguous range.
    /// </summary>
    internal PublicationTable<WorldLevelEffect> LevelEffects { get; init; } =
        PublicationTable<WorldLevelEffect>.Empty;

    internal PublicationTable<WorldPlotNodeAction> PlotNodeActions { get; init; } =
        PublicationTable<WorldPlotNodeAction>.Empty;

    internal PublicationTable<WorldPassiveAbility> PassiveAbilities { get; init; } =
        PublicationTable<WorldPassiveAbility>.Empty;

    internal PublicationTable<WorldCharacter> Characters { get; init; } =
        PublicationTable<WorldCharacter>.Empty;

    internal PublicationTable<WorldDiscoveryTree> DiscoveryTrees { get; init; } =
        PublicationTable<WorldDiscoveryTree>.Empty;

    internal PublicationTable<WorldRecipeBook> RecipeBooks { get; init; } =
        PublicationTable<WorldRecipeBook>.Empty;

    internal PublicationTable<WorldPlotNode> PlotNodes { get; init; } =
        PublicationTable<WorldPlotNode>.Empty;

    /// <summary>
    /// Every plot-and-action pair: whether the plot offers the action, whether it holds an instance
    /// of it, and what one run would cost. Read through <see cref="WorldPlotActionLookup"/>.
    /// </summary>
    internal PublicationTable<WorldPlotAction> PlotActions { get; init; } =
        PublicationTable<WorldPlotAction>.Empty;

    /// <summary>
    /// Every runtime action instance each plot holds, sorted by plot, then action, then position.
    /// Read through <see cref="WorldPlotActionInstanceLookup"/>, which answers with a range because
    /// a plot may hold several instances of one action.
    /// </summary>
    internal PublicationTable<WorldPlotActionInstance> PlotActionInstances { get; init; } =
        PublicationTable<WorldPlotActionInstance>.Empty;

    /// <summary>
    /// The game's action queues, as occupancy. A plan may be shaped by what the last reading said
    /// fits; whether one more action actually fits is the action boundary's answer, not this table's.
    /// </summary>
    internal PublicationTable<WorldActionQueue> ActionQueues { get; init; } =
        PublicationTable<WorldActionQueue>.Empty;

    /// <summary>
    /// What occupies each slot of the plot-action queue, sorted by queue and then position.
    /// </summary>
    internal PublicationTable<WorldActionQueueSlot> ActionQueueSlots { get; init; } =
        PublicationTable<WorldActionQueueSlot>.Empty;

    /// <summary>
    /// The player's equipped spell loadout, sorted by position. Reached by
    /// <see cref="WorldSpellSlotLookup"/>, because a slot is a position rather than an entity.
    /// </summary>
    internal PublicationTable<WorldSpellSlot> SpellSlots { get; init; } =
        PublicationTable<WorldSpellSlot>.Empty;

    /// <summary>
    /// What each equipped spell costs to cast and to sustain, sorted by slot, then kind, then
    /// resource. Reached by <see cref="WorldSpellCostLookup"/>.
    /// </summary>
    internal PublicationTable<WorldSpellCost> SpellCosts { get; init; } =
        PublicationTable<WorldSpellCost>.Empty;

    /// <summary>
    /// Exact native mastery-XP inputs retained for this lifecycle. Consumers use the monotonic
    /// sequence to process each observation once even when several world generations repeat it.
    /// </summary>
    internal PublicationTable<WorldMasteryExperience> MasteryExperience { get; init; } =
        PublicationTable<WorldMasteryExperience>.Empty;

    /// <summary>The recipes in the native ConceptRecipes registry and their compatible slot types.</summary>
    internal PublicationTable<WorldConceptRecipe> ConceptRecipes { get; init; } =
        PublicationTable<WorldConceptRecipe>.Empty;

    /// <summary>The currently active Concept assignments, keyed by their recipe identities.</summary>
    internal PublicationTable<WorldAlchemyInstance> AlchemyInstances { get; init; } =
        PublicationTable<WorldAlchemyInstance>.Empty;

    /// <summary>Raw/derived scalar basis for arbitrary Concept prospective drain quantities.</summary>
    internal PublicationTable<WorldConceptDrainBasis> ConceptDrainBasis { get; init; } =
        PublicationTable<WorldConceptDrainBasis>.Empty;

    /// <summary>
    /// Authored and current Concept drains, keyed by recipe and resource. Prospective quantities are
    /// deliberately absent as rows: consumers derive arbitrary targets from the published basis.
    /// </summary>
    internal PublicationTable<WorldAlchemyCost> AlchemyCosts { get; init; } =
        PublicationTable<WorldAlchemyCost>.Empty;

    /// <summary>The ordinary alchemy list and next native click decision, keyed by recipe.</summary>
    internal PublicationTable<WorldAlchemyLoadoutDecision> AlchemyLoadout { get; init; } =
        PublicationTable<WorldAlchemyLoadoutDecision>.Empty;

    /// <summary>Per-active-stack resource use for each ordinary alchemy recipe.</summary>
    internal PublicationTable<WorldAlchemyUsageCost> AlchemyUsageCosts { get; init; } =
        PublicationTable<WorldAlchemyUsageCost>.Empty;

    /// <summary>
    /// What each plot's author decided about it. Keyed by plot rather than keyed <em>as</em> a plot,
    /// so the plot's identity stays claimed exactly once.
    /// </summary>
    internal PublicationTable<WorldPlotAuthoring> PlotAuthoring { get; init; } =
        PublicationTable<WorldPlotAuthoring>.Empty;

    /// <summary>Every phase each plot authors, in the order the plot lists them.</summary>
    internal PublicationTable<WorldPlotPhaseDescriptor> PlotPhaseDescriptors { get; init; } =
        PublicationTable<WorldPlotPhaseDescriptor>.Empty;

    /// <summary>
    /// Every authored effect block, described by its shape. What a consumer concludes from the shape
    /// is that consumer's policy.
    /// </summary>
    internal PublicationTable<WorldEffectBlock> EffectBlocks { get; init; } =
        PublicationTable<WorldEffectBlock>.Empty;

    /// <summary>
    /// Every authored condition and explicit group on an entity's next level, keyed by the entity or
    /// prerequisite-link tier it gates and read through <see cref="WorldEntityRequirementLookup"/>.
    /// </summary>
    /// <remarks>
    /// The game's own answer takes a level argument — <c>prerequisitesPerLevel.Check(level)</c> — so
    /// unlike the whole-entity gate it cannot be published as a boolean. The conditions themselves can
    /// be, and everything they compare against is already a row in this same snapshot, which is what
    /// lets a worker reach the verdict without asking the game. An entity with no row here authored no
    /// per-level condition, which is the game's own unconditional pass rather than a gap in the read.
    /// Link tiers additionally carry a container-root row so an authored empty tier cannot be confused
    /// with a selected tier that does not exist.
    /// </remarks>
    internal PublicationTable<WorldEntityRequirement> EntityRequirements { get; init; } =
        PublicationTable<WorldEntityRequirement>.Empty;

    /// <summary>
    /// The ordered membership of every list variable a requirement compares against, keyed by the
    /// list and read through <see cref="WorldRequirementListLookup"/>.
    /// </summary>
    /// <remarks>
    /// A list comparison folds over the whole list rather than following one edge, so the membership
    /// is what a worker needs; each member's own visibility is a per-pass row of its own category.
    /// Only lists the game marks static are here — a list the run plays into cannot be published by
    /// an epoch-scoped reader without going stale, and the collection report names it when one is.
    /// </remarks>
    internal PublicationTable<WorldRequirementListMember> RequirementListMembers { get; init; } =
        PublicationTable<WorldRequirementListMember>.Empty;

    /// <summary>
    /// The live active-link and passive-cache gates for every authored prerequisite-link tier.
    /// </summary>
    internal PublicationTable<WorldPrerequisiteLinkTier> PrerequisiteLinkTiers { get; init; } =
        PublicationTable<WorldPrerequisiteLinkTier>.Empty;

    internal PublicationTable<WorldTreasurePool> TreasurePools { get; init; } =
        PublicationTable<WorldTreasurePool>.Empty;

    /// <summary>
    /// Unity's fixed timestep, in seconds, as of the collection that produced this snapshot.
    /// </summary>
    /// <remarks>
    /// A property of the tick rather than of any entity, so it sits here rather than being copied
    /// into eighty resource readings. It is on the snapshot at all because the ported rate chain
    /// needs it and <c>Time.fixedDeltaTime</c> is a Unity static that may only be touched on the main
    /// thread — reading it during capture is the only way a worker can have it.
    /// <para>
    /// The other three per-tick globals the rate chain reads are <c>DoubleVariable</c>s and are
    /// already in <see cref="DoubleVariables"/>; only this one has no registry to come from.
    /// </para>
    /// </remarks>
    internal double FixedDeltaTime { get; init; }

    /// <summary>The Unity frame at which this immutable world reading began.</summary>
    internal long CollectedAtFrame { get; init; }

    /// <summary>
    /// Which run of the game this snapshot describes, as opposed to how new it is.
    /// </summary>
    /// <remarks>
    /// A save load or a reset can leave an entity's identity intact while replacing the object behind
    /// it, which no generation comparison can detect — a generation says the world was re-read, not
    /// that it is the same world. This is the native lifecycle counter the host replaces its runners
    /// on, read during capture and carried through derivation unchanged, so a consumer can compare it
    /// against the lifecycle its own cycle was pinned to. It plays no part in the world-freshness
    /// gate: an epoch is not a frame and never participates in that comparison.
    /// </remarks>
    internal long CollectedAtEpoch { get; init; }

    /// <summary>
    /// UTC ticks captured beside <see cref="CollectedAt"/> on the Unity thread. This is diagnostic
    /// publication data, not a scheduler clock; it gives external readers a wall-clock collection
    /// time without asking them to interpret this process's monotonic origin.
    /// </summary>
    internal long CollectedAtUtcTicks { get; init; }

    /// <summary>
    /// Monotonic time at which the collection that produced this snapshot began.
    /// </summary>
    /// <remarks>
    /// Unlike the world generation, which names the Unity frame, this gives diagnostics a duration
    /// even when frame cadence varies. It is inert published data and does not participate in
    /// freshness or scheduling.
    /// </remarks>
    internal MonotonicTimestamp CollectedAt { get; init; }

    // Lookups live on WorldLookup rather than here. A member per category would be a forwarding
    // one-liner repeated once per table, and the record is meant to grow to roughly thirty of them.
}

/// <summary>
/// The empty world state, held outside <see cref="GameWorldState"/> because published shapes may
/// not own non-constant static storage — the structural validator rejects it, since a static field on
/// a published type is exactly the shared mutable surface publication is meant to eliminate.
/// </summary>
internal static class GameWorldStateDefaults
{
    /// <summary>
    /// The snapshot every consumer sees before the first collection completes. Empty rather than
    /// absent, so a service that starts early reads "nothing known yet" from the normal lookup path
    /// instead of needing a null check the type system cannot enforce.
    /// </summary>
    internal static readonly GameWorldState Empty = new();
}
