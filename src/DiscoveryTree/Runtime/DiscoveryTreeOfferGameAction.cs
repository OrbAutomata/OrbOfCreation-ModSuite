using System;
using System.Collections;
using System.Threading;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Lifecycle-scoped re-drive of the native Discovery Tree offer pipeline. Admission is re-read on
/// Unity's thread; each verb verifies only its identity-defining native transition.
/// </summary>
internal sealed class DiscoveryTreeOfferGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private DiscoveryTreeOfferNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal DiscoveryTreeOfferGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal DiscoveryTreeOfferSubmission Submit(in DiscoveryTreeOfferAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.WrongThread,
                $"Discovery Tree offers are bound to Unity thread {_mainThreadId}, not thread {Environment.CurrentManagedThreadId}.");
        if (_bindings is not { } native)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.ContractUnavailable,
                _bindingFailure.Length == 0
                    ? "The lifecycle-scoped Discovery Tree offer binding set is unavailable."
                    : _bindingFailure);

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.LifecycleReplaced,
                "The current lifecycle epoch could not be read: " + ex.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != currentEpoch)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.LifecycleReplaced,
                $"Action lifecycle {action.LifecycleEpoch} is stale; the live lifecycle is {currentEpoch}.");

        try
        {
            if (!TryResolveTree(native, action.TreeId, out var tree, out var reason))
                return DiscoveryTreeOfferSubmission.Reject(DiscoveryTreeOfferPreflight.IdentityUnavailable, reason);
            if (!native.IsVisible(tree))
                return DiscoveryTreeOfferSubmission.Reject(
                    DiscoveryTreeOfferPreflight.TreeUnavailable,
                    "The game is not showing " +
                    EntityIdentityFormatter.PlayerName(action.TreeId) + " yet.");

            return action.Kind switch
            {
                DiscoveryTreeOfferActionKind.Initiate => SubmitInitiate(in action, native, tree),
                DiscoveryTreeOfferActionKind.Select => SubmitSelect(in action, native, tree),
                DiscoveryTreeOfferActionKind.Confirm => SubmitConfirm(in action, native, tree),
                DiscoveryTreeOfferActionKind.Reroll => SubmitReroll(in action, native, tree),
                _ => DiscoveryTreeOfferSubmission.Reject(
                    DiscoveryTreeOfferPreflight.ContractUnavailable,
                    $"Unknown Discovery Tree offer action kind {(int)action.Kind}."),
            };
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.ContractUnavailable,
                "Discovery Tree offer preflight failed before mutation: " + ex.GetBaseException().Message);
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

    private DiscoveryTreeOfferSubmission SubmitInitiate(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsIdle(tree)) return WrongMode(action.Kind, "Idle");
        if (!native.HasRemainingDiscoveries(tree) && !native.HasImmediateRequired(tree))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.NoDiscoveries,
                "The native tree reports neither a remaining main-pool discovery nor an immediate required discovery.");

        var cost = native.GetNextCost(tree);
        if (cost is null || cost.GetType() != native.CostType)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.ContractUnavailable,
                "DiscoveryTreeSO.GetNextItemCost() returned a non-ResourceCostList value.");
        if (!native.HasEnough(cost))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.Unaffordable,
                "The next discovery on " +
                EntityIdentityFormatter.PlayerName(action.TreeId) +
                " costs more than is held.");
        if (!TryCapturePermit(out var reason))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.MutationPermitUnavailable, reason);

        var stage = DiscoveryTreeOfferNativeStage.Payment;
        var nativeCalls = 0;
        try
        {
            native.PerformCost(cost);
            nativeCalls++;
            stage = DiscoveryTreeOfferNativeStage.Initiate;
            native.Initiate(tree);
            nativeCalls++;
            return CompleteModeTransition(in action, native, tree, nativeCalls);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (IsCraftingBestEffort(native, tree))
                return Verified(nativeCalls, "The tree entered Crafting mode before the native exception.");
            return Fault(in action, DiscoveryTreeOfferPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, nativeCalls,
                "Native initiate threw before Crafting mode was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private DiscoveryTreeOfferSubmission SubmitSelect(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree)) return WrongMode(action.Kind, "Choice");
        if (!TryResolveOfferedItem(native, tree, action.OfferId, out _, out var reason, out var rejection))
            return DiscoveryTreeOfferSubmission.Reject(rejection, reason);
        if (!TryCapturePermit(out reason))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.MutationPermitUnavailable, reason);

        var offerId = action.OfferId;
        return ExecuteSingle(in action, DiscoveryTreeOfferNativeStage.Select,
            () => native.Select(tree, offerId),
            () => ReadGuid(native, native.ReadSelected(tree)) == offerId,
            "The requested offered UUID is selected.");
    }

    private DiscoveryTreeOfferSubmission SubmitConfirm(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree)) return WrongMode(action.Kind, "Choice");
        if (!TryResolveOfferedItem(native, tree, action.OfferId, out var item, out var reason, out var rejection))
            return DiscoveryTreeOfferSubmission.Reject(rejection, reason);
        var selected = ReadGuid(native, native.ReadSelected(tree));
        if (selected != action.OfferId)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.OfferUnavailable,

                // An empty selection is no selection. Rendering the all-zeros GUID as if it named
                // an offer told a caller to go look for an entity that does not exist.
                selected == Guid.Empty
                    ? "No offer is selected, so there is nothing to confirm."
                    : EntityIdentityFormatter.PlayerName(selected) +
                      " is the selected offer, not " +
                      EntityIdentityFormatter.PlayerName(action.OfferId) + ".");
        if (!TryCapturePermit(out reason))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.MutationPermitUnavailable, reason);

        return ExecuteSingle(in action, DiscoveryTreeOfferNativeStage.Confirm,
            () => native.Confirm(tree),
            () => native.IsItemDiscovered(item),
            "The requested offered UUID is discovered.");
    }

    private DiscoveryTreeOfferSubmission SubmitReroll(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree)) return WrongMode(action.Kind, "Choice");
        if (native.HasImmediateRequired(tree))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.RerollUnavailable,
                "The immediate-required discovery path does not expose reroll in the native UI.");
        var offers = native.ReadCurrentChoices(tree);
        var rerolls = native.ReadRerolls(tree);
        if (rerolls <= 0)
            return DiscoveryTreeOfferSubmission.RerollsExhausted(
                "No rerolls are left on this tree.", rerolls);
        if (offers.Count == 0)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.RerollUnavailable,
                "This tree is showing no offers to reroll.");
        if (!TryCapturePermit(out var reason))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.MutationPermitUnavailable, reason);

        var stage = DiscoveryTreeOfferNativeStage.Reroll;
        var nativeCalls = 0;
        try
        {
            native.Reroll(tree);
            nativeCalls++;
            stage = DiscoveryTreeOfferNativeStage.ClearSelection;
            try
            {
                native.Select(tree, Guid.Empty);
                nativeCalls++;
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                nativeCalls++;
            }
            return CompleteModeTransition(in action, native, tree, nativeCalls);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (IsCraftingBestEffort(native, tree))
                return Verified(nativeCalls, "The tree entered Crafting mode before the native exception.");
            return Fault(in action, DiscoveryTreeOfferPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, nativeCalls,
                "Native reroll threw before Crafting mode was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private static DiscoveryTreeOfferSubmission CompleteModeTransition(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree,
        int nativeCalls) =>
        native.IsCrafting(tree)
            ? Verified(nativeCalls, "The tree entered Crafting mode.")
            : Fault(in action, DiscoveryTreeOfferPreflight.VerificationFailed,
                DiscoveryTreeOfferNativeStage.Verification,
                NativeMutationOutcome.PostconditionFailed, nativeCalls,
                "The tree did not enter Crafting mode.");

    private static DiscoveryTreeOfferSubmission ExecuteSingle(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeStage stage,
        Action execute,
        Func<bool> landed,
        string success)
    {
        try
        {
            execute();
            return landed()
                ? Verified(1, success)
                : Fault(in action, DiscoveryTreeOfferPreflight.VerificationFailed,
                    DiscoveryTreeOfferNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, 1,
                    $"The requested {action.Kind} transition was not observable.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (LandedBestEffort(landed))
                return Verified(1, $"The requested {action.Kind} transition landed before the native exception.");
            return Fault(in action, DiscoveryTreeOfferPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, 1,
                $"Native {action.Kind} threw before its requested transition was observable: {ex.GetBaseException().Message}");
        }
    }

    private static DiscoveryTreeOfferSubmission Verified(int nativeCalls, string reason) =>
        new(DiscoveryTreeOfferPreflight.Proceeded, DiscoveryTreeOfferNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(Math.Max(1, nativeCalls), 1, 1), reason);

    private static DiscoveryTreeOfferSubmission Fault(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferPreflight preflight,
        DiscoveryTreeOfferNativeStage stage,
        NativeMutationOutcome outcome,
        int nativeCalls,
        string reason) =>
        new(preflight, stage, outcome,
            new NativeMutationCallOutcome(Math.Max(1, nativeCalls), 1, 0),
            $"Discovery Tree offer {stage} failed on tree {EntityIdentityFormatter.PlayerName(action.TreeId)}: {reason}");

    private static DiscoveryTreeOfferSubmission WrongMode(
        DiscoveryTreeOfferActionKind kind,
        string expected) =>
        DiscoveryTreeOfferSubmission.Reject(
            DiscoveryTreeOfferPreflight.WrongMode,
            $"{kind} requires native {expected} mode.");

    private static bool TryResolveTree(
        DiscoveryTreeOfferNativeBindings native,
        Guid treeId,
        out object tree,
        out string reason)
    {
        tree = null!;
        var matches = 0;
        foreach (var value in native.ReadTrees())
        {
            if (value is null || value.GetType() != native.TreeType) continue;
            if (native.ReadTreeIdentity(value) != treeId) continue;
            tree = value;
            matches++;
        }
        if (matches == 1)
        {
            reason = string.Empty;
            return true;
        }
        reason = matches == 0
            ? $"No exact DiscoveryTreeSO with identity {EntityIdentityFormatter.PlayerName(treeId)} exists in the live registry."
            : $"DiscoveryTreeSO identity {EntityIdentityFormatter.PlayerName(treeId)} is ambiguous across {matches} exact live instances.";
        return false;
    }

    private static bool TryResolveOfferedItem(
        DiscoveryTreeOfferNativeBindings native,
        object tree,
        Guid offerId,
        out object item,
        out string reason,
        out DiscoveryTreeOfferPreflight rejection)
    {
        item = null!;
        if (!Contains(native, native.ReadCurrentChoices(tree), offerId))
        {
            reason = $"Identity {EntityIdentityFormatter.PlayerName(offerId)} is not in the tree's current native offer set.";
            rejection = DiscoveryTreeOfferPreflight.OfferUnavailable;
            return false;
        }
        var resolved = native.GetItem(tree, offerId);
        if (resolved is null || !native.ItemType.IsInstanceOfType(resolved) ||
            native.ReadItemIdentity(resolved) != offerId)
        {
            reason = $"Current offer {EntityIdentityFormatter.PlayerName(offerId)} did not resolve to one exact IDiscoverable identity.";
            rejection = DiscoveryTreeOfferPreflight.IdentityUnavailable;
            return false;
        }
        if (native.IsItemDiscovered(resolved))
        {
            reason = $"Current offer {EntityIdentityFormatter.PlayerName(offerId)} is already discovered.";
            rejection = DiscoveryTreeOfferPreflight.AlreadyDiscovered;
            return false;
        }
        item = resolved;
        reason = string.Empty;
        rejection = DiscoveryTreeOfferPreflight.Proceeded;
        return true;
    }

    private static bool Contains(DiscoveryTreeOfferNativeBindings native, IList values, Guid id)
    {
        for (var index = 0; index < values.Count; index++)
            if (ReadGuid(native, values[index]) == id) return true;
        return false;
    }

    private static Guid ReadGuid(DiscoveryTreeOfferNativeBindings native, object? value)
    {
        if (value is null) throw new InvalidOperationException("A GuidContainer was null.");
        return native.ReadGuid(value);
    }

    private bool TryCapturePermit(out string reason)
    {
        try
        {
            if (_tryCaptureMutationPermit())
            {
                reason = string.Empty;
                return true;
            }
            reason = _readOwnershipFailure();
            if (string.IsNullOrWhiteSpace(reason))
                reason = "The suite no longer owns DiscoveryTreeOfferLifecycle.";
            return false;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            reason = "The Discovery Tree mutation permit could not be captured: " + ex.GetBaseException().Message;
            return false;
        }
    }

    private void BindLifecycle()
    {
        if (DiscoveryTreeOfferNativeBindings.TryCreate(
                out var bindings, out var reason, _resolveType, _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsCraftingBestEffort(DiscoveryTreeOfferNativeBindings native, object tree)
    {
        try { return native.IsCrafting(tree); }
        catch (Exception ex) when (IsExpected(ex)) { return false; }
    }

    private static bool LandedBestEffort(Func<bool> landed)
    {
        try { return landed(); }
        catch (Exception ex) when (IsExpected(ex)) { return false; }
    }

    private static bool IsExpected(Exception exception) => exception is not
        StackOverflowException and not OutOfMemoryException and not AccessViolationException;
}
