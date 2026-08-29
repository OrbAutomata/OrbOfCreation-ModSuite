using System;
using System.Linq;
using System.Reflection;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Runtime.ServiceCycle;

public sealed class AutoConceptNativeAdapterTests : IDisposable
{
    private static readonly Guid RecipeId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReplacementId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public AutoConceptNativeAdapterTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
    }

    [Theory]
    [Trait("Category", "HeadlessIntegration")]
    [InlineData("ordinary-alchemy")]
    [InlineData("wrong-scholar-type")]
    public void RejectsRecipeListsOutsideTheAuditedConceptDomain(string typeUuid)
    {
        var recipe = new AlchemyRecipeSO("invalid", "Invalid", new[] { new AlchemyTypeSO(typeUuid) });
        InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());

        var initialized = runtime.TryInitialize(out var reason);

        Assert.False(initialized);
        Assert.Contains("without verified Scholar type evidence", reason);
        Assert.False(runtime.IsReady);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void BatchedDepthRefusesWhenTheLiveMasteryCapCannotHonorTheRequest()
    {
        var resource = new ConceptResource();
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Valid concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(2.0, 0)),
            drainCost = new ConceptCostVector(
                new ConceptCostEntry(resource, new BigDouble(10.0, 0))),
        };
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 2, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 20, Guid.Empty, 1, in belief);

        var submission = runtime.Submit(in action, new AutoConceptConfiguration());

        Assert.Equal(AutoConceptPreflight.ResourceBackpressure, submission.Preflight);
        Assert.Contains("at most 2 more", submission.Reason, StringComparison.Ordinal);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.MutationsCommitted);
        Assert.Empty(active.value);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void NoOpAssignmentBlocksUntilLifecycleRecovery()
    {
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "No-op concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(2.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        active.SuppressAddMutation = true;
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 2, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, in belief);
        var config = new AutoConceptConfiguration();

        var failed = runtime.Submit(in action, in config);

        Assert.False(failed.Verified);
        Assert.Contains("PostconditionFailed", failed.Reason);
        Assert.Equal(1, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.MutationsCommitted);
        Assert.NotNull(runtime.BlockedReason);
        var blocked = runtime.Submit(in action, in config);
        Assert.Equal(AutoConceptPreflight.ContractUnavailable, blocked.Preflight);
        Assert.Contains("blocked until the next lifecycle", blocked.Reason);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.NativeCallsAttempted);

        active.SuppressAddMutation = false;
        runtime.InvalidateLifecycle();
        var recovered = runtime.Submit(in action, in config);

        Assert.True(recovered.Verified, recovered.Reason);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void PositiveDrainFailsClosedWhenNativeResourceIsAtZero()
    {
        var resource = new ConceptResource { AtZero = true };
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Zero resource concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(4.0, 0)),
            drainCost = new ConceptCostVector(
                new ConceptCostEntry(resource, new BigDouble(1.0, 0))),
        };
        InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 4, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 4, Guid.Empty, 1, in belief);

        var submission = runtime.Submit(in action, new AutoConceptConfiguration());

        Assert.Equal(AutoConceptPreflight.ResourceBackpressure, submission.Preflight);
        Assert.Contains("at zero", submission.Reason);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void SubmitRevalidatesIdentityQuantityProjectionAndVerifierAtTheBoundary()
    {
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Verified concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(2.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 2, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, in belief);
        var config = new AutoConceptConfiguration();

        var submission = runtime.Submit(in action, in config);

        Assert.True(submission.Verified, submission.Reason);
        Assert.Equal(1, submission.AppliedDelta);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void SubmitRefusesWhenTheLiveQuantityNoLongerMatchesThePlanBelief()
    {
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Changed concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(2.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        active.value.Add(new AlchemyInstance(recipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 2, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 2, Guid.Empty, 1, in belief);
        var config = new AutoConceptConfiguration();

        var submission = runtime.Submit(in action, in config);

        Assert.Equal(AutoConceptPreflight.OwnershipChanged, submission.Preflight);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void RotationCanUseAReleasedTypelessSlotAcrossConceptTypes()
    {
        var activeType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var replacementType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString());
        var activeRecipe = RecipeWithType(RecipeId, "Active", activeType);
        var replacementRecipe = RecipeWithType(ReplacementId, "Replacement", replacementType);
        var active = InstallNativeLists(activeRecipe, replacementRecipe);
        active.TypelessSlots = 1;
        active.value.Add(new AlchemyInstance(activeRecipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(1, 1, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.RotateOut,
            RecipeId,
            1,
            ReplacementId,
            1,
            in belief);

        var submission = runtime.Submit(in action, new AutoConceptConfiguration());

        Assert.True(submission.Verified, submission.Reason);
        Assert.Equal(-1, submission.AppliedDelta);
        Assert.Empty(active.value);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void RotationDoesNotTradeATypeSpecificSlotForAnotherType()
    {
        var activeType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var replacementType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString());
        var activeRecipe = RecipeWithType(RecipeId, "Active", activeType);
        var replacementRecipe = RecipeWithType(ReplacementId, "Replacement", replacementType);
        var active = InstallNativeLists(activeRecipe, replacementRecipe);
        active.TypelessSlots = 0;
        active.TypeSlots[activeType] = 1;
        active.value.Add(new AlchemyInstance(activeRecipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(1, 1, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.RotateOut,
            RecipeId,
            1,
            ReplacementId,
            1,
            in belief);

        var submission = runtime.Submit(in action, new AutoConceptConfiguration());

        Assert.Equal(AutoConceptPreflight.SlotUnavailable, submission.Preflight);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void RotationDoesNotDegradeWhenTheNativeAdmissionReadThrows()
    {
        var activeType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var replacementType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString());
        var activeRecipe = RecipeWithType(RecipeId, "Active", activeType);
        var replacementRecipe = RecipeWithType(ReplacementId, "Replacement", replacementType);
        var active = InstallNativeLists(activeRecipe, replacementRecipe);
        active.TypelessSlots = 1;
        active.ThrowOnCanAdd = true;
        active.value.Add(new AlchemyInstance(activeRecipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(1, 1, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.RotateOut,
            RecipeId,
            1,
            ReplacementId,
            1,
            in belief);

        var exception = Assert.Throws<TargetInvocationException>(
            () => runtime.Submit(in action, new AutoConceptConfiguration()));

        Assert.Contains("CanAddInstance failed", exception.GetBaseException().Message);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void RotationRevalidatesThatTheReplacementIsStillUnlocked()
    {
        var type = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var activeRecipe = RecipeWithType(RecipeId, "Active", type);
        var replacementRecipe = RecipeWithType(ReplacementId, "Locked", type);
        replacementRecipe.discovered = false;
        var active = InstallNativeLists(activeRecipe, replacementRecipe);
        active.TypelessSlots = 1;
        active.value.Add(new AlchemyInstance(activeRecipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(1, 1, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.RotateOut,
            RecipeId,
            1,
            ReplacementId,
            1,
            in belief);

        var submission = runtime.Submit(in action, new AutoConceptConfiguration());

        Assert.Equal(AutoConceptPreflight.SlotUnavailable, submission.Preflight);
        Assert.Contains("unlock", submission.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    private static AlchemyRecipeSO RecipeWithType(
        Guid id,
        string name,
        AlchemyTypeSO type) =>
        new(id.ToString("D"), name, new[] { type })
        {
            coreType = type,
            maxUsageSlots = new ValueModifierRecord(new BigDouble(1.0, 0)),
        };

    private static AlchemyInstanceListVariable InstallNativeLists(params AlchemyRecipeSO[] recipes)
    {
        var active = new AlchemyInstanceListVariable();
        active.SetGuid(new Guid(AutoConceptNativeAdapter.ActiveConceptsUuid));
        var recipeList = new AlchemyRecipeListVariable { value = recipes.ToList() };
        recipeList.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
        IdScriptableObject.RuntimeLookup[new Guid(AutoConceptNativeAdapter.ActiveConceptsUuid)] = active;
        IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = recipeList;
        return active;
    }
}
