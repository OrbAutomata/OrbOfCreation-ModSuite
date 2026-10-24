using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

/// <summary>
/// Which of the game's own gates the suite watched shut on a group that stopped short of the levels
/// it was asked for.
/// </summary>
/// <remarks>
/// Only the structure family reaches anything but <see cref="None"/>: the suite drives that one
/// level at a time and re-runs the game's gates itself between levels, so it sees which one said no.
/// An upgrade multi-buy breaks inside the game's own loop and the suite sees only the delta, and a
/// reason invented for it would be the confident face the queued-mutation answer exists to refuse.
/// </remarks>
internal enum AutoBuyGroupStop
{
    /// <summary>The group ran to its full count, or nothing observed why it did not.</summary>
    None,

    /// <summary>The next level's price was not met.</summary>
    NextLevelUnaffordable,

    /// <summary>The game stopped admitting the purchase.</summary>
    NotAdmitted,
}

/// <summary>
/// The neutral outcome of one native purchase submission: either a preflight rejection with no
/// mutation, or an attempted audited mutation carrying its <see cref="NativeMutationOutcome"/> and
/// call evidence. It never exposes a native object — the action port maps it to a service result.
/// </summary>
internal readonly struct AutoBuyPurchaseSubmission
{
    private AutoBuyPurchaseSubmission(
        NativeActionOutcome outcome,
        int requestedLevels,
        int committedLevels,
        in AutoBuyAdmissionDiagnosis diagnosis,
        in AutoBuyLiveCostSnapshot liveCosts,
        int maximumAmount = -1,
        int withheldBySuite = 0,
        AutoBuyGroupStop groupStop = AutoBuyGroupStop.None,
        AutoBuyLiveCostSnapshot nextLevelCosts = default)
    {
        NextLevelCosts = nextLevelCosts;
        GroupStop = groupStop;
        WithheldBySuite = withheldBySuite;
        Outcome = outcome;
        RequestedLevels = requestedLevels;
        CommittedLevels = committedLevels;
        Diagnosis = diagnosis;
        LiveCosts = liveCosts;
        MaximumAmount = maximumAmount;
    }

    /// <summary>
    /// The code, reason and call evidence of this submission. A topology refusal's reason names the
    /// epoch it is stamped at, the epoch the plan asked for, and how many rows it holds, because
    /// those three numbers are the whole difference between the causes.
    /// </summary>
    public NativeActionOutcome Outcome { get; }

    /// <summary>
    /// Which admission term refused, when the game refused admission. Every other outcome leaves it
    /// unread.
    /// </summary>
    public AutoBuyAdmissionDiagnosis Diagnosis { get; }

    /// <summary>
    /// The native first-level cost rows read immediately before this submission. On a refusal this
    /// is the same snapshot carried by <see cref="Diagnosis"/>.
    /// </summary>
    public AutoBuyLiveCostSnapshot LiveCosts { get; }

    /// <summary>How many levels this single native call was asked to buy (the "Y" of "X of Y").</summary>
    public int RequestedLevels { get; }

    /// <summary>
    /// How many levels the call actually committed (the "X" of "X of Y") — the queued-level delta the
    /// verifier observed. A bulk upgrade may commit fewer than <see cref="RequestedLevels"/> yet still
    /// be a success; only zero is a failure.
    /// </summary>
    public int CommittedLevels { get; }

    /// <summary>
    /// The ceiling a refusal's own sentence named, or -1 where the refusal names none. A sentence
    /// that quotes a number carries that same number as a field, read from the same reading the
    /// sentence was written from, so the two can never disagree.
    /// </summary>
    public int MaximumAmount { get; }

    /// <summary>
    /// How many levels of the ask the suite kept back because the queue had no room for them. Nought
    /// on every press that delivered the whole ask, and on every press the game itself cut short —
    /// this is the suite's own half of a shortfall, and the settled answer owes the caller a reason
    /// for it in the same sentence that reports the delivery.
    /// </summary>
    public int WithheldBySuite { get; }

    /// <summary>
    /// Which of the game's gates the suite watched shut on a group that stopped early, where it
    /// watched at all.
    /// </summary>
    public AutoBuyGroupStop GroupStop { get; }

    /// <summary>
    /// What the level after this press would cost, read live once the press stopped short of the
    /// levels it was asked for. It is read only where the suite could not watch the gate — an
    /// upgrade multi-buy — so the settled answer has a fact to offer in place of a reason it does
    /// not have. Unread everywhere else.
    /// </summary>
    public AutoBuyLiveCostSnapshot NextLevelCosts { get; }

    /// <summary>
    /// The live action queue holds nothing more, so no level of this ask can be queued. No mutation
    /// is attempted, and the ceiling it names is nought: this is the one purchase refusal a smaller
    /// amount does not fix. Reached from the action adapter rather than the native adapter: it is
    /// decided by the live queue reading, before a candidate is resolved. An ask larger than the
    /// room is not this — that one fills what fits and says what it withheld.
    /// </summary>
    public static AutoBuyPurchaseSubmission ActionQueueFull() =>
        new(
            NativeActionOutcome.Reject(AutoBuyActionResultCodes.ActionQueueFull, string.Empty),
            0,
            0,
            default,
            AutoBuyLiveCostSnapshot.Unavailable(
                AutoBuyLiveCostReadStatus.PurchaseCostUnavailable),
            maximumAmount: 0);

    /// <summary>
    /// The same submission, carrying how many levels of the caller's ask never reached the game
    /// because the queue had no room for them.
    /// </summary>
    public AutoBuyPurchaseSubmission WithSuiteWithheld(int withheld) =>
        new(
            Outcome,
            RequestedLevels,
            CommittedLevels,
            Diagnosis,
            LiveCosts,
            MaximumAmount,
            Math.Max(0, withheld),
            GroupStop,
            NextLevelCosts);

    public static AutoBuyPurchaseSubmission Rejected(ServiceActionResultCode code) =>
        Rejected(code, string.Empty);

    public static AutoBuyPurchaseSubmission Rejected(ServiceActionResultCode code, GameActionReason reason) =>
        Refused(NativeActionOutcome.Reject(code, reason), default);

    public static AutoBuyPurchaseSubmission CandidateUnavailable() =>
        Refused(
            NativeActionOutcome.FaultBeforeCall(
                CommonActionResultCodes.AdapterFault,
                "The candidate could not be resolved."),
            default);

    public static AutoBuyPurchaseSubmission ContractUnavailable(GameActionReason reason) =>
        Refused(
            NativeActionOutcome.FaultBeforeCall(CommonActionResultCodes.AdapterFault, reason),
            default);

    public static AutoBuyPurchaseSubmission AffordabilityUnavailable(
        in AutoBuyAdmissionDiagnosis diagnosis) =>
        Refused(
            NativeActionOutcome.FaultBeforeCall(
                CommonActionResultCodes.AdapterFault,
                "Live affordability could not be read."),
            in diagnosis);

    public static AutoBuyPurchaseSubmission NotAdmissible(in AutoBuyAdmissionDiagnosis diagnosis) =>
        Refused(
            diagnosis.Classification == AutoBuyRefusalClassification.AffordabilityChanged
                ? NativeActionOutcome.Skip(CommonActionResultCodes.Skipped, diagnosis.Describe())
                : NativeActionOutcome.Reject(CommonActionResultCodes.NativeRejected, diagnosis.Describe()),
            in diagnosis);

    private static AutoBuyPurchaseSubmission Refused(
        NativeActionOutcome outcome,
        in AutoBuyAdmissionDiagnosis diagnosis)
    {
        var liveCosts = diagnosis.LiveCosts;
        return new AutoBuyPurchaseSubmission(outcome, 0, 0, in diagnosis, in liveCosts);
    }

    public static AutoBuyPurchaseSubmission Attempted(
        NativeMutationEvidence<int> evidence,
        int requestedLevels,
        in AutoBuyLiveCostSnapshot liveCosts,
        AutoBuyGroupStop groupStop = AutoBuyGroupStop.None,
        AutoBuyLiveCostSnapshot nextLevelCosts = default)
    {
        var committed = evidence.HasBefore && evidence.HasAfter
            ? Math.Max(0, evidence.After - evidence.Before)
            : 0;
        var calls = NativeMutationCallOutcome.FromEvidence(evidence);
        return new AutoBuyPurchaseSubmission(
            evidence.IsVerified
                ? NativeActionOutcome.Commit(calls, string.Empty)
                : NativeActionOutcome.Fault(
                    CommonActionResultCodes.AdapterFault,
                    evidence.Outcome,
                    calls,
                    $"The native purchase of {requestedLevels} levels did not apply."),
            requestedLevels,
            committed,
            default,
            in liveCosts,
            maximumAmount: -1,
            withheldBySuite: 0,
            groupStop,
            nextLevelCosts);
    }

    public static AutoBuyPurchaseSubmission Attempted(
        NativeMutationEvidence<int> evidence,
        int requestedLevels)
    {
        var liveCosts = AutoBuyLiveCostSnapshot.Unavailable(
            AutoBuyLiveCostReadStatus.PurchaseCostUnavailable);
        return Attempted(evidence, requestedLevels, in liveCosts);
    }
}

