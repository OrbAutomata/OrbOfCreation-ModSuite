using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class SpellWorkbenchNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-workbench.manager-type-action",
        "loadout.spell-recipe.type-action",
        "loadout.identity.type-action",
        "generic-level.glyph.type-action",
        "generic-level.cost.type-action",
        "generic-level.tuple.type-action",
        "generic-level.resource.type-action",
        "loadout.spell.type-action",
        "spell-workbench.guid-container-type-action",
        "spell-workbench.view-type-action",
        "spell-manager.instance",
        "spell-workbench.glyph-list-type-action",
        "spell-workbench.spell-list-type-action",
        "spell-workbench.manager-selected-core-action",
        "spell-workbench.manager-selected-augments-action",
        "spell-manager.active-spells",
        "spell-workbench.manager-get-usage-cost-action",
        "spell-workbench.manager-create",
        "spell-workbench.recipe-all-action",
        "id-scriptable-object.get-guid-action",
        "spell-recipe.is-discovered",
        "spell-workbench.recipe-get-glyphs-action",
        "spell-workbench.recipe-create-empty-action",
        "spell-workbench.recipe-selected-level-action",
        "spell-workbench.recipe-usage-requirements-action",
        "resource-cost-list.has-enough",
        "resource-cost-list.get-entries",
        "resource-tuple.get-value",
        "resource-tuple.resource-concept",
        "spell-workbench.resource-has-amount-action",
        "spell-workbench.resource-get-missing-action",
        "spell-workbench.glyph-is-available",
        "spell-workbench.glyph-is-augment",
        "spell-composition.glyph-get-maximum-usages-action",
        "spell-composition.glyph-meets-non-level-requirements-action",
        "spell-workbench.list-value-action",
        "spell-workbench.list-set-stack-action",
        "spell-workbench.list-stacked-record-action",
        "spell-workbench.loadout-has-empty-action",
        "spell-workbench.spell-reference-action",
        "spell-workbench.spell-guid-container-action",
        "spell-workbench.spell-set-level-action",
        "spell-workbench.spell-is-unique-action",
        "spell-composition.spell-set-augments-action",
        "spell-composition.spell-get-augment-glyphs-action",
        "spell-composition.stacked-record-type-action",
        "spell-composition.stacked-record-construct-action",
        "spell-composition.stacked-record-set-action",
        "spell-composition.stacked-record-quantity-action",
        "spell-composition.stacked-record-items-action",
        "spell-composition.spell-augment-stack-action",
        "spell-workbench.list-max-action",
        "discovery-tree-offer.guid-container-value",
        "spell-workbench.owning-view-availability-action",
    };

    private SpellWorkbenchNativeBindings(Type recipeType, Type glyphType, Type spellType,
        Func<object?> manager, Func<IList> recipes,
        Func<object, object> core, Func<object, object> augments, Func<object, object> active,
        Func<object, IList> glyphValues, Func<object, IList> activeValues, Func<object, Guid> identity,
        Func<object, IList> recipeGlyphs, Func<object, bool> discovered,
        Func<object, bool> hasEnough,
        Func<object, IList> costEntries, Func<object, BigDouble> costValue,
        Func<object, object?> costResource, Func<object, BigDouble, bool> resourceHasAmount,
        Func<object, BigDouble> resourceMissing,
        Func<IList> createGlyphList, Func<object, bool> glyphAvailable,
        Func<object, bool> glyphAugment, Func<object, int> glyphMaximumUsages,
        Action<object, object> setListStack, Func<object, object?> readListStack,
        Func<object, int> listMax, Func<object, bool> hasEmpty,
        Func<object, object> usageCost,
        Action<object, object> create, Func<object, int, object> createEmpty,
        Func<object, int> selectedLevel, Func<object, bool> usageRequirements,
        Action<object, int> setLevel, Action<object, object> setAugments,
        Func<object, bool> unique, Func<object, IList> spellAugments,
        Func<object, object?> spellAugmentStack,
        Func<IList, object, bool> meetsNonLevelRequirements,
        Func<object> createStackedRecord, Action<object, object, int> setStackedRecord,
        Func<object, object, int> readStackedQuantity, Func<object, IList> readStackedItems,
        Func<object, object?> spellReference,
        Func<object, object?> spellGuid, Func<object, Guid> guidValue,
        Type viewType, Func<object, bool> viewAvailable)
    {
        RecipeType = recipeType;
        GlyphType = glyphType;
        SpellType = spellType;
        ReadManager = manager;
        ReadRecipes = recipes;
        ReadCore = core;
        ReadAugments = augments;
        ReadActive = active;
        ReadGlyphValues = glyphValues;
        ReadActiveValues = activeValues;
        ReadIdentity = identity;
        ReadRecipeGlyphs = recipeGlyphs;
        IsDiscovered = discovered;
        HasEnough = hasEnough;
        ReadCostEntries = costEntries;
        ReadCostValue = costValue;
        ReadCostResource = costResource;
        HasResourceAmount = resourceHasAmount;
        ReadResourceMissing = resourceMissing;
        CreateGlyphList = createGlyphList;
        IsGlyphAvailable = glyphAvailable;
        IsGlyphAugment = glyphAugment;
        GetGlyphMaximumUsages = glyphMaximumUsages;
        SetListStack = setListStack;
        ReadListStack = readListStack;
        GetListMax = listMax;
        HasEmpty = hasEmpty;
        GetUsageCost = usageCost;
        Create = create;
        CreateEmptySpell = createEmpty;
        GetSelectedSpellLevel = selectedLevel;
        HasMetUsageRequirements = usageRequirements;
        SetSpellLevel = setLevel;
        SetSpellAugments = setAugments;
        IsUniqueSpell = unique;
        ReadSpellAugments = spellAugments;
        ReadSpellAugmentStack = spellAugmentStack;
        MeetsNonLevelRequirements = meetsNonLevelRequirements;
        CreateStackedRecord = createStackedRecord;
        SetStackedRecord = setStackedRecord;
        ReadStackedQuantity = readStackedQuantity;
        ReadStackedItems = readStackedItems;
        ReadSpellReference = spellReference;
        ReadSpellGuid = spellGuid;
        ReadGuidValue = guidValue;
        ViewType = viewType;
        IsViewAvailable = viewAvailable;
    }

    internal Type RecipeType { get; }

    /// <summary>
    /// The screen the load button lives on. <c>ViewSO.IsAvailable()</c> is
    /// <c>prerequisites.Container.Check()</c> — the same question the game asks before it draws the
    /// tab — so a locked screen is a fact the boundary reads rather than a state it infers.
    /// </summary>
    internal Type ViewType { get; }
    internal Func<object, bool> IsViewAvailable { get; }
    internal Type GlyphType { get; }
    internal Type SpellType { get; }
    internal Func<object?> ReadManager { get; }
    internal Func<IList> ReadRecipes { get; }
    internal Func<object, object> ReadCore { get; }
    internal Func<object, object> ReadAugments { get; }
    internal Func<object, object> ReadActive { get; }
    internal Func<object, IList> ReadGlyphValues { get; }
    internal Func<object, IList> ReadActiveValues { get; }
    internal Func<object, Guid> ReadIdentity { get; }
    internal Func<object, IList> ReadRecipeGlyphs { get; }
    internal Func<object, bool> IsDiscovered { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Func<object, IList> ReadCostEntries { get; }
    internal Func<object, BigDouble> ReadCostValue { get; }
    internal Func<object, object?> ReadCostResource { get; }
    internal Func<object, BigDouble, bool> HasResourceAmount { get; }

    /// <summary><c>ResourceSO.GetMissing()</c>: the room a bandwidth allocation has left.</summary>
    internal Func<object, BigDouble> ReadResourceMissing { get; }
    internal Func<IList> CreateGlyphList { get; }
    internal Func<object, bool> IsGlyphAvailable { get; }
    internal Func<object, bool> IsGlyphAugment { get; }
    internal Func<object, int> GetGlyphMaximumUsages { get; }
    /// <summary>
    /// The stack write the game's own augment UI performs, which sets the multiplicity record
    /// <c>SpellManager.CreateRecipe</c> bakes from and the value list beside it in one call.
    /// </summary>
    internal Action<object, object> SetListStack { get; }

    /// <summary>
    /// The staged multiplicity record, or nothing on a list the author did not mark stackable —
    /// in which case the game bakes no augments at all from it.
    /// </summary>
    internal Func<object, object?> ReadListStack { get; }

    /// <summary>
    /// How many different items the list holds at once — the augment selection's own ceiling,
    /// <c>Max Spell Augment Slots</c>, or <c>int.MaxValue</c> on a list the author left unbounded.
    /// </summary>
    internal Func<object, int> GetListMax { get; }
    internal Func<object, bool> HasEmpty { get; }
    internal Func<object, object> GetUsageCost { get; }
    internal Action<object, object> Create { get; }
    internal Func<object, int, object> CreateEmptySpell { get; }
    internal Func<object, int> GetSelectedSpellLevel { get; }
    internal Func<object, bool> HasMetUsageRequirements { get; }
    internal Action<object, int> SetSpellLevel { get; }
    internal Action<object, object> SetSpellAugments { get; }
    internal Func<object, bool> IsUniqueSpell { get; }
    internal Func<object, IList> ReadSpellAugments { get; }

    /// <summary>
    /// The multiplicity record a loaded spell was baked from. <see cref="ReadSpellAugments"/>
    /// projects it to distinct items and loses the counts, so only this can answer whether a spell
    /// carries two of a glyph or one.
    /// </summary>
    internal Func<object, object?> ReadSpellAugmentStack { get; }
    internal Func<IList, object, bool> MeetsNonLevelRequirements { get; }
    internal Func<object> CreateStackedRecord { get; }
    internal Action<object, object, int> SetStackedRecord { get; }
    internal Func<object, object, int> ReadStackedQuantity { get; }
    internal Func<object, IList> ReadStackedItems { get; }
    internal Func<object, object?> ReadSpellReference { get; }
    internal Func<object, object?> ReadSpellGuid { get; }
    internal Func<object, Guid> ReadGuidValue { get; }

    internal static bool TryCreate(Func<string, Type?> resolveType,
        Func<string, bool> includeContract, out SpellWorkbenchNativeBindings? bindings,
        out string reason) =>
        NativeBindingSet.TryBind("spell workbench", ContractIds, resolveType, includeContract, set =>
        {
            var managerType = set.Type("spell-workbench.manager-type-action", "SpellManager");
            var glyphListType = set.Type("spell-workbench.glyph-list-type-action", "GlyphListVariable");
            var spellListType = set.Type("spell-workbench.spell-list-type-action", "SpellListVariable");
            var recipeType = set.Type("loadout.spell-recipe.type-action", "SpellRecipeSO");
            var identityType = set.Type("loadout.identity.type-action", "IdScriptableObject");
            var glyphType = set.Type("generic-level.glyph.type-action", "GlyphSO");
            var costType = set.Type("generic-level.cost.type-action", "ResourceCostList");
            var costTupleType = set.Type("generic-level.tuple.type-action", "ResourceTuple");
            var resourceType = set.Type("generic-level.resource.type-action", "ResourceSO");
            var spellType = set.Type("loadout.spell.type-action", "Spell");
            var guidType = set.Type("spell-workbench.guid-container-type-action", "GuidContainer");
            var viewType = set.Type("spell-workbench.view-type-action", "ViewSO");
            var stackedType = set.Type("spell-composition.stacked-record-type-action", "Stacked.StackedIdRecord`1")
                .MakeGenericType(glyphType);
            var glyphList = typeof(List<>).MakeGenericType(glyphType);
            var costTupleList = typeof(List<>).MakeGenericType(costTupleType);
            var recipeList = typeof(List<>).MakeGenericType(recipeType);
            var spellList = typeof(List<>).MakeGenericType(spellType);

            return new SpellWorkbenchNativeBindings(
                recipeType, glyphType, spellType,
                set.StaticField<Func<object?>>("spell-manager.instance", managerType, "instance", managerType),
                set.StaticField<Func<IList>>("spell-workbench.recipe-all-action", recipeType, "All", recipeList),
                set.Field<Func<object, object>>(
                    "spell-workbench.manager-selected-core-action", managerType, "selectedCoreGlyphs", glyphListType),
                set.Field<Func<object, object>>(
                    "spell-workbench.manager-selected-augments-action", managerType, "selectedAugmentGlyphs",
                    glyphListType),
                set.Field<Func<object, object>>("spell-manager.active-spells", managerType, "activeSpells", spellListType),
                set.Field<Func<object, IList>>("spell-workbench.list-value-action", glyphListType, "value", glyphList),
                set.Field<Func<object, IList>>("spell-workbench.list-value-action", spellListType, "value", spellList),
                set.StableUuid("id-scriptable-object.get-guid-action", identityType),
                set.Call<Func<object, IList>>(
                    "spell-workbench.recipe-get-glyphs-action", recipeType, "GetGlyphRecipe", glyphList),
                set.Call<Func<object, bool>>("spell-recipe.is-discovered", recipeType, "IsDiscovered", typeof(bool)),
                set.Call<Func<object, bool>>("resource-cost-list.has-enough", costType, "HasEnough", typeof(bool)),
                set.Call<Func<object, IList>>("resource-cost-list.get-entries", costType, "GetEntries", costTupleList),
                set.Call<Func<object, BigDouble>>("resource-tuple.get-value", costTupleType, "GetValue", typeof(BigDouble)),
                set.Field<Func<object, object?>>("resource-tuple.resource-concept", costTupleType, "resource", resourceType),
                set.Call<Func<object, BigDouble, bool>>(
                    "spell-workbench.resource-has-amount-action", resourceType, "HasAmount", typeof(bool),
                    typeof(BigDouble)),
                set.Call<Func<object, BigDouble>>(
                    "spell-workbench.resource-get-missing-action", resourceType, "GetMissing", typeof(BigDouble)),
                Expression.Lambda<Func<IList>>(Expression.Convert(Expression.New(glyphList), typeof(IList))).Compile(),
                set.Call<Func<object, bool>>("spell-workbench.glyph-is-available", glyphType, "IsAvailable", typeof(bool)),
                set.Call<Func<object, bool>>("spell-workbench.glyph-is-augment", glyphType, "IsSpellAugment", typeof(bool)),
                set.Call<Func<object, int>>(
                    "spell-composition.glyph-get-maximum-usages-action", glyphType, "GetMaxUsages", typeof(int)),
                set.Call<Action<object, object>>(
                    "spell-workbench.list-set-stack-action", glyphListType, "SetStack", typeof(void), stackedType),
                set.Call<Func<object, object?>>(
                    "spell-workbench.list-stacked-record-action", glyphListType, "GetStackedRecord", stackedType),
                set.Call<Func<object, int>>("spell-workbench.list-max-action", glyphListType, "GetMax", typeof(int)),
                set.Call<Func<object, bool>>(
                    "spell-workbench.loadout-has-empty-action", spellListType, "HasEmptySpot", typeof(bool)),
                set.StaticCall<Func<object, object>>(
                    "spell-workbench.manager-get-usage-cost-action", managerType, "GetUsageCostOfSpell", costType,
                    spellType),
                set.Call<Action<object, object>>(
                    "spell-workbench.manager-create", managerType, "CreateRecipe", typeof(void), recipeType),
                set.Call<Func<object, int, object>>(
                    "spell-workbench.recipe-create-empty-action", recipeType, "CreateEmpty", spellType, typeof(int)),
                set.Call<Func<object, int>>(
                    "spell-workbench.recipe-selected-level-action", recipeType, "GetSelectedSpellLevel", typeof(int)),
                set.Call<Func<object, bool>>(
                    "spell-workbench.recipe-usage-requirements-action", recipeType, "HasMetUsageRequirements",
                    typeof(bool)),
                set.Call<Action<object, int>>(
                    "spell-workbench.spell-set-level-action", spellType, "SetLevel", typeof(void), typeof(int)),
                set.Call<Action<object, object>>(
                    "spell-composition.spell-set-augments-action", spellType, "SetAugmentGlyphs", typeof(void),
                    stackedType),
                set.Call<Func<object, bool>>(
                    "spell-workbench.spell-is-unique-action", spellType, "IsUniqueSpell", typeof(bool)),
                set.Call<Func<object, IList>>(
                    "spell-composition.spell-get-augment-glyphs-action", spellType, "GetAugmentGlyphs", glyphList),
                set.Field<Func<object, object?>>(
                    "spell-composition.spell-augment-stack-action", spellType, "augmentGlyphRefs", stackedType),
                set.StaticCall<Func<IList, object, bool>>(
                    "spell-composition.glyph-meets-non-level-requirements-action", glyphType, "MeetsNonLvRequirements",
                    typeof(bool), glyphList, spellType),
                set.New<Func<object>>("spell-composition.stacked-record-construct-action", stackedType),
                set.Call<Action<object, object, int>>(
                    "spell-composition.stacked-record-set-action", stackedType, "Set", typeof(void), glyphType,
                    typeof(int)),
                set.Call<Func<object, object, int>>(
                    "spell-composition.stacked-record-quantity-action", stackedType, "GetQuantity", typeof(int),
                    glyphType),
                set.Call<Func<object, IList>>(
                    "spell-composition.stacked-record-items-action", stackedType, "GetItems", glyphList),
                set.Call<Func<object, object?>>(
                    "spell-workbench.spell-reference-action", spellType, "get_reference", recipeType),
                set.Field<Func<object, object?>>(
                    "spell-workbench.spell-guid-container-action", spellType, "guidContainer", guidType),
                set.Call<Func<object, Guid>>("discovery-tree-offer.guid-container-value", guidType, "get_guid", typeof(Guid)),
                viewType,
                set.Call<Func<object, bool>>(
                    "spell-workbench.owning-view-availability-action", viewType, "IsAvailable", typeof(bool)));
        }, out bindings, out reason);
}
