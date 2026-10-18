using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped, Unity-main-thread equipment loadout boundary.</summary>
internal sealed class EquipmentLoadoutGameAction : NativeGameAction<EquipmentLoadoutNativeBindings>
{
    private const string Screen = "Workshop > Artifacts";

    private static readonly GameActionCodes Codes = new(
        EquipmentLoadoutActionResultCodes.WrongThread,
        EquipmentLoadoutActionResultCodes.ContractUnavailable,
        EquipmentLoadoutActionResultCodes.MutationPermitUnavailable,
        EquipmentLoadoutActionResultCodes.PostCommitFault,
        EquipmentLoadoutActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal EquipmentLoadoutGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Equipment loadout", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out EquipmentLoadoutNativeBindings? bindings, out string reason) =>
                EquipmentLoadoutNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal bool TryValidateStoredEntry(
        long lifecycleEpoch,
        object candidate,
        int quantity,
        out object? equipmentType,
        out int typeMaximum,
        out object? usageCost,
        out string reason)
    {
        equipmentType = null;
        typeMaximum = 0;
        usageCost = null;
        if (!TryEnter(lifecycleEpoch, out var native, out var refusal))
        {
            reason = refusal.Reason;
            return false;
        }
        if (candidate is null || candidate.GetType() != native.EquipmentType)
        {
            reason = "A saved artifact has the wrong native type.";
            return false;
        }
        var id = RuntimeIdentityRegistryBinding.Shared.ReadStableUuid(candidate);
        if (id == Guid.Empty)
        {
            reason = "A saved artifact has no stable identity.";
            return false;
        }
        var resolution = _registry.Resolve(id, native.EquipmentType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            !ReferenceEquals(resolution.Value, candidate))
        {
            reason = EntityIdentityFormatter.PlayerName(id) +
                " is not the current artifact instance.";
            return false;
        }
        if (!native.IsCreated(candidate))
        {
            reason = EntityIdentityFormatter.PlayerName(id) + " has not been created.";
            return false;
        }
        var maximum = Math.Max(native.MaximumStacks(candidate), 0);
        if (quantity <= 0 || quantity > maximum)
        {
            reason = EntityIdentityFormatter.PlayerName(id) + " stores " + quantity +
                " stacks, but the live maximum is " + maximum + ".";
            return false;
        }
        equipmentType = native.ReadEquipmentType(candidate);
        usageCost = native.UsageCost(candidate);
        if (equipmentType is null || usageCost is null)
        {
            reason = "The saved artifact's type or usage cost is unavailable.";
            return false;
        }
        typeMaximum = Math.Max(native.TypeMaximum(equipmentType), 0);
        reason = string.Empty;
        return true;
    }

    internal EquipmentLoadoutSubmission Submit(in EquipmentLoadoutAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new EquipmentLoadoutSubmission(outcome),
            native => Press(in request, native));
    }

