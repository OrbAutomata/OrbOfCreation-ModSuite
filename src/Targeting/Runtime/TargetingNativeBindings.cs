using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set; execution performs no reflection.</summary>
internal sealed class TargetingNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "targeting.manager.type-action", "targeting.target-link.type-action",
        "auto-items.targetable.type", "targeting.tooltipable.type-action",
        "structure-lifecycle.structure.type-action", "targeting.effect-result-info.type-action",
        "targeting-manager.is-targeting", "targeting-manager.get-link",
        "targeting-manager.submit-target", "target-link.get-random",
        "targeting.target-link-get-all-targets-action",
        "targeting.target-link-check-target-action",
        "targeting.target-link-has-target-action",
        "targeting.target-link-target-action",
        "targeting.target-link-result-info-action",
        "targeting.effect-result-info-cancel-action",
        "targeting.effect-result-info-is-cancelled-action",
        "id-scriptable-object.get-guid-action",
    };

    private TargetingNativeBindings(Type linkType, Type structureType,
        Func<bool> isTargeting, Func<object?> getLink, Func<object, IList> getAllTargets,
        Func<object, object?> getRandom, Func<object, object, bool> checkTarget,
        Func<object, bool> hasTarget, Func<object, object?> readTarget,
        Func<object, object?> readResultInfo, Action<object> cancel,
        Func<object, bool> isCancelled, Action<object> submitTarget, Func<object, Guid> getGuid)
    {
        LinkType = linkType; StructureType = structureType; IsTargeting = isTargeting;
        GetLink = getLink; GetAllTargets = getAllTargets; GetRandom = getRandom;
        CheckTarget = checkTarget; HasTarget = hasTarget; ReadTarget = readTarget;
        ReadResultInfo = readResultInfo; Cancel = cancel; IsCancelled = isCancelled;
        SubmitTarget = submitTarget; GetGuid = getGuid;
    }

    internal Type LinkType { get; }
    internal Type StructureType { get; }
    internal Func<bool> IsTargeting { get; }
    internal Func<object?> GetLink { get; }
    internal Func<object, IList> GetAllTargets { get; }
    internal Func<object, object?> GetRandom { get; }
    internal Func<object, object, bool> CheckTarget { get; }
    internal Func<object, bool> HasTarget { get; }
    internal Func<object, object?> ReadTarget { get; }
    internal Func<object, object?> ReadResultInfo { get; }
    internal Action<object> Cancel { get; }
    internal Func<object, bool> IsCancelled { get; }
    internal Action<object> SubmitTarget { get; }
    internal Func<object, Guid> GetGuid { get; }

    internal static bool TryCreate(Func<string, Type?> resolveType, Func<string, bool> include,
        out TargetingNativeBindings? bindings, out string reason) =>
        NativeBindingSet.TryBind("targeting", ContractIds, resolveType, include, set =>
        {
            var manager = set.Type("targeting.manager.type-action", "TargetingManager");
            var link = set.Type("targeting.target-link.type-action", "TargetingManager+TargetLink");
            var targetable = set.Type("auto-items.targetable.type", "Targeting.ITargetable");
            var tooltipable = set.Type("targeting.tooltipable.type-action", "ITooltipable");
            var structure = set.Type("structure-lifecycle.structure.type-action", "StructureSO");
            var resultInfo = set.Type("targeting.effect-result-info.type-action", "EffectResultInfo");
            return new TargetingNativeBindings(
                link, structure,
                set.StaticCall<Func<bool>>("targeting-manager.is-targeting", manager, "IsTargeting", typeof(bool)),
                set.StaticCall<Func<object?>>("targeting-manager.get-link", manager, "GetTargetingLink", link),
                set.Call<Func<object, IList>>("targeting.target-link-get-all-targets-action",
                    link, "GetAllTargets", typeof(List<>).MakeGenericType(tooltipable)),
                set.Call<Func<object, object?>>("target-link.get-random", link, "GetRandom", targetable),
                set.Call<Func<object, object, bool>>("targeting.target-link-check-target-action",
                    link, "CheckTarget", typeof(bool), targetable),
                set.Call<Func<object, bool>>("targeting.target-link-has-target-action",
                    link, "HasTarget", typeof(bool)),
                set.Field<Func<object, object?>>("targeting.target-link-target-action", link, "target", targetable),
                set.Field<Func<object, object?>>("targeting.target-link-result-info-action",
                    link, "resultInfo", resultInfo),
                set.Call<Action<object>>("targeting.effect-result-info-cancel-action",
                    resultInfo, "Cancel", typeof(void)),
                set.Call<Func<object, bool>>("targeting.effect-result-info-is-cancelled-action",
                    resultInfo, "IsCancelled", typeof(bool)),
                set.StaticCall<Action<object>>("targeting-manager.submit-target",
                    manager, "SubmitTarget", typeof(void), targetable),
                set.StableUuid("id-scriptable-object.get-guid-action", structure));
        }, out bindings, out reason);
}
