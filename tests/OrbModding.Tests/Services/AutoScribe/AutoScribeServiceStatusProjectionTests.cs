using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class AutoScribeServiceStatusProjectionTests
{
    [Fact]
    public void QueueEvidenceReasonSurvivesProjectionIntoTheUserStatus()
    {
        var profile = AutoScribeIdentityCatalog.Audited;
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration
            {
                Mode = AutoScribeOperationMode.Active,
            },
        };
        var store = new ReusableActionStore<AutoScribeCycleAction>();
        store.BeginWrite();
        AutoScribeCycleEvaluator.Evaluate(
            WorldWithoutQueueEvidence(profile),
            in configuration,
            profile,
            enabledRoles: null,
            afterCraftCostOrder: -1,
            new ServiceActionWriter<AutoScribeCycleAction>(store),
            out var decision);
        Assert.Equal(0, store.Count);
        Assert.Equal(AutoScribeEvidenceReason.QueueEvidenceUnavailable, decision.BlockedReason);
        var state = AutoScribeCycleState.Create(new LifecycleGeneration(1));
        state.RecordDecision(in decision);
        var projection = Projection(in state);

        Assert.True(AutoScribeServiceProjection.TryRead(
            in projection,
            out var kind,
            out var blockedRole,
            out var blockedReason));
        var status = AutoScribeServiceCycleDiagnosticsBridge.ProjectStatus(
            profile,
            emergencyDisabled: false,
            ownsActionFamily: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            new AutoScribeActionHealth(),
            cycleObserved: true,
            kind,
            blockedRole,
            blockedReason);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.State);
        Assert.Equal(FeatureStatusReasonCode.EvidenceUnavailable, status.Reason);
        Assert.Contains(
            "ActiveScribeInstances capacity evidence was missing or contradictory.",
            status.Summary);
        Assert.DoesNotContain("has complete evidence", status.Summary);
    }

    [Fact]
    public void GenuineActionFailurePrecedesLaterEvidenceBackpressure()
    {
        const string failureReason = "The live recipe relation contradicted its audited role.";
        var failure = AutoScribeSubmission.Reject(
            AutoScribePreflight.RelationshipMismatch,
            failureReason);
        var health = new AutoScribeActionHealth();
        Assert.True(health.Observe(in failure));

        var status = AutoScribeServiceCycleDiagnosticsBridge.ProjectStatus(
            AutoScribeIdentityCatalog.Audited,
            emergencyDisabled: false,
            ownsActionFamily: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            health,
            cycleObserved: true,
            AutoScribeDecisionKind.EvidenceBlocked,
            blockedRole: 0,
            AutoScribeEvidenceReason.QueueEvidenceUnavailable);

        Assert.Equal(FeatureStatusState.ContractUnavailable, status.State);
        Assert.Equal(FeatureStatusReasonCode.IdentityMismatch, status.Reason);
        Assert.Equal(failureReason, status.Summary);
    }

    [Fact]
    public void EveryDeclaredEvidenceReasonSurvivesProjectionAndRendersItsOwnSummary()
    {
        var summaries = new HashSet<string>();
        foreach (AutoScribeEvidenceReason reason in
            Enum.GetValues(typeof(AutoScribeEvidenceReason)))
        {
            // AutoScribeDecisionMetrics is the evaluator's complete output contract. Materialize
            // each declared reason there so a future enum addition must survive the same state and
            // projection path before it can acquire a user-facing summary.
            var decision = new AutoScribeDecisionMetrics(
                enabledRoles: 1,
                deficientRoles: 0,
                externalRoles: 0,
                plannedActions: 0,
                selectedCraftCostOrder: -1,
                AutoScribeDecisionKind.EvidenceBlocked,
                blockedRoleOrdinal: 0,
                reason);
            var state = AutoScribeCycleState.Create(new LifecycleGeneration(1));
            state.RecordDecision(in decision);
            var projection = Projection(in state);

            Assert.True(AutoScribeServiceProjection.TryRead(
                in projection,
                out var kind,
                out var blockedRole,
                out var projectedReason));
            Assert.Equal(AutoScribeDecisionKind.EvidenceBlocked, kind);
            Assert.Equal(0, blockedRole);
            Assert.Equal(reason, projectedReason);

            var status = Status(kind, blockedRole, projectedReason);
            Assert.Equal(ExpectedReasonText(reason), status.Summary);
            Assert.True(summaries.Add(status.Summary), $"Duplicate summary for {reason}.");
        }
    }

    [Fact]
    public void UnknownProjectedReasonIsLoudInsteadOfClaimingCompleteEvidence()
    {
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        builder.Add(
            new ServiceProjectionKey(AutoScribeServiceProjection.DecisionKindKey),
            ServiceProjectionValue.FromInteger((int)AutoScribeDecisionKind.EvidenceBlocked));
        builder.Add(
            new ServiceProjectionKey(AutoScribeServiceProjection.BlockedRoleKey),
            ServiceProjectionValue.FromInteger(0));
        builder.Add(
            new ServiceProjectionKey(AutoScribeServiceProjection.BlockedReasonKey),
            ServiceProjectionValue.FromInteger(10));
        var projection = builder.CaptureSnapshot();

        Assert.True(AutoScribeServiceProjection.TryRead(
            in projection,
            out var kind,
            out var blockedRole,
            out var reason));
        var status = Status(kind, blockedRole, reason);

        Assert.Equal(AutoScribeEvidenceReason.Unknown, reason);
        Assert.Contains("unknown evidence reason", status.Summary);
        Assert.DoesNotContain("has complete evidence", status.Summary);
    }

    private static ServiceStateProjectionSnapshot Projection(in AutoScribeCycleState state)
    {
        var buffer = new ServiceStateProjectionWriteBuffer(
            ServiceStateProjectionSnapshot.MaximumEntryCount);
        var builder = new ServiceStateProjectionBuilder(buffer);
        AutoScribeServiceProjection.Write(in state, builder);
        return builder.CaptureSnapshot();
    }

    private static AutoScribeServiceCycleDiagnosticsBridge.AutoScribeFeatureStatus Status(
        AutoScribeDecisionKind kind,
        int blockedRole,
        AutoScribeEvidenceReason reason) =>
        AutoScribeServiceCycleDiagnosticsBridge.ProjectStatus(
            AutoScribeIdentityCatalog.Audited,
            emergencyDisabled: false,
            ownsActionFamily: true,
            ownershipReason: string.Empty,
            bindingsAvailable: true,
            bindingFailure: string.Empty,
            new AutoScribeActionHealth(),
            cycleObserved: true,
            kind,
            blockedRole,
            reason);

    private static string ExpectedReasonText(AutoScribeEvidenceReason reason)
    {
        var role = AutoScribeIdentityCatalog.Audited.Roles[0];
        var prefix = $"{role.DisplayName} ({role.Key.Value})";
        return reason switch
        {
            AutoScribeEvidenceReason.Unknown =>
                prefix + " is blocked because the service projection reported an unknown evidence reason.",
            AutoScribeEvidenceReason.None => prefix + " has complete evidence.",
            AutoScribeEvidenceReason.CollectionUnavailable =>
                prefix + " is blocked because the Scribe relationship collection was incomplete.",
            AutoScribeEvidenceReason.RecipeRegistryIncomplete =>
                prefix + " is blocked because ScribeCraftingRecipes was not exactly the six audited recipes.",
            AutoScribeEvidenceReason.RecipeMissing =>
                prefix + $" is blocked because recipe {EntityIdentityFormatter.Format(role.Recipe!.Value.Uuid)} was absent.",
            AutoScribeEvidenceReason.RecipeRelationshipMismatch =>
                prefix + " is blocked because its live recipe/type/output/level relationship contradicted the audited role.",
            AutoScribeEvidenceReason.TargetLevelUnavailable =>
                prefix + " is blocked because its per-Scroll progression frontier was unavailable.",
            AutoScribeEvidenceReason.TargetEvidenceMissing =>
                prefix + " is blocked because its Scroll target relationship was unavailable.",
            AutoScribeEvidenceReason.NonPositiveCarryLimit =>
                prefix + " is blocked because native Gain() silently drops positive Scroll " +
                "output when maximum carry load is non-positive.",
            AutoScribeEvidenceReason.TargetEvidenceContradictory =>
                prefix + " is blocked because its Scroll target count contradicted the completeness marker.",
            AutoScribeEvidenceReason.QueueEvidenceUnavailable =>
                prefix + " is blocked because ActiveScribeInstances capacity evidence was missing or contradictory.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }

    private static GameWorldState WorldWithoutQueueEvidence(
        AutoScribeIdentityProfile profile)
    {
        var recipes = new List<WorldScribeRecipe>();
        for (var index = 0; index < profile.Roles.Count; index++)
        {
            var role = profile.Roles[index];
            if (!role.IsProducible) continue;
            recipes.Add(new WorldScribeRecipe(
                role.Recipe!.Value.Uuid,
                profile.RecipeType.Uuid,
                role.Scroll.Uuid,
                visible: true,
                usesQuantityAsLevel: true));
        }
        return new GameWorldState
        {
            CollectedAtFrame = 10,
            CollectedAtEpoch = 1,
            CollectionCategories = Table(new WorldCollectionCategoryStatus(
                ScrollCoveragePlanner.CollectionCategory,
                WorldCategoryOutcome.Collected,
                sampled: 1,
                skipped: 0,
                firstFailure: string.Empty)),
            ScribeRecipes = Table(recipes.ToArray()),
            ScribeQueues = PublicationTable<WorldScribeQueue>.Empty,
        };
    }

    private static PublicationTable<T> Table<T>(params T[] rows)
        where T : struct => PublicationTable<T>.Create(rows, rows.Length);
}
