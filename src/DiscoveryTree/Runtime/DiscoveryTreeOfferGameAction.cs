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
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("the screen this tree is drawn on"));

        long currentEpoch;
        try { currentEpoch = _readLifecycleEpoch(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("the screen this tree is drawn on", ex));
        }
        if (action.LifecycleEpoch != currentEpoch)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

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
                GameActionAnswer.CouldNotRead("the screen this tree is drawn on", ex));
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
        if (!native.IsIdle(tree))
            return WrongMode("This tree is already showing offers to choose from.");
        if (!native.HasRemainingDiscoveries(tree) && !native.HasImmediateRequired(tree))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.NoDiscoveries,
                "This tree has no discovery left to start.");

        var cost = native.GetNextCost(tree);
        if (cost is null || cost.GetType() != native.CostType)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.ContractUnavailable,
                "The game did not report a price for the next discovery.");
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
                GameActionAnswer.GameErrored("the screen this tree is drawn on", ex));
        }
    }

    private DiscoveryTreeOfferSubmission SubmitSelect(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree))
            return WrongMode("This tree is not showing any offers to choose from right now.");
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
        if (!native.IsChoice(tree))
            return WrongMode("This tree is not showing any offers to choose from right now.");
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
        if (!native.IsChoice(tree))
            return WrongMode("This tree is not showing any offers to choose from right now.");
        if (native.HasImmediateRequired(tree))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferPreflight.RerollUnavailable,
                "A required discovery cannot be rerolled.");
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
                GameActionAnswer.GameErrored("the screen this tree is drawn on", ex));
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
                    GameActionAnswer.ChangeNotSeen("the screen this tree is drawn on"));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (LandedBestEffort(landed))
                return Verified(1, $"The requested {action.Kind} transition landed before the native exception.");
            return Fault(in action, DiscoveryTreeOfferPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew, 1,
                GameActionAnswer.GameErrored("the screen this tree is drawn on", ex));
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

    private static DiscoveryTreeOfferSubmission WrongMode(string reason) =>
        DiscoveryTreeOfferSubmission.Reject(DiscoveryTreeOfferPreflight.WrongMode, reason);

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
            ? "That discovery tree is not in this run."
            : "That id names more than one live discovery tree.";
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
            reason = $"{EntityIdentityFormatter.PlayerName(offerId)} is not one of the offers this tree is showing.";
            rejection = DiscoveryTreeOfferPreflight.OfferUnavailable;
            return false;
        }
        var resolved = native.GetItem(tree, offerId);
        if (resolved is null || !native.ItemType.IsInstanceOfType(resolved) ||
            native.ReadItemIdentity(resolved) != offerId)
        {
            reason = $"The offer {EntityIdentityFormatter.PlayerName(offerId)} did not resolve to exactly one thing to discover.";
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
                reason = "Another part of the suite is driving discovery this instant; try " +
                    "again in a moment.";
            return false;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            reason = GameActionAnswer.CouldNotRead("the screen this tree is drawn on", ex);
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
