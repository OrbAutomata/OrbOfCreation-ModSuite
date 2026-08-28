using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for visible crafting-instance controls.</summary>
internal sealed class CraftingInstanceLifecycleGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private CraftingInstanceLifecycleNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal CraftingInstanceLifecycleGameAction(
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

    internal CraftingInstanceLifecycleSubmission Submit(
        in CraftingInstanceLifecycleAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(CraftingInstanceLifecyclePreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(CraftingInstanceLifecyclePreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Workshop > Crafting"));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(CraftingInstanceLifecyclePreflight.LifecycleReplaced,
                "The current game lifecycle could not be read: " +
                exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(CraftingInstanceLifecyclePreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            var resolution = _registry.Resolve(action.RecipeId, native.RecipeType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return Reject(CraftingInstanceLifecyclePreflight.IdentityUnavailable,
                    resolution.IsResolved ? GameActionAnswer.Replaced("recipe") : resolution.Reason);
            var recipe = resolution.Value!;
            if (!TryResolvePage(native, recipe, out var page, out var pageReason))
                return Reject(CraftingInstanceLifecyclePreflight.PageRelationAmbiguous, pageReason);
            var queue = native.PageQueue(page!);
            var automation = native.PageAutomation(page!);
            var admission = Admit(in action, native, page!, recipe, queue, automation);
            if (admission.Preflight != CraftingInstanceLifecyclePreflight.Proceeded)
                return admission;
            if (!_tryCaptureMutationPermit())
                return Reject(CraftingInstanceLifecyclePreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, page!, recipe, queue, automation);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(CraftingInstanceLifecyclePreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Workshop > Crafting"));
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

    private static CraftingInstanceLifecycleSubmission Admit(
        in CraftingInstanceLifecycleAction action,
        CraftingInstanceLifecycleNativeBindings native,
        object page,
        object recipe,
        object queue,
        object automation)
    {
        if (action.Kind == CraftingInstanceLifecycleActionKind.Automate)
        {
            if (!native.RecipeVisible(recipe))
                return Reject(CraftingInstanceLifecyclePreflight.NotVisible,
                    "This recipe is not visible yet.");
            var existing = native.QueueInstance(automation, recipe);
            if (existing is null && !native.QueueHasRoom(automation))
                return Reject(CraftingInstanceLifecyclePreflight.AutomationFull,
                    "The automated crafting list has no empty slot.");
            return AutomationAmount(native, page, recipe, automation, out _, out var reason)
                ? Proceed()
                : Reject(CraftingInstanceLifecyclePreflight.MultiBuyUnavailable, reason);
        }
        var source = action.Kind == CraftingInstanceLifecycleActionKind.CancelManual
            ? queue
            : automation;
        var instance = native.QueueInstance(source, recipe);
        if (!ValidInstance(native, instance, action.RecipeId,
                action.Kind == CraftingInstanceLifecycleActionKind.CancelAutomation))
            return Reject(CraftingInstanceLifecyclePreflight.InstanceUnavailable,
                action.Kind == CraftingInstanceLifecycleActionKind.CancelManual
                    ? "This recipe is not in the manual crafting queue."
                    : "This recipe is not in the automated crafting list.");
        if (action.Kind == CraftingInstanceLifecycleActionKind.CancelAutomation &&
            native.MultiBuy() <= 0)
            return Reject(CraftingInstanceLifecyclePreflight.MultiBuyUnavailable,
                "The game's multi-buy amount must be positive.");
        return Proceed();
    }

    private static CraftingInstanceLifecycleSubmission Execute(
        in CraftingInstanceLifecycleAction action,
        CraftingInstanceLifecycleNativeBindings native,
        object page,
        object recipe,
        object queue,
        object automation)
    {
        var source = action.Kind == CraftingInstanceLifecycleActionKind.CancelManual
            ? queue
            : automation;
        var instance = native.QueueInstance(source, recipe);
        var before = action.Kind == CraftingInstanceLifecycleActionKind.CancelManual ||
            instance is null ? 0 : native.AutomationQuantity(instance);
        var stage = CraftingInstanceLifecycleNativeStage.NativeCallback;
        try
        {
            if (action.Kind == CraftingInstanceLifecycleActionKind.Automate)
            {
                if (instance is null && !native.QueueHasRoom(automation))
                    return Reject(CraftingInstanceLifecyclePreflight.AutomationFull,
                        "The automated crafting list filled before the action ran.");
                if (!AutomationAmount(native, page, recipe, automation, out var amount,
                        out var amountReason))
                    return Reject(CraftingInstanceLifecyclePreflight.MultiBuyUnavailable,
                        amountReason);
                instance = native.Automate(automation, recipe, amount);
            }
            else if (action.Kind == CraftingInstanceLifecycleActionKind.CancelAutomation)
            {
                if (!ValidInstance(native, instance, action.RecipeId, expectedAuto: true))
                    return Reject(CraftingInstanceLifecyclePreflight.InstanceUnavailable,
                        "This recipe left the automated crafting list before cancellation.");
                var amount = native.MultiBuy();
                if (amount <= 0)
                    return Reject(CraftingInstanceLifecyclePreflight.MultiBuyUnavailable,
                        "The game's multi-buy amount must be positive.");
                // RemoveAutomation adds its amount; the game's own automation strip negates
                // the multi-buy before calling it (UICraftingInstanceList.OnClickInstance).
                native.RemoveAutomation(automation, instance!, -amount);
            }
            else if (action.Kind == CraftingInstanceLifecycleActionKind.CancelManual)
            {
                if (!ValidInstance(native, instance, action.RecipeId, expectedAuto: false))
                    return Reject(CraftingInstanceLifecyclePreflight.InstanceUnavailable,
                        "This recipe left the manual crafting queue before cancellation.");
                native.Cancel(instance!);
                native.QueueRemove(queue, instance!);
            }
            else
            {
                throw new InvalidOperationException("Unsupported crafting-instance control.");
            }

            stage = CraftingInstanceLifecycleNativeStage.Verification;
            return OutcomeObserved(in action, native, recipe, queue, automation, instance, before)
                ? Verified()
                : Fault(in action, CraftingInstanceLifecyclePreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    GameActionAnswer.ChangeNotSeen("Workshop > Crafting"),
                    SideEffect(in action, native, instance, before));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (OutcomeObserved(in action, native, recipe, queue, automation, instance, before))
                return Verified();
            return Fault(in action, CraftingInstanceLifecyclePreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("Workshop > Crafting"),
                SideEffect(in action, native, instance, before));
        }
    }

    /// <summary>
    /// A failing transition still leaves whatever the native call already wrote. Re-reading the
    /// automation quantity is what lets a refusal name the damage instead of hiding it.
    /// </summary>
    private static CraftingInstanceLifecycleSideEffect SideEffect(
        in CraftingInstanceLifecycleAction action,
        CraftingInstanceLifecycleNativeBindings native,
        object? instance,
        int before)
    {
        if (action.Kind == CraftingInstanceLifecycleActionKind.CancelManual || instance is null)
            return default;
        try
        {
            var after = native.AutomationQuantity(instance);
            return after == before
                ? default
                : CraftingInstanceLifecycleSideEffect.Automation(before, after);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return default;
        }
    }

    private static bool OutcomeObserved(
        in CraftingInstanceLifecycleAction action,
        CraftingInstanceLifecycleNativeBindings native,
        object recipe,
        object queue,
        object automation,
        object? instance,
        int before)
    {
        if (action.Kind == CraftingInstanceLifecycleActionKind.CancelManual)
            return instance is not null && !ContainsReference(native.QueueValues(queue), instance);
        if (instance is null) return false;
        if (action.Kind == CraftingInstanceLifecycleActionKind.Automate)
            return ValidInstance(native, native.QueueInstance(automation, recipe),
                action.RecipeId, expectedAuto: true) &&
                native.AutomationQuantity(instance) > before;
        // A cancel that empties the entry makes the game drop it from the automation list, so
        // absence is the same observation as a smaller quantity.
        return !ContainsReference(native.QueueValues(automation), instance) ||
            native.AutomationQuantity(instance) < before;
    }

    private static bool AutomationAmount(
        CraftingInstanceLifecycleNativeBindings native,
        object page,
        object recipe,
        object automation,
        out int amount,
        out string reason)
    {
        var multiBuy = native.MultiBuy();
        if (multiBuy <= 0)
        {
            amount = 0;
            reason = "The game's multi-buy amount must be positive.";
            return false;
        }
        var desired = native.CalcAutomated(
            native.RecipeMultiBuyQuantity(recipe, native.QueueQuantity(automation, recipe)));
        var remaining = Math.Max(desired - native.CurrentAutomation(page, recipe), 1);
        amount = Math.Min(multiBuy, remaining);
        reason = string.Empty;
        return amount > 0;
    }

    private static bool TryResolvePage(
        CraftingInstanceLifecycleNativeBindings native,
        object recipe,
        out object? page,
        out string reason)
    {
        page = null;
        var pages = native.Pages();
        for (var index = 0; index < pages.Length; index++)
        {
            var candidate = pages.GetValue(index);
            if (candidate is null || candidate.GetType() != native.PageType) continue;
            var recipes = native.PageRecipes(candidate);
            var contains = false;
            for (var recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
                if (ReferenceEquals(recipes[recipeIndex], recipe))
                {
                    contains = true;
                    break;
                }
            if (!contains) continue;
            if (page is not null)
            {
                reason = "This recipe appears on more than one crafting page.";
                return false;
            }
            page = candidate;
        }
        if (page is null)
        {
            reason = "This recipe is not on a loaded crafting page.";
            return false;
        }
        if (!ReferenceEquals(native.PageMainType(page), native.RecipeMainType(recipe)))
        {
            reason = "This recipe does not match its loaded crafting page.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool ValidInstance(
        CraftingInstanceLifecycleNativeBindings native,
        object? instance,
        Guid recipeId,
        bool expectedAuto) =>
        instance is not null && instance.GetType() == native.InstanceType &&
        native.InstanceRecipe(instance) == recipeId && native.IsAuto(instance) == expectedAuto;

    private static bool ContainsReference(IList values, object instance)
    {
        for (var index = 0; index < values.Count; index++)
            if (ReferenceEquals(values[index], instance)) return true;
        return false;
    }

    private static CraftingInstanceLifecycleSubmission Proceed() =>
        new(CraftingInstanceLifecyclePreflight.Proceeded,
            CraftingInstanceLifecycleNativeStage.None,
            NativeMutationOutcome.BeforeCaptureFailed, default, string.Empty);

    private static CraftingInstanceLifecycleSubmission Reject(
        CraftingInstanceLifecyclePreflight preflight,
        string reason) => CraftingInstanceLifecycleSubmission.Reject(preflight, reason);

    private static CraftingInstanceLifecycleSubmission Verified() =>
        new(CraftingInstanceLifecyclePreflight.Proceeded,
            CraftingInstanceLifecycleNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            "The requested crafting-instance transition is visible.");

    private static CraftingInstanceLifecycleSubmission Fault(
        in CraftingInstanceLifecycleAction action,
        CraftingInstanceLifecyclePreflight preflight,
        CraftingInstanceLifecycleNativeStage stage,
        NativeMutationOutcome outcome,
        string reason,
        CraftingInstanceLifecycleSideEffect sideEffect) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Crafting " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.RecipeId) + ": " + reason,
            sideEffect);

    private void BindLifecycle()
    {
        if (CraftingInstanceLifecycleNativeBindings.TryCreate(
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
