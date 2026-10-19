using System;
using OrbModding.Common.Runtime;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

public enum ServiceActionDisposition
{
    Committed = 1,
    Rejected = 2,
    Faulted = 3,
    Skipped = 4,
}

public readonly struct ServiceActionResultCode : IEquatable<ServiceActionResultCode>
{
    public const int FirstFeatureCode = 1024;

    /// <summary>A feature code and the one wire name every surface answers it with.</summary>
    /// <remarks>
    /// Feature numbers are namespaced per feature and reused across features, so the number alone
    /// cannot say which refusal it is. The name travels on the code instead of in a table keyed by
    /// number, so two features sharing a number can never answer with each other's name.
    /// Equality stays by number.
    /// </remarks>
    public ServiceActionResultCode(int value, string name)
    {
        if (value < FirstFeatureCode)
            throw new ArgumentOutOfRangeException(nameof(value), "Feature action-result codes must use the feature-reserved range.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Every action-result code carries its wire name.", nameof(name));
        Value = value;
        _name = name;
    }

    private ServiceActionResultCode(int value, string name, bool reserved)
    {
        Value = value;
        _name = name;
    }

    private readonly string? _name;

    public int Value { get; }
    public string Name => _name ?? string.Empty;
    public bool IsValid => Value is >= 1 and <= 8 || Value >= FirstFeatureCode;
    internal bool IsFeatureCode => Value >= FirstFeatureCode;
    internal static ServiceActionResultCode Reserved(int value, string name) => new(value, name, true);
    public bool Equals(ServiceActionResultCode other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ServiceActionResultCode other && Equals(other);
    public override int GetHashCode() => Value;
    public override string ToString() => Value.ToString();
    public static bool operator ==(ServiceActionResultCode left, ServiceActionResultCode right) => left.Equals(right);
    public static bool operator !=(ServiceActionResultCode left, ServiceActionResultCode right) => !left.Equals(right);
}

public static class CommonActionResultCodes
{
    public static ServiceActionResultCode Committed => ServiceActionResultCode.Reserved(1, "committed");
    public static ServiceActionResultCode EmergencyStop => ServiceActionResultCode.Reserved(2, "emergency_stop");
    public static ServiceActionResultCode LifecycleReplaced => ServiceActionResultCode.Reserved(3, "lifecycle_replaced");
    public static ServiceActionResultCode ServiceDisabled => ServiceActionResultCode.Reserved(4, "service_disabled");
    public static ServiceActionResultCode NativeRejected => ServiceActionResultCode.Reserved(5, "native_rejected");
    public static ServiceActionResultCode PolicyRejected => ServiceActionResultCode.Reserved(6, "policy_rejected");
    public static ServiceActionResultCode AdapterFault => ServiceActionResultCode.Reserved(7, "adapter_fault");
    public static ServiceActionResultCode Skipped => ServiceActionResultCode.Reserved(8, "skipped");
}

internal static class NativeMutationCallOutcomeValidation
{
    internal static void Validate(in NativeMutationCallOutcome outcome, string parameterName)
    {
        if (outcome.NativeCallsAttempted < 0 ||
            outcome.MutationAttempts < 0 ||
            outcome.MutationsCommitted < 0 ||
            outcome.MutationAttempts > outcome.NativeCallsAttempted ||
            outcome.MutationsCommitted > outcome.MutationAttempts)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

}

/// <summary>
/// Batch-wide native-call evidence. Per-action evidence remains the game's compact integer shape;
/// accumulation uses 64-bit counters so bookkeeping cannot overflow after a native mutation committed.
/// </summary>
public readonly struct ServiceNativeCallTotals
{
    public ServiceNativeCallTotals(long nativeCallsAttempted, long mutationAttempts, long mutationsCommitted)
    {
        if (nativeCallsAttempted < 0 || mutationAttempts < 0 || mutationsCommitted < 0 ||
            mutationAttempts > nativeCallsAttempted || mutationsCommitted > mutationAttempts)
            throw new ArgumentOutOfRangeException(nameof(nativeCallsAttempted));
        NativeCallsAttempted = nativeCallsAttempted;
        MutationAttempts = mutationAttempts;
        MutationsCommitted = mutationsCommitted;
    }

    public long NativeCallsAttempted { get; }
    public long MutationAttempts { get; }
    public long MutationsCommitted { get; }

    public static implicit operator ServiceNativeCallTotals(NativeMutationCallOutcome outcome) =>
        From(in outcome);

    internal static ServiceNativeCallTotals From(in NativeMutationCallOutcome outcome)
    {
        NativeMutationCallOutcomeValidation.Validate(in outcome, nameof(outcome));
        return new ServiceNativeCallTotals(
            outcome.NativeCallsAttempted,
            outcome.MutationAttempts,
            outcome.MutationsCommitted);
    }

    internal static ServiceNativeCallTotals Add(
        in ServiceNativeCallTotals left,
        in NativeMutationCallOutcome right)
    {
        NativeMutationCallOutcomeValidation.Validate(in right, nameof(right));
        return new ServiceNativeCallTotals(
            checked(left.NativeCallsAttempted + right.NativeCallsAttempted),
            checked(left.MutationAttempts + right.MutationAttempts),
            checked(left.MutationsCommitted + right.MutationsCommitted));
    }
}

public readonly struct ServiceNativeMutationEvidence
{
    private ServiceNativeMutationEvidence(
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome)
    {
        Outcome = outcome;
        CallOutcome = callOutcome;
    }

    public NativeMutationOutcome Outcome { get; }
    public NativeMutationCallOutcome CallOutcome { get; }
    public bool IsValid => IsCoherent(Outcome, CallOutcome);

    public static ServiceNativeMutationEvidence Observed(
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome)
    {
        if (!IsCoherent(outcome, callOutcome))
            throw new ArgumentException("Native outcome and call counts are not coherent.", nameof(callOutcome));
        return new ServiceNativeMutationEvidence(outcome, callOutcome);
    }

    private static bool IsCoherent(
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome)
    {
        if (callOutcome.NativeCallsAttempted < 0 ||
            callOutcome.MutationAttempts < 0 ||
            callOutcome.MutationsCommitted < 0 ||
            callOutcome.MutationAttempts > callOutcome.NativeCallsAttempted ||
            callOutcome.MutationsCommitted > callOutcome.MutationAttempts)
        {
            return false;
        }

        return outcome switch
        {
            NativeMutationOutcome.Verified =>
                callOutcome.MutationAttempts > 0 &&
                callOutcome.MutationsCommitted == callOutcome.MutationAttempts,
            NativeMutationOutcome.BeforeCaptureFailed =>
                callOutcome.NativeCallsAttempted == 0 &&
                callOutcome.MutationAttempts == 0 &&
                callOutcome.MutationsCommitted == 0,
            NativeMutationOutcome.ExecutionThrew or
            NativeMutationOutcome.AfterCaptureFailed or
            NativeMutationOutcome.PostconditionFailed =>
                callOutcome.MutationAttempts > 0 &&
                callOutcome.MutationsCommitted == 0,
            _ => false,
        };
    }
}

public readonly struct ServiceActionResult
{
    private ServiceActionResult(
        ServiceActionDisposition disposition,
        ServiceActionResultCode code,
        ServiceActionEffect effect,
        bool hasNativeEvidence,
        ServiceNativeMutationEvidence nativeEvidence,
        ServicePublicationEvidence publicationEvidence,
        string reason,
        bool backsOffTarget = false,
        MonotonicDuration? targetBackoffCap = null,
        bool quietFirstFailure = false)
    {
        Disposition = disposition;
        Code = code;
        Effect = effect;
        HasNativeEvidence = hasNativeEvidence;
        NativeEvidence = nativeEvidence;
        PublicationEvidence = publicationEvidence;
        _reason = reason;
        BacksOffTarget = backsOffTarget;
        TargetBackoffCap = targetBackoffCap;
        QuietFirstFailure = quietFirstFailure;
    }

    private readonly string? _reason;

    public ServiceActionDisposition Disposition { get; }
    public ServiceActionResultCode Code { get; }

    /// <summary>
    /// What kind of effect this action had. A disposition says whether the action succeeded; the
    /// effect says what succeeding meant, and therefore which evidence has to back it.
    /// </summary>
    public ServiceActionEffect Effect { get; }

    public bool HasNativeEvidence { get; }
    public ServiceNativeMutationEvidence NativeEvidence { get; }
    public ServicePublicationEvidence PublicationEvidence { get; }

    /// <summary>The exact sentence a refusal or fault gives; empty on success.</summary>
    public string Reason => _reason ?? string.Empty;

    public bool HasPublicationEvidence => Effect == ServiceActionEffect.Publication;

    /// <summary>
    /// The refusal backs its own target off, and the runner retries that target when due exactly as
    /// it retries one whose action faulted.
    /// </summary>
    public bool BacksOffTarget { get; }

    /// <summary>The longest wait the doubling may reach for this target; the service's policy maximum when absent.</summary>
    public MonotonicDuration? TargetBackoffCap { get; }

    /// <summary>
    /// The target's first refusal is one the game gives now and then, so it backs off without being
    /// announced until a later world refuses it again.
    /// </summary>
    public bool QuietFirstFailure { get; }

    /// <summary>
    /// Native-call totals for this action. A publishing action contributes zeroes, which is the
    /// truth: it made no native call. Reporting an attempt so that it looked like every other
    /// committed action would corrupt the budget for the services that really do call the game.
    /// </summary>
    public NativeMutationCallOutcome NativeCallOutcome =>
        HasNativeEvidence ? NativeEvidence.CallOutcome : default;

    public bool IsValid => Code.IsValid && IsAllowedCode(Code, Disposition) &&
        (!BacksOffTarget || Disposition is ServiceActionDisposition.Skipped or ServiceActionDisposition.Rejected) &&
        (BacksOffTarget || TargetBackoffCap is null && !QuietFirstFailure) &&
        Disposition switch
    {
        ServiceActionDisposition.Committed => Effect switch
        {
            ServiceActionEffect.NativeMutation =>
                HasNativeEvidence && NativeEvidence.IsValid &&
                NativeEvidence.Outcome == NativeMutationOutcome.Verified,
            ServiceActionEffect.Publication =>
                !HasNativeEvidence && PublicationEvidence.IsValid,
            _ => false,
        },
        ServiceActionDisposition.Skipped =>
            Effect == ServiceActionEffect.None && !HasNativeEvidence ||
            Effect == ServiceActionEffect.NativeMutation &&
                HasNativeEvidence && NativeEvidence.IsValid &&
                NativeEvidence.Outcome == NativeMutationOutcome.PostconditionFailed &&
                NativeEvidence.CallOutcome.MutationAttempts > 0 &&
                NativeEvidence.CallOutcome.MutationsCommitted == 0,
        ServiceActionDisposition.Rejected =>
            Effect == ServiceActionEffect.None && !HasNativeEvidence,
        ServiceActionDisposition.Faulted =>
            Effect != ServiceActionEffect.Publication &&
            (!HasNativeEvidence || NativeEvidence.IsValid && NativeEvidence.Outcome != NativeMutationOutcome.Verified),
        _ => false,
    };

    public static ServiceActionResult Committed(
        ServiceActionResultCode code,
        ServiceNativeMutationEvidence nativeEvidence) =>
        Create(
            ServiceActionDisposition.Committed, code, ServiceActionEffect.NativeMutation,
            true, nativeEvidence, default);

    /// <summary>
    /// A committed action whose effect was handing an immutable snapshot to a publisher rather than
    /// mutating the game.
    /// </summary>
    public static ServiceActionResult CommittedPublication(
        ServiceActionResultCode code,
        ServicePublicationEvidence publicationEvidence) =>
        Create(
            ServiceActionDisposition.Committed, code, ServiceActionEffect.Publication,
            false, default, publicationEvidence);

    public static ServiceActionResult Rejected(ServiceActionResultCode code, string reason = "") =>
        Create(
            ServiceActionDisposition.Rejected, code, ServiceActionEffect.None,
            false, default, default, reason);
    public static ServiceActionResult Skipped(
        ServiceActionResultCode code,
        string reason = "") =>
        Create(
            ServiceActionDisposition.Skipped, code, ServiceActionEffect.None,
            false, default, default, reason);
    public static ServiceActionResult Skipped(
        ServiceActionResultCode code,
        ServiceNativeMutationEvidence nativeEvidence,
        string reason = "") =>
        Create(
            ServiceActionDisposition.Skipped, code, ServiceActionEffect.NativeMutation,
            true, nativeEvidence, default, reason);
    public static ServiceActionResult Faulted(ServiceActionResultCode code, string reason = "") =>
        Create(
            ServiceActionDisposition.Faulted, code, ServiceActionEffect.None,
            false, default, default, reason);
    public static ServiceActionResult Faulted(
        ServiceActionResultCode code,
        ServiceNativeMutationEvidence nativeEvidence,
        string reason = "") =>
        Create(
            ServiceActionDisposition.Faulted, code, ServiceActionEffect.NativeMutation,
            true, nativeEvidence, default, reason);

    /// <summary>
    /// The same result, answered with a sentence its reader composed from facts the action did
    /// not see.
    /// </summary>
    public ServiceActionResult WithReason(string? reason) =>
        new(Disposition, Code, Effect, HasNativeEvidence, NativeEvidence, PublicationEvidence,
            Disposition == ServiceActionDisposition.Committed ? string.Empty : reason ?? string.Empty,
            BacksOffTarget, TargetBackoffCap, QuietFirstFailure);

    /// <summary>
    /// The same refusal, backing its target off on the service's retry gate. A fault always backs its
    /// target off; a refusal does so only when the service says the target, not the moment, is wrong.
    /// </summary>
    public ServiceActionResult BackingOffTarget(
        MonotonicDuration? maximumBackoff = null,
        bool quietFirstFailure = false)
    {
        if (Disposition is not (ServiceActionDisposition.Skipped or ServiceActionDisposition.Rejected))
            throw new InvalidOperationException("Only a skipped or rejected action asks to back its target off.");
        return new ServiceActionResult(
            Disposition, Code, Effect, HasNativeEvidence, NativeEvidence, PublicationEvidence, Reason,
            true, maximumBackoff, quietFirstFailure);
    }

    private static ServiceActionResult Create(
        ServiceActionDisposition disposition,
        ServiceActionResultCode code,
        ServiceActionEffect effect,
        bool hasNativeEvidence,
        ServiceNativeMutationEvidence nativeEvidence,
        ServicePublicationEvidence publicationEvidence,
        string reason = "")
    {
        if (!code.IsValid) throw new ArgumentException("A stable result code is required.", nameof(code));
        if (!IsAllowedCode(code, disposition))
            throw new ArgumentException("Result code does not belong to this action disposition.", nameof(code));
        if (hasNativeEvidence && !nativeEvidence.IsValid)
            throw new ArgumentException("Observed native evidence must be coherent.", nameof(nativeEvidence));
        if (effect == ServiceActionEffect.Publication && hasNativeEvidence)
            throw new ArgumentException(
                "A publishing action cannot carry native mutation evidence.",
                nameof(nativeEvidence));
        if (effect != ServiceActionEffect.Publication && publicationEvidence != default)
            throw new ArgumentException(
                "Only a publishing action may carry publication evidence.",
                nameof(publicationEvidence));
        switch (disposition)
        {
            case ServiceActionDisposition.Committed when effect == ServiceActionEffect.Publication:
                if (!publicationEvidence.IsValid)
                    throw new ArgumentException(
                        "A committed publishing action requires the generation it published.",
                        nameof(publicationEvidence));
                break;
            case ServiceActionDisposition.Committed:
                if (!hasNativeEvidence || nativeEvidence.Outcome != NativeMutationOutcome.Verified)
                    throw new ArgumentException("A committed action requires observed verified native evidence.", nameof(nativeEvidence));
                break;
            case ServiceActionDisposition.Skipped:
                if (effect == ServiceActionEffect.None && !hasNativeEvidence)
                    break;
                if (effect != ServiceActionEffect.NativeMutation ||
                    !hasNativeEvidence ||
                    nativeEvidence.Outcome != NativeMutationOutcome.PostconditionFailed ||
                    nativeEvidence.CallOutcome.MutationAttempts <= 0 ||
                    nativeEvidence.CallOutcome.MutationsCommitted != 0)
                {
                    throw new ArgumentException(
                        "A native skipped action requires observed zero-commit postcondition evidence.",
                        nameof(nativeEvidence));
                }
                break;
            case ServiceActionDisposition.Rejected:
                if (hasNativeEvidence)
                    throw new ArgumentException("A rejected action cannot carry native mutation evidence.", nameof(nativeEvidence));
                break;
            case ServiceActionDisposition.Faulted:
                if (hasNativeEvidence && nativeEvidence.Outcome == NativeMutationOutcome.Verified)
                    throw new ArgumentException("Rejected or faulted actions cannot carry verified native evidence.", nameof(nativeEvidence));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        return new ServiceActionResult(
            disposition, code, effect, hasNativeEvidence, nativeEvidence, publicationEvidence,
            disposition == ServiceActionDisposition.Committed ? string.Empty : reason ?? string.Empty);
    }

    private static bool IsAllowedCode(
        ServiceActionResultCode code,
        ServiceActionDisposition disposition)
    {
        if (code.IsFeatureCode) return true;

        return disposition switch
        {
            ServiceActionDisposition.Committed => code == CommonActionResultCodes.Committed,
            ServiceActionDisposition.Skipped => code == CommonActionResultCodes.Skipped,
            ServiceActionDisposition.Rejected =>
                code == CommonActionResultCodes.EmergencyStop ||
                code == CommonActionResultCodes.LifecycleReplaced ||
                code == CommonActionResultCodes.ServiceDisabled ||
                code == CommonActionResultCodes.NativeRejected ||
                code == CommonActionResultCodes.PolicyRejected,
            ServiceActionDisposition.Faulted => code == CommonActionResultCodes.AdapterFault,
            _ => false,
        };
    }

    public static ServiceNativeCallTotals AddNativeOutcomes(
        in ServiceNativeCallTotals left,
        in NativeMutationCallOutcome right) =>
        ServiceNativeCallTotals.Add(in left, in right);
}
