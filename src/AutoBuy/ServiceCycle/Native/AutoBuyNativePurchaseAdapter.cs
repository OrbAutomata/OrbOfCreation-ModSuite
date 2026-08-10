using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

/// <summary>
/// Preflight disposition of a single native purchase submission, before any audited mutation
/// was invoked. <see cref="Proceeded"/> means the native mutation was attempted and the
/// submission carries verifier evidence; the others are the reasons a submission never got that far.
/// </summary>
internal enum AutoBuyPurchasePreflight
{
    Proceeded = 0,
    CandidateUnavailable,
    NotAdmissible,
    AffordabilityUnavailable,
    SingleBuyUnavailable,
    OwningViewUnavailable,
    OwningViewRelationMissing,
    OwningViewRelationUnreadable,
    OwningViewRelationContradictory,
    StructureUnavailable,
    DestinationCapacityFull,
    DestinationCapacityContractUnavailable,
    DestinationCapacityIdentityMismatch,

    /// <summary>The suite never bound the owning-view topology contract on this build.</summary>
    OwningViewTopologyUnbound,

    /// <summary>
    /// The topology holds no admission evidence for the lifecycle this purchase was planned under.
    /// </summary>
    OwningViewTopologyUncaptured,

    /// <summary>The captured relation carries a status this build does not model.</summary>
    OwningViewRelationStatusUnmodeled,

    /// <summary>The live owning-screen availability read did not answer.</summary>
    OwningViewAvailabilityUnreadable,
}

/// <summary>
/// The neutral outcome of one native purchase submission: either a preflight rejection with no
/// mutation, or an attempted audited mutation carrying its <see cref="NativeMutationOutcome"/> and
/// call evidence. It never exposes a native object — the action port maps it to a service result.
/// </summary>
internal readonly struct AutoBuyPurchaseSubmission
{
    private AutoBuyPurchaseSubmission(
        AutoBuyPurchasePreflight preflight,
        bool hasEvidence,
        NativeMutationOutcome outcome,
        NativeMutationCallOutcome callOutcome,
        int requestedLevels,
        int committedLevels,
        in AutoBuyAdmissionDiagnosis diagnosis,
        in AutoBuyLiveCostSnapshot liveCosts,
        string reason)
    {
        Preflight = preflight;
        HasEvidence = hasEvidence;
        Outcome = outcome;
        CallOutcome = callOutcome;
        RequestedLevels = requestedLevels;
        CommittedLevels = committedLevels;
        Diagnosis = diagnosis;
        LiveCosts = liveCosts;
        Reason = reason;
    }

    public AutoBuyPurchasePreflight Preflight { get; }

    /// <summary>
    /// The one sentence this refusal owes a caller, when the preflight class alone cannot carry it —
    /// a topology refusal names the epoch it is stamped at, the epoch the plan asked for, and how
    /// many rows it holds, because those three numbers are the whole difference between the causes.
    /// Empty everywhere else: a class with a fixed sentence needs no second copy of it here.
    /// </summary>
    public string Reason { get; }

    public bool HasEvidence { get; }
    public NativeMutationOutcome Outcome { get; }
    public NativeMutationCallOutcome CallOutcome { get; }
    public bool Verified => HasEvidence && Outcome == NativeMutationOutcome.Verified;

    /// <summary>
    /// Which admission term refused, when the preflight was
    /// <see cref="AutoBuyPurchasePreflight.NotAdmissible"/>. Every other disposition leaves it unread.
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

    public static AutoBuyPurchaseSubmission Rejected(AutoBuyPurchasePreflight preflight) =>
        Rejected(preflight, default, string.Empty);

    public static AutoBuyPurchaseSubmission Rejected(
        AutoBuyPurchasePreflight preflight,
        string reason) =>
        Rejected(preflight, default, reason);

    public static AutoBuyPurchaseSubmission Rejected(
        AutoBuyPurchasePreflight preflight,
        in AutoBuyAdmissionDiagnosis diagnosis) =>
        Rejected(preflight, in diagnosis, string.Empty);

    private static AutoBuyPurchaseSubmission Rejected(
        AutoBuyPurchasePreflight preflight,
        in AutoBuyAdmissionDiagnosis diagnosis,
        string reason)
    {
        if (preflight == AutoBuyPurchasePreflight.Proceeded)
            throw new ArgumentOutOfRangeException(nameof(preflight));
        var liveCosts = diagnosis.LiveCosts;
        return new AutoBuyPurchaseSubmission(
            preflight,
            hasEvidence: false,
            default,
            default,
            0,
            0,
            in diagnosis,
            in liveCosts,
            reason);
    }

    public static AutoBuyPurchaseSubmission Attempted(
        NativeMutationEvidence<int> evidence,
        int requestedLevels,
        in AutoBuyLiveCostSnapshot liveCosts)
    {
        var committed = evidence.HasBefore && evidence.HasAfter
            ? Math.Max(0, evidence.After - evidence.Before)
            : 0;
        return new AutoBuyPurchaseSubmission(
            AutoBuyPurchasePreflight.Proceeded,
            hasEvidence: true,
            evidence.Outcome,
            NativeMutationCallOutcome.FromEvidence(evidence),
            requestedLevels,
            committed,
            default,
            in liveCosts,
            string.Empty);
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
    private readonly System.Collections.Generic.Dictionary<Type, PurchaseAccessors?> _accessors =
        new System.Collections.Generic.Dictionary<Type, PurchaseAccessors?>();
    private readonly CandidateIndex?[] _candidateIndices = new CandidateIndex?[2];
    private readonly NativePurchaseViewAdmissionResolver? _viewAdmission;
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
    }

    internal AutoBuyNativePurchaseAdapter(
        AutomataProfileOperations profileOperations,
        NativePurchaseViewAdmissionResolver viewAdmission)
    {
        _profileOperations = profileOperations ??
            throw new ArgumentNullException(nameof(profileOperations));
        _viewAdmission = viewAdmission ?? throw new ArgumentNullException(nameof(viewAdmission));
    }
#else
    public AutoBuyNativePurchaseAdapter()
    {
        NativePurchaseViewAdmissionResolver.TryCreateProduction(
            ReflectionUtil.FindLoadedType,
            out _viewAdmission,
            out _);
    }

    internal AutoBuyNativePurchaseAdapter(NativePurchaseViewAdmissionResolver viewAdmission) =>
        _viewAdmission = viewAdmission ?? throw new ArgumentNullException(nameof(viewAdmission));
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
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);

        bool resolved;
        object source;
        PurchaseAccessors accessors;
