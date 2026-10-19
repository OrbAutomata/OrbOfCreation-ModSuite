using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpEntityDetailFixture;

namespace OrbModding.ProfileTests;

[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpEntityDetailRequirementTests : IDisposable
{
    public GameMcpEntityDetailRequirementTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void RequirementsExpandOrderedLinkTiersAndPreserveAndOrGroups()
    {
        var owner = Upgrade();
        var research = ResearchStub(level: 0);
        var either = new Requirements.OrRequirement();
        either.orConditions.Add(Require(research, 5));
        var all = new Requirements.AndRequirement();
        all.andConditions.Add(Require(research, 6));
        all.andConditions.Add(new Requirements.OrRequirement());
        either.orConditions.Add(all);
        owner.prerequisitesPerLevel.prerequisites.Add(either);

        var link = new global::PrerequisiteLinkSO();
        global::PrerequisiteLinkSO.All.Add(link);
        link.linkTiers.Add(Tier(Require(research, 1)));
        link.linkTiers.Add(Tier(Require(research, 2)));
        // A third rung, and the row asks for the last of them. Round 15's two gates
        // (Conceptualization and Innovation, both on the same named-tier link) asked for tier 0
        // and read "at tier 1"; a fixture asking for tier 1 would have read right by accident,
        // because the number printed was the met pair's hard-coded one.
        link.linkTiers.Add(Tier(Require(research, 3)));
        owner.prerequisitesPerLevel.prerequisites.Add(
            new Requirements.PrerequisiteLinkRequirement
            {
                item = link,
                reqType = Requirements.PrerequisiteLinkType.Tier,
                value = new Requirements.LeveledValue { baseValue = 2d },
            });

        var result = Explain(Collect(), owner.GetGuid(), 920);

        Assert.Equal("unavailable", (string?)result["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)result["reasonCode"]);
        var requirements = Assert.IsType<JObject>(result["requirements"]);
        var root = Assert.IsType<JObject>(requirements["root"]);
        Assert.Equal("AND", (string?)root["operator"]);
        var top = root["children"]!.OfType<JObject>().ToArray();
        Assert.Equal(2, top.Length);
        Assert.Equal("OR", (string?)top[0]["operator"]);
        var orChildren = top[0]["children"]!.OfType<JObject>().ToArray();
        Assert.Equal("AND", (string?)orChildren[1]["operator"]);
        var andChildren = orChildren[1]["children"]!.OfType<JObject>().ToArray();
        Assert.Equal(2, andChildren.Length);
        Assert.EndsWith(" at level 6 (at 0)", (string?)andChildren[0]["needs"]);
        // A group three levels deep is a shape no authored container has, so it says so in one
        // authored sentence pointing at the screen that draws it.
        var unread = andChildren[1];
        Assert.False((bool)unread["met"]!);
        Assert.Null(unread["reasonCode"]);
        Assert.StartsWith(
            "This requirement is one the suite cannot read yet; open ",
            (string?)unread["reason"]);
        Assert.Null(unread["diagnostics"]);
        Assert.Null(unread["verdict"]);

        // What it needs, in the screen's words, and whether it holds. Nothing else: the tree
        // position, the native class, the selected value's internal name and three thresholds of
        // which one is ever displayed were all the suite explaining itself.
        var firstLeaf = orChildren[0];
        Assert.Equal(GameMcpTestHarness.Handle(research.GetGuid()),
            (string?)firstLeaf["requirement"]!["uuid"]);
        Assert.EndsWith(" at level 5 (at 0)", (string?)firstLeaf["needs"]);
        Assert.False((bool)firstLeaf["met"]!);
        Assert.Equal(
            new[] { "needs", "met", "requirement" },
            firstLeaf.Children<Newtonsoft.Json.Linq.JProperty>()
                .Select(property => property.Name)
                .ToArray());

        // The gate reads as what the one tier it asks about needs, in the same words every other
        // leaf uses. Named after its asset it read "the InnovationTiers gate", a name no screen
        // draws, beside an id whose read was a dead end; a tier of one condition now points at
        // that condition's own target, and the gate keeps its own `met`.
        var gate = top[1];
        Assert.EndsWith(" at level 3 (at 0)", (string?)gate["needs"]);
        Assert.DoesNotContain("gate", (string?)gate["needs"], StringComparison.Ordinal);
        Assert.False((bool)gate["met"]!);
        Assert.Equal(GameMcpTestHarness.Handle(research.GetGuid()),
            (string?)gate["requirement"]!["uuid"]);
        Assert.Null(gate["prerequisiteLinkTiers"]);
        Assert.Equal(2, (int)gate["otherTiers"]!);
        Assert.Null(gate["children"]);

        // Tier 0 is the rung this row does not ask about, and none of its conditions, frame
        // counters or enabled flags ride along.
        Assert.DoesNotContain("at level 1", gate.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("at level 2", gate.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("evaluatedFrame", gate.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// An authored empty AND group compares nothing and the game counts it met. The explanation used
    /// to call it an unsupported value and unmet, beside needs text saying the group is empty.
    /// </summary>
    [Fact]
    public void An_empty_and_group_is_explained_as_met()
    {
        var owner = Upgrade();
        owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.AndRequirement());

        var result = Explain(Collect(), owner.GetGuid(), 922);

        var group = Assert.Single(
            Assert.IsType<JObject>(result["requirements"]!["root"])["children"]!.OfType<JObject>())!;
        Assert.Equal("nothing — this group is empty", (string?)group["needs"]);
        Assert.True((bool)group["met"]!);
        Assert.Null(group["reasonCode"]);
    }

    [Fact]
    public void A_met_gate_reads_as_what_its_tier_needs_and_points_at_it()
    {
        var owner = Upgrade();
        var innovation = ResearchStub(level: 1);
        var link = new global::PrerequisiteLinkSO();
        global::PrerequisiteLinkSO.All.Add(link);
        var tier = Tier(Require(innovation, 1));
        tier.isPassiveEnabled = true;
        link.linkTiers.Add(tier);
        owner.prerequisitesPerLevel.prerequisites.Add(
            new Requirements.PrerequisiteLinkRequirement
            {
                item = link,
                reqType = Requirements.PrerequisiteLinkType.Base,
            });

        var result = Explain(Collect(), owner.GetGuid(), 921);

        var root = Assert.IsType<JObject>(result["requirements"]!["root"]);
        var gate = Assert.Single(root["children"]!.OfType<JObject>())!;
        Assert.EndsWith(" at level 1", (string?)gate["needs"]);
        Assert.DoesNotContain("gate", (string?)gate["needs"], StringComparison.Ordinal);
        Assert.True((bool)gate["met"]!);
        Assert.Equal(GameMcpTestHarness.Handle(innovation.GetGuid()),
            (string?)gate["requirement"]!["uuid"]);
        Assert.Null(gate["children"]);
    }

    [Fact]
    public void ImprovedCastingExpandsItsOrPrerequisiteAndUsesNativeCompletionTruth()
    {
        var improvedId = Guid.Parse("21628be0-4377-4b13-b28c-171ab29324bf");
        var expansionId = Guid.Parse("779fcab3-7ac8-4b7c-a96b-fed313a4fa51");
        var wizardryId = Guid.Parse("fcd15239-47d1-41b9-bad0-59826fb41ba4");
        var improved = ResearchStub(level: 1, id: improvedId, maxLevel: 1);
        var expansion = ResearchStub(level: 0, id: expansionId, maxLevel: 20);
        var wizardry = ResearchStub(level: 5, id: wizardryId, maxLevel: 20);
        var either = new Requirements.OrRequirement();
        either.orConditions.Add(Require(wizardry, 5));
        either.orConditions.Add(Require(expansion, 15));
        improved.levelPrerequisites.prerequisites.Add(either);
        improved.levelPrerequisites.ParameterizedCheckResult = true;

        var result = Explain(Collect(), improvedId, 923);

        var responseBytes = System.Text.Encoding.UTF8.GetByteCount(
            result.ToString(Newtonsoft.Json.Formatting.None));
        // A third narrower than it was, and every byte that went was the suite talking about itself:
        // tree coordinates, the native class, three thresholds per leaf of which one is displayed,
        // and a code restating `met: false`.
        Assert.True(responseBytes < 1_664, "explanation was " + responseBytes + " bytes");

        // A block that answered carries no verdict line. Inside a batch that silence is what
        // separates it from the block beside it that refused.
        Assert.Null(result["status"]);
        Assert.Null(result["reasonCode"]);
        Assert.NotNull(result["row"]);
        var requirements = Assert.IsType<JObject>(result["requirements"]);
        Assert.Null(requirements["applicable"]);
        var root = Assert.IsType<JObject>(requirements["root"]);
        var orGroup = Assert.Single(root["children"]!.OfType<JObject>());
        Assert.Equal("OR", (string?)orGroup["operator"]);
        var leaves = orGroup["children"]!.OfType<JObject>().ToArray();
        Assert.Equal(2, leaves.Length);
        Assert.Equal(GameMcpTestHarness.Handle(wizardryId), (string?)leaves[0]["requirement"]!["uuid"]);
        Assert.EndsWith(" at level 5", (string?)leaves[0]["needs"]);
        Assert.True((bool)leaves[0]["met"]!);
        Assert.Equal(GameMcpTestHarness.Handle(expansionId), (string?)leaves[1]["requirement"]!["uuid"]);
        // The two numbers a reader was pairing by eye out of `current` and `required` are one
        // phrase, and a met row does not print its own current at all — it is not news.
        Assert.EndsWith(" at level 15 (at 0)", (string?)leaves[1]["needs"]);
        Assert.False((bool)leaves[1]["met"]!);
        Assert.Null(leaves[0]["current"]);
        Assert.Null(leaves[0]["required"]);
        Assert.Null(leaves[1]["current"]);
        Assert.Null(leaves[1]["required"]);

        // Neither leaf carries a class or a sentence. A met row never did; an unmet one used to read
        // `ERR_LOCKED` beside a paragraph restating that a requirement is unmet, which is the one
        // thing `met: false` already says.
        Assert.Null(leaves[0]["reasonCode"]);
        Assert.Null(leaves[0]["reason"]);
        Assert.Null(leaves[1]["reasonCode"]);
        Assert.Null(leaves[1]["reason"]);

        var predicates = result["predicates"]!;
        Assert.False((bool)predicates["available"]!["available"]!);
        Assert.Null(predicates["available"]!["reasonCode"]);

        // `canDevelop` and the row's own `develop` said the identical verdict, code and sentence,
        // so the page says it once — on the action a caller can actually take. The predicate goes
        // only because its twin is right there: the row still owes the whole answer.
        Assert.Null(predicates["canDevelop"]);
        var develop = result["row"]!["develop"]!;
        Assert.False((bool)develop["available"]!);
        Assert.Null(develop["reasonCode"]);
        Assert.NotNull(develop["reason"]);

        var cap = result["blockers"]!["cap"]!;
        Assert.True((bool)cap["blocked"]!);
        Assert.Null(cap["reasonCode"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)cap["reason"]));
        Assert.Equal(1, (int)cap["purchasedLevel"]!);
        Assert.Equal(1, (int)cap["baseLevelExcludingBonus"]!);
        Assert.Equal(0, (int)cap["bonusLevel"]!);
        Assert.Equal(1, (int)cap["totalLevel"]!);
        Assert.Equal(1, (int)cap["effectiveCap"]!);
        Assert.True((bool)cap["nativeComplete"]!);

        // Levels waiting are the research row's own number; their sum with the built ones is not.
        Assert.Equal(0, (int)cap["queuedLevels"]!);
        Assert.Null(cap["committedLevel"]);
    }

    /// <summary>
    /// A locked entity never reads <c>Met</c>. The lock is <c>UpgradeSO.prerequisites</c> and the
    /// rows are <c>prerequisitesPerLevel</c> — two different authored containers — so a block that
    /// published the per-level answer alone said "requirements met" of a thing the screen draws
    /// shut, three live rounds running. There is still no <c>authority</c> paragraph: it restated
    /// the verdict in prose, and eight of its nine appearances in a live round sat under <c>root:
    /// no conditions</c>, declaring a set of authored rows met that does not exist. This is the
    /// post-prestige shape: levels reset to nought, no authored condition published, and the game's
    /// own gate shut.
    /// </summary>
    [Fact]
    public void RequirementsNeverReadMetWhileTheGameHoldsTheEntityShut()
    {
        var id = Guid.Parse("34444444-4444-4444-8444-4444444444a1");
        var reading = new RawUpgradeSample(
            id, level: 0, maxLevel: -1, available: false, queuedLevels: 0,
            buildTime: BigDouble.Zero, developmentTime: 1d, cachedCostLevel: 0);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(in reading, false, false, 0, 0, false, 0d),
            }),
            CollectedAtEpoch = 78,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var result = Explain(world, id, 948);

        Assert.False((bool)result["predicates"]!["available"]!["available"]!);
        Assert.Null(result["predicates"]!["available"]!["reasonCode"]);
        var requirements = result["requirements"]!;
        Assert.Equal("Unmet", (string?)requirements["suiteVerdict"]);
        Assert.Null(requirements["reasonCode"]);

        // The sentence has to add what `predicates.available` does not already say, or it is the
        // restating paragraph coming back under a new name: which of the two authored lists answers
        // which question, now that the block carries both.
        Assert.Equal(
            "The game keeps this locked. What would unlock it is under \"unlocksWhen\"; the " +
            "requirements beside it are what its next level needs, which is a separate list.",
            (string?)requirements["reason"]);
        Assert.Null(requirements["authority"]);

        // One class per fact: the row keeps the fact and gives up its second opinion about it.
        // Prerequisites unmet and nothing bought is the first of the three lifecycle words, and
        // the entity-state block says it in the same word the page would.
        Assert.Equal("locked", (string?)result["row"]!["state"]);
        Assert.Null(result["state"]);
        Assert.Null(result["row"]!["available"]);
        Assert.Null(result["row"]!["reasonCode"]);
        Assert.Null(result["row"]!["reason"]);
    }

    /// <summary>
    /// The shape the game actually has, and the one three live rounds hit: an authored per-level
    /// requirement that is genuinely satisfied on an upgrade the game's own
    /// <c>prerequisites</c> container holds shut. The rows read <c>prerequisitesPerLevel</c>, and
    /// the lock is in a container they do not look at.
    /// </summary>
    [Fact]
    public void AnUpgradeWhoseNextLevelRowsAreMetStillReadsUnmetWhileItsOwnGateIsShut()
    {
        var owner = Upgrade();
        owner.available = false;
        var research = ResearchStub(level: 6);
        owner.prerequisitesPerLevel.prerequisites.Add(Require(research, 5));

        var requirements = Explain(Collect(), owner.GetGuid(), 949)["requirements"]!;

        // The rows themselves are not overwritten: the leaf that holds still says so, and the
        // block's verdict is the screen's rather than that leaf's.
        var leaf = Assert.Single(
            Assert.IsType<JObject>(requirements["root"])["children"]!.OfType<JObject>());
        Assert.True((bool)leaf["met"]!);
        Assert.Equal("Unmet", (string?)requirements["suiteVerdict"]);
        Assert.Null(requirements["reasonCode"]);

        // Nothing is unmet among the rows, so the array that answers "what is stopping this" is
        // still absent — the sentence is what points at the lock.
        Assert.Null(requirements["unmet"]);

        // Nothing was captured for the lock here, so the block says nothing about it rather than
        // publishing an empty node that would read as a lock with no conditions.
        Assert.Null(requirements["unlocksWhen"]);
    }

    /// <summary>
    /// A locked entity's availability verdict points at the unlock block standing beside it, and
    /// says "nothing to look up" only when there is nothing to look up.
    /// </summary>
    /// <remarks>
    /// Learn Expansion is the shape: `available: no` with "the game says nothing about what would
    /// unlock it" printed directly above `unlocksWhen: needs=Wizardry`. The verdict was hardcoded
    /// while the block beside it was read from the captured unlock container, so the one line a
    /// caller would act on contradicted the one line under it.
    /// </remarks>
    [Fact]
    public void ALockedUpgradesVerdictNamesItsUnlockBlockWhenThereIsOne()
    {
        var named = Upgrade();
        named.available = false;
        var shut = ResearchStub(level: 1);
        named.prerequisites.prerequisites.Add(Require(shut, 4));

        var detail = Explain(Collect(), named.GetGuid(), 951);

        var available = detail["predicates"]!["available"]!;
        Assert.False((bool)available["available"]!);
        Assert.Null(available["reasonCode"]);
        Assert.Contains("unlocksWhen", (string?)available["reason"]);
        Assert.NotNull(detail["requirements"]!["unlocksWhen"]);
    }

    /// <summary>
    /// Research answers the same way: Ascend Scholar printed "says nothing about what would unlock
    /// it" directly above its own <c>unlocksWhen</c> table, because the research verdict was still
    /// the hardcoded one the upgrade verdict had already given up.
    /// </summary>
    [Fact]
    public void A_locked_research_verdict_names_its_unlock_block_when_there_is_one()
    {
        var gate = ResearchStub(level: 1);
        var locked = ResearchStub(level: 0);
        locked.available = false;
        locked.visibilityPrerequisites.prerequisites.Add(Require(gate, 4));

        var detail = Explain(Collect(), locked.GetGuid(), 953);

        var available = detail["predicates"]!["available"]!;
        Assert.False((bool)available["available"]!);
        Assert.Contains("unlocksWhen", (string?)available["reason"]);
        Assert.DoesNotContain("says nothing", (string?)available["reason"]);
        Assert.NotNull(detail["requirements"]!["unlocksWhen"]);
    }

    /// <summary>
    /// A research the game would develop but does not show is one the Research grid has no tile
    /// for: Greater Stocked Casts read <c>develop: yes</c> beside <c>state: locked</c>, and the press
    /// queued it. Both reads now refuse it the way the press does, naming what it unlocks with.
    /// </summary>
    [Fact]
    public void A_queued_research_whose_price_is_met_blames_the_level_it_waits_on()
    {
        var gate = ResearchStub(level: 7);
        var super = ResearchStub(level: 0);
        super.levelPrerequisites.prerequisites.Add(Require(gate, 8));
        Assert.True(super.GetDevelopmentCost().HasEnough());
        Assert.False(super.MeetsLevelRequirements());
        global::SettingsManager.ResearchQueueMode = true;
        try
        {
            var develop = Explain(Collect(), super.GetGuid(), 955)["row"]!["develop"]!;
            Assert.False((bool)develop["available"]!);
            Assert.Null(develop["affordable"]);
            Assert.Equal("This does not meet its level requirements yet.", (string?)develop["reason"]);
        }
        finally
        {
            global::SettingsManager.ResearchQueueMode = false;
        }
    }

    [Fact]
    public void A_research_the_grid_does_not_show_cannot_be_developed_and_says_what_unlocks_it()
    {
        var gate = ResearchStub(level: 1);
        var locked = ResearchStub(level: 0);
        locked.available = false;
        locked.visibilityPrerequisites.prerequisites.Add(Require(gate, 4));
        Assert.True(locked.CanDevelop());

        var detail = Explain(Collect(), locked.GetGuid(), 954);

        var develop = detail["row"]!["develop"]!;
        Assert.False((bool)develop["available"]!);
        Assert.Equal(0, (int)develop["levels"]!);
        Assert.StartsWith(
            "This research is locked, so the Research grid draws no tile for it. It unlocks with ",
            (string?)develop["reason"]);
        Assert.Contains("at level 4 (at 1)", (string?)develop["reason"]);
        Assert.Null(detail["predicates"]!["canDevelop"]);
    }

    /// <summary>
    /// The old sentence is still the right one where the game really names no condition.
    /// </summary>
    [Fact]
    public void ALockedUpgradeWithNoUnlockConditionsStillSaysTheGameNamesNothing()
    {
        var silent = Upgrade();
        silent.available = false;

        var detail = Explain(Collect(), silent.GetGuid(), 952);

        var available = detail["predicates"]!["available"]!;
        Assert.False((bool)available["available"]!);
        Assert.Null(available["reasonCode"]);
        Assert.Contains("says nothing about what would unlock it", (string?)available["reason"]);
        Assert.Null(detail["requirements"]?["unlocksWhen"]);
    }

    /// <summary>
    /// A locked entity says what would unlock it, in the same words its other rows use.
    /// </summary>
    /// <remarks>
    /// The lock's conditions and the next level's are two authored lists, and until they were both
    /// captured the block could name the lock but not read it. They stay apart on the wire: the
    /// <c>unmet</c> summary documents itself as the next level's, so folding the lock's leaves into it
    /// would leave a caller unable to tell which list a line came from.
    /// </remarks>
    [Fact]
    public void ALockedUpgradeSaysWhichConditionsWouldUnlockIt()
    {
        var owner = Upgrade();
        owner.available = false;
        var opened = ResearchStub(level: 6);
        var shut = ResearchStub(level: 1);
        owner.prerequisitesPerLevel.prerequisites.Add(Require(opened, 5));
        owner.prerequisitesPerLevel.ParameterizedCheckResult = true;
        owner.prerequisites.prerequisites.Add(Require(shut, 4));

        var requirements = Explain(Collect(), owner.GetGuid(), 949)["requirements"]!;

        Assert.Equal("Unmet", (string?)requirements["suiteVerdict"]);
        Assert.Null(requirements["reasonCode"]);

        var lockNode = Assert.IsType<JObject>(requirements["unlocksWhen"]);
        Assert.Equal("AND", (string?)lockNode["operator"]);
        var leaf = Assert.Single(lockNode["children"]!.OfType<JObject>());

        // The leaf shape is every other requirement row's: what it needs, and whether it holds.
        Assert.False((bool)leaf["met"]!);
        Assert.Equal(new[] { "needs", "met", "requirement" }, leaf.Properties().Select(p => p.Name));
        Assert.Contains("4", (string?)leaf["needs"]);

        // The next level's rows are still the next level's, and still met.
        var nextLevel = Assert.Single(
            Assert.IsType<JObject>(requirements["root"])["children"]!.OfType<JObject>());
        Assert.True((bool)nextLevel["met"]!);

        // The lock's unmet leaf is not folded into the next level's summary.
        Assert.Null(requirements["unmet"]);
    }

    /// <summary>
    /// An entity the game is not holding shut shows nothing new. Its unlock container has latched, so
    /// the game would not walk those conditions again either.
    /// </summary>
    [Fact]
    public void AnUnlockedUpgradeCarriesNoLockExplanation()
    {
        var owner = Upgrade();
        owner.available = true;
        var shut = ResearchStub(level: 1);
        owner.prerequisites.prerequisites.Add(Require(shut, 4));

        var requirements = Explain(Collect(), owner.GetGuid(), 949)["requirements"]!;

        Assert.Null(requirements["unlocksWhen"]);
        Assert.Null(requirements["reasonCode"]);
        Assert.Equal("Met", (string?)requirements["suiteVerdict"]);
    }

    /// <summary>A structure's gate is the same container under a different predicate.</summary>
    [Fact]
    public void ALockedStructureSaysWhichConditionsWouldUnlockIt()
    {
        var owner = Structure();
        owner.available = false;
        var shut = ResearchStub(level: 1);
        owner.prerequisites.prerequisites.Add(Require(shut, 4));

        var requirements = Explain(Collect(), owner.GetGuid(), 949)["requirements"]!;

        Assert.Null(requirements["reasonCode"]);
        var leaf = Assert.Single(
            Assert.IsType<JObject>(requirements["unlocksWhen"])["children"]!.OfType<JObject>());
        Assert.False((bool)leaf["met"]!);
        Assert.Contains("4", (string?)leaf["needs"]);
    }

    /// <summary>
    /// A research authors two visibility containers the game ANDs, and the second one carries a
    /// threshold adjustment of its own. Both have to show, and the verdict has to be their AND.
    /// </summary>
    [Fact]
    public void AHiddenResearchShowsBothVisibilityContainersAsOneAnd()
    {
        var owner = ResearchStub(level: 0);
        owner.available = false;
        var shut = ResearchStub(level: 1);
        owner.visibilityPrerequisites.prerequisites.Add(Require(shut, 4));
        owner.LevelVisibilityPrereq.prerequisites.Add(Require(shut, 2));
        owner.LevelVisibilityPrereq.SetAdjustValue(new BigDouble(-3d));

        var requirements = Explain(Collect(), owner.GetGuid(), 949)["requirements"]!;

        var children = Assert.IsType<JObject>(requirements["unlocksWhen"])["children"]!
            .OfType<JObject>()
            .ToArray();
        Assert.Equal(2, children.Length);

        // The first container is unmet at 4; the second asks for 2 shifted by -3, which holds.
        Assert.False((bool)children[0]["met"]!);
        Assert.True((bool)children[1]["met"]!);
    }

    /// <summary>
    /// A resource the game counts says so on its row, and has no lock to explain.
    /// </summary>
    [Fact]
    public void AResourceTheGameCountsSaysSoAndExplainsNoLock()
    {
        var resource = Resource(inLedger: true);

        var detail = Explain(Collect(), resource.GetGuid(), 949);

        Assert.True((bool)detail["row"]!["inLedger"]!["available"]!);
        Assert.Null(detail["row"]!["inLedger"]!["reasonCode"]);
        Assert.Null(detail["requirements"]);
    }

    /// <summary>
    /// A resource the game has not counted yet says what would add it, read off the container
    /// <c>ResourceSO.CheckVisibility()</c> consults.
    /// </summary>
    /// <remarks>
    /// The bit used to answer "The game is not showing this yet" — which the Scholar page
    /// contradicted while drawing a Scholar Xp bar of its own — and nothing on the wire named the
    /// gate, because <c>visiblePrerequisites</c> was captured nowhere.
    /// </remarks>
    [Fact]
    public void AResourceTheGameHasNotCountedSaysWhatWouldAddIt()
    {
        var resource = Resource(inLedger: false);
        var shut = ResearchStub(level: 1);
        resource.visiblePrerequisites.prerequisites.Add(Require(shut, 4));

        var detail = Explain(Collect(), resource.GetGuid(), 949);

        var ledger = detail["row"]!["inLedger"]!;
        Assert.False((bool)ledger["available"]!);
        Assert.Null(ledger["reasonCode"]);
        Assert.Equal(
            "the game has not counted it yet; world_get prints what would add it under " +
            "\"unlocksWhen\"",
            (string?)ledger["reason"]);

        var unlocks = Assert.IsType<JObject>(detail["requirements"]!["unlocksWhen"]);
        Assert.Equal("AND", (string?)unlocks["operator"]);
        var leaf = Assert.Single(unlocks["children"]!.OfType<JObject>());
        Assert.False((bool)leaf["met"]!);
        Assert.Contains("4", (string?)leaf["needs"]);
    }

    /// <summary>
    /// The ledger bit is not an availability verdict, and no longer answers as one.
    /// </summary>
    /// <remarks>
    /// One bit used to be published twice — as <c>visible</c> and as a copy of it under
    /// <c>available</c> — so Scholar Xp, with a computed capacity and a bar on the open screen, read
    /// as locked and unavailable, and Psi read the same on the line under the price quoted in it.
    /// </remarks>
    [Fact]
    public void AResourcesLedgerBitIsNotCopiedOntoAnAvailabilityVerdict()
    {
        var counted = Resource(inLedger: true);
        var uncounted = Resource(inLedger: false);

        var world = Collect();
        var countedDetail = Explain(world, counted.GetGuid(), 949);
        var uncountedDetail = Explain(world, uncounted.GetGuid(), 950);

        Assert.Null(countedDetail["predicates"]?["available"]);
        Assert.Null(countedDetail["predicates"]?["visible"]);
        Assert.Null(uncountedDetail["predicates"]?["available"]);
        Assert.Null(uncountedDetail["predicates"]?["visible"]);

        // The bit still reaches a caller, once, under the name that says what it is.
        Assert.True((bool)countedDetail["row"]!["inLedger"]!["available"]!);
        Assert.False((bool)uncountedDetail["row"]!["inLedger"]!["available"]!);
    }
}
