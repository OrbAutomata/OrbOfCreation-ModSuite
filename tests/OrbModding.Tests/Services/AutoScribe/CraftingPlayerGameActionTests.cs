using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class CraftingPlayerGameActionTests : IDisposable
{
    private readonly Dictionary<Guid, object> _registry = new();
    private long _lifecycle = 11;
    private bool _permit = true;

    public CraftingPlayerGameActionTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        UnityEngine.Resources.Objects.Clear();
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        UnityEngine.Resources.Objects.Clear();
    }

    [Fact]
    public void DirectRecipeRedrivesNativeExecuteComposite()
    {
        var recipe = Register(Recipe());
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, recipe.ExecuteCalls);
        Assert.Equal(0, recipe.PurchaseCalls);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void DirectRecipeWithoutCraftPublicationCannotReportVerified()
    {
        var recipe = Register(Recipe());
        recipe.SuppressEffectPublication = true;
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerPreflight.VerificationFailed, result.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome);
        Assert.Equal(1, recipe.ExecuteCalls);
    }

    [Fact]
    public void TimedRecipeWithoutAuthoredPageRefusesBeforePayment()
    {
        var recipe = Register(Recipe());
        recipe.timeToComplete = 3d;
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerPreflight.PageRelationAmbiguous, result.Preflight);
        Assert.Contains("timed recipe", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, recipe.ExecuteCalls);
        Assert.Equal(0, recipe.PurchaseCalls);
    }

    [Fact]
    public void StackModeAddsExactRecipeQuantityEvenWhenQueueHasNoFreeSlot()
    {
        var recipe = Register(Recipe());
        var page = Page(recipe, mode: 0, maximum: 1);
        var instance = new CraftingInstance(recipe, new BigDouble(2, 0));
        page.craftingQueueInstances.value.Add(instance);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new BigDouble(3, 0), instance.Quantity);
        Assert.Single(page.craftingQueueInstances.value);
        Assert.Equal(1, recipe.PurchaseCalls);
        Assert.Equal(new NativeMutationCallOutcome(2, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void NewInstanceModeInitiatesAndAdmitsExactRecipe()
    {
        var recipe = Register(Recipe());
        var page = Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.True(result.Verified, result.Reason);
        var instance = Assert.Single(page.craftingQueueInstances.value);
        Assert.Same(recipe, instance.reference);
        Assert.True(instance.Initiated);
        Assert.Equal(new NativeMutationCallOutcome(5, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void InstantPageRecipeCompletesWithoutQueueAdmission()
    {
        var recipe = Register(Recipe());
        recipe.InstantCraftEnabled = true;
        recipe.InstantOutput = new ConsumableSO();
        var page = Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.True(result.Verified, result.Reason);
        Assert.Empty(page.craftingQueueInstances.value);
        Assert.Equal(1, Assert.Single(recipe.InstantOutput.consumableCounts).Quantity);
        Assert.Equal(new NativeMutationCallOutcome(4, 1, 1), result.CallOutcome);
    }

    [Fact]
    public void InstantPageRecipeWithoutNativeCompletionCannotReportVerified()
    {
        var recipe = Register(Recipe());
        recipe.InstantCraftEnabled = true;
        recipe.InstantOutput = new ConsumableSO();
        recipe.SuppressInstantAdmission = true;
        Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerPreflight.VerificationFailed, result.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome);
    }

    [Fact]
    public void DuplicateAuthoredPageRelationRefusesBeforePayment()
    {
        var recipe = Register(Recipe());
        Page(recipe, mode: 0, maximum: 2);
        Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerPreflight.PageRelationAmbiguous, result.Preflight);
        Assert.Contains("more than one", result.Reason);
        Assert.Equal(0, recipe.PurchaseCalls);
    }

    [Fact]
    public void NewInstanceQueueFullAndUnaffordableBothRefuseBeforePayment()
    {
        var recipe = Register(Recipe());
        var page = Page(recipe, mode: 1, maximum: 0);
        using var boundary = Boundary();

        var full = Submit(boundary, recipe);
        recipe.BuyAllowed = false;
        page.craftingQueueInstances.Maximum = 2;
        var unaffordable = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerPreflight.QueueFull, full.Preflight);
        Assert.Equal(CraftingPlayerPreflight.Unaffordable, unaffordable.Preflight);
        Assert.Equal(0, recipe.PurchaseCalls);
    }

    [Fact]
    public void ObservedStackOutcomeAfterNativeThrowStillCommits()
    {
        var recipe = Register(Recipe());
        var page = Page(recipe, mode: 0, maximum: 1);
        var instance = new CraftingInstance(recipe, BigDouble.One)
        {
            ThrowAfterAddQuantity = true,
        };
        page.craftingQueueInstances.value.Add(instance);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(new BigDouble(2, 0), instance.Quantity);
        Assert.False(boundary.IsQuarantined);
    }

    [Fact]
    public void MissingQueuedOutcomeFaultsWithoutPersistentPlayerState()
    {
        var recipe = Register(Recipe());
        var page = Page(recipe, mode: 0, maximum: 1);
        var instance = new CraftingInstance(recipe, BigDouble.One)
        {
            SuppressAddQuantity = true,
        };
        page.craftingQueueInstances.value.Add(instance);
        using var boundary = Boundary();

        var failed = Submit(boundary, recipe);
        var blocked = Submit(boundary, recipe);
        instance.SuppressAddQuantity = false;
        boundary.InvalidateLifecycle();
        var retried = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, failed.Outcome);
        Assert.Equal(CraftingPlayerPreflight.VerificationFailed, blocked.Preflight);
        Assert.True(retried.Verified, retried.Reason);
    }

    [Fact]
    public void LifecyclePermitAndUnityThreadAreRevalidatedBeforeMutation()
    {
        var recipe = Register(Recipe());
        using var boundary = Boundary();

        _lifecycle = 12;
        var stale = Submit(boundary, recipe, lifecycle: 11);
        _lifecycle = 0;
        var invalid = Submit(boundary, recipe, lifecycle: 0);
        _lifecycle = 11;
        _permit = false;
        var noPermit = Submit(boundary, recipe);
        _permit = true;
        var wrongThread = ForeignThread.Run(() => Submit(boundary, recipe));

        Assert.Equal(CraftingPlayerPreflight.LifecycleReplaced, stale.Preflight);
        Assert.Equal(CraftingPlayerPreflight.LifecycleReplaced, invalid.Preflight);
        Assert.Equal(CraftingPlayerPreflight.MutationPermitUnavailable, noPermit.Preflight);
        Assert.Equal(CraftingPlayerPreflight.WrongThread, wrongThread.Preflight);
        Assert.Equal(0, recipe.PurchaseCalls);
    }

    private AutoScribeOneShotCraftGameAction Boundary()
    {
        IDictionary registry = _registry;
        var resolver = new TypedRegistryResolver(
            () => _lifecycle,
            () => TypedRegistrySourceSnapshot.Ready(registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new AutoScribeOneShotCraftGameAction(
            resolver,
            AutoScribeIdentityCatalog.Audited,
            () => _lifecycle,
            () => _permit,
            static () => "CraftingQueueSubmission is not owned.");
    }

    private CraftingPlayerSubmission Submit(
        AutoScribeOneShotCraftGameAction boundary,
        CraftingRecipeSO recipe,
        long? lifecycle = null)
    {
        var action = new CraftingPlayerAction(recipe.GetGuid(), lifecycle ?? _lifecycle);
        return boundary.Submit(in action);
    }

    private CraftingRecipeSO Register(CraftingRecipeSO recipe)
    {
        recipe.SetGuid(Guid.NewGuid());
        _registry.Add(recipe.GetGuid(), recipe);
        IdScriptableObject.RuntimeLookup.Add(recipe.GetGuid(), recipe);
        return recipe;
    }

    private static CraftingRecipeSO Recipe() =>
        new()
        {
            visible = true,
            BuyAllowed = true,
            TotalCost = new ResourceCostList { affordable = true },
            MainType = new CraftingRecipeTypeSO { isLevelType = false },
        };

    private static UICraftingPage Page(CraftingRecipeSO recipe, int mode, int maximum)
    {
        var page = new UICraftingPage
        {
            mainCraftType = recipe.MainType,
            craftMode = new IntVariable { Value = mode },
            craftingQueueInstances = new CraftingInstanceListVariable { Maximum = maximum },
        };
        page.availableRecipes.value.Add(recipe);
        UnityEngine.Resources.Objects.Add(page);
        return page;
    }
}
