using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for every player challenge decision.</summary>
internal sealed class ChallengeGameAction : NativeGameAction<ChallengeNativeBindings>
{
    private const string Screen = "Time > Challenges";

    private const string RestrictedReason =
        "The challenge conflicts with the selected challenge types.";

    private static readonly GameActionCodes Codes = new(
        ChallengeActionResultCodes.WrongThread,
        ChallengeActionResultCodes.ContractUnavailable,
        ChallengeActionResultCodes.MutationPermitUnavailable,
        ChallengeActionResultCodes.PostCommitFault,
        ChallengeActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal ChallengeGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Challenge action", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out ChallengeNativeBindings? bindings, out string reason) =>
                ChallengeNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal ChallengeSubmission Submit(in ChallengeAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new ChallengeSubmission(outcome),
            native => Press(in request, native));
    }

    private ChallengeSubmission Press(in ChallengeAction action, ChallengeNativeBindings native)
    {
        object? target = null;
        if (action.HasTarget)
        {
            var resolution = _registry.Resolve(action.TargetId, native.ChallengeType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return ChallengeSubmission.Reject(ChallengeActionResultCodes.IdentityUnavailable,
                    resolution.IsResolved ? GameActionAnswer.Replaced("challenge") : resolution.Cause);
            target = resolution.Value!;
        }

        object? replaced = null;
        if (action.ReplacedId != Guid.Empty)
        {
            var resolution = _registry.Resolve(action.ReplacedId, native.ChallengeType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return ChallengeSubmission.Reject(ChallengeActionResultCodes.IdentityUnavailable,
                    resolution.IsResolved ? GameActionAnswer.Replaced("challenge") : resolution.Cause);
            replaced = resolution.Value!;
        }

        if (!TryContext(native, out var context, out var contextFailure))
            return ChallengeSubmission.Reject(ChallengeActionResultCodes.ContractUnavailable, contextFailure);
        var before = CaptureAdmission(native, in context, target);
        var preflight = Preflight(
            action.Kind, native, in context, target, replaced, in before, out var reason);
        if (preflight != CommonActionResultCodes.Committed)
            return preflight == ChallengeActionResultCodes.NoRerolls
                ? ChallengeSubmission.RerollsExhausted(reason, before.RerollsLeft)
                : ChallengeSubmission.Reject(preflight, reason);
        if (!TryPermit(out var refusal)) return new ChallengeSubmission(refusal);

        var kind = action.Kind;
        var outcome = Execute(
            action.HasTarget ? EntityIdentityFormatter.PlayerName(action.TargetId) : kind.ToString(),
            "The requested challenge transition is visible.", false,
            () => OutcomeLanded(kind, native, in context, target, replaced, in before),
            () => Call(kind, native, in context, target, replaced, in before),
            static (_, landed) => landed);
        if (outcome.Verified || kind != ChallengeActionKind.Reroll) return new ChallengeSubmission(outcome);
        var after = NativeMutationVerifier.TryReadAfterCall(() => native.AsInt(context.RerollsLeft), out var left)
            ? left
            : -1;
        return new ChallengeSubmission(outcome, before.RerollsLeft, after);
    }

    private static NativeStop? Call(ChallengeActionKind kind, ChallengeNativeBindings native,
        in NativeContext context, object? target, object? replaced,
        in ChallengeAdmissionState before)
    {
        switch (kind)
        {
            case ChallengeActionKind.Select:
                // The screen's own two presses, in the order a player makes them: free the row
                // this one takes over, then pick this one.
                if (replaced is not null)
                {
                    native.Toggle(context.Preferred, replaced);
                    // The type conflict is the game's question about the list as it stands, and
                    // the row just given up is no longer in it. Asked one press earlier it
                    // answers for a list nobody is selecting into, and refuses the swap the
                    // screen performs — so it is asked here, and a no puts the row back.
                    if (!before.Selected && native.Restricted(context.Preferred, target!))
                    {
                        native.Toggle(context.Preferred, replaced);
                        var preferred = context.Preferred;
                        return NativeStop.Refused(ChallengeActionResultCodes.SelectionRestricted,
                            RestrictedReason, () => native.Contains(preferred, replaced));
                    }
                }
                native.Toggle(context.Preferred, target!);
                return null;
            case ChallengeActionKind.Queue:
                native.ToggleQueue(target!);
                return null;
            case ChallengeActionKind.Abandon:
                native.Abandon(target!);
                return null;
            case ChallengeActionKind.Reroll:
                if (before.ChallengesFetched)
                    native.SetInt(context.RerollsLeft, checked(before.RerollsLeft - 1));
                else
                    native.SetBool(context.Fetched, true);
                native.Reroll(context.ChallengeManager);
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static ServiceActionResultCode Preflight(ChallengeActionKind kind,
        ChallengeNativeBindings native, in NativeContext context, object? target, object? replaced,
        in ChallengeAdmissionState before, out string reason)
    {
        reason = string.Empty;
        if (kind == ChallengeActionKind.Select)
        {
            if (!before.Selected && !before.InTimeOffers && !before.InPrestigeOffers)
            { reason = "The challenge is not in either current offer list."; return ChallengeActionResultCodes.OfferUnavailable; }
            if (!before.Selected && !native.HasEmptySpot(context.Preferred))
            {
                if (replaced is null)
                {
                    reason = "Every selection this cycle allows is taken, and more than one is " +
                        "held, so which one to give up is the caller's choice: select one of them " +
                        "to give it up, then select this one.";
                    return ChallengeActionResultCodes.SelectionFull;
                }
                if (!native.Contains(context.Preferred, replaced))
                {
                    reason = "The selection this one would have replaced is no longer held.";
                    return ChallengeActionResultCodes.SelectionFull;
                }
            }
            // A swap's conflict cannot be settled here: the game reads it off the selection as it
            // stands, and the give-up press has not happened. Execute asks it in the state the
            // press leaves behind.
            if (!before.Selected && replaced is null &&
                native.Restricted(context.Preferred, target!))
            { reason = RestrictedReason; return ChallengeActionResultCodes.SelectionRestricted; }
            return CommonActionResultCodes.Committed;
        }
        if (kind == ChallengeActionKind.Queue)
        {
            if (!before.InTimeOffers && !before.InPrestigeOffers)
            { reason = "The challenge is not in either current offer list."; return ChallengeActionResultCodes.OfferUnavailable; }
            if (before.TargetState is not (0 or 1))
            { reason = "A challenge that has already run cannot be queued again until the next reset."; return ChallengeActionResultCodes.InvalidState; }
            return CommonActionResultCodes.Committed;
        }
        if (kind == ChallengeActionKind.Abandon)
        {
            if (before.TargetState != 2)
            {
                reason = before.TargetState == 1
                    ? "This challenge is queued, not running. A queued challenge starts running " +
                      "at the next reset, and only a running one can be abandoned; queue it off " +
                      "instead if you do not want it."
                    : "Only a challenge the reset started can be abandoned. This one is not " +
                      "running: queue it, reset, and it will be.";
                return ChallengeActionResultCodes.InvalidState;
            }
            return CommonActionResultCodes.Committed;
        }
        if (!before.WorldCycleComplete)
        { reason = "Challenge fetching is unavailable until a world cycle is complete."; return ChallengeActionResultCodes.FetchUnavailable; }
        if (before.ChallengesFetched && before.RerollsLeft <= 0)
        { reason = "No challenge rerolls remain."; return ChallengeActionResultCodes.NoRerolls; }
        return CommonActionResultCodes.Committed;
    }

    private static ChallengeAdmissionState CaptureAdmission(
        ChallengeNativeBindings native, in NativeContext context, object? target)
    {
        var selected = target is not null && native.Contains(context.Preferred, target);
        var time = target is not null && native.Contains(context.TimeOffers, target);
        var prestige = target is not null && native.Contains(context.PrestigeOffers, target);
        return new ChallengeAdmissionState(target is null ? -1 : native.State(target), selected, time, prestige,
            native.GetBool(context.CycleComplete), native.GetBool(context.Fetched),
            native.AsInt(context.RerollsLeft));
    }

    /// <summary>
    /// One press of the offer button is bookkeeping the game's own UI performs: the first press of a
    /// world cycle sets <c>hasFetchedChallenges</c>, every later press decrements
    /// <c>challengeRerollsLeft</c>, and only then does either callback ask for offers. That
    /// bookkeeping write is therefore the press's settled postcondition. What comes back is not: the
    /// eligible pool can be small enough that an honest redraw returns the same offers in the same
    /// order, so an unchanged offer set is an outcome of the press, never evidence it did not
    /// happen. The caller learns whether the offers moved from <c>changed</c> on the settled delta.
    /// </summary>
    private static bool OutcomeLanded(ChallengeActionKind kind,
        ChallengeNativeBindings native, in NativeContext context, object? target, object? replaced,
        in ChallengeAdmissionState before) => kind switch
    {
        // A replacement is two presses and both are the postcondition: the row asked for is held and
        // the row it took over is not. Half a swap is a selection the caller did not ask for.
        ChallengeActionKind.Select =>
            native.Contains(context.Preferred, target!) == !before.Selected &&
            (replaced is null || !native.Contains(context.Preferred, replaced)),
        ChallengeActionKind.Queue => before.TargetState == 0
            ? native.State(target!) == 1
            : before.TargetState == 1 && native.State(target!) == 0,
        ChallengeActionKind.Abandon => native.State(target!) == 4,
        ChallengeActionKind.Reroll => before.ChallengesFetched
            ? native.AsInt(context.RerollsLeft) == before.RerollsLeft - 1
            : native.GetBool(context.Fetched),
        _ => false,
    };

    private static bool TryContext(ChallengeNativeBindings native, out NativeContext context, out string reason)
    {
        var challengeManager = native.ChallengeManager();
        var resetManager = native.ResetManager();
        if (challengeManager is null || challengeManager.GetType() != native.ChallengeManagerType ||
            resetManager is null || resetManager.GetType() != native.ResetManagerType)
        { context = default; reason = "The native challenge managers were unavailable."; return false; }
        var preferred = native.Preferred(challengeManager);
        var time = native.TimeOffers(challengeManager);
        var prestige = native.PrestigeOffers(resetManager);
        var left = native.RerollsLeft(resetManager);
        var complete = native.CycleComplete(resetManager);
        var fetched = native.Fetched(resetManager);
        if (preferred is null || time is null || prestige is null || left is null ||
            complete is null || fetched is null)
        {
            context = default;
            reason = "The game did not answer whether this is allowed right now; open " +
                "Time > Challenges and read it again.";
            return false;
        }
        context = new NativeContext(challengeManager, resetManager, preferred, time, prestige,
            left, complete, fetched);
        reason = string.Empty;
        return true;
    }

    private readonly struct NativeContext
    {
        internal NativeContext(object challengeManager, object resetManager, object preferred,
            object timeOffers, object prestigeOffers, object rerollsLeft,
            object cycleComplete, object fetched)
        {
            ChallengeManager = challengeManager;
            ResetManager = resetManager;
            Preferred = preferred;
            TimeOffers = timeOffers;
            PrestigeOffers = prestigeOffers;
            RerollsLeft = rerollsLeft;
            CycleComplete = cycleComplete;
            Fetched = fetched;
        }
        internal object ChallengeManager { get; }
        internal object ResetManager { get; }
        internal object Preferred { get; }
        internal object TimeOffers { get; }
        internal object PrestigeOffers { get; }
        internal object RerollsLeft { get; }
        internal object CycleComplete { get; }
        internal object Fetched { get; }
    }

}
