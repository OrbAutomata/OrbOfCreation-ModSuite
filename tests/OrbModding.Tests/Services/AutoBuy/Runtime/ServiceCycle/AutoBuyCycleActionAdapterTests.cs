using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

public sealed class AutoBuyCycleActionAdapterTests : IDisposable
{
    private const long PlannedEpoch = 7;

    public AutoBuyCycleActionAdapterTests() => ResetNativeState();

    public void Dispose() => ResetNativeState();

    [Fact]
    public void Execute_StructurePurchase_CommitsWithVerifiedEvidence()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Committed, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
        Assert.Equal(4, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_UpgradePurchase_PinsSingleBuyAndCommits()
    {
        global::GlobalVariables.MultiBuy.Value = 5;
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            level = 2,
        };
        global::UpgradeSO.All.Add(upgrade);

        var result = Execute(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(3, upgrade.GetQueuedPurchaseLevel());
        // The scope restored the operator's multiplier after the single-level purchase.
        Assert.Equal(5, global::GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void Execute_LifecycleEpochDrift_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch + 1);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, structure.queuedQuantity);
    }

    /// <summary>
    /// The epoch the boundary trusts is the one the plan's own world was collected under, not the one
    /// the runner was built with.
    /// </summary>
    /// <remarks>
    /// The two come apart in exactly one window, and it is a real one: the game reloads, the collector
    /// reads the new run, and the host has not yet been told to replace the runner. The plan is about
    /// the world it was made from, and that world is the current one — so the purchase stands. Judging
    /// it by the runner's frozen lifecycle would refuse a purchase that is perfectly good, which is
    /// what this used to do.
    /// </remarks>
    [Fact]
    public void Execute_WorldCollectedAheadOfTheRunnersLifecycle_PurchasesAnyway()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch + 1,
            plannedEpoch: PlannedEpoch + 1);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(2, structure.queuedQuantity);
    }

    /// <summary>
    /// The mirror of the case above, and the one that matters for safety: a plan made against the run
    /// the game has since left is refused however current the runner believes it is.
    /// </summary>
    [Fact]
    public void Execute_PlanFromAPreviousRunOfTheGame_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            plannedEpoch: PlannedEpoch - 1);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, structure.queuedQuantity);
    }

    /// <summary>
    /// A world nobody collected authorises nothing. Zero is the epoch no lifecycle ever has, so it
    /// matches no live reading and the purchase never reaches the game.
    /// </summary>
    [Fact]
    public void Execute_PlanFromAWorldNobodyCollected_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            plannedEpoch: 0);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, structure.queuedQuantity);
    }

    /// <summary>
    /// The live half of the guard fails closed too: a game reporting no epoch authorises nothing, even
    /// when the plan carries the same zero and the two therefore agree.
    /// </summary>
    [Fact]
    public void Execute_GameReportsNoLifecycleEpoch_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: 0,
            plannedEpoch: 0);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_KindNotSelected_RejectsServiceDisabled()
    {
        var structure = new global::StructureSO { uuid = Guid.NewGuid().ToString() };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            structures: false,
            upgrades: true);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.ServiceDisabled, result.Code);
    }

    [Fact]
    public void Execute_CandidateNotFound_FaultsAdapter()
    {
        var result = Execute(AutoBuyCandidateKind.Structure, Guid.NewGuid(), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    /// <summary>
    /// <c>CanPurchase()</c> is still asked immediately before mutating: it folds in live
    /// requirements and queue room, which move between the plan and the call.
    /// </summary>
    [Fact]
    public void Execute_NotAdmissible_RejectsNative()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = false,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.NativeRejected, result.Code);
    }

    // ---- A refusal names the term that refused ---------------------------------------------------

    /// <summary>
    /// The price is what refused, and the submission says so.
    /// </summary>
    /// <remarks>
    /// <c>CanPurchase()</c> answers with one bool over several conditions, so "the game said no" is
    /// all a refusal used to carry. Reading the terms the game exposes individually turns that into a
    /// cause a person can act on.
    /// </remarks>
    [Fact]
    public void Submit_RefusedOnPrice_NamesTheCostList()
    {
        var mana = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(3.2, 3),
        };
        var spark = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(1.9, 1),
        };
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        upgrade.purchaseCost.costs.Add(new global::ResourceTuple(mana, new BigDouble(1.6, 3)));
        upgrade.purchaseCost.costs.Add(new global::ResourceTuple(spark, new BigDouble(2.0, 1)));
        global::UpgradeSO.All.Add(upgrade);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count: 1);

        Assert.Equal(AutoBuyPurchasePreflight.NotAdmissible, submission.Preflight);
        Assert.Equal(AutoBuyAdmissionTerm.Passed, submission.Diagnosis.IsAvailable);
        Assert.Equal(AutoBuyAdmissionTerm.Refused, submission.Diagnosis.HasEnough);
        Assert.Equal("GetPurchaseCost().HasEnough()", submission.Diagnosis.RefusingTerm);
        Assert.Equal(AutoBuyRefusalClassification.AffordabilityChanged, submission.Diagnosis.Classification);
        Assert.True(submission.Diagnosis.LiveCosts.IsComplete);
        var rows = submission.Diagnosis.LiveCosts.Rows;
        Assert.Equal(2, rows.Length);
        Assert.Equal(Guid.Parse(mana.uuid), rows[0].ResourceId);
        Assert.Equal(new BigDouble(1.6, 3), rows[0].Cost);
        Assert.Equal(mana.GetTrueQuantity(), rows[0].Available);
        Assert.Equal(Guid.Parse(spark.uuid), rows[1].ResourceId);
        Assert.Equal(new BigDouble(2.0, 1), rows[1].Cost);
        Assert.Equal(spark.GetTrueQuantity(), rows[1].Available);
    }

    /// <summary>
    /// StructureSO.CanPurchase() does not include affordability, so the action boundary must ask the
    /// exact native cost list before calling Purchase(true). Otherwise the native method silently
    /// declines the purchase and the verifier can report only an unexplained zero delta.
    /// </summary>
    [Fact]
    public void Submit_StructureUnaffordable_RefusesBeforeTheNativeCall()
    {
        var resource = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(1.0, 0),
        };
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        structure.purchaseCost.costs.Add(
            new global::ResourceTuple(resource, new BigDouble(2.0, 0)));
        global::StructureSO.All.Add(structure);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 1);

        Assert.Equal(AutoBuyPurchasePreflight.NotAdmissible, submission.Preflight);
        Assert.False(submission.HasEvidence);
        Assert.Equal(AutoBuyAdmissionTerm.Refused, submission.Diagnosis.HasEnough);
        Assert.Equal(AutoBuyRefusalClassification.AffordabilityChanged, submission.Diagnosis.Classification);
        Assert.Equal(0, structure.queuedQuantity);
        Assert.Equal(0, structure.purchaseCost.PerformCalls);
    }

    /// <summary>
    /// The exact reported shape — affordable in the plan, price-only refusal at the boundary — is a
    /// pre-native skip. It records the disagreement but does not turn it into a structural rejection.
    /// </summary>
    [Theory]
    [InlineData("2fa66381-76be-42d5-a25b-31cb5790f03a", "cc4f0000-0000-0000-0000-000000000000", 2.0, 1, 2.5335543615217575, 1)]
    [InlineData("30263415-650b-4544-85f9-cff8afdb089b", "55758000-0000-0000-0000-000000000000", 4.5, 57, 2.6160792960345307, 58)]
    public void Execute_PriceOnlyRefusal_SkipsWithoutNativeEvidence(
        string candidateText,
        string resourceText,
        double costMantissa,
        int costExponent,
        double plannedAvailableMantissa,
        int plannedAvailableExponent)
    {
        var candidateId = Guid.Parse(candidateText);
        var resourceId = Guid.Parse(resourceText);
        var resource = new global::ResourceSO
        {
            uuid = resourceId.ToString(),
            quantity = new BigDouble(costMantissa * 0.9, costExponent),
        };
        var upgrade = new global::UpgradeSO
        {
            uuid = candidateId.ToString(),
            available = true,
            purchasable = true,
        };
        var cost = new BigDouble(costMantissa, costExponent);
        upgrade.purchaseCost.costs.Add(new global::ResourceTuple(resource, cost));
        global::UpgradeSO.All.Add(upgrade);
        var belief = Belief(
            resourceId,
            cost,
            new BigDouble(plannedAvailableMantissa, plannedAvailableExponent));
        var refusals = new RecordingRefusals();

        var result = Execute(
            AutoBuyCandidateKind.Upgrade,
            candidateId,
            nativeEpoch: PlannedEpoch,
            refusals: refusals,
            belief: belief);

        Assert.Equal(ServiceActionDisposition.Skipped, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Skipped, result.Code);
        Assert.False(result.HasNativeEvidence);
        var report = Assert.Single(refusals.Reports);
        Assert.Equal(AutoBuyRefusalClassification.AffordabilityChanged, report.Diagnosis.Classification);
        Assert.Equal(resourceId, Assert.Single(report.Diagnosis.LiveCosts.Rows.ToArray()).ResourceId);
    }

    [Fact]
    public void Execute_LaterAffordabilityRefusal_NamesEarlierSameBatchResourceOverlap()
    {
        var shared = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(4.0, 1),
        };
        var first = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        first.purchaseCost.costs.Add(
            new global::ResourceTuple(shared, new BigDouble(2.0, 1)));
        var second = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        second.purchaseCost.costs.Add(
            new global::ResourceTuple(shared, new BigDouble(3.0, 1)));
        global::UpgradeSO.All.Add(first);
        global::UpgradeSO.All.Add(second);
        var refusals = new RecordingRefusals();
        var adapter = new AutoBuyCycleActionAdapter(
            NativeAdapter(),
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            refusals,
            new FixedWorldGeneration(new WorldGeneration(9)));

        var firstResult = adapter.TryExecute(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Upgrade,
                Guid.Parse(first.uuid),
                PlannedEpoch,
                count: 1,
                Belief(Guid.Parse(shared.uuid), new BigDouble(2.0, 1), shared.GetTrueQuantity()),
                new MonotonicTimestamp(100)),
            Config(structures: true, upgrades: true),
            Context(actionIndex: 0, attemptedAt: 130));
        var secondResult = adapter.TryExecute(
            new AutoBuyCycleAction(
                AutoBuyCandidateKind.Upgrade,
                Guid.Parse(second.uuid),
                PlannedEpoch,
                count: 1,
                Belief(Guid.Parse(shared.uuid), new BigDouble(3.0, 1), shared.GetTrueQuantity()),
                new MonotonicTimestamp(100)),
            Config(structures: true, upgrades: true),
            Context(actionIndex: 1, attemptedAt: 150));

        Assert.Equal(ServiceActionDisposition.Committed, firstResult.Disposition);
        Assert.Equal(ServiceActionDisposition.Skipped, secondResult.Disposition);
        var report = Assert.Single(refusals.Reports);
        var earlier = Assert.Single(report.EarlierPurchases.ToArray());
        Assert.Equal(Guid.Parse(first.uuid), earlier.Uuid);
        Assert.Equal(0, earlier.ActionIndex);
        Assert.Equal(1, earlier.CommittedLevels);
        Assert.Equal((ulong)9, report.LatestWorldGeneration);
        Assert.Equal(new MonotonicTimestamp(100), report.WorldCollectedAt);
        Assert.Equal(new MonotonicTimestamp(150), report.AdmissionAttemptedAt);
    }

    [Fact]
    public void Submit_StructureUnavailable_RefusesBeforeTheThinCanPurchase()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = false,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 1);

        Assert.Equal(AutoBuyPurchasePreflight.StructureUnavailable, submission.Preflight);
        Assert.Equal(0, structure.queuedQuantity);
    }

    /// <summary>
    /// A topology stamped for another run is its own refusal, and it says which run it holds.
    /// </summary>
    /// <remarks>
    /// This used to share one preflight and one number with four other causes, so the twenty-seven
    /// minutes of refused purchases it produced looked exactly like an unbound contract. All three
    /// numbers are in the sentence because none can be inferred from the others: stamped-at-zero was
    /// never published under a lifecycle, stamped-at-another-epoch means the game moved on, and the
    /// row count separates both from a topology that was captured and simply holds nothing.
    /// </remarks>
    [Fact]
    public void Submit_TopologyStampedForAnotherRun_NamesBothEpochsAndTheRowCount()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);

        var submission = NativeAdapter(lifecycleEpoch: 3).Submit(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            count: 1,
            lifecycleEpoch: 9);

        Assert.Equal(AutoBuyPurchasePreflight.OwningViewTopologyUncaptured, submission.Preflight);
        Assert.Contains("epoch 3", submission.Reason);
        Assert.Contains("epoch 9", submission.Reason);
        Assert.Contains("1 row(s)", submission.Reason);
        Assert.Equal(0, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_LockedOwningView_RefusesForcedActionWithNamedCode()
    {
        global::ViewSO.All[0].available = false;
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.OwningViewUnavailable, result.Code);
        Assert.Equal(0, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_MissingOwningViewRelation_RefusesWithNamedCode()
    {
        global::ViewSO.All[0].relevantLists.Clear();
        var structure = new global::StructureSO { uuid = Guid.NewGuid().ToString() };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(AutoBuyActionResultCodes.OwningViewRelationMissing, result.Code);
        Assert.Equal(0, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_UnreadableOwningViewRelation_RefusesWithNamedCode()
    {
        global::ViewSO.All[0].relevantLists = null!;
        var structure = new global::StructureSO { uuid = Guid.NewGuid().ToString() };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(AutoBuyActionResultCodes.OwningViewRelationUnreadable, result.Code);
        Assert.Equal(0, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_TwoVisibleOwningViewRoutes_AdmitsExactlyOnePurchase()
    {
        var secondList = new global::StructureListVariable { value = global::StructureSO.All };
        var secondView = new global::ViewSO { available = true };
        secondView.availableLists.Add(secondList);
        global::ViewSO.All.Add(secondView);
        var structure = new global::StructureSO { uuid = Guid.NewGuid().ToString() };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(1, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_OneOfTwoOwningViewRoutesVisible_AdmitsExactlyOnePurchase()
    {
        global::ViewSO.All[0].available = false;
        var secondList = new global::StructureListVariable { value = global::StructureSO.All };
        var secondView = new global::ViewSO { available = true };
        secondView.availableLists.Add(secondList);
        global::ViewSO.All.Add(secondView);
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(1, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_LockedParentAndVisibleChildSharingOneList_RefusesBeforePayment()
    {
        global::ViewSO.All.Clear();
        var sharedList = new global::StructureListVariable { value = global::StructureSO.All };
        var lockedParent = new global::ViewSO { available = false };
        lockedParent.relevantLists.Add(sharedList);
        var visibleChild = new global::ViewSO { available = true };
        visibleChild.relevantLists.Add(sharedList);
        global::ViewSO.All.Add(lockedParent);
        global::ViewSO.All.Add(visibleChild);
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.OwningViewUnavailable, result.Code);
        Assert.Equal(0, structure.queuedQuantity);
        Assert.Equal(0, structure.purchaseCost.PerformCalls);
    }

    [Fact]
    public void Execute_NoVisibleOwningViewRoute_RefusesWithoutPayment()
    {
        global::ViewSO.All[0].available = false;
        var secondList = new global::StructureListVariable { value = global::StructureSO.All };
        var secondView = new global::ViewSO { available = false };
        secondView.availableLists.Add(secondList);
        global::ViewSO.All.Add(secondView);
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.OwningViewUnavailable, result.Code);
        Assert.Equal(0, structure.queuedQuantity);
        Assert.Equal(0, structure.purchaseCost.PerformCalls);
    }

    [Fact]
    public void Execute_ContradictoryOwningViewRelation_RefusesWithNamedCode()
    {
        var structure = new global::StructureSO { uuid = Guid.NewGuid().ToString() };
        structure.structureType.SetStructuresForTests(
            new System.Collections.Generic.List<global::StructureSO>());
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(AutoBuyActionResultCodes.OwningViewRelationContradictory, result.Code);
        Assert.Equal(0, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_FullAuditedDestination_RefusesBeforePaymentWithNamedCode()
    {
        var upgrade = AspectUpgrade(slotCount: 1, occupied: 1);

        var result = Execute(
            AutoBuyCandidateKind.Upgrade,
            Guid.Parse(upgrade.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.DestinationCapacityFull, result.Code);
        Assert.Equal(0, upgrade.queuedLevels);
        Assert.Equal(0, upgrade.purchaseCost.PerformCalls);
    }

    [Fact]
    public void Execute_RoomyAuditedDestination_CommitsUnchanged()
    {
        var upgrade = AspectUpgrade(slotCount: 2, occupied: 1);

        var result = Execute(
            AutoBuyCandidateKind.Upgrade,
            Guid.Parse(upgrade.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(1, upgrade.queuedLevels);
        Assert.Equal(1, upgrade.purchaseCost.PerformCalls);
    }

    [Fact]
    public void Execute_DestinationMaximumIdentityMismatch_RefusesWithNamedCode()
    {
        var upgrade = AspectUpgrade(slotCount: 2, occupied: 1, exactMaximumIdentity: false);

        var result = Execute(
            AutoBuyCandidateKind.Upgrade,
            Guid.Parse(upgrade.uuid),
            nativeEpoch: PlannedEpoch);

        Assert.Equal(AutoBuyActionResultCodes.DestinationCapacityIdentityMismatch, result.Code);
        Assert.Equal(0, upgrade.queuedLevels);
        Assert.Equal(0, upgrade.purchaseCost.PerformCalls);
    }

    /// <summary>
    /// The queued-level cap is the term that livelocked a live session while the log blamed the price.
    /// </summary>
    [Fact]
    public void Submit_RefusedOnTheQueuedLevelCap_NamesIt()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            level = 1,
            queuedLevels = 1,
            maxLevel = 2,
        };
        global::UpgradeSO.All.Add(upgrade);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count: 1);

        Assert.Equal(AutoBuyPurchasePreflight.NotAdmissible, submission.Preflight);
        Assert.Equal(AutoBuyAdmissionTerm.Passed, submission.Diagnosis.IsMaxLevel);
        Assert.Equal(AutoBuyAdmissionTerm.Refused, submission.Diagnosis.IsMaxQueuedLevel);
        Assert.Equal("IsMaxQueuedLevel()", submission.Diagnosis.RefusingTerm);
    }

    /// <summary>
    /// Every readable term passes and the game still refused, so the cause is the one that cannot be
    /// read parameterlessly — the per-level prerequisites — and the diagnosis says so by elimination
    /// rather than blaming a term that answered.
    /// </summary>
    [Fact]
    public void Submit_RefusedWithEveryReadableTermPassing_LeavesNoTermNamed()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = false,
        };
        global::StructureSO.All.Add(structure);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 1);

        Assert.Equal(AutoBuyPurchasePreflight.NotAdmissible, submission.Preflight);
        Assert.Equal(string.Empty, submission.Diagnosis.RefusingTerm);
        Assert.True(submission.Diagnosis.WasAsked);
        Assert.Equal(
            "refused by an unreadable admission term (per-level prerequisites by elimination, " +
            "which the planner modelled as met)",
            submission.Diagnosis.Describe());
    }

    /// <summary>
    /// A refused plan is handed on with both halves of the disagreement: what the worker believed,
    /// and which live term contradicted it.
    /// </summary>
    [Fact]
    public void Execute_NotAdmissible_ReportsTheRefusalWithBothHalves()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            level = 1,
            queuedLevels = 1,
            maxLevel = 2,
        };
        global::UpgradeSO.All.Add(upgrade);
        var resourceId = Guid.NewGuid();
        var belief = new AutoBuyPlanBelief(
            isAvailable: true,
            hasFiniteLevels: true,
            isMaxLevel: false,
            isMaxQueuedLevel: false,
            currentLevel: 1,
            queuedLevels: 0,
            costResourceCount: 1,
            pricedResourceCount: 1,
            costRatio: 0.25,
            bindingResourceId: resourceId,
            bindingIsBandwidth: false,
            bindingCost: new BigDouble(2.0, 0),
            bindingAvailable: new BigDouble(8.0, 0),
            bindingReserveFloor: default);
        var refusals = new RecordingRefusals();

        var result = Execute(
            AutoBuyCandidateKind.Upgrade,
            Guid.Parse(upgrade.uuid),
            nativeEpoch: PlannedEpoch,
            refusals: refusals,
            belief: belief);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        var report = Assert.Single(refusals.Reports);
        Assert.Equal(Guid.Parse(upgrade.uuid), report.Uuid);
        Assert.Equal(AutoBuyCandidateKind.Upgrade, report.Kind);
        Assert.Equal("IsMaxQueuedLevel()", report.Diagnosis.RefusingTerm);
        // The plan believed there was room; the game says the queue already reached the cap.
        Assert.False(report.Belief.IsMaxQueuedLevel);
        Assert.Equal(0.25, report.Belief.CostRatio);
        Assert.Equal(resourceId, report.Belief.BindingResourceId);
        Assert.Equal(PlannedEpoch, report.CollectedAtEpoch);
        Assert.Equal((ulong)1, report.WorldGeneration);
        Assert.Equal((ulong)1, report.ConfigGeneration);
    }

    [Fact]
    public void Execute_CommittedPurchase_ReportsNoRefusal()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);
        var refusals = new RecordingRefusals();

        Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            refusals: refusals);

        Assert.Empty(refusals.Reports);
    }

    /// <summary>
    /// Running out of queue room is the operator's reserve doing its job, not the planner being
    /// wrong, so it stands nothing down.
    /// </summary>
    [Fact]
    public void Execute_QueueReserveReached_ReportsNoRefusal()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);
        var refusals = new RecordingRefusals();

        Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            leaveQueueSlots: 2,
            remainingRoom: 2,
            refusals: refusals);

        Assert.Empty(refusals.Reports);
    }

    private sealed class RecordingRefusals : IAutoBuyRefusalResponsePort
    {
        internal System.Collections.Generic.List<AutoBuyRefusalReport> Reports { get; } = new();

        public void ObserveRefusal(in AutoBuyRefusalReport report) => Reports.Add(report);
    }

    /// <summary>
    /// Structure availability can drop after planning and is therefore re-read at the boundary.
    /// </summary>
    /// <remarks>
    /// The structure CanPurchase contract does not contain this term, so only the explicit live gate
    /// can stop a previously planned action after its own availability drops.
    /// </remarks>
    [Fact]
    public void Execute_StructureAvailabilityDroppedAfterPlanning_RefusesWithNamedCode()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = false,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.StructureUnavailable, result.Code);
        Assert.Equal(3, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_MutationDidNotApply_FaultsWithZeroCommitEvidence()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 0,
            ApplyPurchaseMutation = false,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), nativeEpoch: PlannedEpoch);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.NotEqual(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
        Assert.Equal(1, result.NativeEvidence.CallOutcome.MutationAttempts);
        Assert.Equal(0, result.NativeEvidence.CallOutcome.MutationsCommitted);
        Assert.Equal(0, structure.queuedQuantity);
    }

    [Fact]
    public void Submit_UpgradeBulk_CommitsEveryRequestedLevelAndRestoresMultiplier()
    {
        global::GlobalVariables.MultiBuy.Value = 7;
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::UpgradeSO.All.Add(upgrade);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count: 3);

        Assert.True(submission.Verified);
        Assert.Equal(3, submission.RequestedLevels);
        Assert.Equal(3, submission.CommittedLevels);
        Assert.Equal(3, upgrade.GetQueuedPurchaseLevel());
        // The scope restored the operator's multiplier after the bulk call.
        Assert.Equal(7, global::GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void Submit_UpgradeBulk_PartialPurchaseIsStillVerified()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            maxLevel = 2,
        };
        global::UpgradeSO.All.Add(upgrade);

        // The multiplier requests three levels but the upgrade caps at two: "bought 2 of 3".
        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count: 3);

        Assert.True(submission.Verified);
        Assert.Equal(3, submission.RequestedLevels);
        Assert.Equal(2, submission.CommittedLevels);
        Assert.Equal(2, upgrade.GetQueuedPurchaseLevel());
    }

    [Fact]
    public void Submit_UpgradeSingle_CommitsExactlyOneLevel()
    {
        global::GlobalVariables.MultiBuy.Value = 9;
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedLevels = 4,
        };
        global::UpgradeSO.All.Add(upgrade);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count: 1);

        Assert.True(submission.Verified);
        Assert.Equal(1, submission.RequestedLevels);
        Assert.Equal(1, submission.CommittedLevels);
        Assert.Equal(5, upgrade.GetQueuedPurchaseLevel());
        Assert.Equal(9, global::GlobalVariables.MultiBuy.Value);
    }

    /// <summary>
    /// A structure asked for several levels queues several, one native call each.
    /// </summary>
    /// <remarks>
    /// The native structure purchase forces exactly one level and consults no multiplier, so bulk
    /// development is the same call repeated — which is why the count used to be discarded here and
    /// the game's own bulk-build setting did nothing.
    /// </remarks>
    [Fact]
    public void Submit_StructureBulk_QueuesEveryRequestedLevel()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 2,
        };
        global::StructureSO.All.Add(structure);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 5);

        Assert.True(submission.Verified);
        Assert.Equal(5, submission.RequestedLevels);
        Assert.Equal(5, submission.CommittedLevels);
        Assert.Equal(7, structure.queuedQuantity);
    }

    [Fact]
    public void Submit_StructureSingle_QueuesExactlyOne()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 2,
        };
        global::StructureSO.All.Add(structure);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 1);

        Assert.True(submission.Verified);
        Assert.Equal(1, submission.RequestedLevels);
        Assert.Equal(1, submission.CommittedLevels);
        Assert.Equal(3, structure.queuedQuantity);
    }

    /// <summary>
    /// A group that runs out partway is a partial success, not a refusal.
    /// </summary>
    /// <remarks>
    /// Each level past the first is guarded by the game's own admission check, so a group that stops
    /// early stops because the game said so. Treating that as a failed submission would stand the
    /// service down over the ordinary end of a bulk buy.
    /// </remarks>
    [Fact]
    public void Submit_StructureBulk_StopsWhenTheGameStopsAdmitting()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 0,
        };
        // The holdings cover three levels; the fourth is where the game says no.
        structure.purchaseCost.AffordableLevels = 3;
        global::StructureSO.All.Add(structure);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Structure, Guid.Parse(structure.uuid), count: 6);

        Assert.True(submission.Verified);
        Assert.Equal(6, submission.RequestedLevels);
        Assert.Equal(3, submission.CommittedLevels);
        Assert.Equal(3, structure.queuedQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submit_RejectsNonPositiveCount(int count)
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::UpgradeSO.All.Add(upgrade);

        var submission = NativeAdapter()
            .Submit(AutoBuyCandidateKind.Upgrade, Guid.Parse(upgrade.uuid), count);

        Assert.False(submission.Verified);
        Assert.Equal(AutoBuyPurchasePreflight.CandidateUnavailable, submission.Preflight);
        Assert.Equal(0, upgrade.GetQueuedPurchaseLevel());
    }

    [Fact]
    public void Execute_QueueReserveReached_RejectsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 2,
        };
        global::StructureSO.All.Add(structure);

        // Only the reserved slots remain free, so no purchase may be submitted.
        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            leaveQueueSlots: 2,
            remainingRoom: 2);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.NativeRejected, result.Code);
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(2, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_RoomAboveReserve_ProceedsToPurchase()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 0,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            leaveQueueSlots: 1,
            remainingRoom: 2);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(1, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_QueueRoomUnavailable_FaultsWithoutMutation()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            roomReadable: false);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(3, structure.queuedQuantity);
    }

    /// <param name="nativeEpoch">What the live game says its epoch is when the action is submitted.</param>
    /// <param name="plannedEpoch">
    /// The epoch the world this purchase was planned from was collected under, which the action
    /// carries to the boundary. Defaults to agreeing with the cycle's own lifecycle, which is the
    /// ordinary case; a test that wants them to disagree says so.
    /// </param>
    private static ServiceActionResult Execute(
        AutoBuyCandidateKind kind,
        Guid uuid,
        long nativeEpoch,
        long plannedEpoch = PlannedEpoch,
        bool structures = true,
        bool upgrades = true,
        int leaveQueueSlots = 0,
        int remainingRoom = 64,
        bool roomReadable = true,
        AutoBuyCandidateKinds owned = AutoBuyCandidateKinds.All,
        IAutoBuyRefusalResponsePort? refusals = null,
        AutoBuyPlanBelief belief = default)
    {
        var adapter = new AutoBuyCycleActionAdapter(
            NativeAdapter(plannedEpoch),
            new FakeQueueRoom(remainingRoom, roomReadable),
            () => nativeEpoch,
            () => owned,
            refusals ?? IgnoreRefusals.Instance);
        return adapter.TryExecute(
            new AutoBuyCycleAction(kind, uuid, plannedEpoch, count: 1, belief),
            Config(structures, upgrades, leaveQueueSlots),
            Context());
    }

    /// <summary>
    /// A purchase for a kind another plugin holds the lease for is declined, without mutating.
    /// </summary>
    /// <remarks>
    /// The lease is checked here rather than while deciding because it can move mid-cycle, and
    /// because a copy carried into the frame is an answer that has to be taken again anyway. Auto Buy
    /// carried one and never read it, so until this check existed it purchased for families it had
    /// stood down from. Kinds are checked one at a time: holding structures says nothing about
    /// upgrades.
    /// </remarks>
    [Theory]
    [InlineData((int)AutoBuyCandidateKinds.Upgrades)]
    [InlineData((int)AutoBuyCandidateKinds.None)]
    public void Execute_StructurePurchaseWithoutTheStructureLease_IsDeclined(int owned)
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(structure);

        var result = Execute(
            AutoBuyCandidateKind.Structure,
            Guid.Parse(structure.uuid),
            nativeEpoch: PlannedEpoch,
            owned: (AutoBuyCandidateKinds)owned);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.ActionFamilyUnavailable, result.Code);
        Assert.False(result.HasNativeEvidence);
        Assert.Equal(3, structure.queuedQuantity);
    }

    [Fact]
    public void Execute_UpgradePurchaseWithoutTheUpgradeLease_IsDeclined()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            level = 2,
        };
        global::UpgradeSO.All.Add(upgrade);

        var result = Execute(
            AutoBuyCandidateKind.Upgrade,
            Guid.Parse(upgrade.uuid),
            nativeEpoch: PlannedEpoch,
            owned: AutoBuyCandidateKinds.Structures);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.ActionFamilyUnavailable, result.Code);
        Assert.Equal(2, upgrade.level);
    }

    private static ServiceActionContext Context(
        int actionIndex = 0,
        long attemptedAt = 1) =>
        new ServiceActionContext(
            new ServiceCycleIdentity(
                new ServiceId("AutoBuy"),
                new LifecycleGeneration((ulong)PlannedEpoch),
                new ConfigGeneration(1),
                new StrategyGeneration(1),
                new WorldGeneration(1),
                new CycleId(1)),
            new BatchId(1),
            new ActionId(checked((ulong)actionIndex + 1)),
            actionIndex,
            new MonotonicTimestamp(attemptedAt));

    private static AutoBuyPlanBelief Belief(
        Guid resourceId,
        BigDouble cost,
        BigDouble available) =>
        new(
            isAvailable: true,
            hasFiniteLevels: false,
            isMaxLevel: false,
            isMaxQueuedLevel: false,
            currentLevel: 0,
            queuedLevels: 0,
            costResourceCount: 1,
            pricedResourceCount: 1,
            costRatio: 0.5,
            bindingResourceId: resourceId,
            bindingIsBandwidth: false,
            bindingCost: cost,
            bindingAvailable: available,
            bindingReserveFloor: default);

    private sealed class FixedWorldGeneration : IServiceWorldGenerationSource
    {
        private readonly WorldGeneration _generation;

        internal FixedWorldGeneration(WorldGeneration generation) => _generation = generation;

        public bool TryGetLatestGeneration(out WorldGeneration generation)
        {
            generation = _generation;
            return true;
        }
    }

    private static SuiteRuntimeConfiguration Config(bool structures, bool upgrades, int leaveQueueSlots = 0) =>
        new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                IncludeStructures = structures,
                IncludeUpgrades = upgrades,
                LeaveQueueSlots = leaveQueueSlots,
            },
        };

    [Fact]
    public void Execute_CandidateAddedAfterStaticCapture_IsNotReEnumeratedAtActionTime()
    {
        var adapter = new AutoBuyCycleActionAdapter(
            NativeAdapter(),
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);
        var added = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 5,
        };
        global::StructureSO.All.Add(added);

        var result = adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Structure, Guid.Parse(added.uuid), PlannedEpoch),
            Config(structures: true, upgrades: true),
            Context());

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.OwningViewTopologyUncaptured, result.Code);
        Assert.Equal(5, added.queuedQuantity);
    }

    [Fact]
    public void Execute_RebuiltNativeListCannotReusePriorLifecycleRouteEvidence()
    {
        var original = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        global::StructureSO.All.Add(original);
        var adapter = new AutoBuyCycleActionAdapter(
            NativeAdapter(),
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);
        var warmup = adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Structure, Guid.Parse(original.uuid), PlannedEpoch),
            Config(structures: true, upgrades: true),
            Context());
        Assert.Equal(ServiceActionDisposition.Committed, warmup.Disposition);

        // Same list reference and count, entirely new membership. The candidate index may recover,
        // but the authored route snapshot is lifecycle evidence and may not be rebuilt here.
        global::StructureSO.All.Clear();
        var replacement = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 3,
        };
        global::StructureSO.All.Add(replacement);

        var result = adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Structure, Guid.Parse(replacement.uuid), PlannedEpoch),
            Config(structures: true, upgrades: true),
            Context());

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.OwningViewTopologyUncaptured, result.Code);
        Assert.Equal(3, replacement.queuedQuantity);
    }

    [Fact]
    public void Execute_NativePurchaseThrows_FaultsAdapterWithoutEvidence()
    {
        var adapter = new AutoBuyCycleActionAdapter(
            new ThrowingPurchasePort(),
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);

        var result = adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch),
            Config(structures: true, upgrades: true),
            Context());

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.False(result.HasNativeEvidence);
    }

    /// <summary>
    /// One upgrade action is one native call but several queue stacks — the game queues one entry per
    /// committed level and its purchase loop never consults the queue room. Checking that a single slot
    /// exists above the reserve therefore does not mean the whole request fits above it.
    /// </summary>
    [Fact]
    public void Execute_UpgradeMultiBuyLargerThanTheRoomAboveTheReserve_ClampsToIt()
    {
        var purchases = new RecordingPurchasePort();
        var adapter = new AutoBuyCycleActionAdapter(
            purchases,
            new FakeQueueRoom(5, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);

        adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch, count: 4),
            Config(structures: true, upgrades: true, leaveQueueSlots: 3),
            Context());

        Assert.Equal(2, purchases.LastCount);
    }

    [Fact]
    public void Execute_UpgradeMultiBuyThatFitsAboveTheReserve_SubmitsTheFullCount()
    {
        var purchases = new RecordingPurchasePort();
        var adapter = new AutoBuyCycleActionAdapter(
            purchases,
            new FakeQueueRoom(10, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);

        adapter.TryExecute(
            new AutoBuyCycleAction(AutoBuyCandidateKind.Upgrade, Guid.NewGuid(), PlannedEpoch, count: 4),
            Config(structures: true, upgrades: true, leaveQueueSlots: 3),
            Context());

        Assert.Equal(4, purchases.LastCount);
    }

    [Fact]
    public void EarlierVerifiedOverspendSkipsTheNowUnpayableActionBeforeNativeDispatch()
    {
        var resource = Guid.NewGuid();
        var purchases = new ReconciliationPurchasePort(resource, firstActualCost: new BigDouble(15));
        var adapter = new AutoBuyCycleActionAdapter(
            purchases,
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);

        var first = adapter.TryExecute(
            ReconciledAction(Guid.NewGuid(), resource, cost: 10, remaining: 20),
            Config(structures: true, upgrades: true),
            Context(actionIndex: 0));
        var second = adapter.TryExecute(
            ReconciledAction(Guid.NewGuid(), resource, cost: 10, remaining: 10),
            Config(structures: true, upgrades: true),
            Context(actionIndex: 1));

        Assert.Equal(ServiceActionDisposition.Committed, first.Disposition);
        Assert.Equal(ServiceActionDisposition.Skipped, second.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.BatchSpendDrift, second.Code);
        Assert.False(second.HasNativeEvidence);
        Assert.Equal(1, purchases.Submissions);
    }

    [Fact]
    public void ARealBoundaryRefusalKeepsItsExactCodeAfterSpendReconciliation()
    {
        var resource = Guid.NewGuid();
        var purchases = new ReconciliationPurchasePort(
            resource,
            firstActualCost: new BigDouble(10),
            secondPreflight: AutoBuyPurchasePreflight.OwningViewUnavailable);
        var adapter = new AutoBuyCycleActionAdapter(
            purchases,
            new FakeQueueRoom(64, readable: true),
            () => PlannedEpoch,
            () => AutoBuyCandidateKinds.All,
            IgnoreRefusals.Instance);

        adapter.TryExecute(
            ReconciledAction(Guid.NewGuid(), resource, cost: 10, remaining: 30),
            Config(structures: true, upgrades: true),
            Context(actionIndex: 0));
        var refused = adapter.TryExecute(
            ReconciledAction(Guid.NewGuid(), resource, cost: 10, remaining: 20),
            Config(structures: true, upgrades: true),
            Context(actionIndex: 1));

        Assert.Equal(ServiceActionDisposition.Rejected, refused.Disposition);
        Assert.Equal(AutoBuyActionResultCodes.OwningViewUnavailable, refused.Code);
        Assert.Equal(2, purchases.Submissions);
    }

    private static AutoBuyCycleAction ReconciledAction(
        Guid candidate,
        Guid resource,
        double cost,
        double remaining) =>
        new(
            AutoBuyCandidateKind.Upgrade,
            candidate,
            PlannedEpoch,
            count: 1,
            default,
            default,
            plannedSpend: PublicationTable<AutoBuyPlannedSpend>.Create(new[]
            {
                new AutoBuyPlannedSpend(
                    resource,
                    new BigDouble(cost),
                    new BigDouble(remaining),
                    default),
            }));

    private sealed class ReconciliationPurchasePort : IAutoBuyNativePurchasePort
    {
        private readonly Guid _resource;
        private readonly BigDouble _firstActualCost;
        private readonly AutoBuyPurchasePreflight _secondPreflight;

        internal ReconciliationPurchasePort(
            Guid resource,
            BigDouble firstActualCost,
            AutoBuyPurchasePreflight secondPreflight = AutoBuyPurchasePreflight.Proceeded)
        {
            _resource = resource;
            _firstActualCost = firstActualCost;
            _secondPreflight = secondPreflight;
        }

        internal int Submissions { get; private set; }

        public AutoBuyPurchaseSubmission Submit(
            AutoBuyCandidateKind kind,
            Guid uuid,
            int count,
            long lifecycleEpoch)
        {
            Submissions++;
            if (Submissions == 2 && _secondPreflight != AutoBuyPurchasePreflight.Proceeded)
                return AutoBuyPurchaseSubmission.Rejected(_secondPreflight);

            var queued = 0;
            var evidence = NativeMutationVerifier.Execute(
                "Auto Buy Upgrade",
                uuid.ToString(),
                "queued level +1",
                () => queued,
                () => queued++,
                static (before, after) => after == before + 1);
            var live = AutoBuyLiveCostSnapshot.Complete(new[]
            {
                new AutoBuyLiveCostRow(_resource, false, _firstActualCost, new BigDouble(100)),
            });
            return AutoBuyPurchaseSubmission.Attempted(evidence, requestedLevels: 1, in live);
        }
    }

    private sealed class RecordingPurchasePort : IAutoBuyNativePurchasePort
    {
        internal int LastCount { get; private set; }

        public AutoBuyPurchaseSubmission Submit(
            AutoBuyCandidateKind kind,
            Guid uuid,
            int count,
            long lifecycleEpoch)
        {
            LastCount = count;
            return AutoBuyPurchaseSubmission.Rejected(AutoBuyPurchasePreflight.CandidateUnavailable);
        }
    }

    private sealed class IgnoreRefusals : IAutoBuyRefusalResponsePort
    {
        internal static IgnoreRefusals Instance { get; } = new();
        public void ObserveRefusal(in AutoBuyRefusalReport report)
        {
        }
    }

    private sealed class ThrowingPurchasePort : IAutoBuyNativePurchasePort
    {
        public AutoBuyPurchaseSubmission Submit(
            AutoBuyCandidateKind kind,
            Guid uuid,
            int count,
            long lifecycleEpoch) =>
            throw new InvalidOperationException("native purchase threw");
    }

    private sealed class FakeQueueRoom : IAutoBuyQueueRoomPort
    {
        private readonly int _room;
        private readonly bool _readable;

        public FakeQueueRoom(int room, bool readable)
        {
            _room = room;
            _readable = readable;
        }

        public bool TryReadRemainingRoom(out int remainingRoom)
        {
            remainingRoom = _readable ? _room : 0;
            return _readable;
        }
    }

    private static global::UpgradeSO AspectUpgrade(
        int slotCount,
        int occupied,
        bool exactMaximumIdentity = true)
    {
        var maximum = new global::IntVariable
        {
            uuid = exactMaximumIdentity
                ? KnownEntities.WorldAspectSlots.Uuid
                : Guid.NewGuid(),
            Value = slotCount,
        };
        var destination = new global::ViewListVariable
        {
            uuid = KnownEntities.CreatedWorldAspects.Uuid,
            maxSizeVariable = maximum,
        };
        for (var index = 0; index < occupied; index++)
            destination.value.Add(new global::ViewSO());
        var addition = new global::ViewListVariable.ListTuple
        {
            list = destination,
            element = new global::ViewSO(),
        };
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        upgrade.viewListAdditions.Add(addition);
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }

    private static void ResetNativeState()
    {
        global::StructureSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::ViewSO.All.Clear();
        var structures = new global::StructureListVariable { value = global::StructureSO.All };
        var upgrades = new global::UpgradeListVariable { value = global::UpgradeSO.All };
        var owningView = new global::ViewSO { available = true };
        owningView.relevantLists.Add(structures);
        owningView.relevantLists.Add(upgrades);
        global::ViewSO.All.Add(owningView);
        global::GlobalVariables.MultiBuy = new global::IntVariable();
        NativeMultiBuyScope.ResetQuarantineForTests();
    }

    private static AutoBuyNativePurchaseAdapter NativeAdapter(long lifecycleEpoch = PlannedEpoch)
    {
        Assert.True(
            NativePurchaseViewAdmissionResolver.TryCreate(
                OrbModding.Common.Runtime.World.WorldNativeTypes.Resolve,
                out var topology,
                out var topologyFailure),
            topologyFailure);
        topology!.ReadAll(
            lifecycleEpoch,
            new OrbModding.Common.Runtime.World.WorldRelationBuffer<WorldPurchaseViewRelation>(),
            new OrbModding.Common.Runtime.World.WorldRelationBuffer<WorldPurchaseViewRoute>(),
            out _,
            out _);
        return new AutoBuyNativePurchaseAdapter(topology);
    }
}
