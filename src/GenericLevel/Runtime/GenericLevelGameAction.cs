using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>One lifecycle-scoped boundary for the game's ordinary ILevelable list controls.</summary>
internal sealed class GenericLevelGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private GenericLevelNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal GenericLevelGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ??
            throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ??
            throw new ArgumentNullException(nameof(readOwnershipFailure));
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

    internal GenericLevelSubmission Submit(in GenericLevelAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(GenericLevelPreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(GenericLevelPreflight.ContractUnavailable, _bindingFailure);

        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(GenericLevelPreflight.LifecycleReplaced,
                "The current game lifecycle could not be read: " +
                exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(GenericLevelPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());
        if (!native.TryTarget(action.NativeType, out var targetBinding))
            return Reject(GenericLevelPreflight.WrongDomain,
                action.NativeType is "ResearchSO" or "SpellRecipeSO"
                    ? "Use the research or spell-level control for this entity."
                    : "This entity does not have an ordinary level-list control.");

        try
        {
            var resolution = _registry.Resolve(action.TargetId, targetBinding.TargetType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return Reject(GenericLevelPreflight.IdentityUnavailable,
                    resolution.IsResolved
                        ? GameActionAnswer.Replaced("level target")
                        : resolution.Reason);
            var target = resolution.Value!;
            var admission = Admit(in action, native, targetBinding, target);
            if (admission.Preflight != GenericLevelPreflight.Proceeded) return admission;
            if (!_tryCaptureMutationPermit())
                return Reject(GenericLevelPreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, targetBinding, target);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(GenericLevelPreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("the screen this is levelled on"));
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

    private static GenericLevelSubmission Admit(
        in GenericLevelAction action,
        GenericLevelNativeBindings native,
        GenericLevelTargetBinding binding,
        object target)
    {
        if (binding.IsHidden is not null && binding.IsHidden(target))
            return Reject(GenericLevelPreflight.Hidden,
                "This level target is hidden from the player.");
        if (binding.IsDiscovered is not null && !binding.IsDiscovered(target))
            return Reject(GenericLevelPreflight.Undiscovered,
                "This level target has not been discovered.");
        if (!binding.IsVisible(target))
            return Reject(GenericLevelPreflight.Hidden,
                "This level target is not visible on its level screen.");
        if (!binding.IsAvailable(target))
            return Reject(GenericLevelPreflight.Unavailable,
                "This level target is not available on its level screen.");

        if (action.Kind == GenericLevelActionKind.Purchase)
        {
            if (!binding.CanLevel(target))
                return Reject(GenericLevelPreflight.CannotLevel,
                    "This entity cannot gain another paid level.");
            var cost = binding.GetLevelCost(target);
            if (cost is null)
                return Reject(GenericLevelPreflight.ContractUnavailable,
                    "The next level price is unavailable.");
            return native.HasEnough(cost)
                ? Proceed()
                : Unaffordable(native, cost, "level");
        }
        if (action.Kind != GenericLevelActionKind.Bonus)
            return Reject(GenericLevelPreflight.WrongDomain,
                "That level control is not available.");
        if (!binding.SupportsBonus)
            return Reject(GenericLevelPreflight.BonusUnavailable,
                "This entity has no bonus-level button.");
        var bonusCost = binding.GetBonusCost!(target);
        if (bonusCost is null)
            return Reject(GenericLevelPreflight.ContractUnavailable,
                "The next bonus-level price is unavailable.");
        if (!native.ResourcesVisible(bonusCost))
            return Reject(GenericLevelPreflight.ResourcesHidden,
                "The resources required for a bonus level are not visible yet.");
        return native.HasEnough(bonusCost)
            ? Proceed()
            : Unaffordable(native, bonusCost, "bonus level");
    }

    private static GenericLevelSubmission Execute(
        in GenericLevelAction action,
        GenericLevelNativeBindings native,
        GenericLevelTargetBinding binding,
        object target)
    {
        var before = action.Kind == GenericLevelActionKind.Bonus
            ? binding.GetBonusLevels!(target)
            : binding.GetLevel(target);
        var stage = GenericLevelNativeStage.NativeCallback;
        try
        {
            for (var index = 0; index < action.Amount; index++)
            {
                if (action.Kind == GenericLevelActionKind.Purchase)
                {
                    if (!binding.CanLevel(target)) break;
                    var cost = binding.GetLevelCost(target) ??
                        throw new InvalidOperationException("GetLevelCost returned null before payment");
                    if (!native.HasEnough(cost)) break;
                    binding.PurchaseLevel(target);
                }
                else
                {
                    var cost = binding.GetBonusCost!(target) ??
                        throw new InvalidOperationException("GetFreeLevelCost returned null before mutation");
                    if (!native.ResourcesVisible(cost) || !native.HasEnough(cost)) break;
                    binding.PurchaseBonus!(target);
                }
            }

            stage = GenericLevelNativeStage.Verification;
            return Current(in action, binding, target) > before
                ? Verified()
                : Fault(in action, GenericLevelPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    "The requested level did not increase.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (Current(in action, binding, target) > before) return Verified();
            return Fault(in action, GenericLevelPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("the screen this is levelled on"));
        }
    }

    private static int Current(
        in GenericLevelAction action,
        GenericLevelTargetBinding binding,
        object target) => action.Kind == GenericLevelActionKind.Bonus
            ? binding.GetBonusLevels!(target)
            : binding.GetLevel(target);

    private static GenericLevelSubmission Unaffordable(
        GenericLevelNativeBindings native,
        object cost,
        string label)
    {
        var rows = native.CostEntries(cost);
        if (rows is null)
            return Reject(GenericLevelPreflight.ContractUnavailable,
                "The " + label + " price has no readable resource rows.");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row is null) continue;
            var resource = native.CostResource(row);
            if (resource is null) continue;
            var amount = native.CostValue(row);
            if (!native.HasResourceAmount(resource, amount))
                return Reject(GenericLevelPreflight.Unaffordable,
                    EntityIdentityFormatter.PlayerName(native.ResourceGuid(resource)) +
                    " is short for this " + label + ".");
        }
        return Reject(GenericLevelPreflight.ContractUnavailable,
            "The game refused this " + label +
            " price without identifying a short resource.");
    }

    private static GenericLevelSubmission Proceed() =>
        new(GenericLevelPreflight.Proceeded, GenericLevelNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, string.Empty);

    private static GenericLevelSubmission Reject(GenericLevelPreflight preflight, string reason) =>
        GenericLevelSubmission.Reject(preflight, reason);

    private static GenericLevelSubmission Verified() =>
        new(GenericLevelPreflight.Proceeded,
            GenericLevelNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            "The requested level increase is visible.");

    private static GenericLevelSubmission Fault(
        in GenericLevelAction action,
        GenericLevelPreflight preflight,
        GenericLevelNativeStage stage,
        NativeMutationOutcome outcome,
        string reason) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Level " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.TargetId) + ": " + reason);

    private void BindLifecycle()
    {
        if (GenericLevelNativeBindings.TryCreate(
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
