using System;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.Mentor.Runtime.ServiceCycle;

public sealed class MentorNativeAdapterTests : IDisposable
{
    private static readonly Guid Recipient =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public MentorNativeAdapterTests() => IdScriptableObject.RuntimeLookup.Clear();

    public void Dispose() => IdScriptableObject.RuntimeLookup.Clear();

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void SpellGrantResolvesIdentityAndVerifiesTheExactExperienceDelta()
    {
        var spell = new SpellRecipeSO
        {
            uuid = Recipient.ToString("D"),
            discovered = true,
            masteryLevel = 2,
            masteryExperience = new BigDouble(2),
        };
        IdScriptableObject.RuntimeLookup[Recipient] = spell;
        using var adapter = new MentorNativeAdapter();
        var action = Action(MasteryExperienceDomain.Spell, 5);

        var result = adapter.Grant(in action);

        Assert.True(result.Verified);
        Assert.Equal(new BigDouble(5), spell.masteryExperience);
        Assert.Equal(1, result.CallOutcome.MutationsCommitted);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void RecipientAtTheSourceCeilingIsRefusedWithoutMutation()
    {
        var spell = new SpellRecipeSO
        {
            uuid = Recipient.ToString("D"),
            discovered = true,
            masteryLevel = 5,
        };
        IdScriptableObject.RuntimeLookup[Recipient] = spell;
        using var adapter = new MentorNativeAdapter();
        var action = Action(MasteryExperienceDomain.Spell, 5);

        var result = adapter.Grant(in action);

        Assert.Equal(CommonActionResultCodes.NativeRejected, result.Code);
        Assert.Equal(
            "recipient is no longer available below the source mastery ceiling",
            result.Reason);
        Assert.Equal(0, spell.MasteryGrantCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void ArtifactGrantKeepsContainerSavedXpAndMasteryLevelInSync()
    {
        var equipment = new EquipmentSO { uuid = Recipient.ToString("D") };
        equipment.SetMasteryState(2, new BigDouble(2), new BigDouble(10));
        IdScriptableObject.RuntimeLookup[Recipient] = equipment;
        using var adapter = new MentorNativeAdapter();
        var action = Action(MasteryExperienceDomain.Artifact, 5);

        var result = adapter.Grant(in action);

        Assert.True(result.Verified);
        Assert.Equal(new BigDouble(5), equipment.masteryXp);
        Assert.Equal(new BigDouble(5), ((ExperienceContainer)equipment.GetExperienceElement()).GetExperience());
        Assert.Equal(2, equipment.masteryLevel);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void ASpellGrantTooSmallToChangeAHugeXpIsRefusedWithoutCallingTheGame()
    {
        var experience = new BigDouble(7.5488, 128);
        var spell = new SpellRecipeSO
        {
            uuid = Recipient.ToString("D"),
            discovered = true,
            masteryLevel = 2,
            masteryExperience = experience,
        };
        IdScriptableObject.RuntimeLookup[Recipient] = spell;
        using var adapter = new MentorNativeAdapter();
        var action = Action(MasteryExperienceDomain.Spell, 5);

        var result = adapter.Grant(in action);

        Assert.Equal(CommonActionResultCodes.NativeRejected, result.Code);
        Assert.Equal(
            "the grant is too small to change the recipient's mastery XP at its magnitude",
            result.Reason);
        Assert.Equal(0, spell.MasteryGrantCalls);
        Assert.Equal(experience, spell.masteryExperience);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void AnArtifactGrantTooSmallToChangeAHugeXpIsRefusedWithoutCallingTheGame()
    {
        var equipment = new EquipmentSO { uuid = Recipient.ToString("D") };
        equipment.SetMasteryState(2, new BigDouble(7.5488, 128), new BigDouble(1, 200));
        IdScriptableObject.RuntimeLookup[Recipient] = equipment;
        using var adapter = new MentorNativeAdapter();
        var action = Action(MasteryExperienceDomain.Artifact, 5);

        var result = adapter.Grant(in action);

        Assert.Equal(CommonActionResultCodes.NativeRejected, result.Code);
        Assert.Empty(((ExperienceContainer)equipment.GetExperienceElement()).Grants);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void AnAlchemyGrantThatLevelsTheRecipeUpVerifiesThoughItsXpReset()
    {
        RegisterConceptRecipes(Recipe(Guid.NewGuid(), AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid));
        var recipe = Recipe(Recipient, AlchemyGameplayDomainClassifier.AlchemyTypeUuid);
        recipe.discovered = true;
        recipe.masteryLevel = 2;
        recipe.masteryXp = new BigDouble(9);
        recipe.SetRequiredMasteryExperience(new BigDouble(10));
        IdScriptableObject.RuntimeLookup[Recipient] = recipe;
        using var adapter = new MentorNativeAdapter();
        var action = Action(MasteryExperienceDomain.Alchemy, 5);

        var result = adapter.Grant(in action);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(3, recipe.masteryLevel);
        Assert.Equal(new BigDouble(2), recipe.masteryXp);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void Every_missing_member_disables_the_complete_mentor_binding_set()
    {
        foreach (var missing in MentorNativeBindings.ContractIds)
        {
            IdScriptableObject.RuntimeLookup.Clear();
            var spell = new SpellRecipeSO
            {
                uuid = Recipient.ToString("D"),
                discovered = true,
                masteryLevel = 2,
            };
            IdScriptableObject.RuntimeLookup[Recipient] = spell;
            using var adapter = new MentorNativeAdapter(includeContract: id => id != missing);
            var action = Action(MasteryExperienceDomain.Spell, 5);

            var result = adapter.Grant(in action);

            Assert.True(
                result.Code == CommonActionResultCodes.AdapterFault &&
                result.Reason.Contains(missing, StringComparison.Ordinal),
                missing + " did not disable the set: " + result.Reason);
            Assert.Equal(0, spell.MasteryGrantCalls);
        }
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void AnUnexpectedGameThrowWhileRevalidatingARecipientEscapesInsteadOfBecomingAContractFault()
    {
        var spell = new SpellRecipeSO
        {
            uuid = Recipient.ToString("D"),
            discovered = true,
            masteryLevel = 2,
            DiscoveredReadFailure = new NullReferenceException("injected discovery read failure"),
        };
        IdScriptableObject.RuntimeLookup[Recipient] = spell;
        using var adapter = new MentorNativeAdapter();
        var action = Action(MasteryExperienceDomain.Spell, 5);

        Assert.Throws<NullReferenceException>(() => adapter.Grant(in action));
        Assert.Equal(0, spell.MasteryGrantCalls);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void AnExpectedGameFailureWhileRevalidatingARecipientIsAContractFaultBeforeAnyGrant()
    {
        var spell = new SpellRecipeSO
        {
            uuid = Recipient.ToString("D"),
            discovered = true,
            masteryLevel = 2,
            DiscoveredReadFailure = new InvalidOperationException("injected discovery read failure"),
        };
        IdScriptableObject.RuntimeLookup[Recipient] = spell;
        using var adapter = new MentorNativeAdapter();
        var action = Action(MasteryExperienceDomain.Spell, 5);

        var result = adapter.Grant(in action);

        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.StartsWith("Mentor could not grant mastery XP.", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("injected discovery read failure", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, spell.MasteryGrantCalls);
    }

    private static AlchemyRecipeSO Recipe(Guid uuid, Guid typeUuid)
    {
        var type = new AlchemyTypeSO();
        type.SetGuid(typeUuid);
        var recipe = new AlchemyRecipeSO();
        recipe.SetGuid(uuid);
        recipe.alchemyTypes.Add(type);
        return recipe;
    }

    private static void RegisterConceptRecipes(params AlchemyRecipeSO[] recipes)
    {
        var registry = new AlchemyRecipeListVariable();
        registry.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
        registry.value.AddRange(recipes);
        IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = registry;
    }

    private static MentorCycleAction Action(
        MasteryExperienceDomain domain,
        int ceiling) =>
        new(domain, Recipient, new MentorAmount(3, 0), ceiling, 1);
}
