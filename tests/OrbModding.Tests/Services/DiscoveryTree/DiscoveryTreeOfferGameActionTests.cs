using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.DiscoveryTree;

public sealed class DiscoveryTreeOfferGameActionTests : IDisposable
{
    private const long Epoch = 17;

    public DiscoveryTreeOfferGameActionTests()
    {
        DiscoveryTreeSO.All.Clear();
        ResourceSO.All.Clear();
    }

    public void Dispose()
    {
        DiscoveryTreeSO.All.Clear();
        ResourceSO.All.Clear();
    }

    [Fact]
    public void Initiate_pays_and_commits_the_crafting_transition()
    {
        var resource = Resource();
        var tree = Tree();
        tree.nextItemCost.costs.Add(new ResourceTuple(resource, new BigDouble(25, 0)));
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);
        Assert.Equal(1, tree.nextItemCost.PerformCalls);
        Assert.Equal(1, tree.initiateCalls);
        Assert.Equal(2, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(1, result.CallOutcome.MutationsCommitted);
    }

    [Fact]
    public void Initiate_sub_ulp_payment_does_not_gate_the_requested_transition()
    {
        var mana = Resource(new BigDouble(2.1, 19));
        var knowledge = Resource(new BigDouble(5.7, 23));
        var tree = Tree();
        tree.nextItemCost.costs.Add(new ResourceTuple(mana, new BigDouble(4.4, 3)));
        tree.nextItemCost.costs.Add(new ResourceTuple(knowledge, new BigDouble(8.9, 6)));
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);
    }

    [Fact]
    public void Initiate_accepts_the_native_maximum_reroll_clamp_even_when_it_reduces_saved_rerolls()
    {
        var tree = Tree();
        tree.rerollsLeft = 1;
        tree.maximumRerolls = 0;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(0, tree.rerollsLeft);
    }

    [Fact]
    public void Initiate_accounting_axis_drift_is_evidence_not_a_gate()
    {
        var tree = Tree();
        tree.driftInitiateEvidence = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);
        Assert.Equal(99, tree.rerollsLeft);
        Assert.True(tree.usedRerollsLastDiscover);
        Assert.NotEmpty(tree.currentChoiceIds);
    }

    [Fact]
    public void Initiate_exception_after_crafting_landed_is_evidence_and_commits()
    {
        var tree = Tree();
        tree.throwAfterInitiate = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);
    }

    [Fact]
    public void Initiate_that_returns_idle_without_offers_faults_with_exact_mode_evidence()
    {
        var tree = Tree();
        tree.suppressInitiate = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.VerificationFailed, result.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Idle, tree.actionMode);
        Assert.Contains("did not enter Crafting mode", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_uses_offer_membership_not_CanDiscover_and_changes_only_selection()
    {
        var (tree, item) = ChoiceTree();
        item.canDiscover = false; // Native future-choice offers are intentionally selectable.
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Select, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(item.GetGuid(), tree.selectedChoiceId.guid);
        Assert.False(item.discovered);
        Assert.Equal(1, tree.selectCalls);
    }

    [Fact]
    public void Select_non_identity_axis_drift_is_evidence_not_a_gate()
    {
        var (tree, item) = ChoiceTree();
        tree.driftSelectEvidence = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Select, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(item.GetGuid(), tree.selectedChoiceId.guid);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Idle, tree.actionMode);
        Assert.Empty(tree.currentChoiceIds);
    }

    [Fact]
    public void Select_exception_after_selection_landed_is_evidence_and_commits()
    {
        var (tree, item) = ChoiceTree();
        tree.throwAfterSelect = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Select, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(item.GetGuid(), tree.selectedChoiceId.guid);
    }

    [Fact]
    public void Confirm_commits_exact_discovery_and_returns_to_idle()
    {
        var (tree, item) = ChoiceTree();
        item.required = false;
        tree.selectedChoiceId = new GuidContainer(item.GetGuid());
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Confirm, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.True(item.discovered);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Idle, tree.actionMode);
    }

    [Fact]
    public void Confirm_required_offer_commits_target_discovery()
    {
        var (tree, item) = ChoiceTree();
        item.required = true;
        tree.selectedChoiceId = new GuidContainer(item.GetGuid());
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Confirm, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.True(item.discovered);
    }

    [Fact]
    public void Confirm_accounting_and_cleanup_drift_is_evidence_not_a_gate()
    {
        var (tree, item) = ChoiceTree();
        tree.selectedChoiceId = new GuidContainer(item.GetGuid());
        tree.driftConfirmEvidence = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Confirm, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.True(item.discovered);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Choice, tree.actionMode);
        Assert.NotEmpty(tree.currentChoiceIds);
    }

    [Fact]
    public void Confirm_exception_after_target_discovery_landed_is_evidence_and_commits()
    {
        var (tree, item) = ChoiceTree();
        tree.selectedChoiceId = new GuidContainer(item.GetGuid());
        item.throwAfterDiscover = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Confirm, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.True(item.discovered);
    }

    [Fact]
    public void Reroll_commits_crafting_and_clears_the_stale_selection()
    {
        var (tree, item) = ChoiceTree();
        var second = Item();
        tree.allDiscoverableItems.Add(second);
        tree.currentChoiceIds.Add(new GuidContainer(second.GetGuid()));
        tree.selectedChoiceId = new GuidContainer(item.GetGuid());
        tree.rerollsLeft = 2;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Reroll, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, tree.rerollsLeft);
        Assert.True(tree.usedRerollsLastDiscover);
        Assert.Empty(tree.currentChoiceIds);
        Assert.Equal(new[] { item.GetGuid(), second.GetGuid() },
            tree.nextExcludedIds.Select(value => value.guid));
        Assert.Equal(Guid.Empty, tree.selectedChoiceId.guid);
        Assert.Equal(2, result.CallOutcome.NativeCallsAttempted);
    }

    /// <remarks>
    /// The refusal used to recite the rule it believed it enforced, and two of its three clauses
    /// were false — a planner that cached the sentence learned never to reroll from a mode that
    /// rerolls fine. It now names the one axis that failed and carries the number it read.
    /// </remarks>
    [Fact]
    public void Reroll_without_budget_refuses_on_the_one_axis_that_failed()
    {
        var (tree, _) = ChoiceTree();
        tree.rerollsLeft = 0;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Reroll, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.False(result.Verified);
        Assert.Equal(DiscoveryTreeOfferPreflight.RerollUnavailable, result.Preflight);
        Assert.Equal("No rerolls are left on this tree.", result.Reason);
        Assert.Equal(0, result.RerollsLeft);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void Reroll_accounting_and_cleanup_drift_is_evidence_not_a_gate()
    {
        var (tree, item) = ChoiceTree();
        tree.selectedChoiceId = new GuidContainer(item.GetGuid());
        tree.rerollsLeft = 2;
        tree.driftRerollEvidence = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Reroll, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);
        Assert.Equal(6, tree.rerollsLeft);
        Assert.False(tree.usedRerollsLastDiscover);
        Assert.NotEmpty(tree.currentChoiceIds);
        Assert.Empty(tree.nextExcludedIds);
    }

    [Fact]
    public void Reroll_exception_after_crafting_landed_is_evidence_and_commits()
    {
        var (tree, _) = ChoiceTree();
        tree.rerollsLeft = 1;
        tree.throwAfterReroll = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Reroll, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);
    }

    [Fact]
    public void Unaffordable_initiate_has_zero_native_mutation_calls()
    {
        var resource = Resource(quantity: 10);
        var tree = Tree();
        tree.nextItemCost.costs.Add(new ResourceTuple(resource, new BigDouble(25, 0)));
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.Unaffordable, result.Preflight);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, tree.nextItemCost.PerformCalls);
        Assert.Equal(0, tree.initiateCalls);
    }

    [Fact]
    public void Invisible_tree_has_zero_native_mutation_calls()
    {
        var tree = Tree();
        tree.visible = false;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.TreeUnavailable, result.Preflight);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, tree.nextItemCost.PerformCalls);
        Assert.Equal(0, tree.initiateCalls);
    }

    [Fact]
    public void Wrong_mode_has_zero_native_mutation_calls()
    {
        var tree = Tree();
        var item = Item();
        tree.allDiscoverableItems.Add(item);
        tree.currentChoiceIds.Add(new GuidContainer(item.GetGuid()));
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Select, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.WrongMode, result.Preflight);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, tree.selectCalls);
    }

    [Fact]
    public void No_remaining_discovery_has_zero_native_mutation_calls()
    {
        var tree = Tree();
        typeof(DiscoveryTreeSO).GetField(
                "hasRemainingDiscovery",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(tree, false);
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.NoDiscoveries, result.Preflight);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, tree.nextItemCost.PerformCalls);
        Assert.Equal(0, tree.initiateCalls);
    }

    [Fact]
    public void Missing_or_stale_offer_has_zero_native_mutation_calls()
    {
        var (tree, item) = ChoiceTree();
        tree.currentChoiceIds.Clear();
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Select, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.OfferUnavailable, result.Preflight);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, tree.selectCalls);
    }

    [Fact]
    public void Already_discovered_offer_has_zero_native_mutation_calls()
    {
        var (tree, item) = ChoiceTree();
        item.discovered = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Select, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.AlreadyDiscovered, result.Preflight);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, tree.selectCalls);
    }

    [Fact]
    public void Reroll_without_native_allowance_has_zero_native_mutation_calls()
    {
        var (tree, _) = ChoiceTree();
        tree.rerollsLeft = 0;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Reroll, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.RerollUnavailable, result.Preflight);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, tree.rerollCalls);
    }

    [Fact]
    public void Duplicate_tree_identity_fails_closed_before_mutation()
    {
        var tree = Tree();
        var duplicate = Tree();
        duplicate.SetGuid(tree.GetGuid());
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.IdentityUnavailable, result.Preflight);
        Assert.Equal("That id names more than one live discovery tree.", result.Reason);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void Off_thread_submission_is_rejected_without_touching_Unity_state()
    {
        var tree = Tree();
        using var action = Action();

        var result = ForeignThread.Run(() => action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch)));

        Assert.Equal(DiscoveryTreeOfferPreflight.WrongThread, result.Preflight);
        Assert.Equal(0, tree.initiateCalls);
    }

    [Fact]
    public void Partial_payment_faults_and_the_next_call_revalidates()
    {
        var first = Resource();
        var second = Resource();
        var tree = Tree();
        tree.nextItemCost.costs.Add(new ResourceTuple(first, new BigDouble(10, 0)));
        tree.nextItemCost.costs.Add(new ResourceTuple(second, new BigDouble(20, 0)));
        tree.nextItemCost.ThrowAfterCostRows = 1;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.PostCommitFault, result.Preflight);
        Assert.Equal(0, result.CallOutcome.MutationsCommitted);
        var next = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));
        Assert.Equal(DiscoveryTreeOfferPreflight.PostCommitFault, next.Preflight);
    }

    [Fact]
    public void Confirm_fault_after_native_reset_does_not_claim_the_target_outcome()
    {
        var (tree, item) = ChoiceTree();
        tree.selectedChoiceId = new GuidContainer(item.GetGuid());
        tree.throwAfterConfirmReset = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Confirm, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.PostCommitFault, result.Preflight);
        Assert.False(item.discovered);
    }

    [Fact]
    public void Reroll_clear_fault_after_transition_is_evidence_and_commits()
    {
        var (tree, item) = ChoiceTree();
        tree.selectedChoiceId = new GuidContainer(item.GetGuid());
        tree.rerollsLeft = 1;
        tree.throwAfterSelectionClear = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Reroll, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(DiscoveryTreeOfferNativeStage.Verification, result.Stage);
        Assert.Equal(Guid.Empty, tree.selectedChoiceId.guid);
        Assert.Equal(0, tree.rerollsLeft);
    }

    [Fact]
    public void Native_postcondition_mismatch_faults_without_persistent_blocking()
    {
        var (tree, item) = ChoiceTree();
        tree.suppressSelect = true;
        using var action = Action();

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Select, tree.GetGuid(), item.GetGuid(), Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.VerificationFailed, result.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome);
        Assert.Equal(1, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(0, result.CallOutcome.MutationsCommitted);
    }

    [Fact]
    public void Lifecycle_invalidation_rebinds_and_stale_action_still_refuses()
    {
        var (tree, item) = ChoiceTree();
        tree.throwAfterSelect = true;
        tree.suppressSelect = true;
        var epoch = Epoch;
        using var action = Action(() => epoch);
        var stale = new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Select, tree.GetGuid(), item.GetGuid(), epoch);

        Assert.Equal(DiscoveryTreeOfferPreflight.PostCommitFault, action.Submit(stale).Preflight);
        epoch++;
        tree.throwAfterSelect = false;
        tree.suppressSelect = false;
        action.InvalidateLifecycle();

        Assert.Equal(DiscoveryTreeOfferPreflight.LifecycleReplaced, action.Submit(stale).Preflight);
        var fresh = new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Select, tree.GetGuid(), item.GetGuid(), epoch);
        Assert.True(action.Submit(fresh).Verified);
    }

    [Fact]
    public void Every_missing_lifecycle_contract_disables_the_complete_binding_set()
    {
        var tree = Tree();
        foreach (var missing in DiscoveryTreeOfferNativeBindings.ContractIds)
        {
            using var action = Action(includeContract: id => id != missing);
            Assert.False(action.BindingsAvailable);
            Assert.Contains(missing, action.BindingFailure, StringComparison.Ordinal);
            var result = action.Submit(new DiscoveryTreeOfferAction(
                DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));
            Assert.Equal(DiscoveryTreeOfferPreflight.ContractUnavailable, result.Preflight);
            Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
        }
        Assert.Equal(0, tree.nextItemCost.PerformCalls);
        Assert.Equal(0, tree.initiateCalls);
    }

    [Fact]
    public void Mutation_permit_is_the_last_suite_gate()
    {
        var tree = Tree();
        using var action = Action(permit: false, ownershipFailure: "manual MCP ownership was revoked");

        var result = action.Submit(new DiscoveryTreeOfferAction(
            DiscoveryTreeOfferActionKind.Initiate, tree.GetGuid(), Guid.Empty, Epoch));

        Assert.Equal(DiscoveryTreeOfferPreflight.MutationPermitUnavailable, result.Preflight);
        Assert.Contains("revoked", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, tree.nextItemCost.PerformCalls);
        Assert.Equal(0, tree.initiateCalls);
    }

    private static DiscoveryTreeOfferGameAction Action(
        Func<long>? epoch = null,
        bool permit = true,
        string ownershipFailure = "",
        Func<string, bool>? includeContract = null) =>
        new(epoch ?? (() => Epoch), () => permit, () => ownershipFailure,
            includeContract: includeContract);

    private static DiscoveryTreeSO Tree()
    {
        var tree = new DiscoveryTreeSO
        {
            actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Idle,
            actionTime = BigDouble.Zero,
            maximumRerolls = 2,
        };
        DiscoveryTreeSO.All.Add(tree);
        return tree;
    }

    private static (DiscoveryTreeSO Tree, DiscoveryTestItemSO Item) ChoiceTree()
    {
        var tree = Tree();
        var item = Item();
        tree.actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Choice;
        tree.allDiscoverableItems.Add(item);
        tree.currentChoiceIds.Add(new GuidContainer(item.GetGuid()));
        return (tree, item);
    }

    private static DiscoveryTestItemSO Item() => new DiscoveryTestItemSO();

    private static ResourceSO Resource(int quantity = 1000)
    {
        var resource = new ResourceSO { quantity = new BigDouble(quantity, 0) };
        ResourceSO.All.Add(resource);
        return resource;
    }

    private static ResourceSO Resource(BigDouble quantity)
    {
        var resource = new ResourceSO { quantity = quantity };
        ResourceSO.All.Add(resource);
        return resource;
    }
}