    private EquipmentLoadoutSubmission Press(in EquipmentLoadoutAction action,
        EquipmentLoadoutNativeBindings native)
    {
        var resolution = _registry.Resolve(action.TargetId, native.EquipmentType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutActionResultCodes.IdentityUnavailable,
                resolution.IsResolved ? GameActionAnswer.Replaced("artifact") : resolution.Reason);
        var target = resolution.Value!;
        var name = EntityIdentityFormatter.PlayerName(action.TargetId);
        if (!native.IsCreated(target))
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutActionResultCodes.NotCreated,
                name + " has not been created.");
        var manager = native.Manager();
        if (manager is null || manager.GetType() != native.ManagerType)
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutActionResultCodes.ContractUnavailable,
                "The artifact loadout is not loaded right now.");
        var list = native.EquippedList(manager);
        var kind = native.ReadEquipmentType(target);
        var cost = native.UsageCost(target);
        if (list is null || kind is null || cost is null)
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutActionResultCodes.ContractUnavailable,
                "The game did not answer whether this is allowed right now; open " +
                "Workshop > Artifacts and read it again.");
        var before = Capture(native, list, target, kind, cost);
        var admission = AdmitAmount(in action, in before);
        if (admission.HasValue) return admission.Value;
        if (!TryPermit(out var refusal)) return new EquipmentLoadoutSubmission(refusal);
        if (!NativeMultiBuyScope.TryEnter(action.Amount, out var scope, out var scopeReason))
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutActionResultCodes.MultiBuyUnavailable,
                "The requested artifact amount could not be applied: " + scopeReason);

        var equip = action.Kind == EquipmentLoadoutActionKind.Equip;
        var expected = equip
            ? checked(before.EquippedStacks + action.Amount)
            : checked(before.EquippedStacks - action.Amount);
        return new EquipmentLoadoutSubmission(Execute(name,
            "The requested artifact stack is equipped.", before.EquippedStacks,
            () => Math.Max(native.Stacks(list, target), 0),
            () =>
            {
                using (scope)
                {
                    if (equip) native.Equip(manager, target);
                    else native.Unequip(manager, target);
                }
            },
            (_, after) => after == expected));
    }

    private static EquipmentLoadoutSubmission? AdmitAmount(in EquipmentLoadoutAction action,
        in EquipmentLoadoutState before)
    {
        if (action.Kind == EquipmentLoadoutActionKind.Equip)
        {
            if (before.EquippedStacks >= before.MaximumStacks)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutActionResultCodes.AlreadyInRequestedState,
                    "The artifact is already equipped at its maximum stack level.");
            if (before.EquippedStacks == 0 && before.UsedSlots >= before.MaximumSlots)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutActionResultCodes.LoadoutFull,
                    "The equipment loadout has no open global slot.");
            if (before.EquippedStacks == 0 && before.TypeUsedSlots >= before.TypeMaximumSlots)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutActionResultCodes.EquipmentTypeFull,
                    "The artifact's equipment type has no open slot.");
            if (!before.UsageAffordable || before.MaximumAffordableStacks <= 0)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutActionResultCodes.UsageUnaffordable,
                    "The native usage-cost evaluators refused this equipment increase.");
            var maximumAdditional = Math.Min(
                Math.Max(before.MaximumStacks - before.EquippedStacks, 0),
                before.MaximumAffordableStacks);
            if (action.Amount <= 0 || action.Amount > maximumAdditional)
                return EquipmentLoadoutSubmission.Reject(
                    EquipmentLoadoutActionResultCodes.AmountUnavailable,
                    "This artifact can equip at most " + maximumAdditional +
                    " more stacks with the current capacity and resources.",
                    maximumAdditional);
            return null;
        }
        if (before.EquippedStacks == 0)
            return EquipmentLoadoutSubmission.Reject(
                EquipmentLoadoutActionResultCodes.AlreadyInRequestedState,
                "The artifact is not equipped.");
        if (action.Amount <= 0 || action.Amount > before.EquippedStacks)
            return EquipmentLoadoutSubmission.Reject(
                EquipmentLoadoutActionResultCodes.AmountUnavailable,
                "This artifact has only " + before.EquippedStacks +
                " equipped stacks to remove.");
        return null;
    }

    private static EquipmentLoadoutState Capture(EquipmentLoadoutNativeBindings native,
        object list, object target, object kind, object cost) =>
        new(Math.Max(native.Stacks(list, target), 0), Math.Max(native.MaximumStacks(target), 0),
            native.Values(list)?.Count ?? throw new InvalidOperationException("Equipment list values were unavailable."),
            Math.Max(native.MaximumSlots(list), 0), Math.Max(native.TypeCount(list, kind), 0),
            Math.Max(native.TypeMaximum(kind), 0), native.HasEnough(cost),
            Math.Max(BigDouble.Floor(native.MaximumTimes(cost)).ToInt(), 0));
}
