using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class ResearchGameAction : NativeGameAction<ResearchNativeBindings>
{
    private const string Screen = "Scholar > Research";

    private static readonly GameActionCodes Codes = new(
        ResearchActionResultCodes.WrongThread,
        ResearchActionResultCodes.ContractUnavailable,
        ResearchActionResultCodes.MutationPermitUnavailable,
        ResearchActionResultCodes.PostCommitFault,
        ResearchActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal ResearchGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Research", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out ResearchNativeBindings? bindings, out string reason) =>
                ResearchNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal ResearchSubmission Submit(in ResearchAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new ResearchSubmission(outcome),
            native => Press(in request, native));
    }

    private ResearchSubmission Press(in ResearchAction action, ResearchNativeBindings native)
    {
        var resolution = _registry.Resolve(action.TargetId, native.ResearchType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution))
            return ResearchSubmission.Reject(ResearchActionResultCodes.IdentityUnavailable,
                resolution.IsResolved ? GameActionAnswer.Replaced("research") : resolution.Cause);
        var target = resolution.Value!;
        var before = Capture(native, target, action.Amount);
        var preflight = Preflight(action.Kind, in before, out var reason);
        if (preflight != CommonActionResultCodes.Committed)
            return ResearchSubmission.Reject(
                preflight,
                reason,
                preflight == ResearchActionResultCodes.AmountUnavailable
                    ? before.LevelsAvailable
                    : -1);
        if (!TryPermit(out var refusal)) return new ResearchSubmission(refusal);
        var kind = action.Kind;
        NativeMultiBuyScope? scope = null;
        if (kind == ResearchActionKind.Develop &&
            !NativeMultiBuyScope.TryEnter(action.Amount, out scope, out var scopeReason))
            return ResearchSubmission.Reject(ResearchActionResultCodes.MultiBuyUnavailable,
                "The requested research amount could not be applied: " + scopeReason);

        return new ResearchSubmission(Execute(EntityIdentityFormatter.PlayerName(action.TargetId),
            "The requested research transition is visible.", false,
            () => OutcomeLanded(kind, native, target, in before),
            () =>
            {
                using (scope) Invoke(kind, native, target);
            },
            static (_, landed) => landed));
    }

    /// <summary>
    /// Which native gate closed, in the order <c>ResearchSO.IsWithinDevelopRange</c> asks it:
    /// <c>IsComplete()</c>, <c>GetDevelopmentCost().HasEnough()</c>,
    /// <c>MeetsLevelRequirements()</c>, then <c>StillHasLeeway()</c> falling back to
    /// <c>IsBelowArtificialMaxLevel() &amp;&amp; IsBelowMaxInvestmentLevel()</c>.
    /// <c>CanDevelop()</c> adds <c>!IsDeveloping()</c> on top of that whole expression.
    /// Returns <c>Proceeded</c> when every gate is open and the zero came from somewhere else.
    /// </summary>
    private static ServiceActionResultCode DevelopBlocker(
        in ResearchAdmissionState state, out string reason)
    {
        reason = string.Empty;
        if (state.Complete)
        { reason = "This research is already maxed."; return ResearchActionResultCodes.AlreadyMaxed; }
        if (!state.CostAffordable)
        { reason = "The next research level costs more than is held."; return ResearchActionResultCodes.Unaffordable; }
        if (!state.MeetsLevelRequirements)
        { reason = "This research does not meet its level requirements yet."; return ResearchActionResultCodes.RequirementsUnmet; }
        // Leeway alone does not close the gate: native accepts leeway OR both caps being open, so
        // this is one gate with one code, and the sentence names the cap that also ran out.
        if (!state.StillHasLeeway &&
            !(state.BelowArtificialMaxLevel && state.BelowMaxInvestmentLevel))
        {
            reason = "This research has no leeway left and is at its " +
                (state.BelowArtificialMaxLevel ? "investment" : "level") + " cap.";
            return ResearchActionResultCodes.LeewayExhausted;
        }
        if (state.IsDeveloping && !state.QueueMode)
        { reason = "A development is already running on this research."; return ResearchActionResultCodes.AlreadyDeveloping; }
        return CommonActionResultCodes.Committed;
    }

    private static ServiceActionResultCode Preflight(ResearchActionKind kind,
        in ResearchAdmissionState state, out string reason)
    {
        reason = string.Empty;
        if ((kind is ResearchActionKind.Develop or ResearchActionKind.Bonus) && !state.Visible)
        {
            reason = "This research is locked, so the Research grid draws no tile for it.";
            return ResearchActionResultCodes.Locked;
        }
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
                    if (blocked != CommonActionResultCodes.Committed) return blocked;
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
                    return ResearchActionResultCodes.AmountUnavailable;
                }
                if (state.QueueMode)
                {
                    if (state.MultiBuy <= 0)
                    { reason = "The native multi-buy setting permits no queued research level."; return ResearchActionResultCodes.MultiBuyUnavailable; }
                    if (state.MaxLevel > 0 && state.Level + state.QueuedLevels >= state.MaxLevel)
                    { reason = "The research queue has no room below its authored maximum level."; return ResearchActionResultCodes.DevelopUnavailable; }
                    if (state.LevelsAvailable <= 0)
                    { reason = "No queued research level is currently available."; return ResearchActionResultCodes.DevelopUnavailable; }
                }
                else if (!state.CanDevelop || !state.CostAffordable)
                { reason = "The next research level is unavailable or unaffordable."; return ResearchActionResultCodes.DevelopUnavailable; }
                return CommonActionResultCodes.Committed;
            case ResearchActionKind.Pause:
            case ResearchActionKind.Resume:
                // Nothing is wrong with the mode the caller named; the game's own queue setting is
                // what closes the two controls, and turning it off reopens them.
                if (state.QueueMode)
                { reason = "Pause and resume are UI-reachable only when Research Queue Mode is disabled."; return ResearchActionResultCodes.InvalidState; }
                if (!state.IsDeveloping || (kind == ResearchActionKind.Pause ? !state.IsActive : state.IsActive))
                {
                    var observed = !state.IsDeveloping
                        ? "idle"
                        : state.IsActive ? "active" : "paused";
                    var required = kind == ResearchActionKind.Pause ? "active" : "paused";
                    reason = "Research is " + observed + "; " +
                        (kind == ResearchActionKind.Pause ? "pause" : "resume") +
                        " requires " + required + " development.";
                    return ResearchActionResultCodes.InvalidState;
                }
                return CommonActionResultCodes.Committed;
            case ResearchActionKind.Cancel:
                if (!state.IsDeveloping)
                { reason = "The research has no active development to cancel."; return ResearchActionResultCodes.InvalidState; }
                return CommonActionResultCodes.Committed;
            case ResearchActionKind.Bonus:
                if (state.IsDeveloping)
                { reason = "The game hides bonus-level submission while research is developing."; return ResearchActionResultCodes.InvalidState; }
                if (!state.CanApplyBonusLevel || state.FreeBonusLevels <= 0)
                { reason = "No associated research type has a free bonus level available."; return ResearchActionResultCodes.BonusUnavailable; }
                return CommonActionResultCodes.Committed;
            default:
                reason =
                    "The suite sent a control this screen has no button for; nothing was applied.";
                return ResearchActionResultCodes.InvalidMode;
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
            native.BelowMaxInvestmentLevel(target), native.IsVisible(target));
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
}
