using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete lifecycle binding set for equipped-spell removal and reordering. Reflection is confined
/// to construction; execution uses exact compiled delegates only.
/// </summary>
internal sealed class SpellLoadoutNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-workbench.manager-type-action",
        "loadout.spell.type-action",
        "spell-workbench.guid-container-type-action",
        "spell-loadout.big-double.type-action",
        "loadout.spell-recipe.type-action",
        "loadout.identity.type-action",
        "spell-workbench.view-type-action",
        "spell-manager.instance",
        "spell-manager.active-spells",
        "spell-workbench.spell-list-type-action",
        "spell-workbench.list-value-action",
        "spell-workbench.spell-guid-container-action",
        "discovery-tree-offer.guid-container-value",
        "spell-loadout.spell-is-empty-action",
        "spell-loadout.spell-at-max-charges-action",
        "spell-loadout.spell-is-casting-action",
        "spell-loadout.spell-readying-cast-action",
        "spell-loadout.spell-current-charges-action",
        "spell-loadout.spell-max-charges-action",
        "spell-loadout.spell-cooldown-remaining-action",
        "spell-loadout.spell-reference-action",
        "spell-loadout.recipe-identity-action",
        "spell-loadout.manager-remove-spell-action",
        "spell-loadout.list-swap-positions-action",
        "spell-loadout.list-update-observable-action",
        "spell-loadout.owning-view-availability-action",
    };

    private SpellLoadoutNativeBindings(
        Type spellType,
        Func<object?> manager,
        Func<object, object> active,
        Func<object, IList> activeValues,
        Func<object, object?> spellGuid,
        Func<object, Guid> guidValue,
        Func<object, bool> isEmpty,
        Func<object, bool> atMaxCharges,
        Func<object, bool> isCasting,
        Func<object, bool> readyingCast,
        Func<object, int> currentCharges,
        Func<object, int> maximumCharges,
        Func<object, BigDouble> cooldownRemaining,
        Func<object, object?> spellRecipe,
        Func<object, Guid> recipeIdentity,
        Action<object, object> remove,
        Action<object, int, int> swap,
        Action<object> updateObservable,
        Type viewType,
        Func<object, bool> viewAvailable)
    {
        SpellType = spellType;
        ReadManager = manager;
        ReadActive = active;
        ReadActiveValues = activeValues;
        ReadSpellGuid = spellGuid;
        ReadGuidValue = guidValue;
        IsEmpty = isEmpty;
        IsAtMaxCharges = atMaxCharges;
        IsCasting = isCasting;
        IsReadyingCast = readyingCast;
        ReadCurrentCharges = currentCharges;
        ReadMaximumCharges = maximumCharges;
        ReadCooldownRemaining = cooldownRemaining;
        ReadSpellRecipe = spellRecipe;
        ReadRecipeIdentity = recipeIdentity;
        Remove = remove;
        Swap = swap;
        UpdateObservable = updateObservable;
        ViewType = viewType;
        IsViewAvailable = viewAvailable;
    }

    internal Type SpellType { get; }

    /// <summary>
    /// The screen the loadout bar lives on. <c>ViewSO.IsAvailable()</c> is
    /// <c>prerequisites.Container.Check()</c> — the same question the game asks before it draws the
    /// tab.
    /// </summary>
    internal Type ViewType { get; }
    internal Func<object, bool> IsViewAvailable { get; }
    internal Func<object?> ReadManager { get; }
    internal Func<object, object> ReadActive { get; }
    internal Func<object, IList> ReadActiveValues { get; }
    internal Func<object, object?> ReadSpellGuid { get; }
    internal Func<object, Guid> ReadGuidValue { get; }
    internal Func<object, bool> IsEmpty { get; }

    /// <summary>
    /// The three reads <c>SpellManager.RemoveSpell</c> itself gates on: it returns without
    /// removing anything unless the spell is at full charges and neither casting nor readying a
    /// cast. <c>Spell.CanRemove()</c> is a different predicate that the game never calls.
    /// </summary>
    internal Func<object, bool> IsAtMaxCharges { get; }
    internal Func<object, bool> IsCasting { get; }
    internal Func<object, bool> IsReadyingCast { get; }

    /// <summary>The numbers the refusal quotes, read live beside the gate that refused.</summary>
    internal Func<object, int> ReadCurrentCharges { get; }
    internal Func<object, int> ReadMaximumCharges { get; }
    internal Func<object, BigDouble> ReadCooldownRemaining { get; }

    /// <summary>The recipe behind an equipped instance, which is the identity a caller can look up.</summary>
    internal Func<object, object?> ReadSpellRecipe { get; }
    internal Func<object, Guid> ReadRecipeIdentity { get; }
    internal Action<object, object> Remove { get; }
    internal Action<object, int, int> Swap { get; }
    internal Action<object> UpdateObservable { get; }

    internal static bool TryCreate(
        Func<string, Type?> resolveType,
        Func<string, bool> includeContract,
        out SpellLoadoutNativeBindings? bindings,
        out string reason) =>
        NativeBindingSet.TryBind("spell loadout", ContractIds, resolveType, includeContract, set =>
        {
            var manager = set.Type("spell-workbench.manager-type-action", "SpellManager");
            var spellList = set.Type("spell-workbench.spell-list-type-action", "SpellListVariable");
            var spell = set.Type("loadout.spell.type-action", "Spell");
            var guid = set.Type("spell-workbench.guid-container-type-action", "GuidContainer");
            var bigDouble = set.Type("spell-loadout.big-double.type-action", "BigDouble");
            var recipe = set.Type("loadout.spell-recipe.type-action", "SpellRecipeSO");
            var identity = set.Type("loadout.identity.type-action", "IdScriptableObject");
            var view = set.Type("spell-workbench.view-type-action", "ViewSO");
            return new SpellLoadoutNativeBindings(
                spell,
                set.StaticField<Func<object?>>("spell-manager.instance", manager, "instance", manager),
                set.Field<Func<object, object>>(
                    "spell-manager.active-spells", manager, "activeSpells", spellList),
                set.Field<Func<object, IList>>(
                    "spell-workbench.list-value-action",
                    spellList, "value", typeof(List<>).MakeGenericType(spell)),
                set.Field<Func<object, object?>>(
                    "spell-workbench.spell-guid-container-action", spell, "guidContainer", guid),
                set.Call<Func<object, Guid>>(
                    "discovery-tree-offer.guid-container-value", guid, "get_guid", typeof(Guid)),
                set.Call<Func<object, bool>>(
                    "spell-loadout.spell-is-empty-action", spell, "IsEmpty", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "spell-loadout.spell-at-max-charges-action", spell, "IsAtMaxCharges", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "spell-loadout.spell-is-casting-action", spell, "IsCasting", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "spell-loadout.spell-readying-cast-action", spell, "IsReadyingCast", typeof(bool)),
                set.Call<Func<object, int>>(
                    "spell-loadout.spell-current-charges-action", spell, "GetCurrSpellCharges", typeof(int)),
                set.Call<Func<object, int>>(
                    "spell-loadout.spell-max-charges-action", spell, "GetMaxSpellCharges", typeof(int)),
                set.Call<Func<object, BigDouble>>(
                    "spell-loadout.spell-cooldown-remaining-action",
                    spell, "GetCooldownTimeRemaining", bigDouble),
                set.Call<Func<object, object?>>(
                    "spell-loadout.spell-reference-action", spell, "get_reference", recipe),
                set.StableUuid("spell-loadout.recipe-identity-action", identity),
                set.Call<Action<object, object>>(
                    "spell-loadout.manager-remove-spell-action", manager, "RemoveSpell", typeof(void), spell),
                set.Call<Action<object, int, int>>(
                    "spell-loadout.list-swap-positions-action",
                    spellList, "SwapPositions", typeof(void), typeof(int), typeof(int)),
                set.Call<Action<object>>(
                    "spell-loadout.list-update-observable-action", spellList, "UpdateObservable", typeof(void)),
                view,
                set.Call<Func<object, bool>>(
                    "spell-loadout.owning-view-availability-action", view, "IsAvailable", typeof(bool)));
        }, out bindings, out reason);
}
