using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldRequirementTraversalTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FailedLinkTierCannotPublishAnEmptyOrPartialAndAsMet(int precedingChildren)
    {
        using var setup = new HealthyWorldReadSetup();
        global::PrerequisiteLinkSO.All.Clear();
        var met = Upgrade(1);
        var broken = new global::UpgradeSO { level = 0, GetGuidFailure = new InvalidOperationException("private tier child payload") };
        var link = new global::PrerequisiteLinkSO();
        link.linkTiers.Add(new global::PrerequisiteLinkSO.LinkDefinition());
        var failedTier = new global::PrerequisiteLinkSO.LinkDefinition();
        for (var index = 0; index < precedingChildren; index++) failedTier.prerequisites.prerequisites.Add(AtLeast(met));
        failedTier.prerequisites.prerequisites.Add(AtLeast(broken));
        link.linkTiers.Add(failedTier);
        var later = new global::PrerequisiteLinkSO.LinkDefinition();
        later.prerequisites.prerequisites.Add(AtLeast(met, 2));
        link.linkTiers.Add(later);
        global::PrerequisiteLinkSO.All.Add(link);
        var buyer = Upgrade(0);
        Price(buyer);
        buyer.prerequisitesPerLevel.prerequisites.Add(new Requirements.PrerequisiteLinkRequirement
        {
            item = link, reqType = Requirements.PrerequisiteLinkType.Tier,
            value = new Requirements.LeveledValue { baseValue = 1 },
        });
        var logs = new System.Collections.Generic.List<string>();
        OrbModding.Common.GameActionFaultLog.ConfigureLog(logs.Add);
        try
        {
            var collector = new GameWorldCollector();
            var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            var report = collector.Collect(frame);
            var world = GameWorldFrameDeriver.Build(frame);
            Assert.True(report.For("entity requirements").HasReadFault);
            Assert.NotEmpty(report.For("entity requirements").Failure.Reason.Reference);
            Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.EvaluateContainer(world, link.GetGuid(), 0, 0));
            Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.EvaluateContainer(world, link.GetGuid(), 1, 0));
            Assert.Equal(WorldRequirementVerdict.Unmet, WorldRequirementEvaluator.EvaluateContainer(world, link.GetGuid(), 2, 0));
            Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(world, buyer.GetGuid(), 1));
            Assert.False(Purchase(world, buyer.GetGuid()).MeetsNextLevelRequirements);
            Assert.Single(logs);

            broken.GetGuidFailure = null;
            global::UpgradeSO.All.Add(broken);
            collector.Collect(frame);
            var recovered = GameWorldFrameDeriver.Build(frame);
            Assert.Equal(WorldRequirementVerdict.Unmet, WorldRequirementEvaluator.Evaluate(recovered, buyer.GetGuid(), 1));
            Assert.False(Purchase(recovered, buyer.GetGuid()).MeetsNextLevelRequirements);
            broken.level = 1;
            collector.Collect(frame);
            var next = GameWorldFrameDeriver.Build(frame);
            Assert.Same(recovered.EntityRequirements, next.EntityRequirements);
            Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(next, buyer.GetGuid(), 1));
            Assert.True(Purchase(next, buyer.GetGuid()).MeetsNextLevelRequirements);
        }
        finally { OrbModding.Common.GameActionFaultLog.ConfigureLog(null); global::PrerequisiteLinkSO.All.Clear(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FiniteDeepGroupsCaptureAndFoldEveryProgramWithoutUsingTheCallStack(int programValue)
    {
        using var setup = new HealthyWorldReadSetup();
        var program = (WorldRequirementProgramKind)programValue;
        var target = Upgrade(1);
        var buyer = Upgrade(0);
        Price(buyer);
        var recipe = new global::AlchemyRecipeSO();
        global::AlchemyRecipeSO.All.Add(recipe);
        Requirements.IRequirementCondition node = AtLeast(target);
        const int groups = 4096;
        for (var depth = 0; depth < groups; depth++)
            node = depth % 2 == 0
                ? new Requirements.AndRequirement { andConditions = { node } }
                : new Requirements.OrRequirement { orConditions = { node } };
        var container = program == WorldRequirementProgramKind.Usage ? recipe.usagePrerequisites
            : program == WorldRequirementProgramKind.Unlock ? buyer.prerequisites : buyer.prerequisitesPerLevel;
        container.prerequisites.Add(node);
        var id = program == WorldRequirementProgramKind.Usage ? recipe.GetGuid() : buyer.GetGuid();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var collector = new GameWorldCollector();
        Assert.True(collector.Collect(frame).For("entity requirements").IsClean);
        var world = GameWorldFrameDeriver.Build(frame);
        Assert.True(WorldEntityRequirementLookup.TryFindRange(world.EntityRequirements, id, out var start, out var count));
        Assert.Equal(groups + 1, count);
        Assert.Equal(groups, world.EntityRequirements[start + count - 1].Depth);
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(world, id, 0, program));
        if (program == WorldRequirementProgramKind.NextLevel) Assert.True(Purchase(world, id).MeetsNextLevelRequirements);
        target.level = 0;
        collector.Collect(frame);
        world = GameWorldFrameDeriver.Build(frame);
        Assert.Equal(WorldRequirementVerdict.Unmet, WorldRequirementEvaluator.Evaluate(world, id, 0, program));
        if (program == WorldRequirementProgramKind.NextLevel) Assert.False(Purchase(world, id).MeetsNextLevelRequirements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TopLevelNullIsDurableUnknownForEveryProgramAndLinkTier(int scope)
    {
        using var setup = new HealthyWorldReadSetup();
        global::PrerequisiteLinkSO.All.Clear();
        var owner = Upgrade(0);
        Price(owner);
        var recipe = new global::AlchemyRecipeSO();
        global::AlchemyRecipeSO.All.Add(recipe);
        var link = new global::PrerequisiteLinkSO();
        link.linkTiers.Add(new global::PrerequisiteLinkSO.LinkDefinition());
        link.linkTiers.Add(new global::PrerequisiteLinkSO.LinkDefinition());
        global::PrerequisiteLinkSO.All.Add(link);
        var nested = Upgrade(0);
        nested.prerequisitesPerLevel.prerequisites.Add(new Requirements.AndRequirement { andConditions = { null! } });
        var program = scope == 1 ? WorldRequirementProgramKind.Usage : scope == 2 ? WorldRequirementProgramKind.Unlock : WorldRequirementProgramKind.NextLevel;
        var container = scope == 1 ? recipe.usagePrerequisites : scope == 2 ? owner.prerequisites
            : scope == 3 ? link.linkTiers[0].prerequisites : owner.prerequisitesPerLevel;
        container.prerequisites.Add(null!);
        if (scope == 3) owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.PrerequisiteLinkRequirement
        {
            item = link, reqType = Requirements.PrerequisiteLinkType.Base,
        });
        var id = scope == 1 ? recipe.GetGuid() : scope == 3 ? link.GetGuid() : owner.GetGuid();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var collector = new GameWorldCollector();
        var report = collector.Collect(frame).For("entity requirements");
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.False(report.IsClean);
        Assert.False(report.HasReadFault);
        Assert.True(WorldEntityRequirementLookup.TryFindRange(first.EntityRequirements, id, out var start, out var count));
        Assert.Contains(first.EntityRequirements.AsSpan().Slice(start, count).ToArray(), row =>
            row.NodeKind == WorldRequirementNodeKind.Leaf && row.ConditionTypeName == "NullRequirementCondition");
        Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.EvaluateContainer(first, id, 0, 0, program));
        Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(first, nested.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.EvaluateContainer(first, link.GetGuid(), 1, 0));
        if (scope is 0 or 3) Assert.False(Purchase(first, owner.GetGuid()).MeetsNextLevelRequirements);
        var generation = frame.StructuralGeneration;
        collector.Collect(frame);
        Assert.Equal(generation, frame.StructuralGeneration);
        Assert.Same(first.EntityRequirements, GameWorldFrameDeriver.Build(frame).EntityRequirements);
        global::PrerequisiteLinkSO.All.Clear();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NullTopLevelConditionListIsDurableUnknownOnlyForItsScope(int scope)
    {
        using var setup = new HealthyWorldReadSetup();
        global::PrerequisiteLinkSO.All.Clear();
        var owner = Upgrade(0);
        Price(owner);
        var emptyOwner = Upgrade(0);
        var recipe = new global::AlchemyRecipeSO();
        global::AlchemyRecipeSO.All.Add(recipe);
        var link = new global::PrerequisiteLinkSO();
        link.linkTiers.Add(new global::PrerequisiteLinkSO.LinkDefinition());
        link.linkTiers.Add(new global::PrerequisiteLinkSO.LinkDefinition());
        link.linkTiers.Add(new global::PrerequisiteLinkSO.LinkDefinition());
        global::PrerequisiteLinkSO.All.Add(link);
        var program = scope == 1 ? WorldRequirementProgramKind.Usage
            : scope == 2 ? WorldRequirementProgramKind.Unlock : WorldRequirementProgramKind.NextLevel;
        var container = scope == 1 ? recipe.usagePrerequisites : scope == 2 ? owner.prerequisites
            : scope == 3 ? link.linkTiers[1].prerequisites : owner.prerequisitesPerLevel;
        container.prerequisites = null!;
        if (scope == 3) owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.PrerequisiteLinkRequirement
        {
            item = link, reqType = Requirements.PrerequisiteLinkType.Tier,
            value = new Requirements.LeveledValue { baseValue = 1 },
        });
        var id = scope == 1 ? recipe.GetGuid() : scope == 3 ? link.GetGuid() : owner.GetGuid();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var collector = new GameWorldCollector();
        var report = collector.Collect(frame).For("entity requirements");
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.False(report.IsClean);
        Assert.Equal(1, report.Skipped);
        Assert.NotEmpty(report.FirstFailure);
        Assert.False(report.HasReadFault);
        Assert.Empty(report.Failure.Reason.Reference);
        Assert.True(WorldEntityRequirementLookup.TryFindRange(first.EntityRequirements, id, out var start, out var count));
        Assert.Contains(first.EntityRequirements.AsSpan().Slice(start, count).ToArray(), row =>
            row.Program == program && row.ContainerIndex == (scope == 3 ? 1 : 0) &&
            row.Kind == WorldRequirementConditionKind.Unknown && row.NodeKind == WorldRequirementNodeKind.Leaf);
        Assert.Equal(WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.EvaluateContainer(first, id, scope == 3 ? 1 : 0, 0, program));
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(first, emptyOwner.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(first, emptyOwner.GetGuid(), 0, WorldRequirementProgramKind.Unlock));
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.EvaluateContainer(first, link.GetGuid(), 0, 0));
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.EvaluateContainer(first, link.GetGuid(), 2, 0));
        if (scope != 1) Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(first, recipe.GetGuid(), 0, WorldRequirementProgramKind.Usage));
        if (scope != 2) Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(first, owner.GetGuid(), 0, WorldRequirementProgramKind.Unlock));
        if (scope is 0 or 3) Assert.False(Purchase(first, owner.GetGuid()).MeetsNextLevelRequirements);
        else Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(first, owner.GetGuid(), 0));
        var generation = frame.StructuralGeneration;
        collector.Collect(frame);
        var second = GameWorldFrameDeriver.Build(frame);
        Assert.Equal(generation, frame.StructuralGeneration);
        Assert.Same(first.EntityRequirements, second.EntityRequirements);
        Assert.Same(first.ConceptRecipes, second.ConceptRecipes);
        global::PrerequisiteLinkSO.All.Clear();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void NullRequiredOwnerContainerIsDurableUnknownWhileAbsentProgramsRemainMet(int scope)
    {
        using var setup = new HealthyWorldReadSetup();
        var buyer = Upgrade(0);
        Price(buyer);
        var empty = Upgrade(0);
        var met = Upgrade(1);
        var resource = new global::ResourceSO();
        global::ResourceSO.All.Add(resource);
        var ownerId = buyer.GetGuid();
        var program = scope is 1 or 3 or 5 or 6 or 8
            ? WorldRequirementProgramKind.Unlock : WorldRequirementProgramKind.NextLevel;
        switch (scope)
        {
            case 0: buyer.prerequisitesPerLevel = null!; break;
            case 1: buyer.prerequisites = null!; break;
            case 2:
            case 3:
                var structure = new global::StructureSO();
                global::StructureSO.All.Add(structure);
                ownerId = structure.GetGuid();
                if (scope == 2) structure.prerequisitesPerLevel = null!;
                else structure.prerequisites = null!;
                break;
            case 5:
            case 6:
                var research = new global::ResearchSO();
                global::ResearchSO.All.Add(research);
                ownerId = research.GetGuid();
                if (scope == 5) research.visibilityPrerequisites = null!;
                else research.visibilityPrerequisites.prerequisites.Add(AtLeast(met));
                if (scope == 6)
                    typeof(global::ResearchSO).GetField("levelVisibilityPrereq",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .SetValue(research, null);
                else research.LevelVisibilityPrereq.prerequisites.Add(AtLeast(met));
                break;
            case 7:
                var book = new global::RecipeBookSO { prerequisites = null! };
                global::RecipeBookSO.All.Add(book);
                ownerId = book.GetGuid();
                break;
            case 8:
                resource.visiblePrerequisites = null!;
                ownerId = resource.GetGuid();
                break;
        }
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var report = collector.Collect(frame).For("entity requirements");
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.False(report.IsClean);
        Assert.Equal(1, report.Skipped);
        Assert.Contains("container", report.FirstFailure, StringComparison.Ordinal);
        Assert.False(report.HasReadFault);
        Assert.Empty(report.Failure.Reason.Reference);
        Assert.True(WorldEntityRequirementLookup.TryFindRange(first.EntityRequirements, ownerId, out var start, out var count));
        var rows = first.EntityRequirements.AsSpan().Slice(start, count).ToArray();
        Assert.Single(rows, row => row.Program == program && row.Kind == WorldRequirementConditionKind.Unknown);
        Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(first, ownerId, 0, program));
        var unaffected = program == WorldRequirementProgramKind.Unlock
            ? WorldRequirementProgramKind.NextLevel : WorldRequirementProgramKind.Unlock;
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(first, ownerId, 0, unaffected));
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(first, empty.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(first, empty.GetGuid(), 0, WorldRequirementProgramKind.Unlock));
        // Resources declare no NextLevel accessor: absence is legitimate, even when Unlock is unreadable.
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(first, resource.GetGuid(), 0));
        if (scope == 0) Assert.False(Purchase(first, buyer.GetGuid()).MeetsNextLevelRequirements);
        if (scope is 5 or 6)
        {
            var unlock = rows.Where(row => row.Program == WorldRequirementProgramKind.Unlock).ToArray();
            Assert.Equal(2, unlock.Length);
            Assert.NotEqual(unlock[0].Ordinal, unlock[1].Ordinal);
            Assert.Single(unlock, row => row.Kind == WorldRequirementConditionKind.Upgrade);
        }
        var generation = frame.StructuralGeneration;
        collector.Collect(frame);
        var second = GameWorldFrameDeriver.Build(frame);
        Assert.Equal(generation, frame.StructuralGeneration);
        Assert.Same(first.EntityRequirements, second.EntityRequirements);
        Assert.Same(first.ConceptRecipes, second.ConceptRecipes);
    }

    [Fact]
    public void ANativeResearchPrerequisiteThrowFaultsTheWholePass()
    {
        using var setup = new HealthyWorldReadSetup();
        var research = new global::ResearchSO { levelPrerequisites = null! };
        global::ResearchSO.All.Add(research);
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };

        Assert.Throws<NullReferenceException>(() => collector.Collect(frame));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullStaticMembershipIsUnknownForSharedOwnersWhileEmptyAndNullElementsRemainUnmet(bool available)
    {
        using var setup = new HealthyWorldReadSetup();
        var firstOwner = Upgrade(0);
        var secondOwner = Upgrade(0);
        var emptyOwner = Upgrade(0);
        var nullElementOwner = Upgrade(0);
        var unrelatedOwner = Upgrade(0);
        var unreadable = new global::ConsumableRefListVariable { isStatic = true, value = null! };
        var empty = new global::ConsumableRefListVariable { isStatic = true };
        var nullElement = new global::ConsumableRefListVariable { isStatic = true };
        nullElement.value.Add(null!);
        foreach (var (owner, list) in new[]
                 { (firstOwner, unreadable), (secondOwner, unreadable), (emptyOwner, empty), (nullElementOwner, nullElement) })
            owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.ListRequirement
            {
                item = list,
                reqType = available ? Requirements.ListRequirementType.AnyAvailable : Requirements.ListRequirementType.AnyVisible,
                value = new Requirements.LeveledValue(),
            });
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var report = collector.Collect(frame).For("entity requirements");
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.False(report.IsClean);
        Assert.Equal(1, report.Skipped);
        Assert.NotEmpty(report.FirstFailure);
        Assert.False(report.HasReadFault);
        Assert.Empty(report.Failure.Reason.Reference);
        Assert.False(WorldRequirementListLookup.TryFindRange(first.RequirementListMembers, unreadable.GetGuid(), out _, out _));
        Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(first, firstOwner.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(first, secondOwner.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Unmet, WorldRequirementEvaluator.Evaluate(first, emptyOwner.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Unmet, WorldRequirementEvaluator.Evaluate(first, nullElementOwner.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(first, unrelatedOwner.GetGuid(), 0));
        Assert.True(WorldRequirementListLookup.TryFindRange(first.RequirementListMembers, empty.GetGuid(), out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(WorldRequirementListMember.HeaderPosition, first.RequirementListMembers[start].Position);
        Assert.True(WorldRequirementListLookup.TryFindRange(first.RequirementListMembers, nullElement.GetGuid(), out start, out count));
        Assert.Equal(2, count);
        Assert.Equal(Guid.Empty, first.RequirementListMembers[start + 1].MemberId);
        var generation = frame.StructuralGeneration;
        collector.Collect(frame);
        var second = GameWorldFrameDeriver.Build(frame);
        Assert.Equal(generation, frame.StructuralGeneration);
        Assert.Same(first.EntityRequirements, second.EntityRequirements);
        Assert.Same(first.RequirementListMembers, second.RequirementListMembers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullGroupChildListRemainsDurableUnknownWhileARealEmptyGroupKeepsItsIdentity(bool or)
    {
        using var setup = new HealthyWorldReadSetup();
        var broken = Upgrade(0);
        var empty = Upgrade(0);
        broken.prerequisitesPerLevel.prerequisites.Add(or
            ? new Requirements.OrRequirement { orConditions = null! }
            : new Requirements.AndRequirement { andConditions = null! });
        empty.prerequisitesPerLevel.prerequisites.Add(or
            ? new Requirements.OrRequirement() : new Requirements.AndRequirement());
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var report = collector.Collect(frame).For("entity requirements");
        var first = GameWorldFrameDeriver.Build(frame);
        Assert.Equal(1, report.Skipped);
        Assert.False(report.HasReadFault);
        Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(first, broken.GetGuid(), 0));
        Assert.Equal(or ? WorldRequirementVerdict.Unmet : WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(first, empty.GetGuid(), 0));
        var generation = frame.StructuralGeneration;
        collector.Collect(frame);
        Assert.Equal(generation, frame.StructuralGeneration);
        Assert.Same(first.EntityRequirements, GameWorldFrameDeriver.Build(frame).EntityRequirements);
    }

    [Fact]
    public void GroupReferenceCycleIsUnknownButSharedAcyclicChildrenRemainUsable()
    {
        using var setup = new HealthyWorldReadSetup();
        var cyclic = Upgrade(0);
        var cycle = new Requirements.AndRequirement();
        cycle.andConditions.Add(cycle);
        cyclic.prerequisitesPerLevel.prerequisites.Add(cycle);
        var good = Upgrade(1);
        var shared = new Requirements.OrRequirement { orConditions = { AtLeast(good) } };
        var owner = Upgrade(0);
        owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.AndRequirement { andConditions = { shared, shared } });
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        var report = new GameWorldCollector().Collect(frame).For("entity requirements");
        var world = GameWorldFrameDeriver.Build(frame);
        Assert.False(report.HasReadFault);
        Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(world, cyclic.GetGuid(), 0));
        Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(world, owner.GetGuid(), 0));
    }

    [Fact]
    public void FailedListMembershipDiscardsItsPrefixAndRetriesForTheNextOwner()
    {
        using var setup = new HealthyWorldReadSetup();
        var carry = new global::IntVariable();
        global::IntVariable.All.Add(carry);
        var globalType = new global::ConsumableTypeSO { maximumCarryLoad = carry };
        var globalTypeId = new Guid("315471ca-0d15-455d-92da-f9d5f95a3c33");
        globalType.SetGuid(globalTypeId);
        global::IdScriptableObject.RuntimeLookup[globalTypeId] = globalType;
        var visible = new global::ConsumableSO { visible = true };
        global::ConsumableSO.All.Add(visible);
        var broken = new global::ConsumableSO { GetGuidFailure = new InvalidOperationException("private list member payload") };
        var list = new global::ConsumableRefListVariable { isStatic = true };
        list.value.Add(visible);
        list.value.Add(broken);
        var first = Upgrade(0);
        var second = Upgrade(0);
        foreach (var owner in new[] { first, second })
            owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.ListRequirement
            {
                item = list, reqType = Requirements.ListRequirementType.AnyVisible,
                value = new Requirements.LeveledValue(),
            });
        var logs = new System.Collections.Generic.List<string>();
        OrbModding.Common.GameActionFaultLog.ConfigureLog(logs.Add);
        try
        {
            var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            var collector = new GameWorldCollector();
            var report = collector.Collect(frame);
            Assert.True(report.For("entity requirements").HasReadFault);
            Assert.True(report.For("consumables").IsClean);
            var world = GameWorldFrameDeriver.Build(frame);
            Assert.True(WorldLookup.TryFind(world.Consumables, visible.GetGuid(), out var visibleRow));
            Assert.True(visibleRow.Visible);
            Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(world, first.GetGuid(), 0));
            Assert.Equal(WorldRequirementVerdict.Unevaluable, WorldRequirementEvaluator.Evaluate(world, second.GetGuid(), 0));
            Assert.False(WorldRequirementListLookup.TryFindRange(world.RequirementListMembers, list.GetGuid(), out _, out _));
            Assert.Equal(2, logs.Count);
            Assert.All(logs, entry => Assert.Contains("private list member payload", entry, StringComparison.Ordinal));
            broken.GetGuidFailure = null;
            global::ConsumableSO.All.Add(broken);
            report = collector.Collect(frame);
            Assert.True(report.For("entity requirements").IsClean);
            Assert.True(report.For("consumables").IsClean);
            world = GameWorldFrameDeriver.Build(frame);
            Assert.True(WorldLookup.TryFind(world.Consumables, broken.GetGuid(), out _));
            Assert.Equal(2, logs.Count);
            Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(world, first.GetGuid(), 0));
            Assert.Equal(WorldRequirementVerdict.Met, WorldRequirementEvaluator.Evaluate(world, second.GetGuid(), 0));
        }
        finally { OrbModding.Common.GameActionFaultLog.ConfigureLog(null); }
    }

    private static global::UpgradeSO Upgrade(int level)
    {
        var upgrade = new global::UpgradeSO { level = level, maxLevel = -1 };
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }
    private static Requirements.UpgradeRequirement AtLeast(global::UpgradeSO target, int count = 1) => new()
    {
        item = target, reqType = Requirements.UpgradeRequirementType.AtLeast,
        value = new Requirements.LeveledValue { baseValue = count },
    };
    private static void Price(global::UpgradeSO owner)
    {
        var resource = new global::ResourceSO();
        global::ResourceSO.All.Add(resource);
        owner.resourceCost.costs.Add(new global::ResourceTuple(resource, new BigDouble(1)));
    }
    private static AutoBuyCandidateRow Purchase(GameWorldState world, Guid id)
    {
        var config = new SuiteRuntimeConfiguration { AutoBuy = new AutoBuyConfiguration { IncludeUpgrades = true, IncludeStructures = false } };
        var frame = default(AutoBuyCycleFrame);
        AutoBuyFrameProjector.Project(ref frame, in config, world);
        return Assert.Single(frame.Candidates.ToArray(), row => row.Uuid == id);
    }
}
