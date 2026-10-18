using System;
using System.Collections.Generic;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class AlchemyGameplayDomainClassifierTests : IDisposable
{
    public AlchemyGameplayDomainClassifierTests()
    {
        IdScriptableObject.RuntimeLookup = new Dictionary<Guid, IdScriptableObject>();
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup = new Dictionary<Guid, IdScriptableObject>();
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_alchemy_classifier_binding_set()
    {
        foreach (var missing in AlchemyGameplayDomainClassifierBindings.ContractIds)
        {
            IdScriptableObject.RuntimeLookup = new Dictionary<Guid, IdScriptableObject>();
            var concept = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid));
            RegisterConceptRecipes(concept);
            using var classifier = new AlchemyGameplayDomainClassifier(
                registryResolver: null, resolveType: null, includeContract: id => id != missing);

            Assert.False(classifier.TryInitialize(out var reason));
            Assert.Equal(AlchemyDomainClassifierStatus.Blocked, classifier.Status);
            Assert.Contains(missing, reason, StringComparison.Ordinal);
            Assert.Equal(AlchemyGameplayDomain.Unknown, classifier.ClassifyRecipe(concept).Domain);
        }
    }

    [Fact]
    public void ExactConceptRegistryAndTypeEvidenceClassifiesScholarRecipe()
    {
        var conceptType = Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid);
        var concept = Recipe(Guid.NewGuid(), conceptType);
        RegisterConceptRecipes(concept);
        using var classifier = new AlchemyGameplayDomainClassifier();

        Assert.True(classifier.TryInitialize(out var reason), reason);
        var result = classifier.ClassifyRecipe(concept);

        Assert.Equal(AlchemyGameplayDomain.ScholarConcept, result.Domain);
        Assert.Equal(concept.GetGuid(), result.RecipeUuid);
        Assert.Contains(conceptType.GetGuid(), result.AlchemyTypeUuids);
        Assert.True(result.Evidence.HasFlag(AlchemyDomainEvidence.ConceptRegistryMember));
        Assert.True(result.Evidence.HasFlag(AlchemyDomainEvidence.KnownScholarConceptType));
        Assert.Equal(EvidenceLevel.SerializedAssetVerified, result.Assessment.Level);
        Assert.Equal(
            EvidenceSource.StaticContract |
            EvidenceSource.SerializedAsset |
            EvidenceSource.RuntimeNativeType |
            EvidenceSource.StableIdentity |
            EvidenceSource.RuntimeRegistry |
            EvidenceSource.NativeRelationship,
            result.Assessment.Sources);
        Assert.True(result.IsMutationGrade);
    }

    [Fact]
    public void ExactOrdinaryTypeOutsideConceptRegistryClassifiesOrdinaryAlchemyRecipe()
    {
        var concept = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid));
        var ordinary = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.BrewingTypeUuid));
        RegisterConceptRecipes(concept);
        using var classifier = new AlchemyGameplayDomainClassifier();

        Assert.True(classifier.TryInitialize(out var reason), reason);
        var result = classifier.ClassifyRecipe(ordinary);

        Assert.Equal(AlchemyGameplayDomain.OrdinaryAlchemy, result.Domain);
        Assert.False(result.Evidence.HasFlag(AlchemyDomainEvidence.ConceptRegistryMember));
        Assert.True(result.Evidence.HasFlag(AlchemyDomainEvidence.KnownOrdinaryAlchemyType));
        Assert.True(result.IsMutationGrade);
    }

    [Fact]
    public void ScholarTypeWithoutConceptRegistryMembershipFailsClosed()
    {
        RegisterConceptRecipes(Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ConceptualizationTypeUuid)));
        var inconsistent = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid));
        using var classifier = new AlchemyGameplayDomainClassifier();

        Assert.True(classifier.TryInitialize(out var reason), reason);
        var result = classifier.ClassifyRecipe(inconsistent);

        Assert.Equal(AlchemyGameplayDomain.Unknown, result.Domain);
        Assert.Contains("absent from the ConceptRecipes", result.Reason, StringComparison.Ordinal);
        Assert.True(result.Assessment.IsContradictory);
        Assert.False(result.IsMutationGrade);
    }

    [Fact]
    public void ConceptRegistryEntryWithoutScholarTypeBlocksSnapshot()
    {
        RegisterConceptRecipes(Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.TransmutationTypeUuid)));
        using var classifier = new AlchemyGameplayDomainClassifier();

        Assert.False(classifier.TryInitialize(out var reason));
        Assert.Equal(AlchemyDomainClassifierStatus.Blocked, classifier.Status);
        Assert.Contains("without verified Scholar type evidence", reason, StringComparison.Ordinal);
        Assert.Equal(AlchemyGameplayDomain.Unknown, classifier.ClassifyRecipe(null).Domain);
    }

    [Fact]
    public void MixedOrdinaryAndScholarTypeEvidenceBlocksConceptSnapshot()
    {
        RegisterConceptRecipes(Recipe(
            Guid.NewGuid(),
            Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid),
            Type(AlchemyGameplayDomainClassifier.AlchemyTypeUuid)));
        using var classifier = new AlchemyGameplayDomainClassifier();

        Assert.False(classifier.TryInitialize(out var reason));
        Assert.Equal(AlchemyDomainClassifierStatus.Blocked, classifier.Status);
        Assert.Contains("without verified Scholar type evidence", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingOrEmptyRegistryIsRetryableAndNeverInfersOrdinaryByExclusion()
    {
        using var classifier = new AlchemyGameplayDomainClassifier();

        Assert.False(classifier.TryInitialize(out _));
        Assert.Equal(AlchemyDomainClassifierStatus.Retryable, classifier.Status);

        RegisterConceptRecipes();
        Assert.False(classifier.TryInitialize(out _));
        var ordinary = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.AlchemyTypeUuid));
        Assert.Equal(AlchemyGameplayDomain.Unknown, classifier.ClassifyRecipe(ordinary).Domain);
    }

    [Fact]
    public void StableUuidAndExactNativeTypeClassifyAllAuditedTypeFamilies()
    {
        RegisterConceptRecipes(Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid)));
        using var classifier = new AlchemyGameplayDomainClassifier();
        Assert.True(classifier.TryInitialize(out var reason), reason);

        Assert.Equal(
            AlchemyGameplayDomain.ScholarConcept,
            classifier.ClassifyType(Type(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid)).Domain);
        Assert.Equal(
            AlchemyGameplayDomain.OrdinaryAlchemy,
            classifier.ClassifyType(Type(AlchemyGameplayDomainClassifier.RefinementTypeUuid)).Domain);

        var unknownType = Type(Guid.NewGuid());
        var unknown = classifier.ClassifyType(unknownType);
        Assert.Equal(AlchemyGameplayDomain.Unknown, unknown.Domain);
        Assert.Equal(unknownType.GetGuid(), Assert.Single(unknown.AlchemyTypeUuids));
    }

    [Fact]
    public void LifecycleInvalidationRejectsCachedEvidenceUntilFreshSnapshot()
    {
        var concept = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid));
        RegisterConceptRecipes(concept);
        using var classifier = new AlchemyGameplayDomainClassifier();
        Assert.True(classifier.TryInitialize(out var reason), reason);
        var generation = classifier.ClassifierGeneration;
        Assert.Equal(AlchemyGameplayDomain.ScholarConcept, classifier.ClassifyRecipe(concept).Domain);

        classifier.InvalidateLifecycle();

        Assert.True(classifier.ClassifierGeneration > generation);
        Assert.Equal(0, classifier.CachedRecipeCount);
        Assert.Equal(AlchemyGameplayDomain.Unknown, classifier.ClassifyRecipe(concept).Domain);
    }

    [Fact]
    public void SharedResolverGenerationInvalidatesReadyClassifierAndReplacementEvidence()
    {
        var concept = Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid));
        RegisterConceptRecipes(concept);
        var generation = 4L;
        var resolver = new TypedRegistryResolver(
            () => generation,
            () => TypedRegistrySourceSnapshot.Ready(IdScriptableObject.RuntimeLookup),
            value => ((IdScriptableObject)value).GetGuid());
        using var classifier = new AlchemyGameplayDomainClassifier(resolver);
        Assert.True(classifier.TryInitialize(out var reason), reason);
        Assert.Equal(4, classifier.LifecycleGeneration);
        Assert.Equal(AlchemyGameplayDomain.ScholarConcept, classifier.ClassifyRecipe(concept).Domain);

        generation = 5;
        var replacement = Recipe(concept.GetGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid));
        RegisterConceptRecipes(replacement);

        Assert.Equal(AlchemyGameplayDomain.Unknown, classifier.ClassifyRecipe(concept).Domain);
        Assert.Equal(AlchemyDomainClassifierStatus.Uninitialized, classifier.Status);
        Assert.True(classifier.TryInitialize(out reason), reason);
        Assert.Equal(5, classifier.LifecycleGeneration);
        Assert.Equal(AlchemyGameplayDomain.ScholarConcept, classifier.ClassifyRecipe(replacement).Domain);
    }

    [Fact]
    public void AnUnexpectedExceptionFromTheNativeIdentityReadEscapesInitialization()
    {
        RegisterConceptRecipes(Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid)));
        var resolver = new TypedRegistryResolver(
            () => 1,
            () => TypedRegistrySourceSnapshot.Ready(IdScriptableObject.RuntimeLookup),
            _ => throw new System.Reflection.TargetException("injected identity read failure"));
        using var classifier = new AlchemyGameplayDomainClassifier(resolver);

        Assert.Throws<System.Reflection.TargetException>(() => classifier.TryInitialize(out _));
    }

    [Fact]
    public void SameUuidDifferentNativeReferenceFailsClosedWithinLifecycle()
    {
        var uuid = Guid.NewGuid();
        var conceptType = Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid);
        var registered = Recipe(uuid, conceptType);
        var replacement = Recipe(uuid, conceptType);
        RegisterConceptRecipes(registered);
        using var classifier = new AlchemyGameplayDomainClassifier();
        Assert.True(classifier.TryInitialize(out var reason), reason);

        var result = classifier.ClassifyRecipe(replacement);

        Assert.Equal(AlchemyGameplayDomain.Unknown, result.Domain);
        Assert.Contains("different lifecycle-scoped native reference", result.Reason, StringComparison.Ordinal);
        Assert.True(result.Assessment.IsContradictory);
    }

    [Fact]
    public void DisplayNameNeverUpgradesUnknownIdentityEvidence()
    {
        RegisterConceptRecipes(Recipe(Guid.NewGuid(), Type(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid)));
        var recipe = Recipe(Guid.NewGuid(), Type(Guid.NewGuid()));
        recipe.name = "Alchemy";
        using var classifier = new AlchemyGameplayDomainClassifier();
        Assert.True(classifier.TryInitialize(out var reason), reason);

        var result = classifier.ClassifyRecipe(recipe);

        Assert.Equal(AlchemyGameplayDomain.Unknown, result.Domain);
        Assert.Equal(EvidenceLevel.RuntimeObserved, result.Assessment.Level);
        Assert.False(result.Assessment.Sources.HasFlag(EvidenceSource.SerializedAsset));
        Assert.False(result.IsMutationGrade);
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
