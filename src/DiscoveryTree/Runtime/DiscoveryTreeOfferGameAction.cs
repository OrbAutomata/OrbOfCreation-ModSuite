using System;
using System.Collections;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Lifecycle-scoped re-drive of the native Discovery Tree offer pipeline. Admission is re-read on
/// Unity's thread; each verb verifies only its identity-defining native transition.
/// </summary>
internal sealed class DiscoveryTreeOfferGameAction : NativeGameAction<DiscoveryTreeOfferNativeBindings>
{
    private const string Screen = "the screen this tree is drawn on";

    private static readonly GameActionCodes Codes = new(
        DiscoveryTreeOfferActionResultCodes.WrongThread,
        DiscoveryTreeOfferActionResultCodes.ContractUnavailable,
        DiscoveryTreeOfferActionResultCodes.MutationPermitUnavailable,
        DiscoveryTreeOfferActionResultCodes.PostCommitFault,
        DiscoveryTreeOfferActionResultCodes.VerificationFailed);

    internal DiscoveryTreeOfferGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
        : base("Discovery Tree offer", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out DiscoveryTreeOfferNativeBindings? bindings, out string reason) =>
                DiscoveryTreeOfferNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
    }

    internal DiscoveryTreeOfferSubmission Submit(in DiscoveryTreeOfferAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new DiscoveryTreeOfferSubmission(outcome),
            native => Press(in request, native));
    }

    private DiscoveryTreeOfferSubmission Press(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native)
    {
        if (!TryResolveTree(native, action.TreeId, out var tree, out var reason))
            return DiscoveryTreeOfferSubmission.Reject(DiscoveryTreeOfferActionResultCodes.IdentityUnavailable, reason);
        if (!native.IsVisible(tree))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferActionResultCodes.TreeUnavailable,
                "The game is not showing " +
                EntityIdentityFormatter.PlayerName(action.TreeId) + " yet.");

        return action.Kind switch
        {
            DiscoveryTreeOfferActionKind.Initiate => SubmitInitiate(in action, native, tree),
            DiscoveryTreeOfferActionKind.Select => SubmitSelect(in action, native, tree),
            DiscoveryTreeOfferActionKind.Confirm => SubmitConfirm(in action, native, tree),
            DiscoveryTreeOfferActionKind.Reroll => SubmitReroll(in action, native, tree),
            _ => DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferActionResultCodes.ContractUnavailable,
                $"Unknown Discovery Tree offer action kind {(int)action.Kind}."),
        };
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
                DiscoveryTreeOfferActionResultCodes.NoDiscoveries,
                "This tree has no discovery left to start.");

        var cost = native.GetNextCost(tree);
        if (cost is null || cost.GetType() != native.CostType)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferActionResultCodes.ContractUnavailable,
                "The game did not report a price for the next discovery.");
        if (!native.HasEnough(cost))
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferActionResultCodes.Unaffordable,
                "The next discovery on " +
                EntityIdentityFormatter.PlayerName(action.TreeId) +
                " costs more than is held.");
        if (!TryPermit(out var refusal)) return new DiscoveryTreeOfferSubmission(refusal);

        return Run(in action, "The tree entered Crafting mode.",
            () => native.IsCrafting(tree),
            () =>
            {
                native.PerformCost(cost);
                native.Initiate(tree);
            });
    }

    private DiscoveryTreeOfferSubmission SubmitSelect(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree))
            return WrongMode("This tree is not showing any offers to choose from right now.");
        if (!TryResolveOfferedItem(native, tree, action.OfferId, out var reason, out var rejection))
            return DiscoveryTreeOfferSubmission.Reject(rejection, reason);
        if (!TryPermit(out var refusal)) return new DiscoveryTreeOfferSubmission(refusal);

        var offerId = action.OfferId;
        return Run(in action, "The requested offered UUID is selected.",
            () => ReadGuid(native, native.ReadSelected(tree)) == offerId,
            () => native.Select(tree, offerId));
    }

    private DiscoveryTreeOfferSubmission SubmitConfirm(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree))
            return WrongMode("This tree is not showing any offers to choose from right now.");
        if (!TryResolveOfferedItem(native, tree, action.OfferId, out var reason, out var rejection))
            return DiscoveryTreeOfferSubmission.Reject(rejection, reason);
        var selected = ReadGuid(native, native.ReadSelected(tree));
        if (selected != action.OfferId)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferActionResultCodes.OfferUnavailable,

                // An empty selection is no selection. Rendering the all-zeros GUID as if it named
                // an offer told a caller to go look for an entity that does not exist.
                selected == Guid.Empty
                    ? "No offer is selected, so there is nothing to confirm."
                    : EntityIdentityFormatter.PlayerName(selected) +
                      " is the selected offer, not " +
                      EntityIdentityFormatter.PlayerName(action.OfferId) + ".");
        if (!TryPermit(out var refusal)) return new DiscoveryTreeOfferSubmission(refusal);

        // The game's own Confirm ends in ResetMode: the tree leaves choice mode and drops its
        // selection. That is the transition this press owns. The discovered flag is a ledger bit
        // the press shares with the by-row route, and on an offer that already carried it the
        // sentinel read true before the press, so it could not witness anything.
        return Run(in action, "The tree took the offer and left choice mode.",
            () => !native.IsChoice(tree),
            () => native.Confirm(tree));
    }

    private DiscoveryTreeOfferSubmission SubmitReroll(
        in DiscoveryTreeOfferAction action,
        DiscoveryTreeOfferNativeBindings native,
        object tree)
    {
        if (!native.IsChoice(tree))
            return WrongMode("This tree is not showing any offers to choose from right now.");
        var offers = native.ReadCurrentChoices(tree);
        var rerolls = native.ReadRerolls(tree);
        if (rerolls <= 0)
            return DiscoveryTreeOfferSubmission.RerollsExhausted(
                "No rerolls are left on this tree.", rerolls);
        if (offers.Count == 0)
            return DiscoveryTreeOfferSubmission.Reject(
                DiscoveryTreeOfferActionResultCodes.RerollUnavailable,
                "This tree is showing no offers to reroll.");
        if (!TryPermit(out var refusal)) return new DiscoveryTreeOfferSubmission(refusal);

        return Run(in action, "The tree entered Crafting mode.",
            () => native.IsCrafting(tree),
            () =>
            {
                native.Reroll(tree);
                native.Select(tree, Guid.Empty);
            });
    }

    private DiscoveryTreeOfferSubmission Run(
        in DiscoveryTreeOfferAction action,
        string success,
        Func<bool> landed,
        Action call) =>
        new(Execute("tree " + EntityIdentityFormatter.PlayerName(action.TreeId), success, false,
            landed, call, static (_, after) => after));

    private static DiscoveryTreeOfferSubmission WrongMode(string reason) =>
        DiscoveryTreeOfferSubmission.Reject(DiscoveryTreeOfferActionResultCodes.WrongMode, reason);

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

    /// <summary>
    /// Whether the offer named by the request is one this tree is showing, and resolves to exactly
    /// one thing. Whether that thing is already discovered is not asked: the game's own Confirm
    /// carries no such check, so refusing on it invented a rule that left a tree the game would
    /// happily clear with no way out of choice mode.
    /// </summary>
    private static bool TryResolveOfferedItem(
        DiscoveryTreeOfferNativeBindings native,
        object tree,
        Guid offerId,
        out string reason,
        out ServiceActionResultCode rejection)
    {
        if (!Contains(native, native.ReadCurrentChoices(tree), offerId))
        {
            reason = $"{EntityIdentityFormatter.PlayerName(offerId)} is not one of the offers this tree is showing.";
            rejection = DiscoveryTreeOfferActionResultCodes.OfferUnavailable;
            return false;
        }
        var resolved = native.GetItem(tree, offerId);
        if (resolved is null || !native.ItemType.IsInstanceOfType(resolved) ||
            native.ReadItemIdentity(resolved) != offerId)
        {
            reason = $"The offer {EntityIdentityFormatter.PlayerName(offerId)} did not resolve to exactly one thing to discover.";
            rejection = DiscoveryTreeOfferActionResultCodes.IdentityUnavailable;
            return false;
        }
        reason = string.Empty;
        rejection = CommonActionResultCodes.Committed;
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
}
