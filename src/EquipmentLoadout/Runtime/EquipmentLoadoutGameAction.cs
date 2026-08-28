using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped, Unity-main-thread equipment loadout boundary.</summary>
internal sealed class EquipmentLoadoutGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private EquipmentLoadoutNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal EquipmentLoadoutGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(_readLifecycleEpoch, identity.Read, identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal bool TryValidateStoredEntry(
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
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            reason = "Equipment loadout validation must run on the Unity main thread.";
            return false;
        }
        if (_bindings is not { } native)
        {
            reason = _bindingFailure;
            return false;
        }
        if (candidate is null || candidate.GetType() != native.EquipmentType)
        {
            reason = "A saved artifact has the wrong native type.";
            return false;
        }
        var id = RuntimeIdentityRegistryBinding.Shared.ReadStableUuid(candidate);
        if (id is null || id == Guid.Empty)
        {
            reason = "A saved artifact has no stable identity.";
            return false;
        }
        var resolution = _registry.Resolve(id.Value, native.EquipmentType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            !ReferenceEquals(resolution.Value, candidate))
        {
            reason = EntityIdentityFormatter.PlayerName(id.Value) +
                " is not the current artifact instance.";
            return false;
        }
        if (!native.IsCreated(candidate))
        {
            reason = EntityIdentityFormatter.PlayerName(id.Value) + " has not been created.";
            return false;
        }
        var maximum = Math.Max(native.MaximumStacks(candidate), 0);
        if (quantity <= 0 || quantity > maximum)
        {
            reason = EntityIdentityFormatter.PlayerName(id.Value) + " stores " + quantity +
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
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Workshop > Artifacts"));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            var resolution = _registry.Resolve(action.TargetId, native.EquipmentType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.IdentityUnavailable,
                    resolution.IsResolved ? GameActionAnswer.Replaced("artifact") : resolution.Reason);
            var target = resolution.Value!;
            if (!native.IsCreated(target))
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.NotCreated,
                    EntityIdentityFormatter.PlayerName(action.TargetId) + " has not been created.");
            var manager = native.Manager();
            if (manager is null || manager.GetType() != native.ManagerType)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.ContractUnavailable,
                    "The artifact loadout is not loaded right now.");
            var list = native.EquippedList(manager);
            var kind = native.ReadEquipmentType(target);
            var cost = native.UsageCost(target);
            if (list is null || kind is null || cost is null)
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.ContractUnavailable,
                    "The game did not answer whether this is allowed right now; open " +
                    "Workshop > Artifacts and read it again.");
            var before = Capture(native, list, target, kind, cost);
            if (action.Kind == EquipmentLoadoutActionKind.Equip)
            {
                if (before.EquippedStacks >= before.MaximumStacks)
                    return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.AlreadyInRequestedState,
                        "The artifact is already equipped at its maximum stack level.");
                if (before.EquippedStacks == 0 && before.UsedSlots >= before.MaximumSlots)
                    return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.LoadoutFull,
                        "The equipment loadout has no open global slot.");
                if (before.EquippedStacks == 0 && before.TypeUsedSlots >= before.TypeMaximumSlots)
                    return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.EquipmentTypeFull,
                        "The artifact's equipment type has no open slot.");
                if (!before.UsageAffordable || before.MaximumAffordableStacks <= 0)
                    return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.UsageUnaffordable,
                        "The native usage-cost evaluators refused this equipment increase.");
                var maximumAdditional = Math.Min(
                    Math.Max(before.MaximumStacks - before.EquippedStacks, 0),
                    before.MaximumAffordableStacks);
                if (action.Amount <= 0 || action.Amount > maximumAdditional)
                    return EquipmentLoadoutSubmission.Reject(
                        EquipmentLoadoutPreflight.AmountUnavailable,
                        "This artifact can equip at most " + maximumAdditional +
                        " more stacks with the current capacity and resources.",
                        maximumAdditional);
            }
            else
            {
                if (before.EquippedStacks == 0)
                    return EquipmentLoadoutSubmission.Reject(
                        EquipmentLoadoutPreflight.AlreadyInRequestedState,
                        "The artifact is not equipped.");
                if (action.Amount <= 0 || action.Amount > before.EquippedStacks)
                    return EquipmentLoadoutSubmission.Reject(
                        EquipmentLoadoutPreflight.AmountUnavailable,
                        "This artifact has only " + before.EquippedStacks +
                        " equipped stacks to remove.");
            }
            if (!_tryCaptureMutationPermit())
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            if (!NativeMultiBuyScope.TryEnter(action.Amount, out var scope, out var scopeReason))
                return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.MultiBuyUnavailable,
                    "The requested artifact amount could not be applied: " + scopeReason);
            using (scope)
                return Execute(in action, native, manager, list, target,
                    before.EquippedStacks, action.Amount);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return EquipmentLoadoutSubmission.Reject(EquipmentLoadoutPreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Workshop > Artifacts"));
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

    private EquipmentLoadoutSubmission Execute(in EquipmentLoadoutAction action,
        EquipmentLoadoutNativeBindings native, object manager, object list, object target,
        int beforeStacks, int requested)
    {
        var stage = EquipmentLoadoutNativeStage.NativeCallback;
        try
        {
            if (action.Kind == EquipmentLoadoutActionKind.Equip) native.Equip(manager, target);
            else native.Unequip(manager, target);
            stage = EquipmentLoadoutNativeStage.Verification;
            var afterStacks = Math.Max(native.Stacks(list, target), 0);
            return OutcomeMatches(action.Kind, beforeStacks, afterStacks, requested)
                ? Verified()
                : Fault(in action, EquipmentLoadoutPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    "Expected equipped stacks " + ExpectedStacks(action.Kind, beforeStacks, requested) +
                    ", observed " + afterStacks + ".");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            var observed = ReadStacksBestEffort(native, list, target);
            if (observed >= 0 && OutcomeMatches(action.Kind, beforeStacks, observed, requested))
                return Verified();
            return Fault(in action, EquipmentLoadoutPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("Workshop > Artifacts"));
        }
    }

    private static EquipmentLoadoutState Capture(EquipmentLoadoutNativeBindings native,
        object list, object target, object kind, object cost) =>
        new(Math.Max(native.Stacks(list, target), 0), Math.Max(native.MaximumStacks(target), 0),
            native.Values(list)?.Count ?? throw new InvalidOperationException("Equipment list values were unavailable."),
            Math.Max(native.MaximumSlots(list), 0), Math.Max(native.TypeCount(list, kind), 0),
            Math.Max(native.TypeMaximum(kind), 0), native.HasEnough(cost),
            Math.Max(BigDouble.Floor(native.MaximumTimes(cost)).ToInt(), 0));

    private static int ExpectedStacks(EquipmentLoadoutActionKind kind, int before, int requested) =>
        kind == EquipmentLoadoutActionKind.Equip
            ? checked(before + requested)
            : checked(before - requested);

    private static bool OutcomeMatches(EquipmentLoadoutActionKind kind,
        int before, int after, int requested) =>
        after == ExpectedStacks(kind, before, requested);

    private static int ReadStacksBestEffort(
        EquipmentLoadoutNativeBindings native, object list, object target)
    {
        try { return Math.Max(native.Stacks(list, target), 0); }
        catch (Exception exception) when (IsExpected(exception)) { return -1; }
    }

    private static EquipmentLoadoutSubmission Verified() =>
        new(EquipmentLoadoutPreflight.Proceeded, EquipmentLoadoutNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
            "The requested artifact stack is equipped.");

    private static EquipmentLoadoutSubmission Fault(in EquipmentLoadoutAction action,
        EquipmentLoadoutPreflight preflight, EquipmentLoadoutNativeStage stage,
        NativeMutationOutcome outcome, string reason)
    {
        var exactReason = "Equipment loadout " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.TargetId) + ": " + reason;
        return new EquipmentLoadoutSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(1, 1, 0), exactReason);
    }

    private void BindLifecycle()
    {
        if (EquipmentLoadoutNativeBindings.TryCreate(out var bindings, out var reason, _resolveType, _includeContract))
        { _bindings = bindings; _bindingFailure = string.Empty; return; }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or TargetInvocationException or OverflowException;
}
