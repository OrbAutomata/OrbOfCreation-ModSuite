using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for every player-visible plot/action pair.</summary>
internal sealed class PlotLifecycleGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private PlotLifecycleNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal PlotLifecycleGameAction(
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

    internal PlotLifecycleSubmission Submit(in PlotLifecycleAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(PlotLifecyclePreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(PlotLifecyclePreflight.ContractUnavailable, _bindingFailure);
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(PlotLifecyclePreflight.LifecycleReplaced,
                "The current game lifecycle could not be read: " + exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(PlotLifecyclePreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            if (!TryResolve(action.PlotId, native.PlotType, out var plot, out var reason) ||
                !TryResolve(action.ActionId, native.ActionType, out var actionObject, out reason))
                return Reject(PlotLifecyclePreflight.IdentityUnavailable, reason);
            if (!TryResolve(PlotLifecycleNativeBindings.ActiveActionsId,
                    native.ListType, out var list, out reason))
                return Reject(PlotLifecyclePreflight.ContractUnavailable, reason);
            if (!native.PlotVisible(plot!))
                return Reject(PlotLifecyclePreflight.PlotUnavailable,
                    EntityIdentityFormatter.PlayerName(action.PlotId) + " is not visible yet.");
            var prototype = FindPrototype(native, plot!, actionObject!);
            if (prototype is null)
                return Reject(PlotLifecyclePreflight.ActionUnavailable,
                    EntityIdentityFormatter.PlayerName(action.ActionId) + " is not offered for " +
                    EntityIdentityFormatter.PlayerName(action.PlotId) + ".");
            var current = native.FindInstance(list!, prototype);
            if (current is not null && current.GetType() != native.InstanceType)
                return Reject(PlotLifecyclePreflight.ContractUnavailable,
                    "The active plot action has an unexpected native type.");
            var before = Quantity(native, current);
            var admission = Admit(in action, native, prototype, list!, current, before);
            if (admission.HasValue) return admission.Value;
            if (!_tryCaptureMutationPermit())
                return Reject(PlotLifecyclePreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, list!, prototype, current, before);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(PlotLifecyclePreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("World > Agromancy"));
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

    private static PlotLifecycleSubmission? Admit(
        in PlotLifecycleAction action,
        PlotLifecycleNativeBindings native,
        object prototype,
        object list,
        object? current,
        int before)
    {
        if (action.Kind == PlotLifecycleActionKind.Remove)
        {
            // Not an amount problem in any sense, and the one case where retrying with a smaller
            // amount is guaranteed useless: the pair is not active at all.
            if (current is null || before <= 0)
                return Reject(PlotLifecyclePreflight.NotActive,
                    EntityIdentityFormatter.PlayerName(action.ActionId) + " is not active on " +
                    EntityIdentityFormatter.PlayerName(action.PlotId) + ".");
            if (action.Amount > before)
                return Reject(PlotLifecyclePreflight.QuantityUnavailable,
                    EntityIdentityFormatter.PlayerName(action.ActionId) + " has only " + before +
                    " active " + (before == 1 ? "instance" : "instances") + " on " +
                    EntityIdentityFormatter.PlayerName(action.PlotId) + ".");
            return null;
        }

        if (!native.InstanceVisible(prototype))
            return Reject(PlotLifecyclePreflight.ActionUnavailable,
                EntityIdentityFormatter.PlayerName(action.ActionId) + " is not available for " +
                EntityIdentityFormatter.PlayerName(action.PlotId) + " yet.");
        if (!native.InstanceAffordable(prototype))
            return Reject(PlotLifecyclePreflight.QuantityUnavailable,
                EntityIdentityFormatter.PlayerName(action.PlotId) +
                " does not have enough remaining quantity for that action.");
        var maximumRemaining = native.InstanceMaximumRemaining(prototype);
        var maximum = native.InstanceMaximum(prototype);
        if (action.Amount > maximumRemaining || before + action.Amount > maximum)
        {
            var remaining = Math.Max(Math.Min(maximumRemaining, maximum - before), 0);
            return Reject(PlotLifecyclePreflight.QuantityUnavailable,
                "The plot currently allows at most " + remaining +
                " more active instances of " + EntityIdentityFormatter.PlayerName(action.ActionId) + ".",
                remaining);
        }
        if ((current is null || before <= 0) && !native.ListHasRoom(list))
            return Reject(PlotLifecyclePreflight.ActionListFull,
                "The active plot-action list has no empty slot.");
        return null;
    }

    private static PlotLifecycleSubmission Execute(
        in PlotLifecycleAction action,
        PlotLifecycleNativeBindings native,
        object list,
        object prototype,
        object? current,
        int before)
    {
        var stage = PlotLifecycleNativeStage.NativeCallback;
        try
        {
            if (action.Kind == PlotLifecycleActionKind.Add)
                native.AddInstance(list, prototype, action.Amount);
            else if (native.InstanceAtMinimum(current!))
                native.InstanceCancel(current!);
            else
                native.RemoveInstance(list, current!, action.Amount);
            stage = PlotLifecycleNativeStage.Verification;
            var after = Quantity(native, native.FindInstance(list, prototype));
            return OutcomeObserved(in action, before, after)
                ? Verified(before, after)
                : Fault(in action, PlotLifecyclePreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    GameActionAnswer.ChangeNotSeen("World > Agromancy"));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            var after = Quantity(native, native.FindInstance(list, prototype));
            if (OutcomeObserved(in action, before, after)) return Verified(before, after);
            return Fault(in action, PlotLifecyclePreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("World > Agromancy"));
        }
    }

    private static bool OutcomeObserved(
        in PlotLifecycleAction action,
        int before,
        int after) =>
        action.Kind == PlotLifecycleActionKind.Add
            ? after == checked(before + action.Amount)
            : after == checked(before - action.Amount);

    private static int Quantity(PlotLifecycleNativeBindings native, object? instance) =>
        instance is null ? 0 : Math.Max(native.InstanceQuantity(instance), 0);

    private static object? FindPrototype(
        PlotLifecycleNativeBindings native,
        object plot,
        object action)
    {
        var candidates = native.PlotInstances(plot);
        object? found = null;
        for (var index = 0; index < (candidates?.Count ?? 0); index++)
        {
            var candidate = candidates![index];
            if (candidate is null || candidate.GetType() != native.InstanceType) continue;
            if (!ReferenceEquals(native.InstancePlot(candidate), plot) ||
                !ReferenceEquals(native.InstanceAction(candidate), action)) continue;
            if (found is not null) return null;
            found = candidate;
        }
        return found;
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

    private static PlotLifecycleSubmission Reject(
        PlotLifecyclePreflight preflight,
        string reason,
        int maximumAmount = -1) =>
        PlotLifecycleSubmission.Reject(preflight, reason, maximumAmount);

    private static PlotLifecycleSubmission Verified(int before, int after) =>
        new(PlotLifecyclePreflight.Proceeded, PlotLifecycleNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
            "The requested plot-action quantity change is visible.", before, after);

    private static PlotLifecycleSubmission Fault(
        in PlotLifecycleAction action,
        PlotLifecyclePreflight preflight,
        PlotLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        string reason) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Plot " + stage + " failed on " + EntityIdentityFormatter.PlayerName(action.PlotId) +
            ": " + reason);

    private void BindLifecycle()
    {
        if (PlotLifecycleNativeBindings.TryCreate(
                out var bindings, out var reason, _resolveType, _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or
            TargetInvocationException or OverflowException;
}