#if SERVICE_CYCLE_PROFILE
        var resolutionStage = _profileOperations.Begin(
            ServiceCycleProfileSpan.AutoBuyActionCandidateResolution,
            in context,
            ServiceCycleProfileTemperature.Warm);
        try
        {
#endif
        resolved = TryResolveCandidate(kind, uuid, out source, out accessors);
#if SERVICE_CYCLE_PROFILE
        }
        finally { resolutionStage.Complete(); }
#endif
        if (!resolved)
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);

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
        var gate = ReadLiveGate(kind, uuid, lifecycleEpoch, source, accessors, out var gateReason);
        if (gate != AutoBuyPurchasePreflight.Proceeded)
            return AutoBuyPurchaseSubmission.Rejected(gate, gateReason);

        // The shipped CanPurchase contracts differ materially. StructureSO checks only its
        // per-level requirements and ActionManager.CanLoadAction(); it checks neither IsAvailable()
        // nor affordability. UpgradeSO checks max queued level, affordability, IsAvailable(), queued
        // level requirements, and ActionManager.CanLoadAction(). The explicit gates around this fold
        // close both structure gaps and keep the owning view outside both methods visible here.
#if SERVICE_CYCLE_PROFILE
        _profileOperations.AddReflectedMethodCall();
#endif
        readable = accessors.TryReadAdmission(source, out admitted);
#if SERVICE_CYCLE_PROFILE
        }
        finally { admissionStage.Complete(); }
