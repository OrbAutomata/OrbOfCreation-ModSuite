using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>One lifecycle-scoped boundary for the game's ordinary ILevelable list controls.</summary>
internal sealed class GenericLevelGameAction : NativeGameAction<GenericLevelNativeBindings>
{
    private const string Screen = "the screen this is levelled on";

    private static readonly GameActionCodes Codes = new(
        GenericLevelActionResultCodes.WrongThread,
        GenericLevelActionResultCodes.ContractUnavailable,
        GenericLevelActionResultCodes.MutationPermitUnavailable,
        GenericLevelActionResultCodes.PostCommitFault,
        GenericLevelActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal GenericLevelGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Level", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out GenericLevelNativeBindings? bindings, out string reason) =>
                GenericLevelNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal GenericLevelSubmission Submit(in GenericLevelAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new GenericLevelSubmission(outcome),
            native => Press(in request, native));
    }

    private GenericLevelSubmission Press(in GenericLevelAction action, GenericLevelNativeBindings native)
    {
        if (!native.TryTarget(action.NativeType, out var binding))
            return Reject(GenericLevelActionResultCodes.WrongDomain,
                action.NativeType is "ResearchSO" or "SpellRecipeSO"
                    ? "Use the research or spell-level control for this entity."
                    : "This entity does not have an ordinary level-list control.");
        var resolution = _registry.Resolve(action.TargetId, binding.TargetType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            return Reject(GenericLevelActionResultCodes.IdentityUnavailable,
                resolution.IsResolved ? GameActionAnswer.Replaced("level target") : resolution.Reason);
        var target = resolution.Value!;
        if (Refusal(in action, native, binding, target) is { } refusal) return new(refusal);
        if (!TryPermit(out var permitRefusal)) return new(permitRefusal);

        var kind = action.Kind;
        var amount = action.Amount;
        var tally = new GenericLevelChargeTally();
        var outcome = Execute(EntityIdentityFormatter.PlayerName(action.TargetId),
            "The requested level increase is visible.",
            Current(kind, binding, target),
            () => Current(kind, binding, target),
            () =>
            {
                for (var index = 0; index < amount; index++)
                {
                    if (!TryPayRung(kind, native, binding, target, ref tally)) break;
                }
            },
            static (before, after) => after > before);
        return outcome.Verified ? new(outcome, tally.Levels, tally.Rows()) : new(outcome);
    }

    private static NativeActionOutcome? Refusal(
        in GenericLevelAction action,
        GenericLevelNativeBindings native,
        GenericLevelTargetBinding binding,
        object target)
    {
        if (binding.IsHidden is not null && binding.IsHidden(target))
            return NativeActionOutcome.Reject(GenericLevelActionResultCodes.Hidden,
                "This level target is hidden from the player.");
        if (binding.IsDiscovered is not null && !binding.IsDiscovered(target))
            return NativeActionOutcome.Reject(GenericLevelActionResultCodes.Undiscovered,
                "This level target has not been discovered.");
        if (!binding.IsVisible(target))
            return NativeActionOutcome.Reject(GenericLevelActionResultCodes.Hidden,
                "This level target is not visible on its level screen.");
        if (!binding.IsAvailable(target))
            return NativeActionOutcome.Reject(GenericLevelActionResultCodes.Unavailable,
                "This level target is not available on its level screen.");

        if (action.Kind == GenericLevelActionKind.Purchase)
        {
            if (!binding.CanLevel(target))
                return NativeActionOutcome.Reject(GenericLevelActionResultCodes.CannotLevel,
                    "This entity cannot gain another paid level.");
            var cost = binding.GetLevelCost(target);
            if (cost is null)
                return NativeActionOutcome.Reject(GenericLevelActionResultCodes.ContractUnavailable,
                    "The next level price is unavailable.");
            return native.HasEnough(cost) ? null : Unaffordable(native, cost, "level");
        }
        if (action.Kind != GenericLevelActionKind.Bonus)
            return NativeActionOutcome.Reject(GenericLevelActionResultCodes.WrongDomain,
                "That level control is not available.");
        if (!binding.SupportsBonus)
            return NativeActionOutcome.Reject(GenericLevelActionResultCodes.BonusUnavailable,
                "This entity has no bonus-level button.");
        var bonusCost = binding.GetBonusCost!(target);
        if (bonusCost is null)
            return NativeActionOutcome.Reject(GenericLevelActionResultCodes.ContractUnavailable,
                "The next bonus-level price is unavailable.");
        if (!native.ResourcesVisible(bonusCost))
            return NativeActionOutcome.Reject(GenericLevelActionResultCodes.ResourcesHidden,
                "The resources required for a bonus level are not visible yet.");
        return native.HasEnough(bonusCost) ? null : Unaffordable(native, bonusCost, "bonus level");
    }

    /// <summary>
    /// One rung of the ladder, priced and paid as the game's own button does. A rung the game
    /// stops offering ends the ladder; the levels already bought are the press's result.
    /// </summary>
    private static bool TryPayRung(
        GenericLevelActionKind kind,
        GenericLevelNativeBindings native,
        GenericLevelTargetBinding binding,
        object target,
        ref GenericLevelChargeTally tally)
    {
        if (kind == GenericLevelActionKind.Purchase)
        {
            if (!binding.CanLevel(target)) return false;
            var cost = binding.GetLevelCost(target) ??
                throw new InvalidOperationException("GetLevelCost returned null before payment");
            if (!native.HasEnough(cost)) return false;
            tally.Take(native, cost);
            binding.PurchaseLevel(target);
            return true;
        }
        var bonusCost = binding.GetBonusCost!(target) ??
            throw new InvalidOperationException("GetFreeLevelCost returned null before mutation");
        if (!native.ResourcesVisible(bonusCost) || !native.HasEnough(bonusCost)) return false;
        tally.Take(native, bonusCost);
        binding.PurchaseBonus!(target);
        return true;
    }

    private static int Current(
        GenericLevelActionKind kind,
        GenericLevelTargetBinding binding,
        object target) => kind == GenericLevelActionKind.Bonus
            ? binding.GetBonusLevels!(target)
            : binding.GetLevel(target);

    private static NativeActionOutcome Unaffordable(
        GenericLevelNativeBindings native,
        object cost,
        string label) =>
        NativeCostShortfall.Refuse(native, cost, label,
            GenericLevelActionResultCodes.Unaffordable, GenericLevelActionResultCodes.ContractUnavailable);

    private static GenericLevelSubmission Reject(ServiceActionResultCode code, string reason) =>
        new(NativeActionOutcome.Reject(code, reason));
}
