using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Complete binding set for Auto Cast: the equipped loadout, the cast and its target rounds, the
/// readiness and charge terms, toggle-off, the ritual reading and the target preflight's walk from a
/// recipe to its target requests.
/// </summary>
/// <remarks>
/// The preflight walk binds exactly the one path on which the game can place a target request the
/// cast opens: <c>SpellRecipeSO.onCastEffects</c>, each block's <c>effectScripts</c>, and an entry
/// whose type is <c>RequestTargetEffectScript</c>. A request nested deeper, inside a chance or
/// duration script's own blocks, was never reached by the name walk this replaced either.
/// </remarks>
internal sealed class AutoCastNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-workbench.manager-type-action",
        "spell-workbench.spell-list-type-action",
        "loadout.spell.type-action",
        "loadout.spell-recipe.type-action",
        "targeting.manager.type-action",
        "targeting.target-link.type-action",
        "auto-items.targetable.type",
        "research-action.settings.type-action",
        "ritual-lifecycle.battle-manager.type-action",
        "auto-items.instant-effect-block.type",
        "auto-items.instant-effect-script.type",
        "auto-items.request-target-effect-script.type",
        "auto-items.target-select-options.type",
        "auto-items.scaling-info.type",
        "spell-manager.instance",
        "spell-manager.active-spells",
        "abstract-list.value",
        "spell-manager.fire-spell-index",
        "spell-manager.can-cast-a-spell",
        "targeting-manager.is-targeting",
        "targeting-manager.get-link",
        "targeting-manager.submit-target",
        "target-link.get-random",
        "auto-cast.spell-can-cast-action",
        "auto-cast.spell-can-charge-action",
        "spell-loadout.spell-is-empty-action",
        "auto-cast.spell-can-fire-action",
        "auto-cast.spell-is-casting-action",
        "spell-loadout.spell-readying-cast-action",
        "auto-cast.spell-is-toggled-action",
        "auto-cast.settings-can-cancel-action",
        "spell-loadout.spell-reference-action",
        "spell.get-scaling-info",
        "spell.set-charge-input",
        "id-scriptable-object.get-guid-action",
        "ritual-lifecycle.battle-manager-instance-action",
        "ritual-lifecycle.battle-active-action",
        "auto-cast.recipe-on-cast-effects-action",
        "auto-cast.block-effect-scripts-action",
        "target-request.options",
        "target-options.has-valid",
    };

    private AutoCastNativeBindings(
        Type spellType,
        Type requestType,
        Func<object?> manager,
        Func<object, object?> activeSpells,
        Func<object, IList?> slots,
        Action<object, int> fireSpellIndex,
        Func<bool> canCastASpell,
        Func<bool> isTargeting,
        Func<object?> getTargetingLink,
        Action<object> submitTarget,
        Func<object, object?> getRandom,
        Func<object, bool> canCast,
        Func<object, bool> canCharge,
        Func<object, bool> isEmpty,
        Func<object, bool> canFire,
        Func<object, bool> isCasting,
        Func<object, bool> isReadyingCast,
        Func<object, bool> isToggled,
        Func<bool> canCancelSpells,
        Func<object, object?> recipe,
        Func<object, object?> scalingInfo,
        Action<object, string, bool> setChargeInput,
        Func<object, Guid> recipeId,
        Func<object?> battleManager,
        Func<object, bool> isInCombat,
        Func<object, IList?> onCastEffects,
        Func<object, IList?> effectScripts,
        Func<object, object?> targetOptions,
        Func<object, object, bool> hasValidTargetsLeft)
    {
        SpellType = spellType;
        RequestType = requestType;
        Manager = manager;
        ActiveSpells = activeSpells;
        Slots = slots;
        FireSpellIndex = fireSpellIndex;
        CanCastASpell = canCastASpell;
        IsTargeting = isTargeting;
        GetTargetingLink = getTargetingLink;
        SubmitTarget = submitTarget;
        GetRandom = getRandom;
        CanCast = canCast;
        CanCharge = canCharge;
        IsEmpty = isEmpty;
        CanFire = canFire;
        IsCasting = isCasting;
        IsReadyingCast = isReadyingCast;
        IsToggled = isToggled;
        CanCancelSpells = canCancelSpells;
        Recipe = recipe;
        ScalingInfo = scalingInfo;
        SetChargeInput = setChargeInput;
        RecipeId = recipeId;
        BattleManager = battleManager;
        IsInCombat = isInCombat;
        OnCastEffects = onCastEffects;
        EffectScripts = effectScripts;
        TargetOptions = targetOptions;
        HasValidTargetsLeft = hasValidTargetsLeft;
    }

    internal Type SpellType { get; }
    internal Type RequestType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> ActiveSpells { get; }
    internal Func<object, IList?> Slots { get; }
    internal Action<object, int> FireSpellIndex { get; }
    internal Func<bool> CanCastASpell { get; }
    internal Func<bool> IsTargeting { get; }
    internal Func<object?> GetTargetingLink { get; }
    internal Action<object> SubmitTarget { get; }
    internal Func<object, object?> GetRandom { get; }
    internal Func<object, bool> CanCast { get; }
    internal Func<object, bool> CanCharge { get; }
    internal Func<object, bool> IsEmpty { get; }
    internal Func<object, bool> CanFire { get; }
    internal Func<object, bool> IsCasting { get; }
    internal Func<object, bool> IsReadyingCast { get; }
    internal Func<object, bool> IsToggled { get; }
    internal Func<bool> CanCancelSpells { get; }
    internal Func<object, object?> Recipe { get; }
    internal Func<object, object?> ScalingInfo { get; }
    internal Action<object, string, bool> SetChargeInput { get; }
    internal Func<object, Guid> RecipeId { get; }
    internal Func<object?> BattleManager { get; }
    internal Func<object, bool> IsInCombat { get; }
    internal Func<object, IList?> OnCastEffects { get; }
    internal Func<object, IList?> EffectScripts { get; }
    internal Func<object, object?> TargetOptions { get; }
    internal Func<object, object, bool> HasValidTargetsLeft { get; }

    internal static bool TryCreate(out AutoCastNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("auto-cast", ContractIds, resolveType, includeContract, set =>
        {
            var manager = set.Type("spell-workbench.manager-type-action", "SpellManager");
            var spellList = set.Type("spell-workbench.spell-list-type-action", "SpellListVariable");
            var spell = set.Type("loadout.spell.type-action", "Spell");
            var recipe = set.Type("loadout.spell-recipe.type-action", "SpellRecipeSO");
            var targeting = set.Type("targeting.manager.type-action", "TargetingManager");
            var link = set.Type("targeting.target-link.type-action", "TargetingManager+TargetLink");
            var targetable = set.Type("auto-items.targetable.type", "Targeting.ITargetable");
            var settings = set.Type("research-action.settings.type-action", "SettingsManager");
            var battle = set.Type("ritual-lifecycle.battle-manager.type-action", "BattleManager");
            var block = set.Type("auto-items.instant-effect-block.type", "InstantEffectBlock");
            var script = set.Type("auto-items.instant-effect-script.type", "IInstantEffectScript");
            var request = set.Type("auto-items.request-target-effect-script.type", "RequestTargetEffectScript");
            var options = set.Type("auto-items.target-select-options.type", "Targeting.TargetSelectOptions");
            var scaling = set.Type("auto-items.scaling-info.type", "ScalingInfo");

            return new AutoCastNativeBindings(
                spell,
                request,
                set.StaticField<Func<object?>>("spell-manager.instance", manager, "instance", manager),
                set.Field<Func<object, object?>>("spell-manager.active-spells", manager, "activeSpells", spellList),
                set.Field<Func<object, IList?>>("abstract-list.value", spellList, "value", ListOf(spell)),
                set.Call<Action<object, int>>(
                    "spell-manager.fire-spell-index", manager, "FireSpellIndex", typeof(void), typeof(int)),
                set.StaticCall<Func<bool>>("spell-manager.can-cast-a-spell", manager, "CanCastASpell", typeof(bool)),
                set.StaticCall<Func<bool>>("targeting-manager.is-targeting", targeting, "IsTargeting", typeof(bool)),
                set.StaticCall<Func<object?>>("targeting-manager.get-link", targeting, "GetTargetingLink", link),
                set.StaticCall<Action<object>>(
                    "targeting-manager.submit-target", targeting, "SubmitTarget", typeof(void), targetable),
                set.Call<Func<object, object?>>("target-link.get-random", link, "GetRandom", targetable),
                set.Call<Func<object, bool>>("auto-cast.spell-can-cast-action", spell, "CanCast", typeof(bool)),
                set.Call<Func<object, bool>>("auto-cast.spell-can-charge-action", spell, "CanCharge", typeof(bool)),
                set.Call<Func<object, bool>>("spell-loadout.spell-is-empty-action", spell, "IsEmpty", typeof(bool)),
                set.Call<Func<object, bool>>("auto-cast.spell-can-fire-action", spell, "CanFire", typeof(bool)),
                set.Call<Func<object, bool>>("auto-cast.spell-is-casting-action", spell, "IsCasting", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "spell-loadout.spell-readying-cast-action", spell, "IsReadyingCast", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "auto-cast.spell-is-toggled-action", spell, "IsToggledSpell", typeof(bool)),
                set.StaticCall<Func<bool>>(
                    "auto-cast.settings-can-cancel-action", settings, "CanCancelSpells", typeof(bool)),
                set.Call<Func<object, object?>>("spell-loadout.spell-reference-action", spell, "get_reference", recipe),
                set.Call<Func<object, object?>>("spell.get-scaling-info", spell, "GetScalingInfo", scaling),
                set.Call<Action<object, string, bool>>(
                    "spell.set-charge-input", spell, "SetChargeInput", typeof(void), typeof(string), typeof(bool)),
                set.StableUuid("id-scriptable-object.get-guid-action", recipe),
                set.StaticField<Func<object?>>(
                    "ritual-lifecycle.battle-manager-instance-action", battle, "instance", battle),
                set.Call<Func<object, bool>>("ritual-lifecycle.battle-active-action", battle, "IsInCombat", typeof(bool)),
                set.Field<Func<object, IList?>>(
                    "auto-cast.recipe-on-cast-effects-action", recipe, "onCastEffects", ListOf(block)),
                set.Field<Func<object, IList?>>(
                    "auto-cast.block-effect-scripts-action", block, "effectScripts", ListOf(script)),
                set.Field<Func<object, object?>>("target-request.options", request, "targetOptions", options),
                set.Call<Func<object, object, bool>>(
                    "target-options.has-valid", options, "HasValidTargetsLeft", typeof(bool), scaling));
        }, out bindings, out reason);

    private static Type ListOf(Type element) => typeof(List<>).MakeGenericType(element);
}
