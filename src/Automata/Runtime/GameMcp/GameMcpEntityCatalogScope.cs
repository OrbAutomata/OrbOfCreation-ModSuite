#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Which native types <c>entity_catalog</c> lists, and which it leaves to the machinery they are.
/// </summary>
/// <remarks>
/// <para>
/// The verb's job is to answer "what is this thing, and is it in this build at all" for an asset a
/// reader could act on knowing. Two thirds of the registry cannot answer that for anybody: the
/// string table, the scaling curves, the animation and colour assets, the one-slot holders behind a
/// UI cursor, the named list variables whose contents are already a published category, and the
/// prerequisite-link nodes whose every tier <c>world_get</c> already expands. Listing them made the
/// verb's own page the reason the verb looked like an index of nothing.
/// </para>
/// <para>
/// The verdict is on the TYPE, never on an asset: a named type is machinery whatever the game calls
/// its instances, and an asset never earns or loses its place by its name. A type nobody has ruled
/// on is listed — a build that loads something new says so on the page rather than dropping it in
/// silence — so this array is the whole of what the verb withholds.
/// </para>
/// <para>
/// <b>Nothing here leaves the identity catalog.</b> The snapshot still holds every loaded id, so an
/// id handle still resolves against the whole set, a row that references one of these ids still
/// prints the name the snapshot holds, a keyword still resolves to its word through the same rows,
/// and <c>world_get</c> still answers for one by name. Only this one page is shorter.
/// </para>
/// </remarks>
internal static class GameMcpEntityCatalogScope
{
    /// <summary>
    /// Whether the catalog's page carries rows of this native type.
    /// </summary>
    internal static bool Lists(string nativeType) =>
        !InternalOnly.Contains(nativeType ?? string.Empty);

    /// <summary>
    /// The types the page withholds, for the reconciliation that proves this list is about this
    /// build: every name is a type the build loads, and none of them is a type the world publishes.
    /// </summary>
    internal static IReadOnlyList<string> InternalOnlyTypes => Names;

    private static readonly string[] Names =
    {
        // Machinery: the string table, the authored curves, the presentation assets, and the
        // one-slot holders a screen keeps its cursor in. None of it is a thing, and eleven of the
        // 242 rows carry a word at all — every one of them an animation or a music title.
        "LocalizedStringSO",
        "ScalingWeightSO",
        "ModifierListVariable",
        "AnimationEffectSO",
        "AnimationSO",
        "InstanceScalingSO",
        "DisplayEffectSO",
        "StringVariable",
        "GuidVariable",
        "FilterVariable",
        "RitualVariable",
        "AlchemyRecipeVariable",
        "RandomVariable",
        "ResearchVariable",
        "SpellRecipeVariable",
        "SpellVariable",
        "UpgradeableObjectVariable",

        // Named lists. Where the membership is a real game fact the contents are already a
        // published category — ConceptRecipes is the concept-recipes page, ActiveActionables is the
        // action queue, ActiveSpells is spell-slots — so the container's own id names a table the
        // caller can read a better version of. Where it is not, it is a Unity field.
        // AlchemySnapshotListVariable and EquipmentSnapshotListVariable are absent on purpose: the
        // world publishes both under snapshot-loadouts, so the page carries them like any other
        // projected type.
        "AchievementListVariable",
        "ActionableListVariable",
        "AdvancementListVariable",
        "AlchemyInstanceListVariable",
        "AlchemyRecipeListVariable",
        "AlchemyTypeListVariable",
        "AttributeGroupListVariable",
        "AttributeListVariable",
        "AutoPurchaseListVariable",
        "ChallengeListVariable",
        "CharacterListVariable",
        "CombatEffectInstanceListVariable",
        "ConsumableRefListVariable",
        "CraftingInstanceListVariable",
        "CraftingRecipeListVariable",
        "CraftingStructureListVariable",
        "CraftingStructureRefListVariable",
        "DiscoveryTreeListVariable",
        "EngagedEffectListVariable",
        "EquipmentListVariable",
        "EquipmentTypeListVariable",
        "EventLogListVariable",
        "GlyphListVariable",
        "HarvestActionInstanceListVariable",
        "HarvestActionListVariable",
        "HarvestElementListVariable",
        "HarvestTypeListVariable",
        "KeyBindingListVariable",
        "LocalizedStringListVariable",
        "MusicTrackListVariable",
        "NumberListVariable",
        "PassiveAbilityListVariable",
        "PassiveAbilityRefListVariable",
        "PinnedObjectListVariable",
        "PlayerLoadoutListVariable",
        "PlotNodeActionInstanceListVariable",
        "PlotNodeActionListVariable",
        "PlotNodeListVariable",
        "PlotNodeTypeListVariable",
        "RasteredThoughtListVariable",
        "RecipeBookListVariable",
        "ResearchListVariable",
        "ResearchTypeListVariable",
        "ResourceListVariable",
        "ResourceTypeListVariable",
        "RitualListVariable",
        "RuneStoneListVariable",
        "SpellListVariable",
        "SpellRecipeListVariable",
        "SpellTypeListVariable",
        "StatusEffectListVariable",
        "StructureListVariable",
        "StructureTypeListVariable",
        "ThoughtStreamListVariable",
        "TimeRuneListVariable",
        "UpgradeListVariable",
        "ViewListVariable",

        // The wiring the requirement graph hangs off. Every tier, its verdict and its own recursive
        // requirement tree already ride inside world_get on each entity the link gates, and all 41
        // rows are nameless, so the page's whole contribution was an internal asset identifier.
        "PrerequisiteLinkSO",

        // Words the player really does read, that no decision can turn on: the key they rebind, the
        // track that is playing, and two combat concepts the game authors nameless, so the page
        // could not say their word even where it wanted to.
        "KeyBindingVariable",
        "MusicTrackSO",
        "ChallengeTypeSO",
        "CombatTargetSO",
    };

    private static readonly HashSet<string> InternalOnly =
        new(Names, StringComparer.Ordinal);
}
#endif
