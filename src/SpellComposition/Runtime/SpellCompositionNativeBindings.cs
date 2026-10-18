using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the four level dials and their unlock views.</summary>
internal sealed class SpellCompositionNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-composition.player.type-action",
        "challenge.int-variable.type-action",
        "spell-workbench.view-type-action",
        "spell-composition.player-instance-action",
        "spell-composition.player-output-level-action",
        "spell-composition.player-maximum-output-level-action",
        "spell-composition.player-reserve-level-action",
        "spell-composition.player-maximum-reserve-level-action",
        "spell-composition.player-druidry-level-action",
        "spell-composition.player-maximum-druidry-level-action",
        "spell-composition.player-alchemy-level-action",
        "spell-composition.player-maximum-alchemy-level-action",
        "spell-composition.dial-view-availability-action",
        "int-variable.as-int",
        "int-variable.set-value",
    };

    private SpellCompositionNativeBindings(
        Func<object?> player,
        Func<object?> outputVariable,
        Func<object, object> maximumOutputVariable,
        Func<object?> reserveVariable,
        Func<object, object> maximumReserveVariable,
        Func<object, object> druidryVariable,
        Func<object, object> maximumDruidryVariable,
        Func<object, object> alchemyVariable,
        Func<object, object> maximumAlchemyVariable,
        Type viewType,
        Func<object, bool> viewAvailable,
        Func<object, int> asInt,
        Action<object, int> setInt)
    {
        ReadPlayer = player;
        ReadOutputVariable = outputVariable;
        ReadMaximumOutputVariable = maximumOutputVariable;
        ReadReserveVariable = reserveVariable;
        ReadMaximumReserveVariable = maximumReserveVariable;
        ReadDruidryVariable = druidryVariable;
        ReadMaximumDruidryVariable = maximumDruidryVariable;
        ReadAlchemyVariable = alchemyVariable;
        ReadMaximumAlchemyVariable = maximumAlchemyVariable;
        ViewType = viewType;
        IsViewAvailable = viewAvailable;
        ReadInt = asInt;
        SetInt = setInt;
    }

    internal Func<object?> ReadPlayer { get; }
    internal Func<object?> ReadOutputVariable { get; }
    internal Func<object, object> ReadMaximumOutputVariable { get; }
    internal Func<object?> ReadReserveVariable { get; }
    internal Func<object, object> ReadMaximumReserveVariable { get; }
    internal Func<object, object> ReadDruidryVariable { get; }
    internal Func<object, object> ReadMaximumDruidryVariable { get; }
    internal Func<object, object> ReadAlchemyVariable { get; }
    internal Func<object, object> ReadMaximumAlchemyVariable { get; }
    internal Type ViewType { get; }

    internal Func<object, bool> IsViewAvailable { get; }
    internal Func<object, int> ReadInt { get; }
    internal Action<object, int> SetInt { get; }

    internal static bool TryCreate(
        Func<string, Type?> resolveType,
        Func<string, bool> includeContract,
        out SpellCompositionNativeBindings? bindings,
        out string reason) =>
        NativeBindingSet.TryBind("level-dial", ContractIds, resolveType, includeContract, set =>
        {
            var player = set.Type("spell-composition.player.type-action", "Player");
            var integer = set.Type("challenge.int-variable.type-action", "IntVariable");
            var view = set.Type("spell-workbench.view-type-action", "ViewSO");
            return new SpellCompositionNativeBindings(
                set.StaticField<Func<object?>>(
                    "spell-composition.player-instance-action", player, "_instance", player),
                set.StaticCall<Func<object?>>(
                    "spell-composition.player-output-level-action", player, "GetSpellOutputLevel", integer),
                set.Field<Func<object, object>>(
                    "spell-composition.player-maximum-output-level-action",
                    player, "maxSpellOutputLevel", integer),
                set.StaticCall<Func<object?>>(
                    "spell-composition.player-reserve-level-action", player, "GetReserveLevel", integer),
                set.Field<Func<object, object>>(
                    "spell-composition.player-maximum-reserve-level-action",
                    player, "maxReserveLevel", integer),
                set.Field<Func<object, object>>(
                    "spell-composition.player-druidry-level-action", player, "druidryOutputLevel", integer),
                set.Field<Func<object, object>>(
                    "spell-composition.player-maximum-druidry-level-action",
                    player, "maxDruidryOutputLevel", integer),
                set.Field<Func<object, object>>(
                    "spell-composition.player-alchemy-level-action", player, "alchemyOutputLevel", integer),
                set.Field<Func<object, object>>(
                    "spell-composition.player-maximum-alchemy-level-action",
                    player, "maxAlchemyOutputLevel", integer),
                view,
                set.Call<Func<object, bool>>(
                    "spell-composition.dial-view-availability-action", view, "IsAvailable", typeof(bool)),
                set.Call<Func<object, int>>("int-variable.as-int", integer, "AsInt", typeof(int)),
                set.Call<Action<object, int>>(
                    "int-variable.set-value", integer, "SetValue", typeof(void), typeof(int)));
        }, out bindings, out reason);
}
