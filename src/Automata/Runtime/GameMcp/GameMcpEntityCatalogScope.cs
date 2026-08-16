#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Which native types <c>entity_catalog</c> lists: the remainder of this build the published world
/// has no row for, minus the machinery that remainder would otherwise be buried under.
/// </summary>
/// <remarks>
/// <para>
/// The page answers one question — what did this build load that no published row covers. A type
/// the world publishes is therefore not on it: <c>world_search</c>, <c>world_list</c> and
/// <c>world_get</c> own those rows, they carry the facts a reader wants about them, and a second
/// page listing the same ids by identity alone only made two surfaces with different scopes look
/// interchangeable.
/// </para>
/// <para>
/// What is left over is still two things, and one of them is worth no row. The string table, the
/// scaling curves, the animation and colour assets, the one-slot holders behind a UI cursor, the
/// named list variables whose contents are already a published category, the prerequisite-link
/// nodes whose every tier <c>world_get</c> already expands, the conditional hint table, the
/// player's own nameless combat actor, and the legacy station the game builds no instance of cannot
/// answer "what is this thing" for anybody, and the array below is the whole of what the page
/// withholds for that reason.
/// </para>
/// <para>
/// The verdict is on the TYPE, never on an asset: a named type is machinery whatever the game calls
/// its instances, and an asset never earns or loses its place by its name. A type nobody has ruled
/// on is listed — a build that loads something new says so on the page rather than dropping it in
/// silence.
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
    /// Whether the catalog's page carries rows of this native type: what the published world holds
    /// no category for, and this build's own machinery is not.
    /// </summary>
    internal static bool Lists(string nativeType) =>
        !IsMachinery(nativeType) &&
        !GameMcpEntityCapabilityMap.TryCategoryForNativeType(nativeType, out _);

    /// <summary>
    /// Whether the page withholds rows of this native type as the build's internal machinery — a
    /// different answer from "the published world already carries a row for it", and the one the
    /// surfaces that signpost this page have to tell apart.
    /// </summary>
    internal static bool IsMachinery(string nativeType) =>
        InternalOnly.Contains(nativeType ?? string.Empty);

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

        // The conditional hint panel's own table. Its one asset derives from IdScriptableObject
        // rather than TooltipableObject, so it carries neither a name nor a sentence; what it holds
        // is a list of (Prerequisites.Container, string) pairs that UIConditionalTextList.PostSetup
        // picks one of. The words are real and the holder is not a thing — the same verdict
        // LocalizedStringSO already has, one register over.
        "ConditionalTextList",

        // The player's combat actor. Also an IdScriptableObject with no name and no sentence: it is
        // the runtime IBattleActor the ritual layer drives, holding a stat block, a death flag and
        // the auto-attack clock. There is one, it is nameless, and the page's whole contribution
        // for it was an internal asset identifier.
        "PlayerCharacter",

        // The legacy Brewing Station. It is a TooltipableObject, and both its displayName and its
        // description are authored empty; its `instances` field names the BrewingStations list
        // variable, whose initialValue and value are both `[]` with isStatic false, so no runtime
        // station can exist to answer for. Its authored recipes are reachable through the
        // consumables they produce. A row here would be a nameless id for a station the game never
        // builds.
        "CraftingStructureSO",
    };

    private static readonly HashSet<string> InternalOnly =
        new(Names, StringComparer.Ordinal);
}
#endif