#endif
        if (!readable)
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);
        if (!admitted)
        {
            // A refusal says either that affordability moved after collection or that the plan and
            // game disagree structurally. Take the fold apart here, on the cold path, while the same
            // native object is in hand.
            return AutoBuyPurchaseSubmission.Rejected(
                AutoBuyPurchasePreflight.NotAdmissible, accessors.Diagnose(source));
        }

        AutoBuyLiveCostSnapshot liveCosts;
        if (kind == AutoBuyCandidateKind.Structure)
        {
            // StructureSO.CanPurchase() omits affordability and Purchase(true) silently no-ops when
            // its cost cannot be paid. Re-read that exact native term before entering the mutation
            // verifier; an unread term is an adapter fault, never evidence that purchase is safe.
            var diagnosis = accessors.Diagnose(source);
            if (diagnosis.HasEnough == AutoBuyAdmissionTerm.Refused)
            {
                return AutoBuyPurchaseSubmission.Rejected(
                    AutoBuyPurchasePreflight.NotAdmissible, in diagnosis);
            }
            if (diagnosis.HasEnough != AutoBuyAdmissionTerm.Passed)
            {
                return AutoBuyPurchaseSubmission.Rejected(
                    AutoBuyPurchasePreflight.AffordabilityUnavailable, in diagnosis);
            }
            liveCosts = diagnosis.LiveCosts;
        }
        else
        {
            liveCosts = accessors.ReadLiveCosts(source);
        }

        if (kind == AutoBuyCandidateKind.Upgrade)
        {
            var capacity = accessors.ReadDestinationCapacity(source);
            if (capacity != AutoBuyPurchasePreflight.Proceeded)
                return AutoBuyPurchaseSubmission.Rejected(capacity);
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
            // Queued-level read before and after the audited Purchase invocation.
            _profileOperations.AddReflectedMethodCalls(3);
            if (kind == AutoBuyCandidateKind.Structure)
            {
                // A structure buys one level per call, so every level past the first is another
                // Purchase and the CanPurchase that guards it, each with its own argument array.
                var extraLevels = Math.Max(0, count - 1);
                _profileOperations.AddReflectedMethodCalls((uint)(extraLevels * 2));
                for (var level = 0; level <= extraLevels; level++)
                    _profileOperations.AddInvocationArgumentArray();
            }
#endif
        return kind == AutoBuyCandidateKind.Structure
            ? SubmitStructure(uuid, lifecycleEpoch, source, accessors, count, in liveCosts)
            : SubmitUpgrade(uuid, source, accessors, count, in liveCosts);
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
    private AutoBuyPurchasePreflight ReadLiveGate(
        AutoBuyCandidateKind kind,
        Guid uuid,
        long lifecycleEpoch,
        object source,
        PurchaseAccessors accessors,
        out string reason)
    {
        var reads = default(NativePurchaseViewAdmissionReadCounts);
        reason = string.Empty;
        try
        {
            if (_viewAdmission is null)
                return AutoBuyPurchasePreflight.OwningViewTopologyUnbound;

            if (!_viewAdmission.TryGetCaptured(
                kind == AutoBuyCandidateKind.Structure
                    ? WorldPurchaseCandidateKind.Structure
                    : WorldPurchaseCandidateKind.Upgrade,
                uuid,
                lifecycleEpoch,
                out var resolution))
            {
                reason = AutoBuyPurchaseNarration.TopologyUncaptured(
                    _viewAdmission.CapturedEpoch,
                    lifecycleEpoch,
                    _viewAdmission.CapturedCount);
                return AutoBuyPurchasePreflight.OwningViewTopologyUncaptured;
            }
            switch (resolution.Relation.Status)
            {
                case WorldPurchaseViewRelationStatus.Missing:
                    return AutoBuyPurchasePreflight.OwningViewRelationMissing;
                case WorldPurchaseViewRelationStatus.Unreadable:
                    return AutoBuyPurchasePreflight.OwningViewRelationUnreadable;
                case WorldPurchaseViewRelationStatus.Contradictory:
                    return AutoBuyPurchasePreflight.OwningViewRelationContradictory;
                case WorldPurchaseViewRelationStatus.Resolved:
                    break;
                default:
                    return AutoBuyPurchasePreflight.OwningViewRelationStatusUnmodeled;
            }

            if (!_viewAdmission.TryReadAvailabilityProfiled(
                    in resolution,
                    ref reads,
                    out var viewAvailable))
                return AutoBuyPurchasePreflight.OwningViewAvailabilityUnreadable;
            if (!viewAvailable)
                return AutoBuyPurchasePreflight.OwningViewUnavailable;

            if (kind == AutoBuyCandidateKind.Structure)
            {
                reads.AddMethodCall();
                if (!accessors.TryReadStructureAvailability(source, out var structureAvailable))
                    return AutoBuyPurchasePreflight.CandidateUnavailable;
                if (!structureAvailable)
                    return AutoBuyPurchasePreflight.StructureUnavailable;
            }

            return AutoBuyPurchasePreflight.Proceeded;
        }
        finally
        {
#if SERVICE_CYCLE_PROFILE
            for (var index = 0u; index < reads.FieldReads; index++)
                _profileOperations.AddReflectedFieldRead();
            _profileOperations.AddReflectedMethodCalls(reads.MethodCalls);
            for (var index = 0u; index < reads.ListEntries; index++)
                _profileOperations.AddListEntry();
#endif
        }
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
        Guid uuid,
        long lifecycleEpoch,
        object source,
        PurchaseAccessors accessors,
        int count,
        in AutoBuyLiveCostSnapshot liveCosts)
    {
        var evidence = NativeMutationVerifier.Execute(
            "Auto Buy Structure",
            uuid.ToString(),
            count == 1
                ? "GetQueuedQuantity exact delta +1"
                : $"GetQueuedQuantity delta in [1, {count}]",
            () => accessors.ReadQueuedLevel(source),
            () =>
            {
                accessors.InvokePurchase(source);
                for (var level = 1; level < count; level++)
                {
                    if (ReadLiveGate(
                            AutoBuyCandidateKind.Structure,
                            uuid,
                            lifecycleEpoch,
                            source,
                            accessors,
                            out _) != AutoBuyPurchasePreflight.Proceeded ||
                        !accessors.TryReadAdmission(source, out var admitted) || !admitted ||
                        accessors.Diagnose(source).HasEnough != AutoBuyAdmissionTerm.Passed)
                        break;
                    accessors.InvokePurchase(source);
                }
            },
            (before, after) => count == 1
                ? after == before + 1
                : after > before && after <= before + count);
        return AutoBuyPurchaseSubmission.Attempted(
            evidence, requestedLevels: count, in liveCosts);
    }

    private static AutoBuyPurchaseSubmission SubmitUpgrade(
        Guid uuid,
        object source,
        PurchaseAccessors accessors,
        int count,
        in AutoBuyLiveCostSnapshot liveCosts)
    {
        // The native upgrade Purchase() honours the global multi-buy multiplier, so requesting
        // exactly `count` levels means pinning the multiplier to `count` for the single call. The
        // scope restores the operator's value afterwards; if it cannot guarantee that, no mutation
        // is attempted. The call commits between one and `count` levels (the game may afford fewer):
        // any committed level is a success, only zero is a failure.
        if (!NativeMultiBuyScope.TryEnter(count, out var scope, out _))
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.SingleBuyUnavailable);

        using (scope)
        {
            var evidence = NativeMutationVerifier.Execute(
                "Auto Buy Upgrade",
                uuid.ToString(),
                $"GetQueuedPurchaseLevel delta in [1, {count}]",
                () => accessors.ReadQueuedLevel(source),
                () => accessors.InvokePurchase(source),
                (before, after) => after > before && after <= before + count);
            return AutoBuyPurchaseSubmission.Attempted(
                evidence, requestedLevels: count, in liveCosts);
        }
    }

    // The native StructureSO.All / UpgradeSO.All membership is constant after game start (only
    // per-candidate state changes), so resolution keeps a UUID index per kind instead of scanning
    // the list per action. The index is keyed to the list's identity and count and self-heals with
    // one rebuild when a lookup misses or a hit's stable id no longer matches (game reload rebuilt
    // the list in place); the lifecycle-epoch guard in the action adapter remains the authority
    // that rejects stale cycles.
    private bool TryResolveCandidate(
        AutoBuyCandidateKind kind,
        Guid uuid,
        out object source,
        out PurchaseAccessors accessors)
    {
        source = null!;
        accessors = null!;
        var listType = kind == AutoBuyCandidateKind.Structure ? "StructureSO" : "UpgradeSO";
        var list = ReadStaticList(listType, "All");
        var index = ResolveIndex(kind, list, rebuild: false);
        if (!TryLookupFresh(index, uuid, out var candidate))
        {
            index = ResolveIndex(kind, list, rebuild: true);
            if (!TryLookupFresh(index, uuid, out candidate)) return false;
        }

        var resolved = ResolveAccessors(candidate.GetType(), kind);
        if (resolved is null) return false;
        source = candidate;
        accessors = resolved;
        return true;
    }

    private bool TryLookupFresh(
        System.Collections.Generic.Dictionary<Guid, object> index,
        Guid uuid,
        out object candidate)
    {
        if (!index.TryGetValue(uuid, out candidate!)) return false;
#if SERVICE_CYCLE_PROFILE
        _profileOperations.AddStableIdRead();
#endif
        return Guid.TryParse(ReflectionUtil.ReadStableId(candidate), out var liveId) && liveId == uuid;
    }

    private System.Collections.Generic.Dictionary<Guid, object> ResolveIndex(
        AutoBuyCandidateKind kind,
        IList list,
        bool rebuild)
    {
        var slot = kind == AutoBuyCandidateKind.Structure ? 0 : 1;
        var cached = _candidateIndices[slot];
        if (!rebuild && cached is not null &&
            ReferenceEquals(cached.Source, list) && cached.Count == list.Count)
        {
            return cached.ByUuid;
        }

        var index = new System.Collections.Generic.Dictionary<Guid, object>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var candidate = list[i];
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddListEntry();
#endif
            if (candidate is null) continue;
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddStableIdRead();
#endif
            if (!Guid.TryParse(ReflectionUtil.ReadStableId(candidate), out var candidateId) ||
                candidateId == Guid.Empty)
                continue;
            // First match wins, mirroring the previous linear-scan semantics.
            if (!index.ContainsKey(candidateId)) index.Add(candidateId, candidate);
        }

        _candidateIndices[slot] = new CandidateIndex(list, list.Count, index);
        return index;
    }

    private sealed class CandidateIndex
    {
        internal CandidateIndex(
            IList source,
            int count,
            System.Collections.Generic.Dictionary<Guid, object> byUuid)
        {
            Source = source;
            Count = count;
            ByUuid = byUuid;
        }

        internal IList Source { get; }
        internal int Count { get; }
        internal System.Collections.Generic.Dictionary<Guid, object> ByUuid { get; }
    }

    private PurchaseAccessors? ResolveAccessors(Type type, AutoBuyCandidateKind kind)
    {
        if (!_accessors.TryGetValue(type, out var accessors))
        {
            accessors = PurchaseAccessors.TryCreate(type, kind);
            _accessors[type] = accessors;
        }

        return accessors;
    }

    private static IList ReadStaticList(string typeName, string memberName)
    {
        var type = ReflectionUtil.FindLoadedType(typeName);
        var value = type?.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) ??
                    type?.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
        return value as IList ?? Array.Empty<object>();
    }

    /// <summary>
    /// Duplicated mutation contract for one candidate kind (AB-SC-013): the exact-type + assembly
    /// guard and the no-arg method discovery mirror the legacy purchase adapters. Structure purchase
    /// is <c>Purchase(bool)</c> verified by <c>GetQueuedQuantity</c>; upgrade purchase is
    /// <c>Purchase()</c> verified by <c>GetQueuedPurchaseLevel</c>.
    /// </summary>
    private sealed class PurchaseAccessors
    {
        private readonly AutoBuyCandidateKind _kind;
        private readonly MethodInfo _canPurchase;
        private readonly MethodInfo _queuedLevel;
        private readonly MethodInfo _purchase;
        private readonly MethodInfo? _isAvailable;
        private readonly MethodInfo? _isMaxLevel;
        private readonly MethodInfo? _isMaxQueuedLevel;
        private readonly MethodInfo? _purchaseCost;
        private readonly DestinationCapacityAccessors? _destinationCapacity;
        private Type? _costListType;
        private MethodInfo? _hasEnough;
        private MethodInfo? _getEntries;
        private Type? _entryType;
        private FieldInfo? _entryResource;
        private MethodInfo? _entryValue;
        private Type? _resourceType;
        private MethodInfo? _isBandwidth;
        private MethodInfo? _getTrueQuantity;
        private MethodInfo? _getMissing;
        private MethodInfo? _getGuid;

        private PurchaseAccessors(
            AutoBuyCandidateKind kind,
            MethodInfo canPurchase,
            MethodInfo queuedLevel,
            MethodInfo purchase,
            MethodInfo? isAvailable,
            MethodInfo? isMaxLevel,
            MethodInfo? isMaxQueuedLevel,
            MethodInfo? purchaseCost,
            DestinationCapacityAccessors? destinationCapacity)
        {
            _kind = kind;
            _canPurchase = canPurchase;
            _queuedLevel = queuedLevel;
            _purchase = purchase;
            _isAvailable = isAvailable;
            _isMaxLevel = isMaxLevel;
            _isMaxQueuedLevel = isMaxQueuedLevel;
            _purchaseCost = purchaseCost;
            _destinationCapacity = destinationCapacity;
        }

        public static PurchaseAccessors? TryCreate(Type type, AutoBuyCandidateKind kind)
        {
            var expected = kind == AutoBuyCandidateKind.Structure ? "StructureSO" : "UpgradeSO";
            if (!string.Equals(type.FullName, expected, StringComparison.Ordinal) ||
                !string.Equals(type.Assembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal))
            {
                return null;
            }

            var canPurchase = FindNoArg(type, "CanPurchase", typeof(bool));
            var queuedLevel = FindNoArg(
                type,
                kind == AutoBuyCandidateKind.Structure ? "GetQueuedQuantity" : "GetQueuedPurchaseLevel",
                typeof(int));
            var purchase = kind == AutoBuyCandidateKind.Structure
                ? FindPurchase(type, new[] { typeof(bool) })
                : FindPurchase(type, Type.EmptyTypes);
            if (canPurchase is null || queuedLevel is null || purchase is null)
                return null;

            // The diagnosis half is optional by design: a kind that has no bounded level declares no
            // level cap to read, and a missing reader means one fewer term named rather than a
            // candidate that cannot be bought.
            return new PurchaseAccessors(
                kind,
                canPurchase,
                queuedLevel,
                purchase,
                FindNoArg(type, "IsAvailable", typeof(bool)),
                FindNoArg(type, "IsMaxLevel", typeof(bool)),
                FindNoArg(type, "IsMaxQueuedLevel", typeof(bool)),
                FindNoArgReturning(type, "GetPurchaseCost"),
                kind == AutoBuyCandidateKind.Upgrade
                    ? DestinationCapacityAccessors.TryCreate(type)
                    : null);
        }

        public bool TryReadAdmission(object source, out bool admitted) =>
            TryInvokeBool(_canPurchase, source, out admitted);

        public bool TryReadStructureAvailability(object source, out bool available)
        {
            available = false;
            return _kind == AutoBuyCandidateKind.Structure && _isAvailable is not null &&
                TryInvokeBool(_isAvailable, source, out available);
        }

        public AutoBuyPurchasePreflight ReadDestinationCapacity(object source) =>
            _kind != AutoBuyCandidateKind.Upgrade
                ? AutoBuyPurchasePreflight.Proceeded
                : _destinationCapacity?.Read(source) ??
                  AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable;

        /// <summary>
        /// Reads each admission term the game exposes on its own, so a refusal can name a cause. Only
        /// called after <see cref="TryReadAdmission"/> said no, or to read the affordability term
        /// that StructureSO.CanPurchase() omits. It answers with <see cref="AutoBuyAdmissionTerm.Unread"/>
        /// rather than throwing; the action boundary treats an unread required term as a fault.
        /// </summary>
        public AutoBuyAdmissionDiagnosis Diagnose(object source)
        {
            var liveCosts = ReadLiveCosts(source, out var costList);
            return new AutoBuyAdmissionDiagnosis(
                ReadTerm(_isAvailable, source, refusesWhen: false),
                ReadTerm(_isMaxLevel, source, refusesWhen: true),
                ReadTerm(_isMaxQueuedLevel, source, refusesWhen: true),
                ReadHasEnough(costList),
                in liveCosts);
        }

        private static AutoBuyAdmissionTerm ReadTerm(MethodInfo? method, object source, bool refusesWhen)
        {
            if (method is null || !TryInvokeBool(method, source, out var value))
                return AutoBuyAdmissionTerm.Unread;
            return value == refusesWhen ? AutoBuyAdmissionTerm.Refused : AutoBuyAdmissionTerm.Passed;
        }

        /// <summary>
        /// The game's own verdict on the price, from the cost list it builds for the next level. The
        /// list type is resolved from the instance the call returns and cached, because the suite
        /// names no game type at compile time.
        /// </summary>
        private AutoBuyAdmissionTerm ReadHasEnough(object? costList)
        {
            if (costList is null) return AutoBuyAdmissionTerm.Unread;
            var costListType = costList.GetType();
            if (!ReferenceEquals(costListType, _costListType))
            {
                _costListType = costListType;
                _hasEnough = FindNoArg(costListType, "HasEnough", typeof(bool));
            }

            return ReadTerm(_hasEnough, costList, refusesWhen: false);
        }

        public AutoBuyLiveCostSnapshot ReadLiveCosts(object source) =>
            ReadLiveCosts(source, out _);

        /// <summary>
        /// Reads the exact live cost list and the spendable magnitude each row compares against,
        /// detached from all native objects. Any incomplete contract returns a named status rather
        /// than a partial list that could masquerade as all rows.
        /// </summary>
        private AutoBuyLiveCostSnapshot ReadLiveCosts(object source, out object? costList)
        {
            costList = null;
            if (_purchaseCost is null)
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.PurchaseCostUnavailable);
            }

            try
            {
                costList = _purchaseCost.Invoke(source, Array.Empty<object>());
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException ||
                                       ex is InvalidOperationException || ex is TargetException ||
                                       ex is MemberAccessException)
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.PurchaseCostUnavailable);
            }

            if (costList is null)
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.PurchaseCostUnavailable);
            }

            var costListType = costList.GetType();
            if (!IsGameType(costListType, "ResourceCostList"))
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.EntryListUnavailable);
            }

            if (!ReferenceEquals(costListType, _costListType))
            {
                _costListType = costListType;
                _hasEnough = FindNoArg(costListType, "HasEnough", typeof(bool));
                _getEntries = FindCostEntries(costListType);
                _entryType = null;
                _entryResource = null;
                _entryValue = null;
                _resourceType = null;
                _isBandwidth = null;
                _getTrueQuantity = null;
                _getMissing = null;
                _getGuid = null;
            }

            object? entriesValue;
            try
            {
                entriesValue = _getEntries?.Invoke(costList, Array.Empty<object>());
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException ||
                                       ex is InvalidOperationException || ex is TargetException ||
                                       ex is MemberAccessException)
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.EntryListUnavailable);
            }

            if (entriesValue is not IList entries)
            {
                return AutoBuyLiveCostSnapshot.Unavailable(
                    AutoBuyLiveCostReadStatus.EntryListUnavailable);
            }

            var rows = new List<AutoBuyLiveCostRow>(entries.Count);
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (entry is null || !TryBindEntry(entry.GetType()))
                {
                    return AutoBuyLiveCostSnapshot.Unavailable(
                        AutoBuyLiveCostReadStatus.EntryContractUnavailable);
                }

                object? resource;
                BigDouble cost;
                try
                {
                    resource = _entryResource!.GetValue(entry);
                    if (_entryValue!.Invoke(entry, Array.Empty<object>()) is not BigDouble value)
                    {
                        return AutoBuyLiveCostSnapshot.Unavailable(
                            AutoBuyLiveCostReadStatus.EntryContractUnavailable);
                    }
                    cost = value;
                }
                catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException ||
                                           ex is InvalidOperationException || ex is TargetException ||
                                           ex is MemberAccessException)
                {
                    return AutoBuyLiveCostSnapshot.Unavailable(
                        AutoBuyLiveCostReadStatus.EntryContractUnavailable);
                }

                if (resource is null || !TryBindResource(resource.GetType()))
                {
                    return AutoBuyLiveCostSnapshot.Unavailable(
                        AutoBuyLiveCostReadStatus.ResourceContractUnavailable);
                }

                if (_getGuid is null)
                {
                    return AutoBuyLiveCostSnapshot.Unavailable(
                        AutoBuyLiveCostReadStatus.IdentityContractUnavailable);
                }

                Guid resourceId;
                try
                {
                    if (_getGuid.Invoke(resource, Array.Empty<object>()) is not Guid readId)
                    {
                        return AutoBuyLiveCostSnapshot.Unavailable(
                            AutoBuyLiveCostReadStatus.IdentityContractUnavailable);
                    }
                    resourceId = readId;
                }
                catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException ||
                                           ex is InvalidOperationException || ex is TargetException ||
                                           ex is MemberAccessException)
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
                    if (_isBandwidth!.Invoke(resource, Array.Empty<object>()) is not bool bandwidth)
                    {
                        return AutoBuyLiveCostSnapshot.Unavailable(
                            AutoBuyLiveCostReadStatus.ResourceContractUnavailable);
                    }
                    var availableValue = bandwidth
                        ? _getMissing!.Invoke(resource, Array.Empty<object>())
                        : _getTrueQuantity!.Invoke(resource, Array.Empty<object>());
                    if (availableValue is not BigDouble available)
                    {
                        return AutoBuyLiveCostSnapshot.Unavailable(
                            AutoBuyLiveCostReadStatus.ResourceContractUnavailable);
                    }

                    rows.Add(new AutoBuyLiveCostRow(resourceId, bandwidth, cost, available));
                }
                catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException ||
                                           ex is InvalidOperationException || ex is TargetException ||
                                           ex is MemberAccessException)
                {
                    return AutoBuyLiveCostSnapshot.Unavailable(
                        AutoBuyLiveCostReadStatus.ResourceContractUnavailable);
                }
            }

            return AutoBuyLiveCostSnapshot.Complete(rows.ToArray());
        }

        private bool TryBindEntry(Type type)
        {
            if (ReferenceEquals(type, _entryType))
                return _entryResource is not null && _entryValue is not null;
            _entryType = type;
            _entryResource = null;
            _entryValue = null;
            if (!IsGameType(type, "ResourceTuple")) return false;
            _entryResource = type.GetField(
                "resource", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _entryValue = FindNoArg(type, "GetValue", typeof(BigDouble));
            return _entryResource is not null &&
                IsGameType(_entryResource.FieldType, "ResourceSO") &&
                _entryValue is not null;
        }

        private bool TryBindResource(Type type)
        {
            if (ReferenceEquals(type, _resourceType))
            {
                return _isBandwidth is not null &&
                    _getTrueQuantity is not null &&
                    _getMissing is not null &&
                    _getGuid is not null;
            }

            _resourceType = type;
            _isBandwidth = null;
            _getTrueQuantity = null;
            _getMissing = null;
            _getGuid = null;
            if (!IsGameType(type, "ResourceSO")) return false;
            _isBandwidth = FindNoArg(type, "IsBandwidthResource", typeof(bool));
            _getTrueQuantity = FindNoArg(type, "GetTrueQuantity", typeof(BigDouble));
            _getMissing = FindNoArg(type, "GetMissing", typeof(BigDouble));
            _getGuid = FindInheritedNoArg(
                type,
                "IdScriptableObject",
                "GetGuid",
                typeof(Guid));
            return _isBandwidth is not null &&
                _getTrueQuantity is not null &&
                _getMissing is not null &&
                _getGuid is not null;
        }

        /// <summary>
        /// The exact authored addition tuple and destination-capacity contract used by upgrades.
        /// A bound destination is admitted only when its exact list/max-variable pair is in the
        /// audited profile and the live list reports an empty spot.
        /// </summary>
        private sealed class DestinationCapacityAccessors
        {
            private const BindingFlags Instance =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private readonly Type _tupleType;
            private readonly Type _viewListType;
            private readonly Type _viewType;
            private readonly Type _intVariableType;
            private readonly FieldInfo _additions;
            private readonly FieldInfo _tupleList;
            private readonly FieldInfo _tupleElement;
            private readonly FieldInfo _maxSizeVariable;
            private readonly MethodInfo _listIdentity;
            private readonly MethodInfo _viewIdentity;
            private readonly MethodInfo _variableIdentity;
            private readonly MethodInfo _hasEmptySpot;

            private DestinationCapacityAccessors(
                Type tupleType,
                Type viewListType,
                Type viewType,
                Type intVariableType,
                FieldInfo additions,
                FieldInfo tupleList,
                FieldInfo tupleElement,
                FieldInfo maxSizeVariable,
                MethodInfo listIdentity,
                MethodInfo viewIdentity,
                MethodInfo variableIdentity,
                MethodInfo hasEmptySpot)
            {
                _tupleType = tupleType;
                _viewListType = viewListType;
                _viewType = viewType;
                _intVariableType = intVariableType;
                _additions = additions;
                _tupleList = tupleList;
                _tupleElement = tupleElement;
                _maxSizeVariable = maxSizeVariable;
                _listIdentity = listIdentity;
                _viewIdentity = viewIdentity;
                _variableIdentity = variableIdentity;
                _hasEmptySpot = hasEmptySpot;
            }

            internal static DestinationCapacityAccessors? TryCreate(Type upgradeType)
            {
                try
                {
                    var viewList = ExactGameType("ViewListVariable");
                    var view = ExactGameType("ViewSO");
                    var intVariable = ExactGameType("IntVariable");
                    var tuple = viewList.GetNestedType(
                        "ListTuple",
                        BindingFlags.Public | BindingFlags.NonPublic) ??
                        throw new InvalidOperationException("ViewListVariable.ListTuple was unavailable.");
                    if (!IsGameType(tuple, "ViewListVariable+ListTuple"))
                        throw new InvalidOperationException("ViewListVariable.ListTuple identity was unavailable.");

                    var tupleBase = tuple.BaseType ??
                        throw new InvalidOperationException("ViewListVariable.ListTuple base was unavailable.");
                    if (!tupleBase.IsGenericType || tupleBase.GetGenericArguments().Length != 2 ||
                        tupleBase.GetGenericArguments()[0] != view ||
                        tupleBase.GetGenericArguments()[1] != viewList ||
                        !string.Equals(
                            tupleBase.GetGenericTypeDefinition().FullName,
                            "GenericListVariable`1+AdditionTuple`1",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "GenericListVariable<ViewSO>.AdditionTuple<ViewListVariable> was unavailable.");
                    }

                    var additions = ExactField(
                        upgradeType,
                        "viewListAdditions",
                        typeof(List<>).MakeGenericType(tuple),
                        declaredOnly: true);
                    var tupleList = ExactField(tupleBase, "list", viewList, declaredOnly: true);
                    var tupleElement = ExactField(tupleBase, "element", view, declaredOnly: true);
                    var abstractList = FindGenericBase(viewList, "AbstractListVariable`1", view);
                    var genericList = FindGenericBase(viewList, "GenericListVariable`1", view);
                    var maxSize = ExactField(
                        abstractList,
                        "maxSizeVariable",
                        intVariable,
                        declaredOnly: true);
                    var hasEmptySpot = ExactNoArg(
                        genericList,
                        "HasEmptySpot",
                        typeof(bool),
                        declaredOnly: true);

                    return new DestinationCapacityAccessors(
                        tuple,
                        viewList,
                        view,
                        intVariable,
                        additions,
                        tupleList,
                        tupleElement,
                        maxSize,
                        ExactNoArg(viewList, "GetGuid", typeof(Guid), declaredOnly: false),
                        ExactNoArg(view, "GetGuid", typeof(Guid), declaredOnly: false),
                        ExactNoArg(intVariable, "GetGuid", typeof(Guid), declaredOnly: false),
                        hasEmptySpot);
                }
                catch (Exception ex) when (ex is InvalidOperationException or AmbiguousMatchException)
                {
                    return null;
                }
            }

            internal AutoBuyPurchasePreflight Read(object source)
            {
                try
                {
                    if (_additions.GetValue(source) is not IList additions)
                        return AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable;
                    for (var index = 0; index < additions.Count; index++)
                    {
                        var tuple = additions[index];
                        if (tuple is null || tuple.GetType() != _tupleType)
                            return AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable;
                        var list = _tupleList.GetValue(tuple);
                        var element = _tupleElement.GetValue(tuple);
                        if (list is null || list.GetType() != _viewListType ||
                            element is null || element.GetType() != _viewType)
                        {
                            return AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable;
                        }

                        if (!TryIdentity(_listIdentity, list, out var listId) ||
                            !TryIdentity(_viewIdentity, element, out _))
                        {
                            return AutoBuyPurchasePreflight.DestinationCapacityIdentityMismatch;
                        }

                        var maximum = _maxSizeVariable.GetValue(list);
                        if (maximum is null) continue;
                        if (maximum.GetType() != _intVariableType ||
                            !TryIdentity(_variableIdentity, maximum, out var maximumId))
                        {
                            return AutoBuyPurchasePreflight.DestinationCapacityIdentityMismatch;
                        }

                        var knownList = listId == KnownEntities.CreatedWorldAspects.Uuid;
                        var knownMaximum = maximumId == KnownEntities.WorldAspectSlots.Uuid;
                        if (knownList != knownMaximum)
                            return AutoBuyPurchasePreflight.DestinationCapacityIdentityMismatch;
                        if (!knownList)
                            return AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable;

                        if (_hasEmptySpot.Invoke(list, Array.Empty<object>()) is not bool empty)
                            return AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable;
                        if (!empty)
                            return AutoBuyPurchasePreflight.DestinationCapacityFull;
                    }

                    return AutoBuyPurchasePreflight.Proceeded;
                }
                catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or
                                           InvalidOperationException or TargetException or
                                           MemberAccessException)
                {
                    return AutoBuyPurchasePreflight.DestinationCapacityContractUnavailable;
                }
            }

            private static Type ExactGameType(string name) =>
                ReflectionUtil.FindLoadedType(name) is { } type && IsGameType(type, name)
                    ? type
                    : throw new InvalidOperationException(name + " was unavailable.");

            private static Type FindGenericBase(Type type, string definitionName, Type argument)
            {
                for (var current = type.BaseType; current is not null; current = current.BaseType)
                {
                    if (!current.IsGenericType) continue;
                    var definition = current.GetGenericTypeDefinition();
                    var arguments = current.GetGenericArguments();
                    if (string.Equals(definition.FullName, definitionName, StringComparison.Ordinal) &&
                        arguments.Length == 1 && arguments[0] == argument)
                        return current;
                }
                throw new InvalidOperationException(definitionName + " was unavailable.");
            }

            private static FieldInfo ExactField(
                Type owner,
                string name,
                Type fieldType,
                bool declaredOnly)
            {
                var flags = Instance | (declaredOnly ? BindingFlags.DeclaredOnly : 0);
                var field = owner.GetField(name, flags);
                return field is not null && field.FieldType == fieldType
                    ? field
                    : throw new InvalidOperationException(owner.Name + "." + name + " was unavailable.");
            }

            private static MethodInfo ExactNoArg(
                Type owner,
                string name,
                Type returnType,
                bool declaredOnly)
            {
                var flags = Instance | (declaredOnly ? BindingFlags.DeclaredOnly : 0);
                var method = owner.GetMethod(name, flags, null, Type.EmptyTypes, null);
                return method is not null && method.ReturnType == returnType
                    ? method
                    : throw new InvalidOperationException(owner.Name + "." + name + " was unavailable.");
            }

            private static bool TryIdentity(MethodInfo method, object source, out Guid identity)
            {
                identity = Guid.Empty;
                if (method.Invoke(source, Array.Empty<object>()) is not Guid value || value == Guid.Empty)
                    return false;
                identity = value;
                return true;
            }
        }

        private static bool IsGameType(Type type, string fullName) =>
            string.Equals(type.FullName, fullName, StringComparison.Ordinal) &&
            string.Equals(type.Assembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal);

        public int ReadQueuedLevel(object source)
        {
            if (_queuedLevel.Invoke(source, Array.Empty<object>()) is int value)
                return value;
            throw new InvalidOperationException("native queued level is unavailable");
        }

        public void InvokePurchase(object source) =>
            _purchase.Invoke(
                source,
                _kind == AutoBuyCandidateKind.Structure ? new object[] { true } : Array.Empty<object>());

        private static MethodInfo? FindNoArg(Type type, string name, Type returnType)
        {
            var method = type.GetMethod(name, ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
            return method is not null && method.DeclaringType == type && method.ReturnType == returnType
                ? method
                : null;
        }

        private static MethodInfo? FindInheritedNoArg(
            Type type,
            string declaringType,
            string name,
            Type returnType)
        {
            var method = type.GetMethod(name, ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
            return method is not null &&
                method.DeclaringType is not null &&
                IsGameType(method.DeclaringType, declaringType) &&
                method.ReturnType == returnType
                ? method
                : null;
        }

        private static MethodInfo? FindCostEntries(Type type)
        {
            var method = FindNoArgReturning(type, "GetEntries");
            if (method is null || !method.ReturnType.IsGenericType ||
                method.ReturnType.GetGenericTypeDefinition() != typeof(List<>))
            {
                return null;
            }

            var arguments = method.ReturnType.GetGenericArguments();
            return arguments.Length == 1 && IsGameType(arguments[0], "ResourceTuple")
                ? method
                : null;
        }

        /// <summary>
        /// The same discovery for a call whose return type the suite cannot name at compile time —
        /// <c>GetPurchaseCost()</c> hands back a <c>ResourceCostList</c>, which lives in the game.
        /// Any reference return will do; what it can answer is decided from the instance itself.
        /// </summary>
        private static MethodInfo? FindNoArgReturning(Type type, string name)
        {
            var method = type.GetMethod(name, ReflectionUtil.InstanceFlags, null, Type.EmptyTypes, null);
            return method is not null && method.DeclaringType == type && method.ReturnType != typeof(void)
                ? method
                : null;
        }

        private static MethodInfo? FindPurchase(Type type, Type[] parameters)
        {
            var method = type.GetMethod("Purchase", ReflectionUtil.InstanceFlags, null, parameters, null);
            return method is not null && method.DeclaringType == type && method.ReturnType == typeof(void)
                ? method
                : null;
        }

        private static bool TryInvokeBool(MethodInfo method, object source, out bool value)
        {
            value = false;
            try
            {
                if (method.Invoke(source, Array.Empty<object>()) is bool result)
                {
                    value = result;
                    return true;
                }
            }
            catch (Exception ex) when (ex is TargetInvocationException || ex is ArgumentException ||
                                       ex is InvalidOperationException || ex is TargetException)
            {
            }

            return false;
        }
    }
}
