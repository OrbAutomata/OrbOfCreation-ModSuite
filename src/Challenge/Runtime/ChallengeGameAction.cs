using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for every player challenge decision.</summary>
internal sealed class ChallengeGameAction : IDisposable
{
    private const string RestrictedReason =
        "The challenge conflicts with the selected challenge types.";

    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private ChallengeNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal ChallengeGameAction(Func<long> readLifecycleEpoch,
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

    internal ChallengeSubmission Submit(in ChallengeAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return ChallengeSubmission.Reject(ChallengePreflight.WrongThread,
                "Challenge actions are bound to Unity thread " + _mainThreadId + ".");
        if (_bindings is not { } native)
            return ChallengeSubmission.Reject(ChallengePreflight.ContractUnavailable, _bindingFailure);
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return ChallengeSubmission.Reject(ChallengePreflight.LifecycleReplaced,
                "The lifecycle epoch could not be read: " + exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return ChallengeSubmission.Reject(ChallengePreflight.LifecycleReplaced,
                "The submitted lifecycle is stale.");

        try
        {
            object? target = null;
            if (action.HasTarget)
            {
                var resolution = _registry.Resolve(action.TargetId, native.ChallengeType);
                if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                    return ChallengeSubmission.Reject(ChallengePreflight.IdentityUnavailable,
                        resolution.IsResolved ? "The challenge resolution became stale." : resolution.Reason);
                target = resolution.Value!;
            }

            object? replaced = null;
            if (action.ReplacedId != Guid.Empty)
            {
                var resolution = _registry.Resolve(action.ReplacedId, native.ChallengeType);
                if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                    return ChallengeSubmission.Reject(ChallengePreflight.IdentityUnavailable,
                        resolution.IsResolved ? "The challenge resolution became stale." : resolution.Reason);
                replaced = resolution.Value!;
            }

            if (!TryContext(native, out var context, out var contextFailure))
                return ChallengeSubmission.Reject(ChallengePreflight.ContractUnavailable, contextFailure);
            var before = CaptureAdmission(native, in context, target);
            var preflight = Preflight(
                action.Kind, native, in context, target, replaced, in before, out var reason);
            if (preflight != ChallengePreflight.Proceeded)
                return preflight == ChallengePreflight.NoRerolls
                    ? ChallengeSubmission.RerollsExhausted(reason, before.RerollsLeft)
                    : ChallengeSubmission.Reject(preflight, reason);
            if (!_tryCaptureMutationPermit())
                return ChallengeSubmission.Reject(ChallengePreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, in context, target, replaced, in before);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return ChallengeSubmission.Reject(ChallengePreflight.ContractUnavailable,
                "Challenge preflight failed before mutation: " + exception.GetBaseException().Message);
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

    private ChallengeSubmission Execute(in ChallengeAction action, ChallengeNativeBindings native,
        in NativeContext context, object? target, object? replaced,
        in ChallengeAdmissionState before)
    {
        var stage = ChallengeNativeStage.NativeCallback;
        try
        {
            switch (action.Kind)
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
                            stage = ChallengeNativeStage.Verification;
                            native.Toggle(context.Preferred, replaced);
                            return native.Contains(context.Preferred, replaced)
                                ? ChallengeSubmission.Reject(
                                    ChallengePreflight.SelectionRestricted, RestrictedReason)
                                : Fault(in action, ChallengePreflight.PostCommitFault, stage,
                                    NativeMutationOutcome.PostconditionFailed,
                                    "The row given up to make room was not restored after the " +
                                    "type conflict refused the swap.",
                                    SettledBudget(action.Kind, native, in context, in before));
                        }
                    }
                    native.Toggle(context.Preferred, target!);
                    break;
                case ChallengeActionKind.Queue:
                    native.ToggleQueue(target!);
                    break;
                case ChallengeActionKind.Abandon:
                    native.Abandon(target!);
                    break;
                case ChallengeActionKind.Reroll:
                    stage = ChallengeNativeStage.DecisionCommit;
                    if (before.ChallengesFetched)
                        native.SetInt(context.RerollsLeft, checked(before.RerollsLeft - 1));
                    else
                        native.SetBool(context.Fetched, true);
                    stage = ChallengeNativeStage.NativeCallback;
                    native.Reroll(context.ChallengeManager);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action.Kind));
            }
            stage = ChallengeNativeStage.Verification;
            return OutcomeLanded(action.Kind, native, in context, target, replaced, in before)
                ? Verified()
                : Fault(in action, ChallengePreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    "The requested challenge identity/outcome transition was not observable.",
                    SettledBudget(action.Kind, native, in context, in before));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (OutcomeLandedBestEffort(action.Kind, native, in context, target, replaced, in before))
                return Verified();
            return Fault(in action, ChallengePreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                "The native challenge pipeline threw before the requested outcome was observable: " +
                exception.GetBaseException().Message,
                SettledBudget(action.Kind, native, in context, in before));
        }
    }

    /// <summary>
    /// The reroll budget on both sides of a press that was attempted. The game's own button spends
    /// before it asks for offers, so a press that then failed still has to say where the scarce
    /// budget landed: a caller must never infer that from an absent key. Both numbers are the
    /// sentinel's own readings, so nothing is collected here that verification did not already read.
    /// </summary>
    private static ChallengeBudget SettledBudget(ChallengeActionKind kind,
        ChallengeNativeBindings native, in NativeContext context, in ChallengeAdmissionState before)
    {
        if (kind != ChallengeActionKind.Reroll) return ChallengeBudget.NotTheAxis;
        try { return new ChallengeBudget(before.RerollsLeft, native.AsInt(context.RerollsLeft)); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return new ChallengeBudget(before.RerollsLeft, -1);
        }
    }

    private static ChallengePreflight Preflight(ChallengeActionKind kind,
        ChallengeNativeBindings native, in NativeContext context, object? target, object? replaced,
        in ChallengeAdmissionState before, out string reason)
    {
        reason = string.Empty;
        if (kind == ChallengeActionKind.Select)
        {
            if (!before.Selected && !before.InTimeOffers && !before.InPrestigeOffers)
            { reason = "The challenge is not in either current offer list."; return ChallengePreflight.OfferUnavailable; }
            if (!before.Selected && !native.HasEmptySpot(context.Preferred))
            {
                if (replaced is null)
                {
                    reason = "Every selection this cycle allows is taken, and more than one is " +
                        "held, so which one to give up is the caller's choice: select one of them " +
                        "to give it up, then select this one.";
                    return ChallengePreflight.SelectionFull;
                }
                if (!native.Contains(context.Preferred, replaced))
                {
                    reason = "The selection this one would have replaced is no longer held.";
                    return ChallengePreflight.SelectionFull;
                }
            }
            // A swap's conflict cannot be settled here: the game reads it off the selection as it
            // stands, and the give-up press has not happened. Execute asks it in the state the
            // press leaves behind.
            if (!before.Selected && replaced is null &&
                native.Restricted(context.Preferred, target!))
            { reason = RestrictedReason; return ChallengePreflight.SelectionRestricted; }
            return ChallengePreflight.Proceeded;
        }
        if (kind == ChallengeActionKind.Queue)
        {
            if (!before.InTimeOffers && !before.InPrestigeOffers)
            { reason = "The challenge is not in either current offer list."; return ChallengePreflight.OfferUnavailable; }
            if (before.TargetState is not (0 or 1))
            { reason = "A challenge that has already run cannot be queued again until the next reset."; return ChallengePreflight.InvalidState; }
            return ChallengePreflight.Proceeded;
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
                return ChallengePreflight.InvalidState;
            }
            return ChallengePreflight.Proceeded;
        }
        if (!before.WorldCycleComplete)
        { reason = "Challenge fetching is unavailable until a world cycle is complete."; return ChallengePreflight.FetchUnavailable; }
        if (before.ChallengesFetched && before.RerollsLeft <= 0)
        { reason = "No challenge rerolls remain."; return ChallengePreflight.NoRerolls; }
        return ChallengePreflight.Proceeded;
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

    private static bool OutcomeLandedBestEffort(ChallengeActionKind kind,
        ChallengeNativeBindings native, in NativeContext context, object? target, object? replaced,
        in ChallengeAdmissionState before)
    {
        try { return OutcomeLanded(kind, native, in context, target, replaced, in before); }
        catch (Exception exception) when (IsExpected(exception)) { return false; }
    }

    private static ChallengeSubmission Verified() =>
        new(ChallengePreflight.Proceeded, ChallengeNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
            "The requested challenge transition is visible.");

    private static ChallengeSubmission Fault(in ChallengeAction action,
        ChallengePreflight preflight, ChallengeNativeStage stage, NativeMutationOutcome outcome,
        string reason, ChallengeBudget budget)
    {
        var target = action.HasTarget ? EntityIdentityFormatter.PlayerName(action.TargetId) : action.Kind.ToString();
        var exactReason = "Challenge action " + stage + " failed on " + target + ": " + reason;
        return new ChallengeSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(1, 1, 0), exactReason, budget.Before, budget.After);
    }

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
        { context = default; reason = "The native challenge decision graph returned a null member."; return false; }
        context = new NativeContext(challengeManager, resetManager, preferred, time, prestige,
            left, complete, fetched);
        reason = string.Empty;
        return true;
    }

    private void BindLifecycle()
    {
        if (ChallengeNativeBindings.TryCreate(out var bindings, out var reason, _resolveType, _includeContract))
        { _bindings = bindings; _bindingFailure = string.Empty; return; }
        _bindings = null;
        _bindingFailure = reason;
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

    private static bool IsExpected(Exception exception) => exception is InvalidOperationException or
        ArgumentException or TargetInvocationException or OverflowException;
}
