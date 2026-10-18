using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the player-visible structure enable/disable button.</summary>
internal sealed class StructureLifecycleNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "structure-lifecycle.structure.type-action",
        "structure-lifecycle.available-action",
        "structure-lifecycle.disabled-action",
        "structure-lifecycle.toggle-action",
    };

    private StructureLifecycleNativeBindings(
        Type structureType,
        Func<object, bool> available,
        Func<object, bool> disabled,
        Action<object> toggle)
    {
        StructureType = structureType;
        Available = available;
        Disabled = disabled;
        Toggle = toggle;
    }

    internal Type StructureType { get; }
    internal Func<object, bool> Available { get; }
    internal Func<object, bool> Disabled { get; }
    internal Action<object> Toggle { get; }

    internal static bool TryCreate(
        out StructureLifecycleNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("structure lifecycle", ContractIds, resolveType, includeContract, set =>
        {
            var structure = set.Type("structure-lifecycle.structure.type-action", "StructureSO");
            return new StructureLifecycleNativeBindings(
                structure,
                set.Call<Func<object, bool>>(
                    "structure-lifecycle.available-action", structure, "IsAvailable", typeof(bool)),
                set.Field<Func<object, bool>>(
                    "structure-lifecycle.disabled-action", structure, "disabled", typeof(bool)),
                set.Call<Action<object>>(
                    "structure-lifecycle.toggle-action", structure, "ToggleDisabled", typeof(void)));
        }, out bindings, out reason);
}
