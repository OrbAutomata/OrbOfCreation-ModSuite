using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for the active harvest element/action lists.</summary>
internal sealed class HarvestLifecycleGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private HarvestLifecycleNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal HarvestLifecycleGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(
            _readLifecycleEpoch, identity.Read, identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal HarvestLifecycleSubmission Submit(in HarvestLifecycleAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(HarvestLifecycleActionResultCodes.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(HarvestLifecycleActionResultCodes.ContractUnavailable,
                GameActionAnswer.NotAttached("World > Agromancy", _bindingFailure));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("World > Agromancy", exception));
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(CommonActionResultCodes.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            if (!TryResolve(action.ElementId, native.ElementType, out var element, out var reason))
                return Reject(HarvestLifecycleActionResultCodes.IdentityUnavailable, reason);
            if (!TryResolve(KnownEntities.ActiveHarvestElements.Uuid,
                    native.ElementListType, out var elementList, out reason) ||
                !TryResolve(KnownEntities.ActiveHarvestActions.Uuid,
                    native.ActionListType, out var actionList, out reason))
                return Reject(HarvestLifecycleActionResultCodes.ContractUnavailable, reason);

            object? prototype = null;
            object? active = null;
            var before = 0;
            if (action.Kind is HarvestLifecycleActionKind.AddElement or
                HarvestLifecycleActionKind.RemoveElement)
            {
                before = native.ElementStacks(elementList!, element!);
                var admission = AdmitElement(in action, native, element!, elementList!, before);
                if (admission.HasValue) return admission.Value;
            }
            else
            {
                if (!TryResolve(action.ActionId, native.ActionType, out var actionObject, out reason))
                    return Reject(HarvestLifecycleActionResultCodes.IdentityUnavailable, reason);
                prototype = FindPrototype(native, element!, actionObject!);
                if (prototype is null)
                    return Reject(HarvestLifecycleActionResultCodes.ActionUnavailable,
                        EntityIdentityFormatter.PlayerName(action.ActionId) +
                        " is not offered for " + EntityIdentityFormatter.PlayerName(action.ElementId) + ".");
                active = native.FindAction(actionList!, prototype);
                if (active is not null && active.GetType() != native.InstanceType)
                    return Reject(HarvestLifecycleActionResultCodes.ContractUnavailable,
                        "The active harvest action has an unexpected native type.");
                before = active is null ? 0 : native.InstanceCount(active);
                var admission = AdmitAction(in action, native, prototype, actionList!, before, active);
                if (admission.HasValue) return admission.Value;
            }

            if (!_tryCaptureMutationPermit())
                return Reject(HarvestLifecycleActionResultCodes.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, element!, elementList!, actionList!,
                prototype, active, before);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return Reject(HarvestLifecycleActionResultCodes.ContractUnavailable,
                GameActionAnswer.CouldNotRead("World > Agromancy", exception));
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    private static HarvestLifecycleSubmission? AdmitElement(
        in HarvestLifecycleAction action,
        HarvestLifecycleNativeBindings native,
        object element,
        object list,
        int current)
    {
        if (!native.ElementVisible(element) || !native.ElementAvailable(element))
            return Reject(HarvestLifecycleActionResultCodes.NotVisible,
                EntityIdentityFormatter.PlayerName(action.ElementId) + " is not available yet.");
        if (action.Kind == HarvestLifecycleActionKind.RemoveElement)
        {
            // Nothing active is a wrong target, not a wrong amount: no smaller ask succeeds.
            if (current <= 0)
                return Reject(HarvestLifecycleActionResultCodes.NotActive,
                    EntityIdentityFormatter.PlayerName(action.ElementId) + " is not active.");
            return action.Amount <= current
                ? null
                : Reject(HarvestLifecycleActionResultCodes.AmountUnavailable,
                    EntityIdentityFormatter.PlayerName(action.ElementId) + " has only " + current +
                    " active " + Plural(current, "instance", "instances") + ".",
                    current);
        }
        if (current == 0 && !native.ElementListHasRoom(list))
            return Reject(HarvestLifecycleActionResultCodes.ElementListFull,
                "The active harvest element list has no empty slot.");
        var cost = native.ElementUsageCost(element);
        if (cost is null || !native.CostHasEnough(cost))
            return Reject(HarvestLifecycleActionResultCodes.ElementUsageUnavailable,
                "There is not enough free resource capacity to activate " +
                EntityIdentityFormatter.PlayerName(action.ElementId) + ".");
        var maximum = native.ElementMaximumInstances(element).ToInt();
        return action.Amount <= maximum
            ? null
            : Reject(HarvestLifecycleActionResultCodes.AmountUnavailable,
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
            return Reject(HarvestLifecycleActionResultCodes.ActionUnavailable,
                EntityIdentityFormatter.PlayerName(action.ActionId) + " is not available for " +
                EntityIdentityFormatter.PlayerName(action.ElementId) + " yet.");
        if (action.Kind == HarvestLifecycleActionKind.RemoveAction)
        {
            if (active is null || current <= 0)
                return Reject(HarvestLifecycleActionResultCodes.NotActive,
                    EntityIdentityFormatter.PlayerName(action.ActionId) + " is not active on " +
                    EntityIdentityFormatter.PlayerName(action.ElementId) + ".");
            return action.Amount <= current
                ? null
                : Reject(HarvestLifecycleActionResultCodes.AmountUnavailable,
                    EntityIdentityFormatter.PlayerName(action.ActionId) + " has only " + current +
                    " active " + Plural(current, "instance", "instances") + " on " +
                    EntityIdentityFormatter.PlayerName(action.ElementId) + ".",
                    current);
        }
        if (active is null && !native.ActionListHasRoom(list))
            return Reject(HarvestLifecycleActionResultCodes.ActionListFull,
                "The active harvest action list has no empty slot.");
        var maximum = native.InstanceMaximum(prototype);
        return current + action.Amount <= maximum
            ? null
            : Reject(HarvestLifecycleActionResultCodes.AmountUnavailable,
                EntityIdentityFormatter.PlayerName(action.ActionId) + " allows at most " + maximum +
                " active " + Plural(maximum, "instance", "instances") + " on " +
                EntityIdentityFormatter.PlayerName(action.ElementId) + ".",
                maximum);
    }

    private static HarvestLifecycleSubmission Execute(
        in HarvestLifecycleAction action,
        HarvestLifecycleNativeBindings native,
        object element,
        object elementList,
        object actionList,
        object? prototype,
        object? active,
        int before)
    {
        var stage = "NativeCallback";
        try
        {
            switch (action.Kind)
            {
                case HarvestLifecycleActionKind.AddElement:
                    native.AddElement(elementList, element, new BigDouble(action.Amount));
                    break;
                case HarvestLifecycleActionKind.RemoveElement:
                    native.RemoveElement(elementList, element, new BigDouble(action.Amount));
                    break;
                case HarvestLifecycleActionKind.AddAction:
                    native.AddAction(actionList, prototype!, action.Amount);
                    break;
                case HarvestLifecycleActionKind.RemoveAction:
                    native.RemoveAction(actionList, active!, action.Amount);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported harvest lifecycle action.");
            }
            stage = "Verification";
            return OutcomeObserved(in action, native, element, elementList, actionList,
                    prototype, before)
                ? Verified()
                : Fault(in action, HarvestLifecycleActionResultCodes.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    GameActionAnswer.ChangeNotSeen("World > Agromancy"));
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            var request = action;
            if (NativeMutationVerifier.TryReadAfterCall(
                    () => OutcomeObserved(in request, native, element, elementList, actionList,
                        prototype, before), out var observed) && observed) return Verified();
            return Fault(in action, HarvestLifecycleActionResultCodes.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("World > Agromancy", exception));
        }
    }

    private static bool OutcomeObserved(
        in HarvestLifecycleAction action,
        HarvestLifecycleNativeBindings native,
        object element,
        object elementList,
        object actionList,
        object? prototype,
        int before)
    {
        int after;
        if (action.Kind is HarvestLifecycleActionKind.AddElement or
            HarvestLifecycleActionKind.RemoveElement)
        {
            after = native.ElementStacks(elementList, element);
        }
        else
        {
            var current = native.FindAction(actionList, prototype!);
            after = current is null ? 0 :
                current.GetType() == native.InstanceType ? native.InstanceCount(current) : before;
        }
        return action.Kind is HarvestLifecycleActionKind.AddElement or
            HarvestLifecycleActionKind.AddAction
            ? after > before
            : after < before;
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

    private bool TryResolve(Guid id, Type type, out object? value, out string reason)
    {
        var resolution = _registry.Resolve(id, type);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
        {
            value = null;
            reason = resolution.IsResolved
                ? EntityIdentityFormatter.PlayerName(id) + " became stale."
                : resolution.Reason;
            return false;
        }
        value = resolution.Value;
        reason = string.Empty;
        return true;
    }

    private static string Plural(int amount, string singular, string plural) =>
        amount == 1 ? singular : plural;

    private static HarvestLifecycleSubmission Reject(
        ServiceActionResultCode code,
        string reason,
        int maximumAmount = -1) =>
        new(NativeActionOutcome.Reject(code, reason), maximumAmount);

    private static HarvestLifecycleSubmission Verified() =>
        new(NativeActionOutcome.Commit(
            new NativeMutationCallOutcome(1, 1, 1),
            "The requested harvest-list transition is visible."));

    private static HarvestLifecycleSubmission Fault(
        in HarvestLifecycleAction action,
        ServiceActionResultCode code,
        string stage,
        NativeMutationOutcome outcome,
        string reason) =>
        new(NativeActionOutcome.Fault(code, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Harvest " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.ElementId) + ": " + reason));

    private void BindLifecycle()
    {
        if (HarvestLifecycleNativeBindings.TryCreate(
                out var bindings, out var reason, _resolveType, _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

}
