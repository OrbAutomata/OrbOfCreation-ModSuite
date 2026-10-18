using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for every player-visible plot/action pair.</summary>
internal sealed class PlotLifecycleGameAction : NativeGameAction<PlotLifecycleNativeBindings>
{
    private const string Screen = "World > Agromancy";

    private static readonly GameActionCodes Codes = new(
        PlotLifecycleActionResultCodes.WrongThread,
        PlotLifecycleActionResultCodes.ContractUnavailable,
        PlotLifecycleActionResultCodes.MutationPermitUnavailable,
        PlotLifecycleActionResultCodes.PostCommitFault,
        PlotLifecycleActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal PlotLifecycleGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Plot", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out PlotLifecycleNativeBindings? bindings, out string reason) =>
                PlotLifecycleNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal PlotLifecycleSubmission Submit(in PlotLifecycleAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new PlotLifecycleSubmission(outcome),
            native => Press(in request, native));
    }

    private PlotLifecycleSubmission Press(in PlotLifecycleAction action, PlotLifecycleNativeBindings native)
    {
        if (!_registry.TryResolveCurrent(action.PlotId, native.PlotType, out var plot, out var reason) ||
            !_registry.TryResolveCurrent(action.ActionId, native.ActionType, out var actionObject, out reason))
            return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.IdentityUnavailable, reason);
        if (!_registry.TryResolveCurrent(PlotLifecycleNativeBindings.ActiveActionsId,
                native.ListType, out var list, out reason))
            return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.ContractUnavailable, reason);
        if (!native.PlotVisible(plot))
            return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.PlotUnavailable,
                EntityIdentityFormatter.PlayerName(action.PlotId) + " is not visible yet.");
        var prototype = FindPrototype(native, plot, actionObject);
        if (prototype is null)
            return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.ActionUnavailable,
                EntityIdentityFormatter.PlayerName(action.ActionId) + " is not offered for " +
                EntityIdentityFormatter.PlayerName(action.PlotId) + ".");
        var current = native.FindInstance(list, prototype);
        if (current is not null && current.GetType() != native.InstanceType)
            return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.ContractUnavailable,
                "The active plot action has an unexpected native type.");
        var before = Quantity(native, current);
        var admission = AdmitQuantity(in action, native, prototype, list, current, before);
        if (admission.HasValue) return admission.Value;
        if (!TryPermit(out var refusal)) return new PlotLifecycleSubmission(refusal);

        var add = action.Kind == PlotLifecycleActionKind.Add;
        var amount = action.Amount;
        var expected = add ? checked(before + amount) : checked(before - amount);
        var outcome = Execute(EntityIdentityFormatter.PlayerName(action.PlotId),
            "The requested plot-action quantity change is visible.", before,
            () => Quantity(native, native.FindInstance(list, prototype)),
            () =>
            {
                if (add)
                    native.AddInstance(list, prototype, amount);
                else if (native.InstanceAtMinimum(current!))
                    native.InstanceCancel(current!);
                else
                    native.RemoveInstance(list, current!, amount);
            },
            (_, after) => after == expected);
        return outcome.Verified
            ? new PlotLifecycleSubmission(outcome, before, expected)
            : new PlotLifecycleSubmission(outcome);
    }

    private static PlotLifecycleSubmission? AdmitQuantity(
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
                return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.NotActive,
                    EntityIdentityFormatter.PlayerName(action.ActionId) + " is not active on " +
                    EntityIdentityFormatter.PlayerName(action.PlotId) + ".");
            if (action.Amount > before)
                return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.QuantityUnavailable,
                    EntityIdentityFormatter.PlayerName(action.ActionId) + " has only " + before +
                    " active " + (before == 1 ? "instance" : "instances") + " on " +
                    EntityIdentityFormatter.PlayerName(action.PlotId) + ".");
            return null;
        }

        if (!native.InstanceVisible(prototype))
            return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.ActionUnavailable,
                EntityIdentityFormatter.PlayerName(action.ActionId) + " is not available for " +
                EntityIdentityFormatter.PlayerName(action.PlotId) + " yet.");
        if (!native.InstanceAffordable(prototype))
            return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.QuantityUnavailable,
                EntityIdentityFormatter.PlayerName(action.PlotId) +
                " does not have enough remaining quantity for that action.");
        var maximumRemaining = native.InstanceMaximumRemaining(prototype);
        var maximum = native.InstanceMaximum(prototype);
        if (action.Amount > maximumRemaining || before + action.Amount > maximum)
        {
            var remaining = Math.Max(Math.Min(maximumRemaining, maximum - before), 0);
            return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.QuantityUnavailable,
                "The plot currently allows at most " + remaining +
                " more active instances of " + EntityIdentityFormatter.PlayerName(action.ActionId) + ".",
                remaining);
        }
        if ((current is null || before <= 0) && !native.ListHasRoom(list))
            return PlotLifecycleSubmission.Refused(PlotLifecycleActionResultCodes.ActionListFull,
                "The active plot-action list has no empty slot.");
        return null;
    }

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
}
