using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

/// <summary>
/// What the identity pass claims, and what it refuses to claim. The two are different scopes on
/// purpose: a lookup is ambiguous only inside one table, while one entity reaching several tables is
/// how the snapshot is built.
/// </summary>
public sealed class WorldIdentityAuditTests
{
    /// <summary>
    /// The relation path a within-table repeat can actually arrive by: it calls
    /// <c>PublicationTable.Create</c> directly, so <see cref="WorldTable.Create"/>'s sort-and-reject
    /// guard never sees the rows.
    /// </summary>
    private static PublicationTable<WorldSpellRecipeAuthoring> Authoring(params Guid[] recipeIds)
    {
        var buffer = new WorldRelationBuffer<WorldSpellRecipeAuthoring>();
        foreach (var recipeId in recipeIds)
            buffer.Append(new WorldSpellRecipeAuthoring(recipeId, 0, 0d, 1d, 0, 0d, 0d));

        return WorldRelationTableDeriver.Build(
            buffer,
            static (left, right) => left.RecipeId.CompareTo(right.RecipeId));
    }

    private static GameWorldState WorldWith(
        Guid variableId,
        PublicationTable<WorldSpellRecipeAuthoring> authoring) =>
        new()
        {
            IntVariables = WorldTable.Create(
                new WorldNumberVariable(variableId, new BigDouble(1d), isPercent: false)),
            SpellRecipeAuthoring = authoring,
        };

    [Fact]
    public void A_snapshot_that_shares_no_identity_agrees_and_says_what_it_asserted()
    {
        var finding = WorldIdentityAudit.Audit(
            WorldWith(Guid.NewGuid(), Authoring(Guid.NewGuid())));

        Assert.Equal(VerificationVerdict.Agree, finding.Verdict);
        Assert.Equal(
            "Identities AGREE: 2 compared, 0 empty, 0 repeated within a table.",
            finding.Headline());
        Assert.Equal(
            "Shared identities: 2 entities, 0 detail rows filed under one of them.",
            finding.Note);
    }

    /// <summary>
    /// The whole point of the restructure: a detail table filed under an identity its owner already
    /// published is the design, so it agrees — and the fact is still published, attributed to the
    /// table that holds the rows.
    /// </summary>
    [Fact]
    public void One_identity_reaching_two_tables_agrees_and_is_published_as_a_named_fact()
    {
        var recipe = Guid.NewGuid();

        var finding = WorldIdentityAudit.Audit(WorldWith(recipe, Authoring(recipe)));

        Assert.Equal(VerificationVerdict.Agree, finding.Verdict);
        Assert.Equal(
            "Identities AGREE: 2 compared, 0 empty, 0 repeated within a table.",
            finding.Headline());
        Assert.Equal(
            "Shared identities: 1 entity, 1 detail row filed under one of them " +
            "(largest: SpellRecipeAuthoring 1).",
            finding.Note);
        Assert.Empty(finding.Detail);
    }

    /// <summary>
    /// The claim that still has teeth. Two rows under one identity in one table make a lookup return
    /// an arbitrary member of the pair, and the relation tables are where that can reach publication.
    /// </summary>
    [Fact]
    public void A_repeat_inside_one_table_disagrees_and_names_the_table()
    {
        var recipe = Guid.NewGuid();

        var finding = WorldIdentityAudit.Audit(
            WorldWith(Guid.NewGuid(), Authoring(recipe, recipe)));

        Assert.Equal(VerificationVerdict.Disagree, finding.Verdict);
        Assert.Equal("Identities DISAGREE: 3 compared, 2 agree, 1 differ.", finding.Headline());
        Assert.Equal(
            new[]
            {
                $"SpellRecipeAuthoring published {recipe} twice, so a lookup for it is ambiguous.",
            },
            finding.Detail);
    }

    /// <summary>
    /// The other half of the surviving claim: the same path publishes an unidentified row without a
    /// word, and an unidentified row is indistinguishable from an uninitialized one.
    /// </summary>
    [Fact]
    public void A_row_with_no_identity_disagrees()
    {
        var finding = WorldIdentityAudit.Audit(
            WorldWith(Guid.NewGuid(), Authoring(Guid.Empty)));

        Assert.Equal(VerificationVerdict.Disagree, finding.Verdict);
        Assert.Equal("Identities DISAGREE: 2 compared, 1 agree, 1 differ.", finding.Headline());
        Assert.Equal(
            new[] { "SpellRecipeAuthoring published a row with no identity." },
            finding.Detail);
    }
}
