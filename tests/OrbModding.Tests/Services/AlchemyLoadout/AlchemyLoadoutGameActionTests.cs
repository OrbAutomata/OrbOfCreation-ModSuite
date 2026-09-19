using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AlchemyLoadout;

public sealed class AlchemyLoadoutGameActionTests : IDisposable
{
    private const long Epoch = 97;
    private readonly IDictionary _registry = new Hashtable();

    public AlchemyLoadoutGameActionTests()
    {
        AlchemyManager.instance = new AlchemyManager();
        RegisterConceptCatalog();
    }

    public void Dispose()
    {
        AlchemyManager.instance = null;
    }

    [Fact]
    public void Add_and_remove_use_the_explicit_amount_and_directional_target_sentinel()
    {
        var recipe = OrdinaryRecipe(5);
        Register(recipe);
        AlchemyManager.instance!.allAlchemy.value.Add(recipe);
        using var boundary = Boundary();

        var added = Submit(boundary, recipe, AlchemyLoadoutActionKind.Add, amount: 2);
        var instance = Assert.Single(AlchemyManager.instance.activeAlchemy.value);
        var addedAmount = instance.GetQueuedQuantity();
        var removed = Submit(boundary, recipe, AlchemyLoadoutActionKind.Remove, amount: 2);

        Assert.True(added.Verified, added.Reason);
        Assert.Equal(2, addedAmount);
        Assert.True(removed.Verified, removed.Reason);
        Assert.Empty(AlchemyManager.instance.activeAlchemy.value);
    }

    [Fact]
    public void Add_and_remove_refuse_amounts_beyond_live_capacity_or_holdings()
    {
        var recipe = OrdinaryRecipe(3);
        Register(recipe);
        using var boundary = Boundary();

        var tooMany = Submit(boundary, recipe, AlchemyLoadoutActionKind.Add, amount: 4);
        var added = Submit(boundary, recipe, AlchemyLoadoutActionKind.Add, amount: 2);
        var removeTooMany = Submit(boundary, recipe, AlchemyLoadoutActionKind.Remove, amount: 3);

        Assert.Equal(AlchemyLoadoutPreflight.UsageUnavailable, tooMany.Preflight);
        Assert.True(added.Verified, added.Reason);
        Assert.Equal(AlchemyLoadoutPreflight.UsageUnavailable, removeTooMany.Preflight);
        Assert.Equal(2, Assert.Single(AlchemyManager.instance!.activeAlchemy.value).queuedQuantity);
    }

    /// <summary>
    /// Loadout order is cosmetic, so the boundary never reorders the list: no swap, no observable
    /// bump, and the two bindings that performed them are no longer bound at all.
    /// </summary>
    [Fact]
    public void The_boundary_never_reorders_the_native_list()
    {
        var first = OrdinaryRecipe(5);
        var second = OrdinaryRecipe(5);
        Register(first);
        AlchemyManager.instance!.activeAlchemy.value.Add(new AlchemyInstance(first) { queuedQuantity = 1 });
        AlchemyManager.instance.activeAlchemy.value.Add(new AlchemyInstance(second) { queuedQuantity = 1 });
        using var boundary = Boundary();

        var result = Submit(boundary, first, AlchemyLoadoutActionKind.Add);

        Assert.True(result.Verified, result.Reason);
        Assert.Same(first, AlchemyManager.instance.activeAlchemy.value[0].get_reference());
        Assert.Equal(0, AlchemyManager.instance.activeAlchemy.UpdateObservableCalls);
        Assert.DoesNotContain(AlchemyLoadoutNativeBindings.ContractIds,
            id => id.Contains("swap", StringComparison.Ordinal) ||
                  id.Contains("update", StringComparison.Ordinal));
    }

    [Fact]
    public void Concept_recipe_is_refused_by_the_shared_domain_classifier()
    {
        var concept = ConceptRecipe();
        Register(concept);
        using var boundary = Boundary();

        var result = Submit(boundary, concept, AlchemyLoadoutActionKind.Add);

        Assert.Equal(AlchemyLoadoutPreflight.WrongDomain, result.Preflight);
        Assert.Empty(AlchemyManager.instance!.activeAlchemy.value);
    }

    [Fact]
    public void Missing_native_transition_fails_the_one_outcome_sentinel()
    {
        var recipe = OrdinaryRecipe(5);
        Register(recipe);
        AlchemyManager.instance!.activeAlchemy.SuppressAddMutation = true;
        using var boundary = Boundary();

        var result = Submit(boundary, recipe, AlchemyLoadoutActionKind.Add);

        Assert.Equal(AlchemyLoadoutPreflight.VerificationFailed, result.Preflight);
        Assert.Empty(AlchemyManager.instance.activeAlchemy.value);
    }

    [Fact]
    public void Unity_thread_is_refused_before_identity_or_native_state()
    {
        var recipe = OrdinaryRecipe(5);
        Register(recipe);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() => Submit(boundary, recipe, AlchemyLoadoutActionKind.Add));

        Assert.Equal(AlchemyLoadoutPreflight.WrongThread, result.Preflight);
        Assert.Empty(AlchemyManager.instance!.activeAlchemy.value);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_lifecycle_binding_set()
    {
        foreach (var missing in AlchemyLoadoutNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private AlchemyLoadoutGameAction Boundary(Func<string, bool>? includeContract = null)
    {
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new AlchemyLoadoutGameAction(() => Epoch, static () => true,
            static () => "AlchemyLoadout ownership was revoked.",
            includeContract: includeContract, registry: resolver,
            classifier: new AlchemyGameplayDomainClassifier(resolver));
    }

    private static AlchemyLoadoutSubmission Submit(AlchemyLoadoutGameAction boundary,
        AlchemyRecipeSO recipe, AlchemyLoadoutActionKind kind, int amount = 1)
    {
        var action = new AlchemyLoadoutAction(kind, recipe.GetGuid(), amount, Epoch);
        return boundary.Submit(in action);
    }

    private void RegisterConceptCatalog()
    {
        var list = new AlchemyRecipeListVariable();
        list.SetGuid(KnownEntities.ConceptRecipes.Uuid);
        list.value.Add(ConceptRecipe());
        _registry.Add(KnownEntities.ConceptRecipes.Uuid, list);
    }

    private void Register(AlchemyRecipeSO recipe) => _registry.Add(recipe.GetGuid(), recipe);

    private static AlchemyRecipeSO OrdinaryRecipe(int maximum)
    {
        var type = new AlchemyTypeSO(AlchemyGameplayDomainClassifier.AlchemyTypeUuid.ToString("D"));
        var recipe = new AlchemyRecipeSO(Guid.NewGuid().ToString("D"), "Ordinary Alchemy", new[] { type })
        {
            coreType = type,
            discovered = true,
            maxUsageSlots = new ValueModifierRecord(new BigDouble(maximum)),
            freeUsageSlots = new ValueModifierRecord(new BigDouble(1)),
        };
        return recipe;
    }

    private static AlchemyRecipeSO ConceptRecipe()
    {
        var type = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString("D"));
        return new AlchemyRecipeSO(Guid.NewGuid().ToString("D"), "Concept", new[] { type })
        {
            coreType = type,
            maxUsageSlots = new ValueModifierRecord(new BigDouble(5)),
        };
    }
}
