using System;
using System.Collections.Generic;
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpRequirementDepthTests
{
    private static readonly Guid OwnerId = Guid.Parse("66000000-0000-4000-8000-000000000001");
    private static readonly Guid LinkId = Guid.Parse("66000000-0000-4000-8000-000000000002");
    private static readonly Guid ResearchId = Guid.Parse("66000000-0000-4000-8000-000000000003");

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Authored_groups_do_not_spend_the_link_expansion_budget(int researchLevel)
    {
        AssertEquivalentGate(depth: 31, researchLevel);
    }

    [Fact]
    public void Deep_finite_groups_keep_the_concrete_tier_explanation()
    {
        AssertEquivalentGate(depth: 4096, researchLevel: 0);
    }

    [Fact]
    public void A_reference_cycle_remains_unevaluable()
    {
        var requirements = ReadRequirements(World(depth: 31, researchLevel: 0, cyclic: true));
        Assert.Equal("Unevaluable", Text(requirements["suiteVerdict"]));
        var leaf = GateLeaf(requirements);
        Assert.False(Assert.IsType<bool>(Assert.IsType<GameMcpScalar>(leaf["met"]).Value));
        Assert.Equal("requirement_unevaluable", Text(leaf["reasonCode"]));
    }

    private static void AssertEquivalentGate(int depth, int researchLevel)
    {
        var shallow = ReadRequirements(World(depth: 0, researchLevel));
        var deep = ReadRequirements(World(depth, researchLevel));
        var shallowLeaf = GateLeaf(shallow);
        var deepLeaf = GateLeaf(deep);
        var expectedMet = researchLevel >= 3;
        Assert.Equal(expectedMet ? "Met" : "Unmet", Text(deep["suiteVerdict"]));
        Assert.Equal(Text(shallow["suiteVerdict"]), Text(deep["suiteVerdict"]));
        var needs = Text(deepLeaf["needs"]);
        Assert.Contains("Depth Research at level 3", needs);
        Assert.Equal(Text(shallowLeaf["needs"]), needs);
        Assert.Equal(ResearchId.ToString("D"), Text(deepLeaf["requirementUuid"]));
        Assert.Equal(expectedMet, Assert.IsType<bool>(Assert.IsType<GameMcpScalar>(deepLeaf["met"]).Value));
        Assert.Null(deepLeaf["reasonCode"]);
        if (!expectedMet)
        {
            Assert.Contains("(at 0)", needs);
            var unmet = Assert.IsType<GameMcpObject>(Assert.Single(
                Assert.IsType<GameMcpArray>(deep["unmet"]).Items));
            Assert.Equal(ResearchId.ToString("D"), Text(unmet["requirementUuid"]));
            Assert.Equal(needs, Text(unmet["needs"]));
        }
    }

    private static GameMcpObject ReadRequirements(GameWorldState world)
    {
        // The typed shipping read is inspected before transport encoding, just like other raw
        // projection contracts. No test-only production entry point or replacement fold is used.
        var result = GameMcpWorldDetail.GetRows(GameMcpTestHarness.Context(world), "upgrades",
            new[] { OwnerId.ToString("D") }).Freeze();
        var block = Assert.IsType<GameMcpObject>(Assert.Single(
            Assert.IsType<GameMcpArray>(result["results"]).Items));
        return Assert.IsType<GameMcpObject>(block["requirements"]);
    }

    private static GameMcpObject GateLeaf(GameMcpObject requirements)
    {
        var node = Assert.IsType<GameMcpObject>(requirements["root"]);
        while (node["operator"] is not null)
            node = Assert.IsType<GameMcpObject>(Assert.Single(
                Assert.IsType<GameMcpArray>(node["children"]).Items));
        return node;
    }

    private static string Text(GameMcpValue? value) =>
        Assert.IsType<string>(Assert.IsType<GameMcpScalar>(value).Value);

    private static GameWorldState World(int depth, int researchLevel, bool cyclic = false)
    {
        var rows = new List<WorldEntityRequirement>();
        AddGroups(rows, OwnerId, WorldRequirementOwnerKind.Upgrade, depth,
            WorldRequirementConditionKind.PrerequisiteLink, LinkId, reqType: 0, threshold: 0);
        AddGroups(rows, LinkId, WorldRequirementOwnerKind.PrerequisiteLink, depth,
            cyclic ? WorldRequirementConditionKind.PrerequisiteLink : WorldRequirementConditionKind.Research,
            cyclic ? LinkId : ResearchId, reqType: cyclic ? 0 : 2, threshold: cyclic ? 0 : 3);
        var ordered = rows.ToArray();
        Array.Sort(ordered, WorldEntityRequirementLookup.Order);
        return new GameWorldState
        {
            EntityIdentities = GameMcpTestHarness.CatalogWith(
                new EntityIdentityName(OwnerId, "UpgradeSO", "Depth Upgrade", "depthUpgrade"),
                new EntityIdentityName(LinkId, "PrerequisiteLinkSO", "Depth Gate", "depthGate"),
                new EntityIdentityName(ResearchId, "ResearchSO", "Depth Research", "depthResearch")),
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                GameMcpUpgradePageTestSupport.Upgrade(OwnerId, bounded: true),
            }),
            Research = PublicationTable<WorldResearch>.Create(new[]
            {
                GameMcpEntityDetailFixture.Research(ResearchId, available: true, level: researchLevel,
                    maxLevel: 20, baseRequirement: 1, effectiveRequirement: 1, leeway: 0),
            }),
            EntityRequirements = PublicationTable<WorldEntityRequirement>.Create(ordered),
            PrerequisiteLinkTiers = PublicationTable<WorldPrerequisiteLinkTier>.Create(new[]
            {
                new WorldPrerequisiteLinkTier(LinkId, 0, activeEnabled: true, passiveEnabled: false,
                    evaluatedFrame: 0, collectedFrame: 1),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                GameMcpTestHarness.AllCategoriesCollected()),
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
    }

    private static void AddGroups(List<WorldEntityRequirement> rows, Guid owner,
        WorldRequirementOwnerKind ownerKind, int depth, WorldRequirementConditionKind leafKind,
        Guid target, int reqType, double threshold)
    {
        for (var ordinal = 0; ordinal <= depth; ordinal++)
        {
            var group = ordinal < depth;
            rows.Add(new WorldEntityRequirement(owner, ownerKind, containerIndex: 0, ordinal,
                parentOrdinal: ordinal - 1, depth: ordinal,
                group ? WorldRequirementNodeKind.Group : WorldRequirementNodeKind.Leaf,
                group ? WorldRequirementOperator.And : WorldRequirementOperator.None,
                group ? WorldRequirementConditionKind.Unknown : leafKind,
                group ? "AndRequirement" : leafKind.ToString(), group ? Guid.Empty : target,
                group ? 0 : reqType, group ? 0 : threshold, default, default));
        }
    }
}