/// <summary>
/// The native execution surface for one Auto Buy purchase, and the only place the service mutates
/// the game. Given a candidate identity the worker planned on, it re-resolves the live native
/// object, revalidates admission against state that can have moved since planning, and submits the
/// purchase through the audited <see cref="NativeMutationVerifier"/>, which reads the queued level
/// either side of the mutation so the returned submission carries evidence of what actually
/// committed rather than what was asked for. A refused submission carries a diagnosis naming which
/// admission term said no.
/// </summary>
internal interface IAutoBuyNativePurchasePort
{
    AutoBuyPurchaseSubmission Submit(
        AutoBuyCandidateKind kind,
        Guid uuid,
        int count,
        long lifecycleEpoch
#if SERVICE_CYCLE_PROFILE
        , in ServiceActionContext context
#endif
        );
}

internal interface IAutoBuyPurchaseTopologyPort
{
    void InvalidateTopology();

    /// <summary>The run the published purchase-screen topology is stamped for, or zero.</summary>
    long CapturedEpoch { get; }

    /// <summary>How many candidates the published topology admits.</summary>
    int CapturedCount { get; }
#if SERVICE_CYCLE_PROFILE
    bool EmitRouteDiagnostic(long lifecycleEpoch, out string? silence);
#endif
}


internal sealed class AutoBuyNativePurchaseAdapter :
    IAutoBuyNativePurchasePort,
    IAutoBuyPurchaseTopologyPort
{
    private readonly NativePurchaseViewAdmissionResolver? _viewAdmission;
    private readonly AutoBuyPurchaseNativeBindings? _bindings;
    private readonly string _bindingFailure;
#if SERVICE_CYCLE_PROFILE
    private readonly AutomataProfileOperations _profileOperations;

    public AutoBuyNativePurchaseAdapter(AutomataProfileOperations profileOperations)
    {
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
        NativePurchaseViewAdmissionResolver.TryCreateProduction(
            ReflectionUtil.FindLoadedType,
            out _viewAdmission,
            out _);
        AutoBuyPurchaseNativeBindings.TryCreate(out _bindings, out _bindingFailure);
    }

    internal AutoBuyNativePurchaseAdapter(
        AutomataProfileOperations profileOperations,
        NativePurchaseViewAdmissionResolver viewAdmission,
        Func<string, bool>? includeContract = null)
    {
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
        _viewAdmission = viewAdmission ?? throw new ArgumentNullException(nameof(viewAdmission));
        AutoBuyPurchaseNativeBindings.TryCreate(
            out _bindings, out _bindingFailure, includeContract: includeContract);
    }
#else
    public AutoBuyNativePurchaseAdapter()
    {
        NativePurchaseViewAdmissionResolver.TryCreateProduction(
            ReflectionUtil.FindLoadedType,
            out _viewAdmission,
            out _);
        AutoBuyPurchaseNativeBindings.TryCreate(out _bindings, out _bindingFailure);
    }

    internal AutoBuyNativePurchaseAdapter(
        NativePurchaseViewAdmissionResolver viewAdmission,
        Func<string, bool>? includeContract = null)
    {
        _viewAdmission = viewAdmission ?? throw new ArgumentNullException(nameof(viewAdmission));
        AutoBuyPurchaseNativeBindings.TryCreate(
            out _bindings, out _bindingFailure, includeContract: includeContract);
    }
#endif

    public void InvalidateTopology() => _viewAdmission?.Invalidate();

    public long CapturedEpoch => _viewAdmission?.CapturedEpoch ?? 0;

    public int CapturedCount => _viewAdmission?.CapturedCount ?? 0;

#if SERVICE_CYCLE_PROFILE
    /// <summary>
    /// Writes one route line per admitted candidate, or explains why it has nothing to write.
    /// </summary>
    /// <remarks>
    /// A silence is only worth reporting once the answer cannot change on its own: a topology
    /// stamped for another run is normal in the first frames of a lifecycle and says nothing, but a
    /// contract that never bound, or a topology published for this run holding no routes, is why
    /// every purchase will be refused for the rest of the run.
    /// </remarks>
    public bool EmitRouteDiagnostic(long lifecycleEpoch, out string? silence)
    {
        silence = null;
        if (_viewAdmission is null)
        {
            silence = "Auto Buy cannot describe its purchase routes: the owning-view topology " +
                "contract never bound, so no purchase will be admitted.";
            return false;
        }

        var rows = _viewAdmission.DescribeCaptured(lifecycleEpoch);
        if (rows.Length == 0)
        {
            if (_viewAdmission.CapturedEpoch == lifecycleEpoch)
            {
                silence = $"Auto Buy's purchase-screen topology is published for run {lifecycleEpoch} " +
                    "and admits nothing, so no purchase will be admitted.";
            }

            return false;
        }

        for (var index = 0; index < rows.Length; index++)
            Plugin.Log?.LogAutomataInfo("Auto Buy route topology: " + rows[index]);
        return true;
    }
#endif

#if !SERVICE_CYCLE_PROFILE
    internal AutoBuyPurchaseSubmission Submit(
        AutoBuyCandidateKind kind,
        Guid uuid,
        int count) => Submit(kind, uuid, count, _viewAdmission?.CapturedEpoch ?? 0);
#endif

    public AutoBuyPurchaseSubmission Submit(
        AutoBuyCandidateKind kind,
        Guid uuid,
        int count,
        long lifecycleEpoch
#if SERVICE_CYCLE_PROFILE
        , in ServiceActionContext context
#endif
        )
    {
        if (kind is not (AutoBuyCandidateKind.Structure or AutoBuyCandidateKind.Upgrade) || count < 1)
            return AutoBuyPurchaseSubmission.CandidateUnavailable();
        if (_bindings is not { } native)
            return AutoBuyPurchaseSubmission.ContractUnavailable(_bindingFailure);

        bool resolved;
        object source;
#if SERVICE_CYCLE_PROFILE
        var resolutionStage = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoBuyActionCandidateResolution,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
#endif
        resolved = TryResolveCandidate(native, kind, uuid, out source);
#if SERVICE_CYCLE_PROFILE
        }
        finally { resolutionStage.Complete(); }
#endif
        if (!resolved)
            return AutoBuyPurchaseSubmission.CandidateUnavailable();

        var readable = false;
        var admitted = false;
#if SERVICE_CYCLE_PROFILE
        var admissionStage = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoBuyActionAdmissionRevalidation,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
#endif
        if (!PassesLiveGate(native, kind, uuid, lifecycleEpoch, source, out var gateRefusal))
            return gateRefusal;

        // The shipped CanPurchase contracts differ materially. StructureSO checks only its
        // per-level requirements and ActionManager.CanLoadAction(); it checks neither IsAvailable()
        // nor affordability. UpgradeSO checks max queued level, affordability, IsAvailable(), queued
        // level requirements, and ActionManager.CanLoadAction(). The explicit gates around this fold
        // close both structure gaps and keep the owning view outside both methods visible here.
        readable = TryReadAdmission(native, kind, source, out admitted);
#if SERVICE_CYCLE_PROFILE
        }
        finally { admissionStage.Complete(); }
