using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.CraftingLifecycle;

public sealed class CraftingInstanceLifecycleGameActionTests : IDisposable
{
    private const long Epoch = 131;
    private readonly IDictionary _registry = new Hashtable();

    public CraftingInstanceLifecycleGameActionTests()
    {
        UnityEngine.Resources.Objects.Clear();
        GlobalVariables.MultiBuy = new IntVariable { Value = 3 };
    }

    public void Dispose() => UnityEngine.Resources.Objects.Clear();

    [Fact]
    public void Automate_adds_one_repetition_whatever_the_screen_multiplier_reads()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(2)).SetAuto(true);
        existing.SetAutomationQuantity(2);
        page.craftingAutomationInstances.value.Add(existing);
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Same(existing, page.craftingAutomationInstances.GetInstance(recipe));
        Assert.Equal(3, existing.GetAutomationQuantity());
        Assert.Equal(3, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void Automated_cancel_removes_one_repetition_and_observes_the_game_written_decrease()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(5)).SetAuto(true);
        existing.SetAutomationQuantity(5);
        page.craftingAutomationInstances.value.Add(existing);
        using var boundary = Boundary(recipe);

        var result = Submit(
            boundary, recipe, CraftingInstanceLifecycleActionKind.CancelAutomation);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(4, existing.GetAutomationQuantity());
        Assert.Single(page.craftingAutomationInstances.value);
    }

    [Fact]
    public void Automated_cancel_hands_the_adding_control_a_negative_amount()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(9)).SetAuto(true);
        existing.SetAutomationQuantity(9);
        page.craftingAutomationInstances.value.Add(existing);
        using var boundary = Boundary(recipe);

        var first = Submit(
            boundary, recipe, CraftingInstanceLifecycleActionKind.CancelAutomation);
        var second = Submit(
            boundary, recipe, CraftingInstanceLifecycleActionKind.CancelAutomation);

        Assert.True(first.Outcome.Verified, first.Outcome.Reason);
        Assert.True(second.Outcome.Verified, second.Outcome.Reason);
        Assert.Equal(7, existing.GetAutomationQuantity());
    }

    [Fact]
    public void Automated_cancel_that_empties_the_entry_observes_its_removal()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(1)).SetAuto(true);
        existing.SetAutomationQuantity(1);
        page.craftingAutomationInstances.value.Add(existing);
        using var boundary = Boundary(recipe);

        var result = Submit(
            boundary, recipe, CraftingInstanceLifecycleActionKind.CancelAutomation);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.True(existing.Removed);
        Assert.Empty(page.craftingAutomationInstances.value);
    }

    [Fact]
    public void A_failed_transition_reports_the_automation_quantity_it_already_moved()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(2)).SetAuto(false);
        existing.SetAutomationQuantity(2);
        page.craftingAutomationInstances.value.Add(existing);
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate);

        Assert.Equal(CraftingInstanceLifecycleActionResultCodes.VerificationFailed, result.Outcome.Code);
        Assert.True(result.SideEffect.Observed);
        Assert.Equal(2, result.SideEffect.AutomationBefore);
        Assert.Equal(3, result.SideEffect.AutomationAfter);
    }

    [Fact]
    public void A_failed_transition_that_wrote_nothing_reports_no_side_effect()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(2)).SetAuto(true);
        existing.SetAutomationQuantity(2);
        page.craftingAutomationInstances.value.Add(existing);
        page.craftingAutomationInstances.SuppressAutomation = true;
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate);

        Assert.Equal(CraftingInstanceLifecycleActionResultCodes.VerificationFailed, result.Outcome.Code);
        Assert.False(result.SideEffect.Observed);
    }

    [Fact]
    public void Manual_cancel_runs_cancel_then_removes_the_exact_instance()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(5));
        page.craftingQueueInstances.value.Add(existing);
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.CancelManual);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.True(existing.Removed);
        Assert.Empty(page.craftingQueueInstances.value);
    }

    [Fact]
    public void Native_automation_no_op_fails_the_directional_sentinel()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(2)).SetAuto(true);
        existing.SetAutomationQuantity(2);
        page.craftingAutomationInstances.value.Add(existing);
        page.craftingAutomationInstances.SuppressAutomation = true;
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate);

        Assert.Equal(CraftingInstanceLifecycleActionResultCodes.VerificationFailed, result.Outcome.Code);
        Assert.Equal(2, existing.GetAutomationQuantity());
    }

    [Fact]
    public void Unity_thread_is_refused_before_native_state()
    {
        var (recipe, _) = Surface();
        using var boundary = Boundary(recipe);

        var result = ForeignThread.Run(() =>
            Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate));

        Assert.Equal(CraftingInstanceLifecycleActionResultCodes.WrongThread, result.Outcome.Code);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_instance_family()
    {
        foreach (var missing in CraftingInstanceLifecycleNativeBindings.ContractIds)
        {
            using var boundary = Boundary(null, id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The game wrote a new automation entry and then threw. The entry is there to read, so the
    /// press committed and the exception only explains it.
    /// </summary>
    [Fact]
    public void Automate_that_throws_after_writing_a_new_entry_is_seen_as_committed()
    {
        var (recipe, page) = Surface();
        page.craftingAutomationInstances.ThrowAfterAutomation = true;
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(1, page.craftingAutomationInstances.GetInstance(recipe)!.AutomationQuantity);
    }

    /// <summary>
    /// The game wrote the repetition, threw, and then would not read back. The press may have
    /// landed, so it is a fault with the attempt recorded, never a refusal a caller would retry.
    /// </summary>
    [Fact]
    public void A_read_back_that_throws_after_the_write_is_a_fault_not_a_refusal()
    {
        var (recipe, page) = Surface();
        var existing = new CraftingInstance(recipe, new BigDouble(2)).SetAuto(true);
        existing.SetAutomationQuantity(2);
        page.craftingAutomationInstances.value.Add(existing);
        page.craftingAutomationInstances.BreakAfterMutation = true;
        using var boundary = Boundary(recipe);

        var result = Submit(boundary, recipe, CraftingInstanceLifecycleActionKind.Automate);

        Assert.Equal(CraftingInstanceLifecycleActionResultCodes.PostCommitFault, result.Outcome.Code);
        Assert.Equal(1, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(ServiceActionDisposition.Faulted,
            result.Outcome.ToResult().Disposition);
    }

    [Fact]
    public void Unity_framework_types_do_not_depend_on_the_game_type_resolver()
    {
        using var boundary = new CraftingInstanceLifecycleGameAction(
            () => Epoch,
            static () => true,
            static () => "Crafting ownership was revoked.",
            resolveType: name => name.StartsWith("UnityEngine.", StringComparison.Ordinal)
                ? null
                : ReflectionUtil.FindLoadedType(name));

        Assert.True(boundary.BindingsAvailable, boundary.BindingFailure);
    }

    private CraftingInstanceLifecycleGameAction Boundary(
        CraftingRecipeSO? recipe,
        Func<string, bool>? includeContract = null)
    {
        if (recipe is not null) _registry.Add(recipe.GetGuid(), recipe);
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new CraftingInstanceLifecycleGameAction(
            () => Epoch,
            static () => true,
            static () => "Crafting ownership was revoked.",
            includeContract: includeContract,
            registry: resolver);
    }

    private static CraftingInstanceLifecycleSubmission Submit(
        CraftingInstanceLifecycleGameAction boundary,
        CraftingRecipeSO recipe,
        CraftingInstanceLifecycleActionKind kind)
    {
        var action = new CraftingInstanceLifecycleAction(kind, recipe.GetGuid(), Epoch);
        return boundary.Submit(in action);
    }

    private static (CraftingRecipeSO Recipe, UICraftingPage Page) Surface()
    {
        var recipe = new CraftingRecipeSO
        {
            uuid = Guid.NewGuid(),
            visible = true,
            MainType = new CraftingRecipeTypeSO(),
        };
        var page = new UICraftingPage
        {
            mainCraftType = recipe.MainType,
            craftingQueueInstances = new CraftingInstanceListVariable
            {
                uuid = Guid.NewGuid(),
                Maximum = 4,
            },
            craftingAutomationInstances = new CraftingInstanceListVariable
            {
                uuid = Guid.NewGuid(),
                Maximum = 4,
                isAutoList = true,
            },
        };
        page.availableRecipes.value.Add(recipe);
        UnityEngine.Resources.Objects.Add(page);
        return (recipe, page);
    }
}
