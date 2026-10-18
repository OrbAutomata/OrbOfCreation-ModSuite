using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for the active harvest element/action lists.</summary>
internal sealed class HarvestLifecycleGameAction : NativeGameAction<HarvestLifecycleNativeBindings>
{
    private const string Screen = "World > Agromancy";

    private static readonly GameActionCodes Codes = new(
        HarvestLifecycleActionResultCodes.WrongThread,
        HarvestLifecycleActionResultCodes.ContractUnavailable,
        HarvestLifecycleActionResultCodes.MutationPermitUnavailable,
        HarvestLifecycleActionResultCodes.PostCommitFault,
        HarvestLifecycleActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal HarvestLifecycleGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Harvest", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out HarvestLifecycleNativeBindings? bindings, out string reason) =>
                HarvestLifecycleNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal HarvestLifecycleSubmission Submit(in HarvestLifecycleAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new HarvestLifecycleSubmission(outcome),
            native => Press(in request, native));
    }

    private HarvestLifecycleSubmission Press(
        in HarvestLifecycleAction action,
        HarvestLifecycleNativeBindings native)
    {
        if (!_registry.TryResolveCurrent(action.ElementId, native.ElementType, out var element, out var reason))
            return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.IdentityUnavailable, reason);
        if (!_registry.TryResolveCurrent(KnownEntities.ActiveHarvestElements.Uuid,
                native.ElementListType, out var elementList, out reason) ||
            !_registry.TryResolveCurrent(KnownEntities.ActiveHarvestActions.Uuid,
                native.ActionListType, out var actionList, out reason))
            return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.ContractUnavailable, reason);

        object? prototype = null;
        object? active = null;
        int before;
        if (action.Kind is HarvestLifecycleActionKind.AddElement or
            HarvestLifecycleActionKind.RemoveElement)
        {
            before = native.ElementStacks(elementList, element);
            var admission = AdmitElement(in action, native, element, elementList, before);
            if (admission.HasValue) return admission.Value;
        }
        else
        {
            if (!_registry.TryResolveCurrent(action.ActionId, native.ActionType, out var actionObject, out reason))
                return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.IdentityUnavailable, reason);
            prototype = FindPrototype(native, element, actionObject);
            if (prototype is null)
                return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.ActionUnavailable,
                    EntityIdentityFormatter.PlayerName(action.ActionId) +
                    " is not offered for " + EntityIdentityFormatter.PlayerName(action.ElementId) + ".");
            active = native.FindAction(actionList, prototype);
            if (active is not null && active.GetType() != native.InstanceType)
                return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.ContractUnavailable,
                    "The active harvest action has an unexpected native type.");
            before = active is null ? 0 : native.InstanceCount(active);
            var admission = AdmitAction(in action, native, prototype, actionList, before, active);
            if (admission.HasValue) return admission.Value;
        }

        if (!TryPermit(out var refusal)) return new HarvestLifecycleSubmission(refusal);
        var kind = action.Kind;
        var amount = action.Amount;
        return new HarvestLifecycleSubmission(Execute(
            EntityIdentityFormatter.PlayerName(action.ElementId),
            "The requested harvest-list transition is visible.", before,
            () => Count(kind, native, element, elementList, actionList, prototype, before),
            () => Call(kind, native, element, elementList, actionList, prototype, active, amount),
            kind is HarvestLifecycleActionKind.AddElement or HarvestLifecycleActionKind.AddAction
                ? static (previous, after) => after > previous
                : static (previous, after) => after < previous));
    }

    private static HarvestLifecycleSubmission? AdmitElement(
        in HarvestLifecycleAction action,
        HarvestLifecycleNativeBindings native,
        object element,
        object list,
        int current)
    {
        if (!native.ElementVisible(element) || !native.ElementAvailable(element))
            return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.NotVisible,
                EntityIdentityFormatter.PlayerName(action.ElementId) + " is not available yet.");
        if (action.Kind == HarvestLifecycleActionKind.RemoveElement)
        {
            // Nothing active is a wrong target, not a wrong amount: no smaller ask succeeds.
            if (current <= 0)
                return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.NotActive,
                    EntityIdentityFormatter.PlayerName(action.ElementId) + " is not active.");
            return action.Amount <= current
                ? null
                : HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.AmountUnavailable,
                    EntityIdentityFormatter.PlayerName(action.ElementId) + " has only " + current +
                    " active " + Plural(current, "instance", "instances") + ".",
                    current);
        }
        if (current == 0 && !native.ElementListHasRoom(list))
            return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.ElementListFull,
                "The active harvest element list has no empty slot.");
        var cost = native.ElementUsageCost(element);
        if (cost is null || !native.CostHasEnough(cost))
            return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.ElementUsageUnavailable,
                "There is not enough free resource capacity to activate " +
                EntityIdentityFormatter.PlayerName(action.ElementId) + ".");
        var maximum = native.ElementMaximumInstances(element).ToInt();
        return action.Amount <= maximum
            ? null
            : HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.AmountUnavailable,
                "The current resource capacity allows at most " + maximum + " more " +
                Plural(maximum, "instance", "instances") + " of " +
                EntityIdentityFormatter.PlayerName(action.ElementId) + ".",
                maximum);
    }

    private static HarvestLifecycleSubmission? AdmitAction(
        in HarvestLifecycleAction action,
        HarvestLifecycleNativeBindings native,
        object prototype,
        object list,
        int current,
        object? active)
    {
        if (!native.InstanceVisible(prototype))
            return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.ActionUnavailable,
                EntityIdentityFormatter.PlayerName(action.ActionId) + " is not available for " +
                EntityIdentityFormatter.PlayerName(action.ElementId) + " yet.");
        if (action.Kind == HarvestLifecycleActionKind.RemoveAction)
        {
            if (active is null || current <= 0)
                return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.NotActive,
                    EntityIdentityFormatter.PlayerName(action.ActionId) + " is not active on " +
                    EntityIdentityFormatter.PlayerName(action.ElementId) + ".");
            return action.Amount <= current
                ? null
                : HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.AmountUnavailable,
                    EntityIdentityFormatter.PlayerName(action.ActionId) + " has only " + current +
                    " active " + Plural(current, "instance", "instances") + " on " +
                    EntityIdentityFormatter.PlayerName(action.ElementId) + ".",
                    current);
        }
        if (active is null && !native.ActionListHasRoom(list))
            return HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.ActionListFull,
                "The active harvest action list has no empty slot.");
        var maximum = native.InstanceMaximum(prototype);
        return current + action.Amount <= maximum
            ? null
            : HarvestLifecycleSubmission.Refused(HarvestLifecycleActionResultCodes.AmountUnavailable,
                EntityIdentityFormatter.PlayerName(action.ActionId) + " allows at most " + maximum +
                " active " + Plural(maximum, "instance", "instances") + " on " +
                EntityIdentityFormatter.PlayerName(action.ElementId) + ".",
                maximum);
    }

    private static void Call(
        HarvestLifecycleActionKind kind,
        HarvestLifecycleNativeBindings native,
        object element,
        object elementList,
        object actionList,
        object? prototype,
        object? active,
        int amount)
    {
        switch (kind)
        {
            case HarvestLifecycleActionKind.AddElement:
                native.AddElement(elementList, element, new BigDouble(amount));
                break;
            case HarvestLifecycleActionKind.RemoveElement:
                native.RemoveElement(elementList, element, new BigDouble(amount));
                break;
            case HarvestLifecycleActionKind.AddAction:
                native.AddAction(actionList, prototype!, amount);
                break;
            case HarvestLifecycleActionKind.RemoveAction:
                native.RemoveAction(actionList, active!, amount);
                break;
            default:
                throw new InvalidOperationException("Unsupported harvest lifecycle action.");
        }
    }

    private static int Count(
        HarvestLifecycleActionKind kind,
        HarvestLifecycleNativeBindings native,
        object element,
        object elementList,
        object actionList,
        object? prototype,
        int before)
    {
        if (kind is HarvestLifecycleActionKind.AddElement or HarvestLifecycleActionKind.RemoveElement)
            return native.ElementStacks(elementList, element);
        var current = native.FindAction(actionList, prototype!);
        return current is null ? 0 :
            current.GetType() == native.InstanceType ? native.InstanceCount(current) : before;
    }

    private static object? FindPrototype(
        HarvestLifecycleNativeBindings native,
        object element,
        object action)
    {
        var candidates = native.ElementActionInstances(element);
        for (var index = 0; index < (candidates?.Count ?? 0); index++)
        {
            var candidate = candidates![index];
            if (candidate is not null && candidate.GetType() == native.InstanceType &&
                ReferenceEquals(native.InstanceElement(candidate), element) &&
                ReferenceEquals(native.InstanceAction(candidate), action)) return candidate;
        }
        return null;
    }

    private static string Plural(int amount, string singular, string plural) =>
        amount == 1 ? singular : plural;
}
