using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The neutral outcome of one native level submission: either a preflight rejection with no mutation,
/// or an attempted audited mutation carrying its outcome and call evidence. It never exposes a native
/// object — the action port maps it to a service result.
/// </summary>
internal readonly struct SpellLevelSubmission
{
    private SpellLevelSubmission(
        NativeActionOutcome outcome,
        bool paidWithoutLevel = false
#if SERVICE_CYCLE_PROFILE
        , ResourceCharge[]? charges = null
#endif
        )
    {
        Outcome = outcome;
        PaidWithoutLevel = paidWithoutLevel;
#if SERVICE_CYCLE_PROFILE
        _charges = charges;
#endif
    }

    public NativeActionOutcome Outcome { get; }

    /// <summary>
    /// The game was pressed past payment and no level rose, so the spell backs off as long as any
    /// action the game charged for without delivering.
    /// </summary>
    public bool PaidWithoutLevel { get; }

#if SERVICE_CYCLE_PROFILE
    private readonly ResourceCharge[]? _charges;

    /// <summary>
    /// The price the game named for a single level on the frame that paid it. A level-all press
    /// is the game's own batch, which names no price to the suite, so it carries none. Only the
    /// strategist's mastery press reports it, so only a profile build reads it.
    /// </summary>
    public ReadOnlySpan<ResourceCharge> Charges =>
        _charges is null ? default : _charges.AsSpan();
#endif

    public static SpellLevelSubmission FromAdmission(NativeActionOutcome outcome) =>
        new(outcome.Code == CommonActionResultCodes.AdapterFault && !outcome.IsFault
            ? NativeActionOutcome.FaultBeforeCall(outcome.Code, outcome.Cause)
            : outcome);

    public static SpellLevelSubmission Rejected(ServiceActionResultCode code, string reason) =>
        new(NativeActionOutcome.Reject(code, reason));

    public static SpellLevelSubmission ContractUnavailable(GameActionReason reason) =>
        new(NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, reason));

    public static SpellLevelSubmission Attempted<TState>(
        NativeMutationEvidence<TState> evidence, string pressed, string noRise)
    {
        var calls = NativeMutationCallOutcome.FromEvidence(evidence);
        if (evidence.IsVerified) return new(NativeActionOutcome.Commit(calls, string.Empty));
        if (!evidence.MutationWasAttempted)
            return new(NativeActionOutcome.Fault(
                CommonActionResultCodes.AdapterFault,
                evidence.Outcome,
                calls,
                Logged("spell-level read failed before the press.", evidence.Exception)));
        var reason = !evidence.HasAfter
            ? $"{pressed} and could not read mastery levels after the press."
            : evidence.Exception is null
                ? $"{pressed} {noRise}."
                : $"{pressed} {noRise} after the game threw.";
        return new(
            NativeActionOutcome.Fault(
                CommonActionResultCodes.AdapterFault, evidence.Outcome, calls, Logged(reason, evidence.Exception)),
            paidWithoutLevel: true);
    }

    public static SpellLevelSubmission ThrewAfterPayment(string pressed, Exception exception) =>
        new(
            NativeActionOutcome.Fault(
                CommonActionResultCodes.AdapterFault,
                NativeMutationOutcome.ExecutionThrew,
                new NativeMutationCallOutcome(1, 1, 0),
                Logged($"{pressed} and no rise was seen after the game threw.", exception)),
            paidWithoutLevel: true);

#if SERVICE_CYCLE_PROFILE
    public SpellLevelSubmission WithCharges(ResourceCharge[]? charges) =>
        new(Outcome, PaidWithoutLevel, charges);
#endif

    private static GameActionReason Logged(string sentence, Exception? exception) =>
        GameActionFaultLog.Reason(sentence, exception, SpellLevelGameAction.Feature);
}
