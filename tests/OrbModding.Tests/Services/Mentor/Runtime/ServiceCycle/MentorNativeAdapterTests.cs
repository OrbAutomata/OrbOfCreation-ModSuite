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
        Assert.Equal(new BigDouble(5), equipment.GetExperienceElement().GetExperience());
        Assert.Equal(2, equipment.masteryLevel);
    }

    private static MentorCycleAction Action(
        MasteryExperienceDomain domain,
        int ceiling) =>
        new(domain, Recipient, new MentorAmount(3, 0), ceiling, 1);
}