#endif
        if (!readable)
            return AutoBuyPurchaseSubmission.CandidateUnavailable();
        if (!admitted)
        {
            // A refusal says either that affordability moved after collection or that the plan and
            // game disagree structurally. Take the fold apart here, on the cold path, while the same
            // native object is in hand.
            return AutoBuyPurchaseSubmission.NotAdmissible(Diagnose(native, kind, source));
        }

        AutoBuyLiveCostSnapshot liveCosts;
        if (kind == AutoBuyCandidateKind.Structure)
        {
            // StructureSO.CanPurchase() omits affordability and Purchase(true) silently no-ops when
            // its cost cannot be paid. Re-read that exact native term before entering the mutation
            // verifier; an unread term is an adapter fault, never evidence that purchase is safe.
            var diagnosis = Diagnose(native, kind, source);
            if (diagnosis.HasEnough == AutoBuyAdmissionTerm.Refused)
            {
                return AutoBuyPurchaseSubmission.NotAdmissible(in diagnosis);
            }
            if (diagnosis.HasEnough != AutoBuyAdmissionTerm.Passed)
            {
                return AutoBuyPurchaseSubmission.AffordabilityUnavailable(in diagnosis);
            }
            liveCosts = diagnosis.LiveCosts;
        }
        else
        {
            liveCosts = ReadLiveCosts(native, kind, source, out _);
        }

        if (kind == AutoBuyCandidateKind.Upgrade)
        {
            var capacity = ReadDestinationCapacity(native, source);
            if (capacity != CommonActionResultCodes.Committed)
                return AutoBuyPurchaseSubmission.Rejected(
                    capacity,
                    AutoBuyPurchaseNarration.DestinationCapacityRefusal(capacity));
        }

        // Preserve the resource identities and first-level terms before the native call spends them.
        // The action adapter keeps these detached values only for this batch, so a later refusal can
        // name earlier purchases that touched the same resources.
