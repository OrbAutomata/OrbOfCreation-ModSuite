using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for every player challenge decision.</summary>
internal sealed class ChallengeGameAction : IDisposable
{
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

            if (!TryContext(native, out var context, out var contextFailure))
                return ChallengeSubmission.Reject(ChallengePreflight.ContractUnavailable, contextFailure);
            var before = CaptureAdmission(action.Kind, native, in context, target);
            var preflight = Preflight(action.Kind, native, in context, target, in before, out var reason);
            if (preflight != ChallengePreflight.Proceeded)
                return preflight == ChallengePreflight.NoRerolls
                    ? ChallengeSubmission.RerollsExhausted(reason, before.RerollsLeft)
                    : ChallengeSubmission.Reject(preflight, reason);
            if (!_tryCaptureMutationPermit())
                return ChallengeSubmission.Reject(ChallengePreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            return Execute(in action, native, in context, target, in before);
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
        in NativeContext context, object? target, in ChallengeAdmissionState before)
    {
        var stage = ChallengeNativeStage.NativeCallback;
        try
        {
            switch (action.Kind)
            {
                case ChallengeActionKind.Select:
                    native.Toggle(context.Preferred, target!);
                    break;
                case ChallengeActionKind.Queue:
                    native.ToggleQueue(target!);
                    break;
                case ChallengeActionKind.Abandon:
                    native.Abandon(target!);
                    break;
                case ChallengeActionKind.FetchTime:
                case ChallengeActionKind.FetchPrestige:
                    stage = ChallengeNativeStage.DecisionCommit;
                    if (before.ChallengesFetched)
                        native.SetInt(context.RerollsLeft, checked(before.RerollsLeft - 1));
                    else
                        native.SetBool(context.Fetched, true);
                    stage = ChallengeNativeStage.NativeCallback;
                    if (action.Kind == ChallengeActionKind.FetchTime)
                        native.FetchTime(context.ChallengeManager);
                    else
                        native.FetchPrestige(context.ResetManager);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action.Kind));
            }
            stage = ChallengeNativeStage.Verification;
            return OutcomeLanded(action.Kind, native, in context, target, in before)
                ? Verified()
                : Fault(in action, ChallengePreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    "The requested challenge identity/outcome transition was not observable.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (OutcomeLandedBestEffort(action.Kind, native, in context, target, in before))
                return Verified();
            return Fault(in action, ChallengePreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                "The native challenge pipeline threw before the requested outcome was observable: " +
                exception.GetBaseException().Message);
        }
    }

    private static ChallengePreflight Preflight(ChallengeActionKind kind,
        ChallengeNativeBindings native, in NativeContext context, object? target,
        in ChallengeAdmissionState before, out string reason)
    {
        reason = string.Empty;
        if (kind == ChallengeActionKind.Select)
        {
            if (!before.Selected && !before.InTimeOffers && !before.InPrestigeOffers)
            { reason = "The challenge is not in either current offer list."; return ChallengePreflight.OfferUnavailable; }
            if (!before.Selected && !native.HasEmptySpot(context.Preferred))
            { reason = "The preferred challenge list has no empty slot."; return ChallengePreflight.SelectionFull; }
            if (!before.Selected && native.Restricted(context.Preferred, target!))
            { reason = "The challenge conflicts with the selected challenge types."; return ChallengePreflight.SelectionRestricted; }
            return ChallengePreflight.Proceeded;
        }
        if (kind == ChallengeActionKind.Queue)
        {
            if (!before.InTimeOffers && !before.InPrestigeOffers)
            { reason = "The challenge is not in either current offer list."; return ChallengePreflight.OfferUnavailable; }
            if (before.TargetState is not (0 or 1))
            { reason = "Only idle or queued challenges can toggle queue state."; return ChallengePreflight.InvalidState; }
            return ChallengePreflight.Proceeded;
        }
        if (kind == ChallengeActionKind.Abandon)
        {
            if (before.TargetState != 2)
            { reason = "Only a currently active challenge can be abandoned."; return ChallengePreflight.InvalidState; }
            return ChallengePreflight.Proceeded;
        }
        if (!before.WorldCycleComplete)
        { reason = "Challenge fetching is unavailable until a world cycle is complete."; return ChallengePreflight.FetchUnavailable; }
        if (before.ChallengesFetched && before.RerollsLeft <= 0)
        { reason = "No challenge rerolls remain."; return ChallengePreflight.NoRerolls; }
        return ChallengePreflight.Proceeded;
    }

    private static ChallengeAdmissionState CaptureAdmission(ChallengeActionKind kind,
        ChallengeNativeBindings native, in NativeContext context, object? target)
    {
        var selected = target is not null && native.Contains(context.Preferred, target);
        var time = target is not null && native.Contains(context.TimeOffers, target);
        var prestige = target is not null && native.Contains(context.PrestigeOffers, target);
        return new ChallengeAdmissionState(target is null ? -1 : native.State(target), selected, time, prestige,
            native.GetBool(context.CycleComplete), native.GetBool(context.Fetched),
            native.AsInt(context.RerollsLeft),
            kind switch
            {
                ChallengeActionKind.FetchTime => CaptureOfferIds(native, context.TimeOffers),
                ChallengeActionKind.FetchPrestige => CaptureOfferIds(native, context.PrestigeOffers),
                _ => Array.Empty<Guid>(),
            });
    }

    private static bool OutcomeLanded(ChallengeActionKind kind,
        ChallengeNativeBindings native, in NativeContext context, object? target,
        in ChallengeAdmissionState before) => kind switch
    {
        ChallengeActionKind.Select => native.Contains(context.Preferred, target!) == !before.Selected,
        ChallengeActionKind.Queue => before.TargetState == 0
            ? native.State(target!) == 1
            : before.TargetState == 1 && native.State(target!) == 0,
        ChallengeActionKind.Abandon => native.State(target!) == 4,
        ChallengeActionKind.FetchTime => !SameOffers(
            before.Offers, CaptureOfferIds(native, context.TimeOffers)),
        ChallengeActionKind.FetchPrestige => !SameOffers(
            before.Offers, CaptureOfferIds(native, context.PrestigeOffers)),
        _ => false,
    };

    private static Guid[] CaptureOfferIds(ChallengeNativeBindings native, object list)
    {
        var values = native.Values(list) ??
            throw new InvalidOperationException("The native challenge offer list was unavailable.");
        var result = new Guid[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index] ??
                throw new InvalidOperationException("A native challenge offer was null.");
            if (value.GetType() != native.ChallengeType)
                throw new InvalidOperationException("A native challenge offer had the wrong type.");
            result[index] = native.Identity(value);
        }
        return result;
    }

    private static bool SameOffers(Guid[] before, Guid[] after)
    {
        if (before.Length != after.Length) return false;
        for (var index = 0; index < before.Length; index++)
            if (before[index] != after[index]) return false;
        return true;
    }

    private static bool OutcomeLandedBestEffort(ChallengeActionKind kind,
        ChallengeNativeBindings native, in NativeContext context, object? target,
        in ChallengeAdmissionState before)
    {
        try { return OutcomeLanded(kind, native, in context, target, in before); }
        catch (Exception exception) when (IsExpected(exception)) { return false; }
    }

    private static ChallengeSubmission Verified() =>
        new(ChallengePreflight.Proceeded, ChallengeNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
            "The requested challenge transition is visible.");

    private static ChallengeSubmission Fault(in ChallengeAction action,
        ChallengePreflight preflight, ChallengeNativeStage stage, NativeMutationOutcome outcome,
        string reason)
    {
        var target = action.HasTarget ? EntityIdentityFormatter.PlayerName(action.TargetId) : action.Kind.ToString();
        var exactReason = "Challenge action " + stage + " failed on " + target + ": " + reason;
        return new ChallengeSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(1, 1, 0), exactReason);
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
