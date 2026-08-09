using System;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The published condition rows answer <c>prerequisitesPerLevel.Check(level)</c> without the game.
/// </summary>
/// <remarks>
/// <para>
/// Every case authors real registry entries, runs the real collector, and evaluates what it published.
/// A hand-built table would agree with whatever the evaluator expected and prove nothing about the two
/// halves meaning the same thing — and the halves are the risk here, since the reader publishes a
/// comparison as an integer and the evaluator decides what that integer means.
/// </para>
/// <para>
/// The numbers a condition is compared against are the game's, not the suite's convenience: a research
/// entry's level is three terms, and a structure is checked at the quantity it already has rather than
/// the one it is buying.
/// </para>
/// </remarks>
public sealed class WorldRequirementEvaluatorTests : IDisposable
{
    public WorldRequirementEvaluatorTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    /// <summary>
    /// The common case. An empty container passes unconditionally in the game, so an owner with no
    /// published rows is admitted rather than treated as unreadable.
    /// </summary>
    [Fact]
    public void AnOwnerWithNoAuthoredConditionsIsMet()
    {
        var upgrade = Upgrade();

        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), upgrade.GetGuid(), 1));
    }

    /// <summary>
    /// The live case this whole model exists for: an upgrade gated on a research entry reaching level
    /// six. Below it the purchase is never planned; at it, it is.
    /// </summary>
    [Fact]
    public void AResearchThresholdRefusesUntilTheResearchReachesIt()
    {
        var scroll = Upgrade();
        var scribing = Research();
        RequireResearch(scroll, scribing, 6d);

        scribing.level = 5;
        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(Collect(), scroll.GetGuid(), 1));

        scribing.level = 6;
        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), scroll.GetGuid(), 1));
    }

    /// <summary>
    /// A research entry's level is what it bought plus what it was granted, in two separate records.
    /// Comparing the bought field alone would refuse purchases the game allows, for every entry
    /// carrying a bonus — which is most of them by the mid game.
    /// </summary>
    [Fact]
    public void AResearchLevelIsTheSumOfItsThreeTerms()
    {
        var scroll = Upgrade();
        var scribing = Research();
        RequireResearch(scroll, scribing, 6d);

        scribing.level = 3;
        scribing.baseLevels = new ValueModifierRecord(new BigDouble(2d, 0));
        scribing.bonusLevels = new ValueModifierRecord(new BigDouble(1d, 0));

        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), scroll.GetGuid(), 1));
    }

    /// <summary>
    /// The threshold moves with the level being bought, so the same condition admits one level and
    /// refuses the next.
    /// </summary>
    [Fact]
    public void AThresholdThatScalesIsCheckedAtTheLevelBeingBought()
    {
        var output = Upgrade();
        var casting = new global::IntVariable { Value = 3 };
        global::IntVariable.All.Add(casting);
        output.prerequisitesPerLevel.prerequisites.Add(new Requirements.NumberRequirement
        {
            item = casting,
            reqType = Requirements.NumberRequirementType.Value,
            value = new Requirements.LeveledValue
            {
                baseValue = 1d,
                perLevel = new ValueModifier(ValueModifier.ValueModifierType.Raw, new BigDouble(1d)),
            },
        });

        var world = Collect();

        // baseValue one, rising by one a level: three at level two, four at level three.
        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(world, output.GetGuid(), 2));
        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(world, output.GetGuid(), 3));
    }

    /// <summary>
    /// An upgrade is checked at the level a purchase made now would reach, counting levels already in
    /// flight. Checking the owned level would let a second purchase be planned against the first one's
    /// requirements.
    /// </summary>
    [Fact]
    public void AnUpgradeIsCheckedAtTheLevelItsNextPurchaseReaches()
    {
        var upgrade = Upgrade();
        upgrade.level = 2;
        upgrade.queuedLevels = 1;

        var world = Collect();
        Assert.True(WorldLookup.TryFind(world.Upgrades, upgrade.GetGuid(), out var published));

        Assert.Equal(4L, WorldRequirementEvaluator.UpgradeCheckLevel(in published));
    }

    /// <summary>
    /// A structure is checked at the quantity it already has, which is one less than the symmetry with
    /// upgrades would suggest. The game's two call sites genuinely differ, and reproducing the tidier
    /// one would shift every structure's gate by a level.
    /// </summary>
    [Fact]
    public void AStructureIsCheckedAtTheQuantityItAlreadyHas()
    {
        var forge = new global::StructureSO { quantity = 3, queuedQuantity = 1 };
        global::StructureSO.All.Add(forge);

        var world = Collect();
        Assert.True(WorldLookup.TryFind(world.Structures, forge.GetGuid(), out var published));

        Assert.Equal(3L, WorldRequirementEvaluator.StructureCheckLevel(in published));
    }

    /// <summary>Every condition class the shipped content authors, evaluated against its own registry.</summary>
    /// <remarks>
    /// One case rather than eight because what is under test is the join: that the kind the reader
    /// classified, the comparison integer it copied, and the published fact the evaluator reaches for
    /// all line up. A per-kind file would pass with the same three things wired to each other wrongly.
    /// </remarks>
    [Fact]
    public void EveryModelledConditionKindReadsItsOwnPublishedFact()
    {
        var gated = Upgrade();

        var prior = Upgrade();
        prior.level = 1;
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = prior,
            reqType = Requirements.UpgradeRequirementType.OneLevel,
            value = new Requirements.LeveledValue(),
        });

        var scribing = Research();
        scribing.level = 6;
        RequireResearch(gated, scribing, 6d);

        var quarry = new global::StructureSO { quantity = 4 };
        global::StructureSO.All.Add(quarry);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.StructureRequirement
        {
            item = quarry,
            reqType = Requirements.StructureRequirementType.Quantity,
            value = new Requirements.LeveledValue { baseValue = 4d },
        });

        var bolt = new global::SpellRecipeSO { masteryLevel = 2, discovered = true };
        global::SpellRecipeSO.All.Add(bolt);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.SpellRequirement
        {
            item = bolt,
            reqType = Requirements.SpellRequirementType.MasteryLevel,
            value = new Requirements.LeveledValue { baseValue = 2d },
        });

        var brew = new global::AlchemyRecipeSO { advancementLevel = 3, discovered = true };
        global::AlchemyRecipeSO.All.Add(brew);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.AlchemyRecipeRequirement
        {
            item = brew,
            reqType = Requirements.AlchemyRecipeType.AdvLevel,
            value = new Requirements.LeveledValue { baseValue = 3d },
        });

        var rite = new global::RitualSO { reachedLevel = 5, discovered = true };
        global::RitualSO.All.Add(rite);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.RitualRequirement
        {
            item = rite,
            reqType = Requirements.RitualRequirementType.ReachedLevel,
            value = new Requirements.LeveledValue { baseValue = 5d },
        });

        var casting = new global::IntVariable { Value = 9 };
        global::IntVariable.All.Add(casting);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.NumberRequirement
        {
            item = casting,
            reqType = Requirements.NumberRequirementType.Value,
            value = new Requirements.LeveledValue { baseValue = 9d },
        });

        var world = Collect();
        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(world, gated.GetGuid(), 1));
        Assert.True(WorldEntityRequirementLookup.TryFindRange(
            world.EntityRequirements, gated.GetGuid(), out var start, out var count));
        Assert.Equal(7, count);
        var selected = new string[count];
        for (var index = 0; index < count; index++)
        {
            var row = world.EntityRequirements[start + index];
            selected[index] = WorldRequirementEvaluator.ExplainLeaf(
                world, in row, 1).SelectedValueKind;
        }
        Assert.Equal(new[]
        {
            "purchased_level",
            "total_level",
            "purchased_quantity",
            "mastery_level",
            "advancement_level",
            "reached_level",
            "numeric_value",
        }, selected);
    }

    [Fact]
    public void DiscoveryRecipeLevelAndPrerequisiteLinkSelectionsAreExplicit()
    {
        var gated = Upgrade();
        var spell = new global::SpellRecipeSO { discovered = true };
        global::SpellRecipeSO.All.Add(spell);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.SpellRequirement
        {
            item = spell,
            reqType = Requirements.SpellRequirementType.Discovered,
            value = new Requirements.LeveledValue(),
        });
        var alchemy = new global::AlchemyRecipeSO { maxLevel = 4, discovered = true };
        global::AlchemyRecipeSO.All.Add(alchemy);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.AlchemyRecipeRequirement
        {
            item = alchemy,
            reqType = Requirements.AlchemyRecipeType.RecipeLevel,
            value = new Requirements.LeveledValue { baseValue = 4d },
        });
        var link = LinkWithTier();
        gated.prerequisitesPerLevel.prerequisites.Add(LinkTo(link));

        var world = Collect();
        Assert.True(WorldEntityRequirementLookup.TryFindRange(
            world.EntityRequirements, gated.GetGuid(), out var start, out var count));
        Assert.Equal(3, count);

        var discovered = world.EntityRequirements[start];
        var recipeLevel = world.EntityRequirements[start + 1];
        var prerequisiteLink = world.EntityRequirements[start + 2];
        Assert.Equal("discovered", WorldRequirementEvaluator.ExplainLeaf(
            world, in discovered, 1).SelectedValueKind);
        Assert.Equal("recipe_level", WorldRequirementEvaluator.ExplainLeaf(
            world, in recipeLevel, 1).SelectedValueKind);
        Assert.Equal("prerequisite_link_gate", WorldRequirementEvaluator.ExplainLeaf(
            world, in prerequisiteLink, 1).SelectedValueKind);
    }

    [Fact]
    public void UnsupportedSelectedValueFailsClosedWithAStructuredReason()
    {
        var gated = Upgrade();
        var prior = Upgrade();
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = prior,
            reqType = Requirements.UpgradeRequirementType.Visible,
            value = new Requirements.LeveledValue(),
        });

        var world = Collect();
        Assert.True(WorldEntityRequirementLookup.TryFindRange(
            world.EntityRequirements, gated.GetGuid(), out var start, out _));
        var row = world.EntityRequirements[start];
        var explanation = WorldRequirementEvaluator.ExplainLeaf(world, in row, 1);

        Assert.Equal(WorldRequirementVerdict.Unevaluable, explanation.Verdict);
        Assert.Equal("unsupported_requirement_value", explanation.ReasonCode);
        Assert.Equal("purchased_level", explanation.SelectedValueKind);
    }

    /// <summary>Every condition has to hold, so one failure among six is a refusal.</summary>
    [Fact]
    public void OneUnmetConditionRefusesTheWholeOwner()
    {
        var gated = Upgrade();
        var scribing = Research();
        scribing.level = 6;
        RequireResearch(gated, scribing, 6d);

        var quarry = new global::StructureSO { quantity = 1 };
        global::StructureSO.All.Add(quarry);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.StructureRequirement
        {
            item = quarry,
            reqType = Requirements.StructureRequirementType.Quantity,
            value = new Requirements.LeveledValue { baseValue = 4d },
        });

        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// A condition class nobody has modelled refuses its owner, and says so as its own verdict rather
    /// than as an ordinary failure. Both refuse the purchase; only one of them is this suite's problem
    /// to fix.
    /// </summary>
    [Fact]
    public void AnUnmodelledConditionClassIsUnevaluableRatherThanUnmet()
    {
        var gated = Upgrade();
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.OpaqueRequirement());

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    [Fact]
    public void AUsageProgramOrGroupIsMetWhenEitherAuthoredResearchArmIsMet()
    {
        var recipe = new global::AlchemyRecipeSO();
        global::AlchemyRecipeSO.All.Add(recipe);
        var unmet = Research();
        var met = Research();
        met.level = 1;
        var group = new Requirements.OrRequirement();
        group.orConditions.Add(ResearchCondition(unmet, 1));
        group.orConditions.Add(ResearchCondition(met, 1));
        recipe.usagePrerequisites.prerequisites.Add(group);

        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(
                Collect(), recipe.GetGuid(), 0, WorldRequirementProgramKind.Usage));
    }

    [Fact]
    public void AnUnknownOrArmDoesNotPoisonAMetArm()
    {
        var gated = Upgrade();
        var met = Research();
        met.level = 1;
        var group = new Requirements.OrRequirement();
        group.orConditions.Add(new Requirements.OpaqueRequirement());
        group.orConditions.Add(ResearchCondition(met, 1));
        gated.prerequisitesPerLevel.prerequisites.Add(group);

        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    [Fact]
    public void AnOrGroupWithNoMetArmAndAnUnknownArmIsUnevaluable()
    {
        var gated = Upgrade();
        var unmet = Research();
        var group = new Requirements.OrRequirement();
        group.orConditions.Add(ResearchCondition(unmet, 1));
        group.orConditions.Add(new Requirements.OpaqueRequirement());
        gated.prerequisitesPerLevel.prerequisites.Add(group);

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    [Fact]
    public void AnOrGroupWithOnlyUnmetArmsIsUnmet()
    {
        var gated = Upgrade();
        var group = new Requirements.OrRequirement();
        group.orConditions.Add(ResearchCondition(Research(), 1));
        group.orConditions.Add(ResearchCondition(Research(), 1));
        gated.prerequisitesPerLevel.prerequisites.Add(group);

        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    [Fact]
    public void EmptyCompositeIdentityMatchesNativeAnyAndAll()
    {
        var any = Upgrade();
        any.prerequisitesPerLevel.prerequisites.Add(new Requirements.OrRequirement());
        var all = Upgrade();
        all.prerequisitesPerLevel.prerequisites.Add(new Requirements.AndRequirement());
        var world = Collect();

        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(world, any.GetGuid(), 1));
        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(world, all.GetGuid(), 1));
    }

    [Fact]
    public void AnUnknownAndArmPoisonsTheGroup()
    {
        var gated = Upgrade();
        var met = Research();
        met.level = 1;
        var group = new Requirements.AndRequirement();
        group.andConditions.Add(ResearchCondition(met, 1));
        group.andConditions.Add(new Requirements.OpaqueRequirement());
        gated.prerequisitesPerLevel.prerequisites.Add(group);

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    [Fact]
    public void ADeeperCompositeShapeIsNamedAndUnevaluable()
    {
        var gated = Upgrade();
        var outer = new Requirements.OrRequirement();
        outer.orConditions.Add(new Requirements.AndRequirement());
        gated.prerequisitesPerLevel.prerequisites.Add(outer);

        var world = Collect();

        Assert.Equal("AndRequirement", world.EntityRequirements[0].ConditionTypeName);
        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(world, gated.GetGuid(), 1));
    }

    /// <summary>
    /// The comparisons that ask another entity for its whole-entity gate reach a <c>Check()</c> that
    /// writes, so they are refused rather than approximated. <c>Visible</c> is the one the shipped
    /// content actually authors.
    /// </summary>
    [Fact]
    public void AComparisonThatWouldHaveToWriteIsUnevaluable()
    {
        var gated = Upgrade();
        var prior = Upgrade();
        prior.level = 3;
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = prior,
            reqType = Requirements.UpgradeRequirementType.Visible,
            value = new Requirements.LeveledValue(),
        });

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// A condition pointing at something the snapshot does not hold is refused rather than read as an
    /// entity at level nought, which would refuse silently and for the wrong reason — or, for a
    /// zero threshold, admit.
    /// </summary>
    [Fact]
    public void AConditionWhoseTargetIsMissingIsUnevaluable()
    {
        var gated = Upgrade();
        var scribing = Research();
        RequireResearch(gated, scribing, 0d);
        global::ResearchSO.All.Clear();

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// An unevaluable condition outranks an unmet one. The purchase is refused either way; the verdict
    /// exists so that whatever narrates the refusal names the suite's gap rather than the save's
    /// progress.
    /// </summary>
    [Fact]
    public void AnUnevaluableConditionOutranksAnUnmetOne()
    {
        var gated = Upgrade();
        var scribing = Research();
        scribing.level = 1;
        RequireResearch(gated, scribing, 6d);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.OpaqueRequirement());

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    [Fact]
    public void NestedAndOrGroupsUseThreeWayFailClosedSemantics()
    {
        var gatedByOr = Upgrade();
        var gatedByAnd = Upgrade();
        var scribing = Research();
        scribing.level = 1;
        var quarry = new global::StructureSO { quantity = 3 };
        global::StructureSO.All.Add(quarry);

        Requirements.IRequirementCondition ResearchLeaf() => new Requirements.ResearchRequirement
        {
            item = scribing,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = 6d },
        };
        Requirements.IRequirementCondition StructureLeaf() => new Requirements.StructureRequirement
        {
            item = quarry,
            reqType = Requirements.StructureRequirementType.Quantity,
            value = new Requirements.LeveledValue { baseValue = 3d },
        };

        var either = new Requirements.OrRequirement();
        either.orConditions.Add(ResearchLeaf());
        var nestedAll = new Requirements.AndRequirement();
        nestedAll.andConditions.Add(StructureLeaf());
        either.orConditions.Add(nestedAll);
        gatedByOr.prerequisitesPerLevel.prerequisites.Add(either);

        var all = new Requirements.AndRequirement();
        all.andConditions.Add(ResearchLeaf());
        var nestedEither = new Requirements.OrRequirement();
        nestedEither.orConditions.Add(StructureLeaf());
        all.andConditions.Add(nestedEither);
        gatedByAnd.prerequisitesPerLevel.prerequisites.Add(all);

        var world = Collect();

        Assert.Equal(WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(world, gatedByOr.GetGuid(), 1));
        Assert.Equal(WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(world, gatedByAnd.GetGuid(), 1));
    }

    [Fact]
    public void APrerequisiteLinkExpandsTheSelectedTierAndNestedLinks()
    {
        var gated = Upgrade();
        var scribing = Research();
        scribing.level = 6;
        var inner = LinkWithTier(Require(scribing, 6d));
        var outer = LinkWithTier(Require(scribing, 99d));
        outer.linkTiers.Add(Tier(new Requirements.PrerequisiteLinkRequirement
        {
            item = inner,
            reqType = Requirements.PrerequisiteLinkType.Base,
            value = new Requirements.LeveledValue(),
        }));
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.PrerequisiteLinkRequirement
        {
            item = outer,
            reqType = Requirements.PrerequisiteLinkType.Tier,
            value = new Requirements.LeveledValue { baseValue = 1d },
        });

        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// A link tier is evaluated at level zero however high the asking entity's level is.
    /// </summary>
    /// <remarks>
    /// <c>LinkDefinition.CheckPassivesEnabled()</c> reaches the tier through the no-argument
    /// <c>Container.Check()</c>, which walks at <c>ConditionInfo.Adjust(adjustValue, 0L)</c>. A tier
    /// has no level of its own, so forwarding the asker's would scale the tier's thresholds by a
    /// number the game never applies there — and that is invisible until a tier authors scaling.
    /// </remarks>
    [Fact]
    public void APrerequisiteLinkTierIsCheckedAtLevelZeroNotTheAskingEntitysLevel()
    {
        var scribing = Research();
        scribing.level = 1;
        var link = LinkWithTier(new Requirements.ResearchRequirement
        {
            item = scribing,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue
            {
                baseValue = 1d,
                perLevel = new ValueModifier(ValueModifier.ValueModifierType.Raw, new BigDouble(1d)),
            },
        });
        var gated = Upgrade();
        gated.prerequisitesPerLevel.prerequisites.Add(LinkTo(link));

        // The tier's threshold is one at level zero and six at level five; the research holds one.
        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 5));
    }

    [Fact]
    public void APrerequisiteLinkCycleFailsClosed()
    {
        var gated = Upgrade();
        var first = LinkWithTier();
        var second = LinkWithTier();
        first.linkTiers[0].prerequisites.prerequisites.Add(LinkTo(second));
        second.linkTiers[0].prerequisites.prerequisites.Add(LinkTo(first));
        gated.prerequisitesPerLevel.prerequisites.Add(LinkTo(first));

        Assert.Equal(WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    [Fact]
    public void AnEmptyAuthoredTierPassesButAnAbsentTierFailsClosed()
    {
        var emptyTier = LinkWithTier();
        var gatedByEmpty = Upgrade();
        gatedByEmpty.prerequisitesPerLevel.prerequisites.Add(LinkTo(emptyTier));
        var gatedByMissing = Upgrade();
        gatedByMissing.prerequisitesPerLevel.prerequisites.Add(
            new Requirements.PrerequisiteLinkRequirement
            {
                item = emptyTier,
                reqType = Requirements.PrerequisiteLinkType.Tier,
                value = new Requirements.LeveledValue { baseValue = 1d },
            });

        var world = Collect();

        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(world, gatedByEmpty.GetGuid(), 1));
        Assert.Equal(WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(world, gatedByMissing.GetGuid(), 1));
    }

    [Fact]
    public void APrerequisiteLinkReproducesNativeActiveAndPassiveCacheShortCircuits()
    {
        var scribing = Research();
        scribing.level = 6;

        var inactive = LinkWithTier(Require(scribing, 6d));
        inactive.linkTiers[0].isActiveEnabled = false;
        var gatedByInactive = Upgrade();
        gatedByInactive.prerequisitesPerLevel.prerequisites.Add(LinkTo(inactive));

        var latched = LinkWithTier(Require(scribing, 99d));
        latched.linkTiers[0].isPassiveEnabled = true;
        var gatedByLatch = Upgrade();
        gatedByLatch.prerequisitesPerLevel.prerequisites.Add(LinkTo(latched));

        var checkedThisFrame = LinkWithTier(Require(scribing, 6d));
        global::GameManager.currentFrame = 12;
        checkedThisFrame.linkTiers[0].currentFrame = 12;
        var gatedByFrameCache = Upgrade();
        gatedByFrameCache.prerequisitesPerLevel.prerequisites.Add(LinkTo(checkedThisFrame));

        var cachedWorld = Collect();

        Assert.Equal(WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(cachedWorld, gatedByInactive.GetGuid(), 1));
        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(cachedWorld, gatedByLatch.GetGuid(), 1));
        Assert.Equal(WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(cachedWorld, gatedByFrameCache.GetGuid(), 1));

        global::GameManager.currentFrame = 13;
        Assert.Equal(WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), gatedByFrameCache.GetGuid(), 1));
    }

    [Fact]
    public void ARequirementGraphBeyondTheExpansionBoundFailsClosed()
    {
        var gated = Upgrade();
        var scribing = Research();
        scribing.level = 99;
        Requirements.IRequirementCondition nested = Require(scribing, 1d);
        for (var depth = 0; depth < 33; depth++)
        {
            var group = new Requirements.AndRequirement();
            group.andConditions.Add(nested);
            nested = group;
        }
        gated.prerequisitesPerLevel.prerequisites.Add(nested);

        Assert.Equal(WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// The generic condition points at an arbitrary upgradeable object and asks for its level, which is
    /// a different expression per target type. Only a number variable's is modelled, so a generic
    /// condition on anything else is refused rather than answered from the wrong override.
    /// </summary>
    /// <remarks>
    /// The modelled half needs a target that is both an upgradeable object and a registered number
    /// variable, which the game has and these stubs do not — the stub registries model the two number
    /// registries without the shared base. The live differential pass is what covers it.
    /// </remarks>
    [Fact]
    public void AGenericConditionOnSomethingThatIsNotANumberVariableIsUnevaluable()
    {
        var gated = Upgrade();
        var quarry = new global::StructureSO { quantity = 9 };
        global::StructureSO.All.Add(quarry);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.GenericRequirement
        {
            item = quarry,
            reqType = Requirements.GenericRequirementType.Level,
            value = new Requirements.LeveledValue { baseValue = 1d },
        });

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// The live case: a link tier is gated on a time rune having been discovered. The target's own
    /// category already publishes the game's <c>IsDiscovered()</c>, so the answer comes from the
    /// implementer the identity belongs to rather than from a guess about which field to read.
    /// </summary>
    [Fact]
    public void ADiscoveredConditionReadsTheTargetCategorysOwnDiscoveryVerdict()
    {
        var gated = Upgrade();
        var rune = TimeRune(discovered: false);
        RequireDiscovered(gated, rune);

        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));

        rune.discovered = true;
        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// A discovery comparison whose target is in no discoverable category is refused. The game answers
    /// true outright for a target implementing nothing, and admitting a purchase on that is exactly the
    /// accident the fail-closed rule exists for.
    /// </summary>
    [Fact]
    public void ADiscoveredConditionOnSomethingUndiscoverableIsRefusedRatherThanAdmitted()
    {
        var gated = Upgrade();
        var quarry = new global::StructureSO { quantity = 9 };
        global::StructureSO.All.Add(quarry);
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.GenericRequirement
        {
            item = quarry,
            reqType = Requirements.GenericRequirementType.Discovered,
            value = new Requirements.LeveledValue(),
        });

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// The live case: the inventory tier is gated on any consumable in the authored master list being
    /// visible. One visible member opens it, and the game's own fold stops at the first true.
    /// </summary>
    [Fact]
    public void AnyVisibleIsMetAsSoonAsOneMemberOfTheListIsVisible()
    {
        var gated = Upgrade();
        Consumable(visible: false);
        var shown = Consumable(visible: false);
        var list = StaticList(global::ConsumableSO.All.ToArray());
        RequireList(gated, list, Requirements.ListRequirementType.AnyVisible);

        Assert.Equal(
            WorldRequirementVerdict.Unmet,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));

        shown.visible = true;
        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// <c>ConsumableSO</c> answers <c>IsAvailable()</c> from the same stored field as
    /// <c>IsVisible()</c>, so the two comparisons agree on it rather than reading different state.
    /// </summary>
    [Fact]
    public void AnyAvailableReadsTheSameStoredGateAsAnyVisible()
    {
        var gated = Upgrade();
        var list = StaticList(Consumable(visible: true));
        RequireList(gated, list, Requirements.ListRequirementType.AnyAvailable);

        Assert.Equal(
            WorldRequirementVerdict.Met,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// The class's third comparison is authored nowhere in this baseline, so it is refused rather
    /// than answered by a branch nothing has ever checked against the game.
    /// </summary>
    [Fact]
    public void TheCountComparisonNoContentAuthorsIsRefusedRatherThanGuessed()
    {
        var gated = Upgrade();
        var list = StaticList(Consumable(visible: true));
        RequireList(gated, list, Requirements.ListRequirementType.Count, threshold: 1d);

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    /// <summary>
    /// A member whose own gate the snapshot does not carry refuses the fold. Treating it as not
    /// visible would answer <em>unmet</em> for a list the game calls satisfied, which is the one
    /// direction a planner cannot recover from.
    /// </summary>
    [Fact]
    public void AMemberWhoseGateIsNotPublishedRefusesTheFold()
    {
        var gated = Upgrade();
        var list = StaticList(new global::ConsumableSO());
        RequireList(gated, list, Requirements.ListRequirementType.AnyVisible);

        Assert.Equal(
            WorldRequirementVerdict.Unevaluable,
            WorldRequirementEvaluator.Evaluate(Collect(), gated.GetGuid(), 1));
    }

    private static global::UpgradeSO Upgrade()
    {
        var upgrade = new global::UpgradeSO { maxLevel = -1 };
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }

    /// <summary>
    /// Consumables only publish once the global consumable type's carry-load edge is reachable, so a
    /// test that wants their visibility rows authors the same registry the game holds.
    /// </summary>
    private static global::ConsumableSO Consumable(bool visible)
    {
        if (global::ConsumableSO.All.Count == 0)
        {
            var carryLoad = new global::IntVariable();
            global::IntVariable.All.Add(carryLoad);
            var globalType = new global::ConsumableTypeSO { maximumCarryLoad = carryLoad };
            globalType.SetGuid(GlobalConsumableTypeId);
            global::IdScriptableObject.RuntimeLookup[GlobalConsumableTypeId] = globalType;
        }

        var consumable = new global::ConsumableSO { visible = visible };
        global::ConsumableSO.All.Add(consumable);
        return consumable;
    }

    private static readonly Guid GlobalConsumableTypeId =
        new("315471ca-0d15-455d-92da-f9d5f95a3c33");

    private static global::ConsumableRefListVariable StaticList(
        params global::ConsumableSO[] members)
    {
        var list = new global::ConsumableRefListVariable { isStatic = true };
        list.value.AddRange(members);
        return list;
    }

    private static void RequireList(
        global::UpgradeSO owner,
        global::AbstractListVariable list,
        Requirements.ListRequirementType reqType,
        double threshold = 0d) =>
        owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.ListRequirement
        {
            item = list,
            reqType = reqType,
            value = new Requirements.LeveledValue { baseValue = threshold },
        });

    private static global::TimeRuneSO TimeRune(bool discovered)
    {
        var rune = new global::TimeRuneSO { discovered = discovered };
        global::TimeRuneSO.All.Add(rune);
        return rune;
    }

    private static void RequireDiscovered(
        global::UpgradeSO owner, global::UpgradeableObject target) =>
        owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.GenericRequirement
        {
            item = target,
            reqType = Requirements.GenericRequirementType.Discovered,
            value = new Requirements.LeveledValue(),
        });

    private static global::ResearchSO Research()
    {
        var research = new global::ResearchSO { maxLevel = 10 };
        global::ResearchSO.All.Add(research);
        return research;
    }

    private static void RequireResearch(
        global::UpgradeSO owner, global::ResearchSO target, double threshold) =>
        owner.prerequisitesPerLevel.prerequisites.Add(ResearchCondition(target, threshold));

    private static Requirements.ResearchRequirement ResearchCondition(
        global::ResearchSO target,
        double threshold) =>
        new Requirements.ResearchRequirement
        {
            item = target,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = threshold },
        };

    private static Requirements.ResearchRequirement Require(
        global::ResearchSO target, double threshold) => new()
        {
            item = target,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = threshold },
        };

    private static global::PrerequisiteLinkSO LinkWithTier(
        params Requirements.IRequirementCondition[] conditions)
    {
        var link = new global::PrerequisiteLinkSO();
        global::PrerequisiteLinkSO.All.Add(link);
        link.linkTiers.Add(Tier(conditions));
        return link;
    }

    private static global::PrerequisiteLinkSO.LinkDefinition Tier(
        params Requirements.IRequirementCondition[] conditions)
    {
        var tier = new global::PrerequisiteLinkSO.LinkDefinition();
        foreach (var condition in conditions) tier.prerequisites.prerequisites.Add(condition);
        return tier;
    }

    private static Requirements.PrerequisiteLinkRequirement LinkTo(
        global::PrerequisiteLinkSO target) => new()
        {
            item = target,
            reqType = Requirements.PrerequisiteLinkType.Base,
            value = new Requirements.LeveledValue(),
        };

    private static GameWorldState Collect()
    {
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
        collector.Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }

    private static void ClearRegistries()
    {
        global::UpgradeSO.All.Clear();
        global::StructureSO.All.Clear();
        global::ResearchSO.All.Clear();
        global::ConsumableSO.All.Clear();
        global::SpellRecipeSO.All.Clear();
        global::AlchemyRecipeSO.All.Clear();
        global::RitualSO.All.Clear();
        global::TimeRuneSO.All.Clear();
        global::RitualManager.instance = new global::RitualManager();
        global::IntVariable.All.Clear();
        global::PrerequisiteLinkSO.All.Clear();
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::GameManager.currentFrame = 0;
    }
}