#if SERVICE_CYCLE_PROFILE
        var submissionStage = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoBuyActionNativeSubmission,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
#endif
        return kind == AutoBuyCandidateKind.Structure
            ? SubmitStructure(native, uuid, lifecycleEpoch, source, count, in liveCosts)
            : SubmitUpgrade(native, uuid, source, count, in liveCosts);
#if SERVICE_CYCLE_PROFILE
        }
        finally { submissionStage.Complete(); }
#endif
    }

    /// <summary>
    /// The live owning-view gate, with each way of failing it kept apart.
    /// </summary>
    /// <remarks>
    /// Five distinct facts used to leave here as one preflight and one result number: an unbound
    /// contract, a lifecycle with no captured topology, a candidate whose chain the game refused
    /// while the topology was being read, a status this build does not model, and a live
    /// availability read that did not answer. Only one of those is fixed by waiting, only one is a
    /// build problem, and the outage that motivated this split took a code trace to tell apart.
    /// </remarks>
    private bool PassesLiveGate(
        AutoBuyPurchaseNativeBindings native,
        AutoBuyCandidateKind kind,
        Guid uuid,
        long lifecycleEpoch,
        object source,
        out AutoBuyPurchaseSubmission refusal)
    {
        refusal = default;
        if (_viewAdmission is null)
            return Refuse(
                AutoBuyActionResultCodes.OwningViewTopologyUnbound,
                "The suite could not bind the purchase-screen topology this build needs, so no " +
                "purchase can be admitted.",
                out refusal);

        if (!_viewAdmission.TryGetCaptured(
            kind == AutoBuyCandidateKind.Structure
                ? WorldPurchaseCandidateKind.Structure
                : WorldPurchaseCandidateKind.Upgrade,
            uuid,
            lifecycleEpoch,
            out var resolution))
        {
            refusal = AutoBuyPurchaseSubmission.Rejected(
                AutoBuyActionResultCodes.OwningViewTopologyUncaptured,
                AutoBuyPurchaseNarration.TopologyUncaptured(
                    _viewAdmission.CapturedEpoch,
                    lifecycleEpoch,
                    _viewAdmission.CapturedCount));
            return false;
        }
        switch (resolution.Relation.Status)
        {
            case WorldPurchaseViewRelationStatus.Missing:
                return Refuse(
                    AutoBuyActionResultCodes.OwningViewRelationMissing,
                    "No purchase screen for this target is in this run's captured topology.",
                    out refusal);
            case WorldPurchaseViewRelationStatus.Unreadable:
                return Refuse(AutoBuyActionResultCodes.OwningViewRelationUnreadable, resolution.Failure.Reason, out refusal);
            case WorldPurchaseViewRelationStatus.Contradictory:
                return Refuse(AutoBuyActionResultCodes.OwningViewRelationContradictory, resolution.Failure.Reason, out refusal);
            case WorldPurchaseViewRelationStatus.Resolved:
                break;
            default:
                return Refuse(AutoBuyActionResultCodes.OwningViewRelationStatusUnmodeled, out refusal);
        }

        if (!_viewAdmission.TryReadAvailability(in resolution, out var viewAvailable, out var viewFailure))
            return Refuse(AutoBuyActionResultCodes.OwningViewAvailabilityUnreadable, viewFailure.Reason, out refusal);
        if (!viewAvailable)
            return Refuse(
                AutoBuyActionResultCodes.OwningViewUnavailable,
                "The screen this is bought from is not available yet.",
                out refusal);

        if (kind == AutoBuyCandidateKind.Structure)
        {
            if (!TryRead(native.StructureAvailable, source, out var structureAvailable))
            {
                refusal = AutoBuyPurchaseSubmission.CandidateUnavailable();
                return false;
            }
            if (!structureAvailable)
                return Refuse(
                    AutoBuyActionResultCodes.StructureUnavailable,
                    "The game has not unlocked this structure yet.",
                    out refusal);
        }

        return true;
    }

    private static bool Refuse(ServiceActionResultCode code, out AutoBuyPurchaseSubmission refusal) =>
        Refuse(code, string.Empty, out refusal);

    private static bool Refuse(
        ServiceActionResultCode code,
        GameActionReason reason,
        out AutoBuyPurchaseSubmission refusal)
    {
        refusal = AutoBuyPurchaseSubmission.Rejected(code, reason);
        return false;
    }

    /// <summary>
    /// Queues up to <paramref name="count"/> structure levels as one audited mutation.
    /// </summary>
    /// <remarks>
    /// The native structure <c>Purchase(true)</c> forces exactly one level and consults no
    /// multiplier, so bulk development is what it has always been: the same call made again, each
    /// time behind the game's own admission check. One verifier scope spans the group, so the
    /// evidence is the queued-level delta the whole group produced — a group that stops early
    /// because the next level is unaffordable is a partial success, exactly like an upgrade
    /// multi-buy, not a refusal.
    /// </remarks>
    private AutoBuyPurchaseSubmission SubmitStructure(
        AutoBuyPurchaseNativeBindings native,
        Guid uuid,
        long lifecycleEpoch,
        object source,
        int count,
        in AutoBuyLiveCostSnapshot liveCosts)
    {
        var stop = AutoBuyGroupStop.None;
        var evidence = NativeMutationVerifier.Execute(
            "Auto Buy Structure",
            uuid.ToString(),
            count == 1
                ? "GetQueuedQuantity exact delta +1"
                : $"GetQueuedQuantity delta in [1, {count}]",
            () => native.StructureQueued(source),
            () =>
            {
                native.StructurePurchase(source, true);
                for (var level = 1; level < count; level++)
                {
                    if (!PassesLiveGate(
                            native,
                            AutoBuyCandidateKind.Structure,
                            uuid,
                            lifecycleEpoch,
                            source,
                            out _) ||
                        !TryRead(native.StructureCanPurchase, source, out var admitted) || !admitted)
                    {
                        stop = AutoBuyGroupStop.NotAdmitted;
                        break;
                    }
                    if (Diagnose(native, AutoBuyCandidateKind.Structure, source).HasEnough !=
                        AutoBuyAdmissionTerm.Passed)
                    {
                        stop = AutoBuyGroupStop.NextLevelUnaffordable;
                        break;
                    }
                    native.StructurePurchase(source, true);
                }
            },
            (before, after) => count == 1
                ? after == before + 1
                : after > before && after <= before + count);
        return AutoBuyPurchaseSubmission.Attempted(
            evidence, requestedLevels: count, in liveCosts, stop);
    }

    private static AutoBuyPurchaseSubmission SubmitUpgrade(
        AutoBuyPurchaseNativeBindings native,
        Guid uuid,
        object source,
        int count,
        in AutoBuyLiveCostSnapshot liveCosts)
    {
        // The native upgrade Purchase() honours the global multi-buy multiplier, so requesting
        // exactly `count` levels means pinning the multiplier to `count` for the single call. The
        // scope restores the operator's value afterwards; if it cannot guarantee that, no mutation
        // is attempted. The call commits between one and `count` levels (the game may afford fewer):
        // any committed level is a success, only zero is a failure.
        if (!NativeMultiBuyScope.TryEnter(count, out var scope, out var scopeReason))
        {
            return AutoBuyPurchaseSubmission.Rejected(
                AutoBuyActionResultCodes.SingleBuyUnavailable,
                $"The suite could not set the game's multi-buy multiplier to {count}, so no " +
                $"purchase was attempted: {scopeReason}.");
        }

        NativeMutationEvidence<int> evidence;
        using (scope)
        {
            evidence = NativeMutationVerifier.Execute(
                "Auto Buy Upgrade",
                uuid.ToString(),
                $"GetQueuedPurchaseLevel delta in [1, {count}]",
                () => native.UpgradeQueued(source),
                () => native.UpgradePurchase(source),
                (before, after) => after > before && after <= before + count);
        }

        // The game's loop breaks inside Purchase() and tells nobody which of its own conditions bit,
        // so the suite has no reason to give and will not invent one. What it can still read is what
        // the level after this press now costs — the same list the game's next press will gate on —
        // and that is the fact the settled answer offers in place of a cause.
        var stoppedShort = evidence.HasBefore && evidence.HasAfter &&
            evidence.After - evidence.Before < count;
        return AutoBuyPurchaseSubmission.Attempted(
            evidence,
            requestedLevels: count,
            in liveCosts,
            AutoBuyGroupStop.None,
            stoppedShort ? ReadLiveCosts(native, AutoBuyCandidateKind.Upgrade, source, out _) : default);
    }

    private bool TryResolveCandidate(
        AutoBuyPurchaseNativeBindings native,
        AutoBuyCandidateKind kind,
        Guid uuid,
        out object source)
    {
        var resolution = TypedRegistryResolver.Shared.Resolve(
            uuid,
            kind == AutoBuyCandidateKind.Structure ? native.StructureType : native.UpgradeType);
        source = resolution.Value!;
        if (!resolution.IsResolved) return false;
#if SERVICE_CYCLE_PROFILE
        _profileOperations.AddStableIdRead();
#endif
        return true;
    }

    private static bool TryReadAdmission(
        AutoBuyPurchaseNativeBindings native,
        AutoBuyCandidateKind kind,
        object source,
        out bool admitted) =>
        TryRead(
            kind == AutoBuyCandidateKind.Structure ? native.StructureCanPurchase : native.UpgradeCanPurchase,
            source,
            out admitted);

    /// <summary>
    /// Reads each admission term the game exposes on its own, so a refusal can name a cause. Only
    /// called after <see cref="TryReadAdmission"/> said no, or to read the affordability term
    /// that StructureSO.CanPurchase() omits. A term the game refused to answer is
    /// <see cref="AutoBuyAdmissionTerm.Unread"/>; the action boundary treats an unread required term
    /// as a fault. StructureSO declares no level cap, so a structure leaves both level terms unread.
    /// </summary>
    private static AutoBuyAdmissionDiagnosis Diagnose(
        AutoBuyPurchaseNativeBindings native,
        AutoBuyCandidateKind kind,
        object source)
    {
        var liveCosts = ReadLiveCosts(native, kind, source, out var costList);
        var structure = kind == AutoBuyCandidateKind.Structure;
        return new AutoBuyAdmissionDiagnosis(
            ReadTerm(structure ? native.StructureAvailable : native.UpgradeAvailable, source, refusesWhen: false),
            structure ? AutoBuyAdmissionTerm.Unread : ReadTerm(native.UpgradeMaxLevel, source, refusesWhen: true),
            structure
                ? AutoBuyAdmissionTerm.Unread
                : ReadTerm(native.UpgradeMaxQueuedLevel, source, refusesWhen: true),
            costList is null ? AutoBuyAdmissionTerm.Unread : ReadTerm(native.HasEnough, costList, refusesWhen: false),
            in liveCosts);
    }

    private static AutoBuyAdmissionTerm ReadTerm(Func<object, bool> read, object source, bool refusesWhen)
    {
        if (!TryRead(read, source, out var value)) return AutoBuyAdmissionTerm.Unread;
        return value == refusesWhen ? AutoBuyAdmissionTerm.Refused : AutoBuyAdmissionTerm.Passed;
    }

    /// <summary>
    /// Reads the exact live cost list and the spendable magnitude each row compares against,
    /// detached from all native objects. Any incomplete contract returns a named status rather
    /// than a partial list that could masquerade as all rows. A cost list, row or resource of
    /// another runtime type than the bound one is a contract violation, never a rebind.
    /// </summary>
    private static AutoBuyLiveCostSnapshot ReadLiveCosts(
        AutoBuyPurchaseNativeBindings native,
        AutoBuyCandidateKind kind,
        object source,
        out object? costList)
    {
        costList = null;
        object? list;
        try
        {
            list = kind == AutoBuyCandidateKind.Structure
                ? native.StructureCost(source)
                : native.UpgradeCost(source);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoBuyLiveCostSnapshot.Unavailable(
                AutoBuyLiveCostReadStatus.PurchaseCostUnavailable);
        }

        if (list is null)
        {
            return AutoBuyLiveCostSnapshot.Unavailable(
                AutoBuyLiveCostReadStatus.PurchaseCostUnavailable);
        }

        if (list.GetType() != native.CostListType)
        {
            return AutoBuyLiveCostSnapshot.Unavailable(
                AutoBuyLiveCostReadStatus.EntryListUnavailable);
        }

        costList = list;
        IList? entries;
        try
        {
            entries = native.CostEntries(list);
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoBuyLiveCostSnapshot.Unavailable(
                AutoBuyLiveCostReadStatus.EntryListUnavailable);
        }

        if (entries is null)
        {
            return AutoBuyLiveCostSnapshot.Unavailable(
                AutoBuyLiveCostReadStatus.EntryListUnavailable);
        }

        var rows = new List<AutoBuyLiveCostRow>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null || entry.GetType() != native.TupleType)
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.EntryContractUnavailable);
            }

            object? resource;
            BigDouble cost;
            try
            {
                resource = native.EntryResource(entry);
                cost = native.EntryValue(entry);
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.EntryContractUnavailable);
            }

            if (resource is null || resource.GetType() != native.ResourceType)
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.ResourceContractUnavailable);
            }

            Guid resourceId;
            try
            {
                resourceId = native.ResourceId(resource);
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.IdentityContractUnavailable);
            }

            if (resourceId == Guid.Empty)
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.InvalidResourceIdentity);
            }

            try
            {
                var bandwidth = native.IsBandwidth(resource);
                var available = bandwidth ? native.Missing(resource) : native.TrueQuantity(resource);
                rows.Add(new AutoBuyLiveCostRow(resourceId, bandwidth, cost, available));
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.ResourceContractUnavailable);
            }
        }

        return AutoBuyLiveCostSnapshot.Complete(rows.ToArray());
    }

    /// <summary>
    /// Whether every list this upgrade adds an element to has room for it. A bound destination is
    /// admitted only when its exact list/max-variable pair is in the audited profile and the live
    /// list reports an empty spot.
    /// </summary>
    private static ServiceActionResultCode ReadDestinationCapacity(
        AutoBuyPurchaseNativeBindings native,
        object source)
    {
        try
        {
            if (native.ViewListAdditions(source) is not IList additions)
                return AutoBuyActionResultCodes.DestinationCapacityContractUnavailable;
            for (var index = 0; index < additions.Count; index++)
            {
                var tuple = additions[index];
                if (tuple is null || tuple.GetType() != native.ListTupleType)
                    return AutoBuyActionResultCodes.DestinationCapacityContractUnavailable;
                var list = native.TupleList(tuple);
                var element = native.TupleElement(tuple);
                if (list is null || list.GetType() != native.ViewListType ||
                    element is null || element.GetType() != native.ViewType)
                {
                    return AutoBuyActionResultCodes.DestinationCapacityContractUnavailable;
                }

                if (!TryIdentity(native.ViewListId, list, out var listId) ||
                    !TryIdentity(native.ViewId, element, out _))
                {
                    return AutoBuyActionResultCodes.DestinationCapacityIdentityMismatch;
                }

                var maximum = native.MaxSizeVariable(list);
                if (maximum is null) continue;
                if (maximum.GetType() != native.IntVariableType ||
                    !TryIdentity(native.IntVariableId, maximum, out var maximumId))
                {
                    return AutoBuyActionResultCodes.DestinationCapacityIdentityMismatch;
                }

                var knownList = listId == KnownEntities.CreatedWorldAspects.Uuid;
                var knownMaximum = maximumId == KnownEntities.WorldAspectSlots.Uuid;
                if (knownList != knownMaximum)
                    return AutoBuyActionResultCodes.DestinationCapacityIdentityMismatch;
                if (!knownList)
                    return AutoBuyActionResultCodes.DestinationCapacityContractUnavailable;

                if (native.HasEmptySpot(list)) continue;
                return native.AsInt(maximum) <= 0
                    ? AutoBuyActionResultCodes.DestinationCapacityLocked
                    : AutoBuyActionResultCodes.DestinationCapacityFull;
            }

            return CommonActionResultCodes.Committed;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return AutoBuyActionResultCodes.DestinationCapacityContractUnavailable;
        }
    }

    private static bool TryIdentity(Func<object, Guid> read, object source, out Guid identity)
    {
        identity = read(source);
        return identity != Guid.Empty;
    }

    private static bool TryRead(Func<object, bool> read, object source, out bool value)
    {
        try
        {
            value = read(source);
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            value = false;
            return false;
        }
    }
}
