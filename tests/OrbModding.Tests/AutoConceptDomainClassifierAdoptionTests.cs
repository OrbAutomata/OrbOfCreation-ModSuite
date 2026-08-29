using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AutoConceptDomainClassifierAdoptionTests : IDisposable
{
    public AutoConceptDomainClassifierAdoptionTests()
    {
        IdScriptableObject.RuntimeLookup = new Dictionary<Guid, IdScriptableObject>();
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup = new Dictionary<Guid, IdScriptableObject>();
    }

    [Fact]
    public void ConstructingTheNativeBoundaryDoesNotInitializeOrScanTheSharedClassifier()
    {
        var classifier = new AlchemyGameplayDomainClassifier();
        using var boundary = new AutoConceptNativeAdapter(classifier);

        Assert.Equal(AlchemyDomainClassifierStatus.Uninitialized, classifier.Status);
        Assert.Equal(0, classifier.CachedRecipeCount);
    }

    [Fact]
    public void NativeBoundaryReportsMissingClassifierEvidenceAsRetryable()
    {
        var classifier = new AlchemyGameplayDomainClassifier();
        using var boundary = new AutoConceptNativeAdapter(classifier);

        Assert.False(boundary.TryInitialize(out var reason));

        Assert.Equal(AlchemyDomainClassifierStatus.Retryable, classifier.Status);
        Assert.Contains(
            "Auto Concept domain classifier is not ready:",
            reason,
            StringComparison.Ordinal);
        Assert.Contains(
            AlchemyGameplayDomainClassifier.ConceptRecipesUuid.ToString("D"),
            reason,
            StringComparison.Ordinal);
        Assert.Contains("resolution failed.", reason, StringComparison.Ordinal);
        Assert.Contains("Status=NotFound", reason, StringComparison.Ordinal);

        var generation = classifier.ClassifierGeneration;
        boundary.InvalidateLifecycle();
        Assert.Equal(AlchemyDomainClassifierStatus.Uninitialized, classifier.Status);
        Assert.True(classifier.ClassifierGeneration > generation);
    }

    [Fact]
    public void ContradictorySharedDomainEvidenceBlocksTheNativeBoundary()
    {
        var conceptType = Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid);
        var ordinaryType = Type(AlchemyGameplayDomainClassifier.AlchemyTypeUuid);
        RegisterConceptRecipes(Recipe(Guid.NewGuid(), conceptType, ordinaryType));
        var classifier = new AlchemyGameplayDomainClassifier();
        using var boundary = new AutoConceptNativeAdapter(classifier);

        Assert.False(boundary.TryInitialize(out var reason));

        Assert.Equal(AlchemyDomainClassifierStatus.Blocked, classifier.Status);
        Assert.Contains("Auto Concept domain classifier blocked:", reason, StringComparison.Ordinal);
    }

    private static AlchemyTypeSO Type(Guid uuid)
    {
        var type = new AlchemyTypeSO();
        type.SetGuid(uuid);
        return type;
    }

    private static AlchemyRecipeSO Recipe(Guid uuid, params AlchemyTypeSO[] types)
    {
        var recipe = new AlchemyRecipeSO();
        recipe.SetGuid(uuid);
        recipe.alchemyTypes.AddRange(types);
        return recipe;
    }

    private static void RegisterConceptRecipes(params AlchemyRecipeSO[] recipes)
    {
        var registry = new AlchemyRecipeListVariable();
        registry.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
        registry.value.AddRange(recipes);
        IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = registry;
    }
}
