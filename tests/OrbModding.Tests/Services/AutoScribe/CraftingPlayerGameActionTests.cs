using OrbModding.TestSupport;
using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class CraftingPlayerGameActionTests : IDisposable
{
    private readonly Dictionary<Guid, object> _registry = new();
    private long _lifecycle = 11;
    private bool _permit = true;

    public CraftingPlayerGameActionTests()
    {
        UnityEngine.Resources.Objects.Clear();
        NativeMultiBuyScope.ResetForTests();
        GlobalVariables.MultiBuy = new IntVariable { Value = 25 };
    }

    public void Dispose()
    {
        UnityEngine.Resources.Objects.Clear();
    }

    [Fact]
    public void DirectRecipeRedrivesNativeExecuteComposite()
    {
        var recipe = Register(Recipe());
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(1, recipe.ExecuteCalls);
        Assert.Equal(0, recipe.PurchaseCalls);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.Outcome.CallOutcome);
        Assert.Equal(BigDouble.One, result.Crafted);
        Assert.Equal(25, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void DirectRecipeWithoutCraftPublicationCannotReportVerified()
    {
        var recipe = Register(Recipe());
        recipe.SuppressEffectPublication = true;
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerActionResultCodes.VerificationFailed, result.Outcome.Code);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome.Mutation);
        Assert.Equal(1, recipe.ExecuteCalls);
    }

    [Fact]
    public void TimedRecipeWithoutAuthoredPageRefusesBeforePayment()
    {
        var recipe = Register(Recipe());
        recipe.timeToComplete = 3d;
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerActionResultCodes.PageRelationAmbiguous, result.Outcome.Code);
        Assert.Contains("timed recipe", result.Outcome.Reason, StringComparison.OrdinalIgnoreCase);
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

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(new BigDouble(3, 0), instance.Quantity);
        Assert.Single(page.craftingQueueInstances.value);
        Assert.Equal(1, recipe.PurchaseCalls);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.Outcome.CallOutcome);
    }

    [Fact]
    public void ACountRecipeQueuesTheWholeAskAsOneInstanceWhateverTheScreenMultiplierReads()
    {
        var recipe = Register(Recipe());
        var page = Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe, amount: 1_000_000);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        var instance = Assert.Single(page.craftingQueueInstances.value);
        Assert.Equal(new BigDouble(1_000_000, 0), instance.Quantity);
        Assert.Equal(new BigDouble(1_000_000, 0), result.Crafted);
        Assert.Equal(1, recipe.PurchaseCalls);
        Assert.Equal(25, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void ACountAskAboveWhatTheStockAffordsQueuesTheAffordablePart()
    {
        var recipe = Register(Recipe());
        recipe.MostAffordable = new BigDouble(40, 0);
        var page = Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe, amount: 45);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(new BigDouble(40, 0), result.Crafted);
        Assert.Equal(new BigDouble(40, 0), Assert.Single(page.craftingQueueInstances.value).Quantity);
    }

    [Fact]
    public void ACountRecipeNothingCanBeAffordedOfRefusesAsUnaffordable()
    {
        var recipe = Register(Recipe());
        recipe.MostAffordable = BigDouble.Zero;
        Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe, amount: 3);

        Assert.Equal(CraftingPlayerActionResultCodes.Unaffordable, result.Outcome.Code);
        Assert.Equal("The cost of another of this craft is not met.", result.Outcome.Reason);
        Assert.Equal(0, recipe.PurchaseCalls);
        Assert.Equal(25, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void AScrollNothingCanBeAffordedOfRefusesAsUnaffordable()
    {
        var recipe = Register(Recipe());
        recipe.useQuantityAsLevel = true;
        recipe.MainType.isLevelType = true;
        recipe.MostAffordable = BigDouble.Zero;
        var page = Page(recipe, mode: 0, maximum: 2);
        page.craftingQueueInstances.value.Add(new CraftingInstance(recipe, new BigDouble(4, 0)));
        using var boundary = Boundary();

        var result = Submit(boundary, recipe, amount: 1);

        Assert.Equal(CraftingPlayerActionResultCodes.Unaffordable, result.Outcome.Code);
        Assert.Equal("The cost of another of this craft is not met.", result.Outcome.Reason);
        Assert.Equal(0, recipe.PurchaseCalls);
    }

    [Fact]
    public void MaxQueuesWhatTheMxPressWould()
    {
        var recipe = Register(Recipe());
        recipe.MostAffordable = new BigDouble(3.96, 9);
        var page = Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe, max: true);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(new BigDouble(3.96, 9), result.Crafted);
        Assert.Equal(new BigDouble(3.96, 9), Assert.Single(page.craftingQueueInstances.value).Quantity);
        Assert.Equal(25, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void AScrollIsOneCraftAtTheScreensX1LevelWhateverTheScreenMultiplierReads()
    {
        var recipe = Register(Recipe());
        recipe.useQuantityAsLevel = true;
        recipe.MainType.isLevelType = true;
        recipe.StartingQuantity = new BigDouble(4, 0);
        var page = Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe, amount: 3);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(BigDouble.One, result.Crafted);
        Assert.Equal(new BigDouble(4, 0), Assert.Single(page.craftingQueueInstances.value).Quantity);
        Assert.Equal(25, GlobalVariables.MultiBuy.Value);
    }

    [Fact]
    public void NewInstanceModeInitiatesAndAdmitsExactRecipe()
    {
        var recipe = Register(Recipe());
        var page = Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        var instance = Assert.Single(page.craftingQueueInstances.value);
        Assert.Same(recipe, instance.reference);
        Assert.True(instance.Initiated);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.Outcome.CallOutcome);
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

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Empty(page.craftingQueueInstances.value);
        Assert.Equal(1, Assert.Single(recipe.InstantOutput.consumableCounts).Quantity);
        Assert.Equal(new NativeMutationCallOutcome(1, 1, 1), result.Outcome.CallOutcome);
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

        Assert.Equal(CraftingPlayerActionResultCodes.VerificationFailed, result.Outcome.Code);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.Outcome.Mutation);
    }

    [Fact]
    public void DuplicateAuthoredPageRelationRefusesBeforePayment()
    {
        var recipe = Register(Recipe());
        Page(recipe, mode: 0, maximum: 2);
        Page(recipe, mode: 1, maximum: 2);
        using var boundary = Boundary();

        var result = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerActionResultCodes.PageRelationAmbiguous, result.Outcome.Code);
        Assert.Contains("more than one", result.Outcome.Reason);
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

        Assert.Equal(CraftingPlayerActionResultCodes.QueueFull, full.Outcome.Code);
        Assert.Equal(CraftingPlayerActionResultCodes.Unaffordable, unaffordable.Outcome.Code);
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

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(new BigDouble(2, 0), instance.Quantity);
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

        Assert.Equal(CraftingPlayerActionResultCodes.VerificationFailed, failed.Outcome.Code);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, failed.Outcome.Mutation);
        Assert.Equal(
            "One-shot crafting Verification failed on " + EntityIdentityFormatter.PlayerName(recipe.GetGuid()) +
            ": " + GameActionAnswer.ChangeNotSeen("Scholar > Scribe"),
            failed.Outcome.Reason);
        Assert.Equal(CraftingPlayerActionResultCodes.VerificationFailed, blocked.Outcome.Code);
        Assert.True(retried.Outcome.Verified, retried.Outcome.Reason);
    }

    [Fact]
    public void LifecyclePermitAndUnityThreadAreRevalidatedBeforeMutation()
    {
        var recipe = Register(Recipe());
        using var boundary = Boundary();

        _lifecycle = 12;
        var stale = Submit(boundary, recipe, lifecycle: 11);
        _lifecycle = 11;
        _permit = false;
        var noPermit = Submit(boundary, recipe);
        _permit = true;
        var wrongThread = ForeignThread.Run(() => Submit(boundary, recipe));

        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, stale.Outcome.Code);
        Assert.Equal(CraftingPlayerActionResultCodes.MutationPermitUnavailable, noPermit.Outcome.Code);
        Assert.Equal(CraftingPlayerActionResultCodes.WrongThread, wrongThread.Outcome.Code);
        Assert.Equal(0, recipe.PurchaseCalls);
    }

    [Fact]
    public void AnUnreadableLifecycleEpochFaultsBeforeTheNativeCall()
    {
        var recipe = Register(Recipe());
        using var boundary = Boundary(
            readLifecycleEpoch: static () => throw new InvalidOperationException("epoch unreadable"));

        var result = Submit(boundary, recipe);

        Assert.True(result.Outcome.IsFault, result.Outcome.Reason);
        Assert.Equal(CraftingPlayerActionResultCodes.ContractUnavailable, result.Outcome.Code);
        Assert.Equal(0, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(0, recipe.ExecuteCalls);
    }

    [Fact]
    public void ARecipeResolvedFromAReplacedRegistryIsRefusedBeforeAnyPress()
    {
        var recipe = Register(Recipe());
        const int readsWhileResolving = 3;
        var reads = 0;
        using var boundary = Boundary(readGeneration: () => ++reads <= readsWhileResolving ? 11 : 12);

        var result = Submit(boundary, recipe);

        Assert.Equal(CraftingPlayerActionResultCodes.RecipeUnavailable, result.Outcome.Code);
        Assert.Equal(EntityIdentityFormatter.PlayerName(recipe.GetGuid()) + " became stale.", result.Outcome.Reason);
        Assert.Equal(0, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(0, recipe.ExecuteCalls);
    }

    private AutoScribeOneShotCraftPlayerGameAction Boundary(
        Func<long>? readLifecycleEpoch = null,
        Func<long>? readGeneration = null)
    {
        IDictionary registry = _registry;
        var resolver = TestRegistryResolver.For(readGeneration ?? (() => _lifecycle), registry);
        return new AutoScribeOneShotCraftPlayerGameAction(
            resolver,
            readLifecycleEpoch ?? (() => _lifecycle),
            () => _permit,
            static () => "CraftingQueueSubmission is not owned.");
    }

    private CraftingPlayerSubmission Submit(
        AutoScribeOneShotCraftPlayerGameAction boundary,
        CraftingRecipeSO recipe,
        long? lifecycle = null,
        int amount = 1,
        bool max = false)
    {
        var action = new CraftingPlayerAction(
            recipe.GetGuid(), lifecycle ?? _lifecycle, amount, max);
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
