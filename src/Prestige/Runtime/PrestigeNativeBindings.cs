using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the native persistent-reset transaction.</summary>
internal sealed class PrestigeNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "prestige.reset-manager.type-action", "prestige.bool-variable.type-action",
        "prestige.reset-manager-instance-action", "prestige.reset-cycle-complete-action",
        "prestige.reset-fetched-action", "prestige.bool-get-action",
        "prestige.reset-logic-action",
    };

    private PrestigeNativeBindings(Type managerType, Func<object?> manager,
        Func<object, object?> cycleComplete, Func<object, object?> challengesFetched,
        Func<object, bool> getBool, Action<object> reset)
    {
        ManagerType = managerType;
        Manager = manager;
        CycleComplete = cycleComplete;
        ChallengesFetched = challengesFetched;
        GetBool = getBool;
        Reset = reset;
    }

    internal Type ManagerType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> CycleComplete { get; }
    internal Func<object, object?> ChallengesFetched { get; }
    internal Func<object, bool> GetBool { get; }
    internal Action<object> Reset { get; }

    internal static bool TryCreate(out PrestigeNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("prestige", ContractIds, resolveType, includeContract, set =>
        {
            var manager = set.Type("prestige.reset-manager.type-action", "PersistentResetManager");
            var boolean = set.Type("prestige.bool-variable.type-action", "BoolVariable");
            return new PrestigeNativeBindings(
                manager,
                set.StaticField<Func<object?>>(
                    "prestige.reset-manager-instance-action", manager, "instance", manager),
                set.Field<Func<object, object?>>(
                    "prestige.reset-cycle-complete-action", manager, "hasCompleteWorldCycle", boolean),
                set.Field<Func<object, object?>>(
                    "prestige.reset-fetched-action", manager, "hasFetchedChallenges", boolean),
                set.Call<Func<object, bool>>("prestige.bool-get-action", boolean, "GetValue", typeof(bool)),
                set.Call<Action<object>>(
                    "prestige.reset-logic-action", manager, "PersistentResetLogic", typeof(void)));
        }, out bindings, out reason);
}
