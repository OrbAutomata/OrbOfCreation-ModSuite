using System;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class ResearchGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly TypedRegistryResolver _registry;
    private readonly int _mainThreadId;
    private ResearchNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal ResearchGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _resolveType = resolveType; _includeContract = includeContract;
        var identity = RuntimeIdentityRegistryBinding.Shared;
        _registry = registry ?? new TypedRegistryResolver(_readLifecycleEpoch, identity.Read, identity.ReadStableUuid);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal ResearchSubmission Submit(in ResearchAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return ResearchSubmission.Reject(ResearchPreflight.WrongThread,
                "Research actions are bound to Unity thread " + _mainThreadId + ".");
        if (_bindings is not { } native)
            return ResearchSubmission.Reject(ResearchPreflight.ContractUnavailable, _bindingFailure);
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return ResearchSubmission.Reject(ResearchPreflight.LifecycleReplaced,
                "The lifecycle epoch could not be read: " + exception.GetBaseException().Message);
        }
        if (action.LifecycleEpoch != epoch)
            return ResearchSubmission.Reject(ResearchPreflight.LifecycleReplaced,
                "The submitted lifecycle is stale.");
        try
        {
            var resolution = _registry.Resolve(action.TargetId, native.ResearchType);
            if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
                return ResearchSubmission.Reject(ResearchPreflight.IdentityUnavailable,
                    resolution.IsResolved ? "The research resolution became stale." : resolution.Reason);
            var target = resolution.Value!;
            var before = Capture(native, target, action.Amount);
            var preflight = Preflight(action.Kind, in before, out var reason);
            if (preflight != ResearchPreflight.Proceeded)
                return ResearchSubmission.Reject(
                    preflight,
                    reason,
                    preflight == ResearchPreflight.AmountUnavailable
                        ? before.LevelsAvailable
                        : -1);
            if (!_tryCaptureMutationPermit())
                return ResearchSubmission.Reject(ResearchPreflight.MutationPermitUnavailable,
                    _readOwnershipFailure());
            if (action.Kind != ResearchActionKind.Develop)
                return Execute(in action, native, target, in before);
            if (!NativeMultiBuyScope.TryEnter(action.Amount, out var scope, out var scopeReason))
                return ResearchSubmission.Reject(ResearchPreflight.MultiBuyUnavailable,
                    "The requested research amount could not be applied: " + scopeReason);
            using (scope)
                return Execute(in action, native, target, in before);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return ResearchSubmission.Reject(ResearchPreflight.ContractUnavailable,
                "Research preflight failed before mutation: " + exception.GetBaseException().Message);
        }
    }

    internal void InvalidateLifecycle()
    { _bindings = null; _bindingFailure = string.Empty; BindLifecycle(); }

    public void Dispose()
    { _bindings = null; _bindingFailure = string.Empty; }

    private ResearchSubmission Execute(in ResearchAction action, ResearchNativeBindings native,
        object target, in ResearchAdmissionState before)
    {
        var stage = ResearchNativeStage.NativeCallback;
        try
        {
            Invoke(action.Kind, native, target);
            stage = ResearchNativeStage.Verification;
            return OutcomeLanded(action.Kind, native, target, in before)
                ? Verified()
                : Fault(in action, ResearchPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    "The exact research target did not make the requested state transition.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (OutcomeLandedBestEffort(action.Kind, native, target, in before))
                return Verified();
            return Fault(in action, ResearchPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                "The native research callback threw before the requested outcome was observable: " +
                exception.GetBaseException().Message);
        }
    }

    /// <summary>
    /// Which native gate closed, in the order <c>ResearchSO.IsWithinDevelopRange</c> asks it:
    /// <c>IsComplete()</c>, <c>GetDevelopmentCost().HasEnough()</c>,
    /// <c>MeetsLevelRequirements()</c>, then <c>StillHasLeeway()</c> falling back to
    /// <c>IsBelowArtificialMaxLevel() &amp;&amp; IsBelowMaxInvestmentLevel()</c>.
    /// <c>CanDevelop()</c> adds <c>!IsDeveloping()</c> on top of that whole expression.
    /// Returns <c>Proceeded</c> when every gate is open and the zero came from somewhere else.
    /// </summary>
    private static ResearchPreflight DevelopBlocker(
        in ResearchAdmissionState state, out string reason)
    {
        reason = string.Empty;
        if (state.Complete)
        { reason = "This research is already maxed."; return ResearchPreflight.AlreadyMaxed; }
        if (!state.CostAffordable)
        { reason = "The next research level costs more than is held."; return ResearchPreflight.Unaffordable; }
        if (!state.MeetsLevelRequirements)
        { reason = "This research does not meet its level requirements yet."; return ResearchPreflight.RequirementsUnmet; }
        // Leeway alone does not close the gate: native accepts leeway OR both caps being open, so
        // this is one gate with one code, and the sentence names the cap that also ran out.
        if (!state.StillHasLeeway &&
            !(state.BelowArtificialMaxLevel && state.BelowMaxInvestmentLevel))
        {
            reason = "This research has no leeway left and is at its " +
                (state.BelowArtificialMaxLevel ? "investment" : "level") + " cap.";
            return ResearchPreflight.LeewayExhausted;
        }
        if (state.IsDeveloping && !state.QueueMode)
        { reason = "A development is already running on this research."; return ResearchPreflight.AlreadyDeveloping; }
        return ResearchPreflight.Proceeded;
    }

    private static ResearchPreflight Preflight(ResearchActionKind kind,
        in ResearchAdmissionState state, out string reason)
    {
        reason = string.Empty;
        switch (kind)
        {
            case ResearchActionKind.Develop:
                // Zero available levels is never an over-ask: no amount would have been admitted,
                // and the caller needs the gate that closed, not the number it closed at. The row
                // already publishes that gate, so the action reads it from the same native
                // predicates rather than folding every cause into one amount refusal.
                if (state.LevelsAvailable <= 0)
                {
                    var blocked = DevelopBlocker(in state, out reason);
                    if (blocked != ResearchPreflight.Proceeded) return blocked;
                }
                if (state.MultiBuy > state.LevelsAvailable)
                {
                    // The ceiling is what this one call admits, and the two modes cap it for
                    // different reasons: with the queue off a develop starts exactly one
                    // development, and with it on the cap is what the levels cost together at this
                    // instant. Neither is a remaining budget, and a sentence that only said "right
                    // now" was read as one — a caller stopped at "at most 1" with five more single
                    // develops waiting to be admitted.
                    var levels = state.LevelsAvailable +
                        (state.LevelsAvailable == 1 ? " level" : " levels");
                    reason = state.QueueMode
                        ? "This call can queue at most " + levels +
                            " with what is held now; a later call is admitted against what is " +
                            "affordable then."
                        : "Research Queue Mode is off, so one develop starts one level and this " +
                            "call takes at most " + levels + ".";
                    return ResearchPreflight.AmountUnavailable;
                }
                if (state.QueueMode)
                {
                    if (state.MultiBuy <= 0)
                    { reason = "The native multi-buy setting permits no queued research level."; return ResearchPreflight.MultiBuyUnavailable; }
                    if (state.MaxLevel > 0 && state.Level + state.QueuedLevels >= state.MaxLevel)
                    { reason = "The research queue has no room below its authored maximum level."; return ResearchPreflight.DevelopUnavailable; }
                    if (state.LevelsAvailable <= 0)
                    { reason = "No queued research level is currently available."; return ResearchPreflight.DevelopUnavailable; }
                }
                else if (!state.CanDevelop || !state.CostAffordable)
                { reason = "The next research level is unavailable or unaffordable."; return ResearchPreflight.DevelopUnavailable; }
                return ResearchPreflight.Proceeded;
            case ResearchActionKind.Pause:
            case ResearchActionKind.Resume:
                if (state.QueueMode)
                { reason = "Pause and resume are UI-reachable only when Research Queue Mode is disabled."; return ResearchPreflight.InvalidMode; }
                if (!state.IsDeveloping || (kind == ResearchActionKind.Pause ? !state.IsActive : state.IsActive))
                {
                    var observed = !state.IsDeveloping
                        ? "idle"
                        : state.IsActive ? "active" : "paused";
                    var required = kind == ResearchActionKind.Pause ? "active" : "paused";
                    reason = "Research is " + observed + "; " +
                        (kind == ResearchActionKind.Pause ? "pause" : "resume") +
                        " requires " + required + " development.";
                    return ResearchPreflight.InvalidState;
                }
                return ResearchPreflight.Proceeded;
            case ResearchActionKind.Cancel:
                if (!state.IsDeveloping)
                { reason = "The research has no active development to cancel."; return ResearchPreflight.InvalidState; }
                return ResearchPreflight.Proceeded;
            case ResearchActionKind.Bonus:
                if (state.IsDeveloping)
                { reason = "The game hides bonus-level submission while research is developing."; return ResearchPreflight.InvalidState; }
                if (!state.CanApplyBonusLevel || state.FreeBonusLevels <= 0)
                { reason = "No associated research type has a free bonus level available."; return ResearchPreflight.BonusUnavailable; }
                return ResearchPreflight.Proceeded;
            default:
                reason = "The research mode is unsupported.";
                return ResearchPreflight.InvalidMode;
        }
    }

    private static void Invoke(ResearchActionKind kind, ResearchNativeBindings native, object target)
    {
        switch (kind)
        {
            case ResearchActionKind.Develop: native.Purchase(target); break;
            case ResearchActionKind.Pause: native.Pause(target); break;
            case ResearchActionKind.Resume: native.Resume(target); break;
            case ResearchActionKind.Cancel: native.Cancel(target); break;
            case ResearchActionKind.Bonus: native.SubmitBonus(target); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static ResearchAdmissionState Capture(ResearchNativeBindings native, object target, int amount)
    {
        var cost = native.DevelopmentCost(target) ??
            throw new InvalidOperationException("ResearchSO.GetDevelopmentCost returned null.");
        return new ResearchAdmissionState(native.QueueMode(), amount,
            native.Level(target), native.QueuedLevels(target), native.SelfBonusLevels(target),
            native.IsActive(target), native.IsDeveloping(target), native.CanDevelop(target),
            native.CanApplyBonusLevel(target), native.FreeBonusLevels(target),
            native.HasEnough(cost), native.MaxLevel(target), QueueableLevels(native, target, amount),
            native.Complete(target), native.MeetsLevelRequirements(target),
            native.StillHasLeeway(target), native.BelowArtificialMaxLevel(target),
            native.BelowMaxInvestmentLevel(target));
    }

    private static int QueueableLevels(ResearchNativeBindings native, object target, int amount)
    {
        if (!native.QueueMode())
        {
            var cost = native.DevelopmentCost(target) ??
                throw new InvalidOperationException("ResearchSO.GetDevelopmentCost returned null.");
            return native.CanDevelop(target) && native.HasEnough(cost) ? 1 : 0;
        }
        var currentQueued = native.QueuedLevels(target);
        var limit = amount;
        if (native.HasMaxLevel(target))
            limit = Math.Min(limit,
                Math.Max(native.MaxLevel(target) - currentQueued - native.Level(target), 0));
        object? aggregate = null;
        var levels = 0;
        for (var index = 0; index < limit; index++)
        {
            var atLevel = checked(native.Level(target) + currentQueued + index);
            var next = native.DevelopmentCostAtLevel(target, checked(atLevel + 1)) ??
                throw new InvalidOperationException("ResearchSO.GetDevelopmentCostAtLevel returned null.");
            aggregate = aggregate is null ? next : native.AddCost(aggregate, next) ??
                throw new InvalidOperationException("ResourceCostList.Add returned null.");
            if (!native.HasEnough(aggregate) || !native.WithinDevelopRangeAt(target, atLevel)) break;
            levels++;
        }
        return levels;
    }

    private static bool OutcomeLanded(ResearchActionKind kind,
        ResearchNativeBindings native, object target,
        in ResearchAdmissionState before) => kind switch
    {
        ResearchActionKind.Develop when before.QueueMode =>
            native.QueuedLevels(target) > before.QueuedLevels,
        ResearchActionKind.Develop =>
            !before.IsDeveloping && native.IsDeveloping(target),
        ResearchActionKind.Pause =>
            before.IsActive && !native.IsActive(target),
        ResearchActionKind.Resume =>
            !before.IsActive && native.IsActive(target),
        ResearchActionKind.Cancel =>
            before.IsDeveloping && !native.IsDeveloping(target),
        ResearchActionKind.Bonus =>
            native.SelfBonusLevels(target) == checked(before.SelfBonusLevels + 1),
        _ => false,
    };

    private static bool OutcomeLandedBestEffort(ResearchActionKind kind,
        ResearchNativeBindings native, object target, in ResearchAdmissionState before)
    {
        try { return OutcomeLanded(kind, native, target, in before); }
        catch (Exception exception) when (IsExpected(exception)) { return false; }
    }

    private static ResearchSubmission Verified() =>
        new(ResearchPreflight.Proceeded, ResearchNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
            "The requested research transition is visible.");

    private static ResearchSubmission Fault(in ResearchAction action, ResearchPreflight preflight,
        ResearchNativeStage stage, NativeMutationOutcome outcome,
        string reason)
    {
        var exactReason = "Research " + stage + " failed on " +
            EntityIdentityFormatter.PlayerName(action.TargetId) + ": " + reason;
        return new ResearchSubmission(preflight, stage, outcome,
            new NativeMutationCallOutcome(1, 1, 0), exactReason);
    }

    private void BindLifecycle()
    {
        if (ResearchNativeBindings.TryCreate(out var bindings, out var reason, _resolveType, _includeContract))
        { _bindings = bindings; _bindingFailure = string.Empty; return; }
        _bindings = null; _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) => exception is InvalidOperationException or
        ArgumentException or TargetInvocationException or OverflowException;
}
