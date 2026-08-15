using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpEntityDetailTests : IDisposable
{
    private static readonly Guid ReadySpellId =
        Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid WaitingSpellId =
        Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid ReadyResearchId =
        Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid BlockedResearchId =
        Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid ReadyCraftingId =
        Guid.Parse("30000000-0000-4000-8000-000000000001");
    private static readonly Guid BlockedCraftingId =
        Guid.Parse("30000000-0000-4000-8000-000000000002");

    public GameMcpEntityDetailTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void ExplanationStaysPinnedAndEveryPlayerPredicateHasTrueAndFalseEvidence()
    {
        var original = new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                Spell(ReadySpellId, discovered: true, hidden: false, masteryLevel: 3),
                Spell(WaitingSpellId, discovered: false, hidden: false, masteryLevel: 1),
            }),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                new WorldSpellSlot(
                    slotIndex: 0,
                    ReadySpellId,
                    occupied: true,
                    casting: false,
                    readyingCast: false,
                    attuning: false,
                    channeled: false,
                    toggled: false,
                    chargeable: true,
                    castReady: true,
                    chargeAvailable: true,
                    resourcesCovered: true,
                    currentCharges: 1,
                    maximumCharges: 1,
                    cooldownRemaining: BigDouble.Zero),
            }),
            Research = PublicationTable<WorldResearch>.Create(new[]
            {
                Research(ReadyResearchId, available: true, level: 6, maxLevel: 20,
                    baseRequirement: 5, effectiveRequirement: 5, leeway: 0),
                Research(BlockedResearchId, available: false, level: 1, maxLevel: 20,
                    baseRequirement: 5, effectiveRequirement: 5, leeway: 0),
            }),
            CraftingRecipes = PublicationTable<WorldCraftingRecipe>.Create(new[]
            {
                Crafting(ReadyCraftingId, visible: true, canBuy: true),
                Crafting(BlockedCraftingId, visible: false, canBuy: false),
            }),
            CollectedAtEpoch = 31,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(original, new WorldGeneration(909));
        var pinned = Snapshot(publisher.ReadLatest());
        publisher.Publish(new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                Spell(ReadySpellId, discovered: false, hidden: true, masteryLevel: 99),
            }),
            CollectedAtEpoch = 32,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        }, new WorldGeneration(910));

        var readySpell = GameMcpTestHarness.Detail(pinned, ReadySpellId);
        var waitingSpell = GameMcpTestHarness.Detail(pinned, WaitingSpellId);
        var readyResearch = GameMcpTestHarness.Detail(pinned, ReadyResearchId);
        var blockedResearch = GameMcpTestHarness.Detail(pinned, BlockedResearchId);
        var readyCrafting = GameMcpTestHarness.Detail(pinned, ReadyCraftingId);
        var blockedCrafting = GameMcpTestHarness.Detail(pinned, BlockedCraftingId);

        Assert.Null(readySpell["worldGeneration"]);
        Assert.Null(readySpell["lifecycleGeneration"]);
        Assert.Equal(3, (int)readySpell["row"]!["masteryLevel"]!);
        // A predicate that holds is published holding. Dropping the passing ones made absence mean
        // "true" on one key and "this entity has no such predicate" on the next.
        Assert.True(Predicate(readySpell, "visible"));
        Assert.True(Predicate(readySpell, "available"));
        Assert.False(Predicate(readySpell, "canDiscover"));
        Assert.True(Predicate(readySpell, "canUse"));
        Assert.True(Predicate(waitingSpell, "canDiscover"));
        Assert.False(Predicate(waitingSpell, "canUse"));
        Assert.True(Predicate(readyResearch, "available"));
        Assert.False(Predicate(blockedResearch, "available"));
        Assert.False(Predicate(blockedResearch, "canDevelop"));
        Assert.True(Predicate(readyCrafting, "visible"));
        Assert.False(Predicate(blockedCrafting, "visible"));
        Assert.False(Predicate(blockedCrafting, "available"));
        Assert.False(Predicate(blockedCrafting, "canPurchase"));
        Assert.Null(readySpell["predicates"]!["canDevelop"]);
        Assert.Null(readySpell["predicates"]!["canPurchase"]);
        Assert.Null(readySpell["requirements"]);
        Assert.Null(readySpell["purchase"]);

        foreach (var explanation in new[]
                 {
                     readySpell, waitingSpell, readyResearch, blockedResearch,
                     readyCrafting, blockedCrafting,
                 })
        {
            if (explanation["predicates"] is not JObject predicateObject) continue;
            foreach (var predicate in predicateObject.Properties())
            {
                var value = Assert.IsType<JObject>(predicate.Value);
                if (!(bool)value["available"]!)
                    Assert.False(string.IsNullOrWhiteSpace((string?)value["reasonCode"]));
            }
        }
    }

    /// <summary>
    /// The slots inside <c>canUse</c> are slot numbers — the one-based number every verb takes and
    /// the pointer into the <c>equipped</c> block the same response already carries in full. They
    /// used to be whole slot rows, so a predicate answered by echoing the block above it.
    /// </summary>
    [Fact]
    public void A_usable_spell_names_its_slot_the_way_every_verb_addresses_it()
    {
        var world = new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                Spell(ReadySpellId, discovered: true, hidden: false, masteryLevel: 3),
            }),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                new WorldSpellSlot(
                    slotIndex: 6,
                    ReadySpellId,
                    occupied: true,
                    casting: false,
                    readyingCast: false,
                    attuning: false,
                    channeled: false,
                    toggled: false,
                    chargeable: true,
                    castReady: true,
                    chargeAvailable: true,
                    resourcesCovered: true,
                    currentCharges: 1,
                    maximumCharges: 1,
                    cooldownRemaining: BigDouble.Zero),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var explanation = Explain(world, ReadySpellId, generation: 941);

        var slots = Assert.IsType<JArray>(explanation["predicates"]!["canUse"]!["slots"]);
        Assert.Equal(7, (int)Assert.Single(slots)!);
    }

    /// <summary>
    /// The game develops research on leeway **or** on being below both caps, so a spent leeway
    /// under open caps blocks nothing. `blocked` was computed from the whole gate while the reason
    /// was picked off the leeway term alone, and a live round read `blocked: no` sitting beside
    /// "Native leeway exhausted." — the block contradicting itself in two adjacent fields. Three
    /// states, three words, and the middle one passes — and a passing axis is not what `blockers`
    /// is for: the block lists what blocks, so an axis with nothing to report is not listed.
    /// </summary>
    [Fact]
    public void A_spent_leeway_under_open_caps_is_not_among_what_blocks()
    {
        var world = new GameWorldState
        {
            Research = PublicationTable<WorldResearch>.Create(new[]
            {
                Research(
                    ReadyResearchId, available: true, level: 4, maxLevel: 20,
                    baseRequirement: 5, effectiveRequirement: 5, leeway: 0,
                    stillHasLeeway: false),
            }),
            CollectedAtEpoch = 43,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var blockers = Assert.IsType<JObject>(
            Explain(world, ReadyResearchId, generation: 943)["blockers"]);

        // The leeway axis had nothing left but `blocked: no`, so it is gone. The cap axis stays
        // even though it is open, because it carries readings of its own — a drop is per field and
        // only where the field is a restatement.
        Assert.Null(blockers["leeway"]);
        var cap = Assert.IsType<JObject>(blockers["cap"]);
        Assert.False((bool)cap["blocked"]!);
        Assert.Equal(4, (int)cap["baseLevelExcludingBonus"]!);
        Assert.Equal(20, (int)cap["effectiveCap"]!);
        Assert.False((bool)cap["nativeComplete"]!);
        Assert.True(GameMcpDecisionReason.IsPassing("native_develops_below_caps"));
    }

    /// <summary>
    /// The other side of the same rule: an axis that really refuses prints in full, because the
    /// numbers behind a no are what a caller acts on. They ride only here — an open axis restating
    /// `researchThresholds` one block down was spending them on a reader who already had them.
    /// </summary>
    [Fact]
    public void A_leeway_that_blocks_prints_in_full_with_the_numbers_behind_it()
    {
        var world = new GameWorldState
        {
            Research = PublicationTable<WorldResearch>.Create(new[]
            {
                Research(
                    ReadyResearchId, available: true, level: 20, maxLevel: 20,
                    baseRequirement: 5, effectiveRequirement: 5, leeway: 0,
                    stillHasLeeway: false),
            }),
            CollectedAtEpoch = 44,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var leeway = Assert.IsType<JObject>(
            Explain(world, ReadyResearchId, generation: 944)["blockers"]!["leeway"]);

        Assert.True((bool)leeway["blocked"]!);
        Assert.Equal("ERR_LOCKED", (string?)leeway["reasonCode"]);
        Assert.NotNull(leeway["reason"]);
        Assert.Equal(20, (int)leeway["currentTotalLevel"]!);
        Assert.Equal(0, (int)leeway["leeway"]!);
        Assert.Equal(5, (int)leeway["effectiveRequirement"]!);
        Assert.True((bool)leeway["nativeMeetsLevelRequirements"]!);
    }

    /// <summary>
    /// A spell in no slot used to refuse with the artifact loadout's sentence — "None of this
    /// artifact is equipped." on a spell recipe, read straight off the wire in a live round. Two
    /// different things are equipped in two different places, so the sentence names which.
    /// </summary>
    [Fact]
    public void An_unequipped_spell_refuses_in_spell_words_not_artifact_words()
    {
        var world = new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                Spell(ReadySpellId, discovered: true, hidden: false, masteryLevel: 3),
            }),
            CollectedAtEpoch = 42,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var canUse = Assert.IsType<JObject>(
            Explain(world, ReadySpellId, generation: 942)["predicates"]!["canUse"]);

        Assert.False((bool)canUse["available"]!);
        Assert.Equal("ERR_NOT_FOUND", (string?)canUse["reasonCode"]);
        Assert.Equal("This spell is not in any spell slot.", (string?)canUse["reason"]);
    }

    /// <summary>
    /// The per-field half of the restatement rule, on one page: a predicate whose row twin is not
    /// published still prints, and a predicate carrying a fact of its own still prints whole. Only
    /// the predicate that repeats its twin word for word goes, and only while the twin is there.
    /// </summary>
    [Fact]
    public void A_predicate_stays_wherever_the_row_beside_it_does_not_already_say_it()
    {
        var world = new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                Spell(ReadySpellId, discovered: true, hidden: false, masteryLevel: 3),
            }),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                new WorldSpellSlot(
                    slotIndex: 6,
                    ReadySpellId,
                    occupied: true,
                    casting: false,
                    readyingCast: false,
                    attuning: false,
                    channeled: false,
                    toggled: false,
                    chargeable: true,
                    castReady: true,
                    chargeAvailable: true,
                    resourcesCovered: true,
                    currentCharges: 1,
                    maximumCharges: 1,
                    cooldownRemaining: BigDouble.Zero),
            }),
            CollectedAtEpoch = 45,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var result = Explain(world, ReadySpellId, generation: 945);
        var predicates = Assert.IsType<JObject>(result["predicates"]);
        var row = Assert.IsType<JObject>(result["row"]);

        // A discovered spell's row offers `loadoutAdd`, never `discover`, so the discovery
        // predicate has no twin to be a second copy of and owes the answer itself.
        Assert.Null(row["discover"]);
        Assert.False((bool)predicates["canDiscover"]!["available"]!);
        Assert.NotNull(predicates["canDiscover"]!["reason"]);

        // `canUse` carries the slot list, which appears nowhere else on the page, so it stays
        // whole even where the row would otherwise cover its verdict.
        var canUse = Assert.IsType<JObject>(predicates["canUse"]);
        Assert.True((bool)canUse["available"]!);
        Assert.Equal(7, (int)Assert.Single(Assert.IsType<JArray>(canUse["slots"]))!);
    }

    /// <summary>
    /// Where a spell can move is the slot list, and the slot list is one read for the whole bar.
    /// Inlined per spell, explaining eight spells delivered the same roster eight times.
    /// </summary>
    [Fact]
    public void A_movable_spell_says_it_can_move_and_never_inlines_the_slot_roster()
    {
        var world = new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[]
            {
                Spell(ReadySpellId, discovered: true, hidden: false, masteryLevel: 3),
            }),
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                new WorldSpellSlot(
                    slotIndex: 0,
                    ReadySpellId,
                    occupied: true,
                    casting: false,
                    readyingCast: false,
                    attuning: false,
                    channeled: false,
                    toggled: false,
                    chargeable: true,
                    castReady: true,
                    chargeAvailable: true,
                    resourcesCovered: true,
                    currentCharges: 1,
                    maximumCharges: 1,
                    cooldownRemaining: BigDouble.Zero),
                new WorldSpellSlot(
                    slotIndex: 1,
                    Guid.Empty,
                    occupied: false,
                    casting: false,
                    readyingCast: false,
                    attuning: false,
                    channeled: false,
                    toggled: false,
                    chargeable: false,
                    castReady: false,
                    chargeAvailable: false,
                    resourcesCovered: false,
                    currentCharges: 0,
                    maximumCharges: 0,
                    cooldownRemaining: BigDouble.Zero),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var explanation = Explain(world, ReadySpellId, generation: 942);
        var equipped = Assert.Single(
            Assert.IsType<JArray>(explanation["row"]!["equipped"]).Values<JObject>())!;

        Assert.True((bool)equipped["move"]!["available"]!);
        Assert.Null(equipped["move"]!["destinations"]);
    }

    [Fact]
    public void ExplanationSeparatesUnknownFromKnownButUnprojectedIdentity()
    {
        var known = Guid.Parse("b4505524-ad2f-4a5a-9d28-df0c30937748");
        var unknown = Guid.Parse("00000000-0000-4000-8000-000000000099");
        var world = new GameWorldState
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var context = GameMcpTestHarness.Context(world, generation: 911);

        var knownResult = GameMcpTestHarness.Detail(context, known);
        var unknownResult = GameMcpTestHarness.Detail(context, unknown);

        Assert.Equal("ERR_NOT_FOUND", (string?)knownResult["reasonCode"]);
        // The catalog knows this id and the game authors no word for it, so the block identifies it
        // by the only label there is and does not dress that label up as a name.
        Assert.Null(knownResult["name"]);
        Assert.Equal("InventoryUnlocked", (string?)knownResult["internalName"]);
        Assert.Equal("entity_catalog", (string?)knownResult["readWith"]!["tool"]);
        Assert.Null(knownResult["nameEvidence"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)unknownResult["reasonCode"]);
        // The name is the missing thing, so a name search is the one remedy that cannot work.
        Assert.Equal("world_categories", (string?)unknownResult["readWith"]!["tool"]);
        Assert.DoesNotContain("entity_catalog", (string?)unknownResult["reason"]);
        // Nothing carries this id, so the block hands it back whole and names nothing. It used to
        // shorten the id to a handle — an address into a published set this id is not in — and add
        // `name: (unnamed 000000)`, which reads as a row whose name went missing rather than as an
        // id with no row at all.
        Assert.Equal(unknown.ToString("D"), (string?)unknownResult["uuid"]);
        Assert.Null(unknownResult["name"]);
        Assert.Null(unknownResult["nameEvidence"]);
        // The known-but-unprojected block is the other case and keeps both: the catalog really does
        // hold that name, and the id really is one this build published.
        Assert.Equal(known.ToString("D").Substring(0, 6), (string?)knownResult["uuid"]);
    }

    /// <remarks>
    /// An equipped spell instance is a runtime object, not a loaded asset, so the asset catalog
    /// does not know it — and the explanation answered "nothing in this process knows this UUID"
    /// and pointed at that same catalog. The world had published the UUID inside a spell-slot row,
    /// so both the claim and the remedy were wrong.
    /// </remarks>
    [Fact]
    public void A_runtime_member_of_a_published_row_is_answered_with_the_row_that_owns_it()
    {
        var instance = Guid.Parse("4e551da3-262b-4bee-9f92-904cb81bf25a");
        var world = new GameWorldState
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
            {
                new WorldSpellSlot(
                    0, instance, Guid.Empty, true, false, false, false, false,
                    false, false, false, false, false, 0, 0, BigDouble.Zero),
            }),
        };

        var result = GameMcpTestHarness.Detail(
            GameMcpTestHarness.Context(world, generation: 913),
            instance);

        Assert.Equal("ERR_NOT_FOUND", (string?)result["reasonCode"]);
        Assert.Equal("world_list", (string?)result["readWith"]!["tool"]);
        Assert.Equal("spell-slots", (string?)result["readWith"]!["category"]);
    }

    [Fact]
    public void RequirementsExpandOrderedLinkTiersAndPreserveAndOrGroups()
    {
        var owner = Upgrade();
        var research = ResearchStub(level: 0);
        var either = new Requirements.OrRequirement();
        either.orConditions.Add(Require(research, 5));
        var all = new Requirements.AndRequirement();
        all.andConditions.Add(Require(research, 6));
        either.orConditions.Add(all);
        owner.prerequisitesPerLevel.prerequisites.Add(either);

        var link = new global::PrerequisiteLinkSO();
        global::PrerequisiteLinkSO.All.Add(link);
        link.linkTiers.Add(Tier(Require(research, 1)));
        link.linkTiers.Add(Tier(Require(research, 2)));
        owner.prerequisitesPerLevel.prerequisites.Add(
            new Requirements.PrerequisiteLinkRequirement
            {
                item = link,
                reqType = Requirements.PrerequisiteLinkType.Tier,
                value = new Requirements.LeveledValue { baseValue = 1d },
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
        Assert.Equal(
            "AndRequirement",
            (string?)orChildren[1]["diagnostics"]!["conditionType"]);
        Assert.Equal("Unevaluable", (string?)orChildren[1]["verdict"]);

        // The player's four facts lead the leaf; the authored-tree and native-class evidence sits
        // under `diagnostics`, where a reader knows it is here to diagnose the suite rather than to
        // be acted on.
        var firstLeaf = orChildren[0];
        Assert.Equal(GameMcpTestHarness.Handle(research.GetGuid()),
            (string?)firstLeaf["requirement"]!["uuid"]);
        Assert.Equal(
            "ResearchSO",
            (string?)firstLeaf["diagnostics"]!["requirementNativeType"]);
        Assert.Equal("total_level", (string?)firstLeaf["diagnostics"]!["selectedValueKind"]);
        Assert.NotNull(firstLeaf["current"]);
        Assert.NotNull(firstLeaf["required"]);
        Assert.False((bool)firstLeaf["met"]!);
        Assert.Equal(
            new[] { "met", "checks", "current", "required", "verdict", "reasonCode",
                "reason", "diagnostics", "requirement" },
            firstLeaf.Children<Newtonsoft.Json.Linq.JProperty>()
                .Select(property => property.Name)
                .ToArray());

        var tiers = top[1]["prerequisiteLinkTiers"]!.OfType<JObject>().ToArray();
        Assert.Equal(new[] { 0, 1 }, tiers.Select(tier => (int)tier["tierIndex"]!).ToArray());
        Assert.False((bool)tiers[0]["selected"]!);
        Assert.True((bool)tiers[1]["selected"]!);
        Assert.All(tiers, tier =>
        {
            var tierRequirements = Assert.IsType<JObject>(tier["requirements"]);
            Assert.Equal("AND", (string?)tierRequirements["operator"]);
        });
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
        Assert.True(responseBytes < 2_409, "explanation was " + responseBytes + " bytes");

        // A block that answered carries no verdict line. Inside a batch that silence is what
        // separates it from the block beside it that refused.
        Assert.Null(result["status"]);
        Assert.Null(result["reasonCode"]);
        Assert.NotNull(result["row"]);
        var requirements = Assert.IsType<JObject>(result["requirements"]);
        Assert.Null(requirements["applicable"]);
        Assert.Equal(1, (long)requirements["checkLevel"]!);
        var root = Assert.IsType<JObject>(requirements["root"]);
        var orGroup = Assert.Single(root["children"]!.OfType<JObject>());
        Assert.Equal("OR", (string?)orGroup["operator"]);
        var leaves = orGroup["children"]!.OfType<JObject>().ToArray();
        Assert.Equal(2, leaves.Length);
        Assert.Equal(GameMcpTestHarness.Handle(wizardryId), (string?)leaves[0]["requirement"]!["uuid"]);
        Assert.Equal("total_level", (string?)leaves[0]["diagnostics"]!["selectedValueKind"]);
        Assert.Equal("5", (string?)leaves[0]["current"]);
        Assert.Equal("5", (string?)leaves[0]["required"]);
        Assert.True((bool)leaves[0]["met"]!);
        Assert.Equal(GameMcpTestHarness.Handle(expansionId), (string?)leaves[1]["requirement"]!["uuid"]);
        Assert.Equal("0", (string?)leaves[1]["current"]);
        Assert.Equal("15", (string?)leaves[1]["required"]);
        Assert.False((bool)leaves[1]["met"]!);
        Assert.Null(requirements["nativeParity"]);

        // A met leaf carries no class and no sentence. It used to carry both — every satisfied
        // condition in the tool's main content read `met: yes … ERR_REFUSED`, which is a caller
        // branching on "has a class means it was refused" being told a finished entity is blocked.
        Assert.Null(leaves[0]["reasonCode"]);
        Assert.Null(leaves[0]["reason"]);
        Assert.Equal("ERR_LOCKED", (string?)leaves[1]["reasonCode"]);
        Assert.NotNull(leaves[1]["reason"]);

        var predicates = result["predicates"]!;
        Assert.False((bool)predicates["available"]!["available"]!);
        Assert.Equal("ERR_STATE", (string?)predicates["available"]!["reasonCode"]);

        // `canDevelop` and the row's own `develop` said the identical verdict, code and sentence,
        // so the page says it once — on the action a caller can actually take. The predicate goes
        // only because its twin is right there: the row still owes the whole answer.
        Assert.Null(predicates["canDevelop"]);
        var develop = result["row"]!["develop"]!;
        Assert.False((bool)develop["available"]!);
        Assert.Equal("ERR_STATE", (string?)develop["reasonCode"]);
        Assert.NotNull(develop["reason"]);

        var cap = result["blockers"]!["cap"]!;
        Assert.True((bool)cap["blocked"]!);
        Assert.Equal("ERR_STATE", (string?)cap["reasonCode"]);
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

    [Fact]
    public void AuthoredTooltipDescriptionLeadsDiscoverableExplanationWhenNativelyAvailable()
    {
        var glyphId = Guid.Parse("168e3734-1ecb-4938-bd4a-d011ff13e201");
        var native = new global::GlyphSO
        {
            DisplayName = "Weak",
            Description = "Reduces the strength of a spell glyph effect.",
        };
        native.SetGuid(glyphId);
        global::IdScriptableObject.RuntimeLookup[glyphId] = native;
        var world = new GameWorldState
        {
            Glyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            CollectedAtEpoch = 38,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var result = Explain(world, glyphId, 924);

        Assert.Equal("Weak", (string?)result["name"]);
        Assert.Equal(
            "Reduces the strength of a spell glyph effect.",
            (string?)result["description"]);
        Assert.True(
            result.Properties().TakeWhile(property => property.Name != "description")
                .All(property => property.Name is "worldGeneration" or "status" or "uuid" or
                    "name" or "category" or "nativeType"));
        // Every predicate that applies ships, passing or not. Asserting only that each carries a
        // boolean would hold just as well if the passing ones went back to being stripped, so the
        // one that passes here is named: an undiscovered discoverable glyph can be discovered.
        var predicates = Assert.IsType<JObject>(result["predicates"]);
        Assert.All(
            predicates.Properties(),
            predicate => Assert.Equal(
                JTokenType.Boolean, predicate.Value["available"]?.Type));
        Assert.True((bool)predicates["canDiscover"]!["available"]!);
        Assert.False((bool)predicates["visible"]!["available"]!);
    }

    [Fact]
    public void UnlimitedAuthoredResearchRetainsLeewayRefusalAndIndependentArtificialCap()
    {
        var id = Guid.Parse("21628be0-4377-4b13-b28c-171ab29324c0");
        var research = new WorldResearch(
            id,
            level: 1,
            queuedLevels: 0,
            researchStage: 0,
            selfBonusLevels: 0,
            maxLevel: -1,
            researchTime: 60,
            isDeveloping: false,
            isActive: false,
            flagged: false,
            available: true,
            visible: true,
            complete: false,
            canDevelop: false,
            withinDevelopRange: false,
            meetsLevelRequirements: true,
            stillHasLeeway: false,
            belowArtificialMaxLevel: false,
            belowMaxInvestmentLevel: true,
            purchasedLevels: 1,
            baseLevel: 1,
            bonusLevel: 0,
            totalLevel: 1,
            artificialMaxLevel: 1,
            hiddenLevel: false,
            levelVisibilityRange: 2,
            requiredStagesCached: 0,
            requiredTimeCached: BigDouble.Zero,
            baseRequirementLevel: 1,
            effectiveRequirementLevel: 1,
            requirementAdjustments: PublicationTable<WorldResearchRequirementAdjustment>.Empty,
            modifiers: new RawResearchModifiers(
                BigDouble.Zero, BigDouble.Zero, new BigDouble(100d),
                new BigDouble(1d), BigDouble.Zero));
        var world = new GameWorldState
        {
            Research = PublicationTable<WorldResearch>.Create(new[] { research }),
            CollectedAtEpoch = 77,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var result = Explain(world, id, 947);

        Assert.Equal("unavailable", (string?)result["status"]);

        // An artificial cap refuses the next develop without finishing anything, so the lifecycle
        // is untouched by it: the row is still available, and the refusal lives on the can-develop
        // axis where it belongs.
        Assert.Equal("available", (string?)result["row"]!["state"]);
        Assert.Null(result["state"]);
        Assert.Null(result["row"]!["complete"]);
        Assert.False((bool)result["predicates"]!["canDevelop"]!["available"]!);
        Assert.Equal("ERR_LIMIT", (string?)result["predicates"]!["canDevelop"]!["reasonCode"]);
        var cap = result["blockers"]!["cap"]!;
        Assert.Equal(1, (int)cap["artificialCap"]!);
        Assert.Null(cap["effectiveCap"]);
        Assert.False((bool)cap["nativeComplete"]!);
    }

    [Fact]
    public void NativeRequirementDisagreementFailsLoudWithBothVerdicts()
    {
        var owner = Upgrade();
        var research = ResearchStub(level: 6);
        owner.prerequisitesPerLevel.prerequisites.Add(Require(research, 6));

        var collected = Collect();
        var result = Explain(collected, owner.GetGuid(), 921);

        Assert.Equal("unavailable", (string?)result["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)result["reasonCode"]);
        var parity = result["requirements"]!["nativeParity"]!;
        Assert.Equal("Met", (string?)parity["suiteVerdict"]);
        Assert.Equal("Unmet", (string?)parity["nativeVerdict"]);

        // One condition, one name: the parity block and the envelope answered the same failure
        // with two different codes, and the sentence was computed for the envelope only.
        Assert.Equal("ERR_UNAVAILABLE", (string?)parity["reasonCode"]);
        Assert.Equal(
            "The suite reads this requirement as Met where the game reads it as Unmet.",
            (string?)parity["reason"]);
        Assert.Equal((string?)result["reason"], (string?)parity["reason"]);

        // Requirements and availability are different questions, and this fixture answers them
        // differently: the game sells the upgrade while its prerequisite verdict reads Unmet. No
        // judgement is being overruled, so nothing names one.
        Assert.Null(result["requirements"]!["authority"]);
    }

    /// <summary>
    /// The gap between "the authored rows are met" and "the game still refuses" is named by the
    /// two fields that already carry it: <c>suiteVerdict</c>, scoped to the rows it read, and
    /// <c>predicates.available</c> with the game's own no. The <c>authority</c> paragraph that used
    /// to sit between them said the first of those again in prose, and a live round emitted it nine
    /// times byte-identical — eight of them on entities whose <c>root</c> is "no conditions", where
    /// it declared a set of authored rows met that does not exist. This is the post-prestige shape:
    /// levels reset to nought, no authored condition published, and the game's own gate shut.
    /// </summary>
    [Fact]
    public void RequirementsMetOnAnEntityTheGameHoldsShutCarryNoRestatingParagraph()
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
        Assert.Equal("ERR_LOCKED", (string?)result["predicates"]!["available"]!["reasonCode"]);
        Assert.Equal("Met", (string?)result["requirements"]!["suiteVerdict"]);
        Assert.Null(result["requirements"]!["authority"]);

        // One class per fact: the row keeps the fact and gives up its second opinion about it.
        // Prerequisites unmet and nothing bought is the first of the three lifecycle words, and
        // the entity-state block says it in the same word the page would.
        Assert.Equal("locked", (string?)result["row"]!["state"]);
        Assert.Null(result["state"]);
        Assert.Null(result["row"]!["available"]);
        Assert.Null(result["row"]!["reasonCode"]);
        Assert.Null(result["row"]!["reason"]);
    }

    [Fact]
    public void ThresholdCostAndTypedBlockersCarryCompleteEvidence()
    {
        var upgradeId = Guid.Parse("40000000-0000-4000-8000-000000000001");
        var resourceId = Guid.Parse("40000000-0000-4000-8000-000000000002");
        var modifierId = Guid.Parse("40000000-0000-4000-8000-000000000003");
        var challengeId = Guid.Parse("50000000-0000-4000-8000-000000000001");
        var adjustmentId = Guid.Parse("50000000-0000-4000-8000-000000000002");
        var challengeAdjustment = new WorldResearchRequirementAdjustment(
            adjustmentId,
            challengeId,
            "ChallengeSO",
            modifierType: 0,
            amount: new BigDouble(-5d),
            order: 0,
            passive: true);
        var rawUpgrade = new RawUpgradeSample(
            upgradeId,
            level: 1,
            maxLevel: 1,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1,
            cachedCostLevel: 1);
        var upgrade = new WorldUpgrade(
            in rawUpgrade,
            isBounded: true,
            isExhausted: true,
            remainingLevels: 0,
            committedLevel: 1,
            isDeveloping: false,
            developmentProgress: 0);
        var cost = new WorldPurchaseCost(
            upgradeId,
            resourceId,
            baseExactAmount: new BigDouble(100d),
            effectiveExactAmount: new BigDouble(250d),
            exactGroupedLevels: 1,
            exactGroupedAmount: new BigDouble(250d),
            modifierSources: PublicationTable<WorldPurchaseCostModifierSource>.Create(new[]
            {
                new WorldPurchaseCostModifierSource(
                    "upgrade.cost_modifier",
                    modifierId,
                    "ValueModifierVariable",
                    "effective cost percent",
                    new BigDouble(150d),
                    hasModifierType: true,
                    modifierType: 3),
            }),
            affordabilityEvaluated: true,
            availableAmount: new BigDouble(200d),
            combinedEffectiveAmount: new BigDouble(250d),
            resourceAffordable: false,
            resourceAffordabilityReasonCode: "insufficient_resource",
            affordable: false,
            affordabilityReasonCode: "unaffordable");
        var research = Research(
            ReadyResearchId,
            available: true,
            level: 4,
            maxLevel: 4,
            baseRequirement: 10,
            effectiveRequirement: 5,
            leeway: 1,
            adjustments: PublicationTable<WorldResearchRequirementAdjustment>.Create(
                new[] { challengeAdjustment }));
        var crafting = Crafting(
            ReadyCraftingId,
            visible: true,
            canBuy: false,
            resources: PublicationTable<WorldCraftingRecipeResource>.Create(new[]
            {
                new WorldCraftingRecipeResource(
                    ReadyCraftingId,
                    WorldCraftingRecipeResourceKind.AuthoredInput,
                    resourceId,
                    new BigDouble(25d),
                    resourceStateAvailable: true,
                    visible: true,
                    bandwidthResource: true,
                    trueQuantity: new BigDouble(100d),
                    isCapped: true,
                    capacity: new BigDouble(100d),
                    usage: new BigDouble(90d),
                    drain: new BigDouble(2d)),
            }),
            drains: PublicationTable<WorldCraftingRecipeDrainBlock>.Create(new[]
            {
                new WorldCraftingRecipeDrainBlock(
                    ReadyCraftingId, blockIndex: 0, necessaryRatio: new BigDouble(0.5d)),
            }));
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[] { upgrade }),
            Research = PublicationTable<WorldResearch>.Create(new[] { research }),
            CraftingRecipes = PublicationTable<WorldCraftingRecipe>.Create(new[] { crafting }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                GameMcpTestHarness.BandwidthResource(
                    resourceId, new BigDouble(90), new BigDouble(100)),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[] { cost }),
            ActionQueues = PublicationTable<WorldActionQueue>.Create(new[]
            {
                new WorldActionQueue(
                    KnownEntities.ActiveActionables.Uuid,
                    Guid.Empty,
                    slotCount: 1,
                    usedSlots: 1,
                    emptySlots: 0,
                    hasEmptySlot: false,
                    consistent: true),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = Snapshot(world, 930);

        var upgradeResult = GameMcpTestHarness.Detail(state, upgradeId);
        var researchResult = GameMcpTestHarness.Detail(state, ReadyResearchId);
        var craftingResult = GameMcpTestHarness.Detail(state, ReadyCraftingId);

        // This world is assembled by hand, so no live entity carries the identity and the game has
        // no answer to compare against. The parity block says which, rather than going missing.
        Assert.Equal("ERR_UNAVAILABLE", (string?)upgradeResult["requirements"]!["nativeParity"]!["reasonCode"]);
        Assert.False((bool)upgradeResult["predicates"]!["canPurchase"]!["available"]!);
        Assert.Equal("ERR_STATE", (string?)upgradeResult["predicates"]!["canPurchase"]!["reasonCode"]);
        Assert.Null(upgradeResult["purchase"]);
        Assert.True((bool)upgradeResult["blockers"]!["queue"]!["blocked"]!);
        Assert.Null(upgradeResult["blockers"]!["queue"]!["evidence"]);
        Assert.True((bool)upgradeResult["blockers"]!["cap"]!["blocked"]!);

        var thresholds = researchResult["researchThresholds"]!;
        Assert.Equal(10, (int)thresholds["baseThreshold"]!);
        Assert.Equal(10, (int)thresholds["scaledThreshold"]!);
        Assert.Equal(5, (int)thresholds["effectiveThreshold"]!);
        var adjustment = Assert.Single(thresholds["activeAdjustments"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(challengeId), (string?)adjustment["source"]!["uuid"]);
        Assert.Equal("ChallengeSO", (string?)adjustment["sourceNativeType"]);
        // This research still has leeway, so that axis is not what refuses and is not listed; the
        // cap is, and it prints in full beside it.
        Assert.Null(researchResult["blockers"]!["leeway"]);
        Assert.True((bool)researchResult["blockers"]!["cap"]!["blocked"]!);
        Assert.Null(researchResult["blockers"]!["bandwidth"]);

        // A discovered recipe's discovery axis had nothing but `blocked: no` to say, so it is not
        // listed; the two axes that do refuse print their rows in full.
        Assert.Null(craftingResult["blockers"]!["recipeDiscovery"]);
        Assert.True((bool)craftingResult["blockers"]!["bandwidth"]!["blocked"]!);
        var bandwidthRow = Assert.Single(
            craftingResult["blockers"]!["bandwidth"]!["rows"]!.Values<JObject>())!;
        Assert.Equal("25", (string?)bandwidthRow["cost"]);
        Assert.Equal("10", (string?)bandwidthRow["amount"]);
        Assert.True((bool)bandwidthRow["bandwidth"]!);
        Assert.Null(bandwidthRow["headroom"]);
        Assert.True((bool)craftingResult["blockers"]!["drain"]!["blocked"]!);
    }

    [Fact]
    public void EachPurchaseCostRowAnswersForItsOwnResourceWithNoAggregateBesideThem()
    {
        var upgradeId = Guid.Parse("d5100000-0000-4000-8000-000000000001");
        var heldId = Guid.Parse("d5100000-0000-4000-8000-000000000002");
        var shortId = Guid.Parse("d5100000-0000-4000-8000-000000000003");
        var rawUpgrade = new RawUpgradeSample(
            upgradeId,
            level: 1,
            maxLevel: 10,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1,
            cachedCostLevel: 1);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                GameWorldStateDeriver.Derive(in rawUpgrade),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
            {
                PriceLine(upgradeId, heldId, held: 400, resourceAffordable: true),
                PriceLine(upgradeId, shortId, held: 5, resourceAffordable: false),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var purchase = Explain(world, upgradeId, 931)["purchase"]!;
        var rows = purchase["rows"]!.Values<JObject>().ToArray();

        Assert.Null(purchase["affordability"]);
        Assert.Single(purchase.Children());
        Assert.True((bool)rows[0]!["affordable"]!);
        Assert.Null(rows[0]!["reasonCode"]);
        Assert.False((bool)rows[1]!["affordable"]!);
        Assert.Equal("ERR_REFUSED", (string?)rows[1]!["reasonCode"]);
    }

    [Fact]
    public void AnEntityWithNothingToSayPublishesEmptyBlocksRatherThanOmittingThem()
    {
        var upgradeId = Guid.Parse("d5100000-0000-4000-8000-000000000011");
        var rawUpgrade = new RawUpgradeSample(
            upgradeId,
            level: 1,
            maxLevel: 10,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1,
            cachedCostLevel: 1);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                GameWorldStateDeriver.Derive(in rawUpgrade),
            }),
            CollectedAtEpoch = 41,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var explanation = Explain(world, upgradeId, 932);

        Assert.NotNull(explanation["predicates"]);
        Assert.NotNull(explanation["blockers"]);
        Assert.Empty(explanation["requirements"]!["root"]!["children"]!.Values<JObject>());
    }

    /// <summary>
    /// One call, three ids, three blocks in the order they were asked. The id nothing carries
    /// refuses on its own and takes neither of its neighbours down with it, and no block echoes an
    /// index back because the order is the correlation.
    /// </summary>
    [Fact]
    public void A_batch_answers_every_id_it_can_and_refuses_only_the_one_it_could_not()
    {
        var upgradeId = Guid.Parse("2442ff7c-5630-4f7f-ac8f-1ea5f3b7a7cc");
        var glyphId = Guid.Parse("cc1cb602-2427-41c3-a2f4-421b4eef2ab4");
        var missing = Guid.Parse("00000000-0000-4000-8000-0000000000aa");
        var rawUpgrade = new RawUpgradeSample(
            upgradeId,
            level: 1,
            maxLevel: 10,
            available: true,
            queuedLevels: 0,
            buildTime: BigDouble.Zero,
            developmentTime: 1,
            cachedCostLevel: 1);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                GameWorldStateDeriver.Derive(in rawUpgrade),
            }),
            Glyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            CollectedAtEpoch = 51,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRows(
            Snapshot(world, 949),
            string.Empty,
            new[]
            {
                glyphId.ToString("D"), missing.ToString("D"), upgradeId.ToString("D"),
            }));
        var blocks = response["results"]!.Values<JObject>().ToArray();

        Assert.Equal(3, blocks.Length);
        Assert.DoesNotContain(
            "inputIndex", response.ToString(Newtonsoft.Json.Formatting.None),
            StringComparison.Ordinal);

        // Two categories in one call: neither block was told which table to look in.
        Assert.Equal("glyphs", (string?)blocks[0]!["category"]);
        Assert.Equal("Accursed", (string?)blocks[0]!["name"]);
        Assert.NotNull(blocks[0]!["predicates"]);
        Assert.Equal("upgrades", (string?)blocks[2]!["category"]);
        Assert.Equal("Alchemist", (string?)blocks[2]!["name"]);
        Assert.Equal("available", (string?)blocks[2]!["row"]!["state"]);
        Assert.NotNull(blocks[2]!["requirements"]);

        Assert.Equal("unavailable", (string?)blocks[1]!["status"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)blocks[1]!["reasonCode"]);
        // The one block in the batch that answers "nothing carries this" echoes the whole id the
        // caller sent and names nothing. A six-character stub of a thirty-six character argument is
        // not something a caller can match against what they typed, and the `(unnamed …)` name it
        // used to carry made an id with no row read as a row missing its name.
        Assert.Equal(missing.ToString("D"), (string?)blocks[1]!["uuid"]);
        Assert.Null(blocks[1]!["name"]);
        Assert.Equal("world_categories", (string?)blocks[1]!["readWith"]!["tool"]);
        Assert.Null(blocks[1]!["row"]);
        Assert.Null(blocks[1]!["predicates"]);
    }

    /// <summary>
    /// An id carries its own table, so a caller holding one from a search, a refusal or an action
    /// response reads it without first learning where it lives — and gets the same block either way.
    /// </summary>
    [Fact]
    public void An_id_resolves_its_own_table_and_naming_that_table_changes_nothing()
    {
        var glyphId = Guid.Parse("cc1cb602-2427-41c3-a2f4-421b4eef2ab4");
        var world = new GameWorldState
        {
            Glyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "glyphs", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "upgrades", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
            }),
            CollectedAtEpoch = 52,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var state = Snapshot(world, 950);

        var resolved = GameMcpTestHarness.Detail(state, glyphId);
        var addressed = GameMcpTestHarness.Detail(state, "glyphs", glyphId);

        Assert.Equal(
            resolved.ToString(Newtonsoft.Json.Formatting.None),
            addressed.ToString(Newtonsoft.Json.Formatting.None));
        Assert.Equal("glyphs", (string?)resolved["category"]);

        // A table the id is not in is the caller insisting, and it is answered as a miss in that
        // table rather than quietly resolved into the one they did not name.
        var elsewhere = GameMcpTestHarness.Detail(state, "upgrades", glyphId);
        Assert.Equal("unavailable", (string?)elsewhere["status"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)elsewhere["reasonCode"]);
        Assert.Equal("upgrades", (string?)elsewhere["readWith"]!["category"]);
    }

    private static WorldPurchaseCost PriceLine(
        Guid ownerId,
        Guid resourceId,
        double held,
        bool resourceAffordable) =>
        new(
            ownerId,
            resourceId,
            baseExactAmount: new BigDouble(100d),
            effectiveExactAmount: new BigDouble(100d),
            exactGroupedLevels: 1,
            exactGroupedAmount: new BigDouble(100d),
            modifierSources: PublicationTable<WorldPurchaseCostModifierSource>.Empty,
            affordabilityEvaluated: true,
            availableAmount: new BigDouble(held),
            combinedEffectiveAmount: new BigDouble(100d),
            resourceAffordable,
            resourceAffordabilityReasonCode:
                resourceAffordable ? string.Empty : "insufficient_resource",
            affordable: false,
            affordabilityReasonCode: "unaffordable");

    /// <summary>
    /// The round's costliest miss. A glyph's detail block published state, discovery, visibility
    /// and price and never said `keywords: Elemental` — the fact that decides which family the
    /// glyph belongs to and therefore which page renders it. A reader working from that block
    /// concluded the read surface contradicted the screen and held the wrong finding for two hours,
    /// while the search row ten minutes earlier had carried the word plainly. One fact, one name,
    /// both verbs.
    /// </summary>
    [Fact]
    public void A_detail_block_carries_the_same_keywords_line_the_search_row_prints()
    {
        var glyphId = Guid.Parse("cc1cb602-2427-41c3-a2f4-421b4eef2ab4");
        var elemental = Guid.Parse("61ee89dd-f896-4863-b3ff-1d07e7cf8896");
        var augment = Guid.Parse("12eb2437-5bd7-4069-b02a-e6f1eee8f0c6");
        var world = new GameWorldState
        {
            Glyphs = PublicationTable<WorldGlyph>.Create(new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            }),
            EntityKeywords = PublicationTable<WorldEntityKeyword>.Create(new[]
            {
                new WorldEntityKeyword(
                    glyphId, WorldKeywordOwnerKind.Glyph,
                    WorldKeywordSource.PrimaryType, 0, elemental),
                new WorldEntityKeyword(
                    glyphId, WorldKeywordOwnerKind.Glyph,
                    WorldKeywordSource.TypeList, 0, augment),
            }),
            CollectedAtEpoch = 44,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var block = Explain(world, glyphId, 944);

        Assert.Equal("Accursed", (string?)block["name"]);
        Assert.Equal("Elemental, Spell Augment", (string?)block["keywords"]);
    }

    private static bool Predicate(JObject explanation, string name) =>
        (bool)explanation["predicates"]![name]!["available"]!;

    private static JObject Explain(GameWorldState world, Guid id, ulong generation) =>
        GameMcpTestHarness.Detail(Snapshot(world, generation), id);

    private static GameMcpFrameContext Snapshot(GameWorldState world, ulong generation)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            world with { EntityIdentities = GameMcpTestHarness.EntityCatalog },
            new WorldGeneration(generation));
        return Snapshot(publisher.ReadLatest());
    }

    private static GameMcpFrameContext Snapshot(
        WorldPublication<GameWorldState> publication)
    {
        if (publication.Snapshot.EntityIdentities.IsBound)
            return GameMcpTestHarness.Context(publication);
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            publication.Snapshot with
            {
                EntityIdentities = GameMcpTestHarness.EntityCatalog,
            },
            publication.Generation);
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static WorldSpellRecipe Spell(
        Guid id,
        bool discovered,
        bool hidden,
        int masteryLevel) => new(
            id,
            discovered,
            discRarityLevel: 0,
            masteryXp: BigDouble.Zero,
            masteryLevel,
            masteryLevelReady: false,
            hiddenDiscovery: hidden,
            isRequiredDiscovery: true,
            penaltyUsageCost: 1,
            castSpeed: 1,
            baseCharges: 1,
            repeatInstantEffects: false,
            spellPowerMod: BigDouble.One,
            spellCostMod: BigDouble.One,
            spellCdSpeedMod: BigDouble.One,
            spellDurationMod: BigDouble.One,
            spellSpecialMod: BigDouble.One,
            spellXpMod: BigDouble.One,
            hasAlertedThisMastery: false);

    private static WorldResearch Research(
        Guid id,
        bool available,
        int level,
        int maxLevel,
        int baseRequirement,
        int effectiveRequirement,
        int leeway,
        PublicationTable<WorldResearchRequirementAdjustment>? adjustments = null,
        bool stillHasLeeway = true) => new(
            id,
            level,
            queuedLevels: 0,
            researchStage: 0,
            selfBonusLevels: 0,
            maxLevel,
            researchTime: 60,
            isDeveloping: false,
            isActive: false,
            flagged: false,
            available,
            visible: available,
            complete: maxLevel > 0 && level >= maxLevel,
            canDevelop: available && (maxLevel <= 0 || level < maxLevel),
            withinDevelopRange: available && (maxLevel <= 0 || level < maxLevel),
            meetsLevelRequirements: level + leeway >= effectiveRequirement,
            stillHasLeeway,
            belowArtificialMaxLevel: true,
            belowMaxInvestmentLevel: maxLevel <= 0 || level < maxLevel,
            purchasedLevels: level,
            baseLevel: level,
            bonusLevel: 0,
            totalLevel: level,
            artificialMaxLevel: 0,
            hiddenLevel: false,
            levelVisibilityRange: 2,
            requiredStagesCached: 0,
            requiredTimeCached: BigDouble.Zero,
            baseRequirement,
            effectiveRequirement,
            adjustments ?? PublicationTable<WorldResearchRequirementAdjustment>.Empty,
            new RawResearchModifiers(
                bonusLevels: BigDouble.Zero,
                baseLevels: BigDouble.Zero,
                power: new BigDouble(100d),
                maxLevelCap: BigDouble.Zero,
                leewayPoints: new BigDouble(leeway)));

    private static WorldCraftingRecipe Crafting(
        Guid id,
        bool visible,
        bool canBuy,
        PublicationTable<WorldCraftingRecipeResource>? resources = null,
        PublicationTable<WorldCraftingRecipeDrainBlock>? drains = null)
    {
        var reading = new RawCraftingRecipeSample(
            id,
            visible,
            canBuy,
            startingQuantity: BigDouble.One,
            useQuantityAsLevel: false,
            timeToComplete: 1,
            outputWithinCapacity: true,
            typeCount: 0,
            authoredInputCount: resources?.Count ?? 0,
            generatedOutputCount: 0,
            consumableOutputCount: 0,
            engagementEffectCount: drains?.Count ?? 0,
            completionEffectCount: 0);
        return new WorldCraftingRecipe(
            in reading,
            PublicationTable<WorldCraftingRecipeTypeLink>.Empty,
            resources ?? PublicationTable<WorldCraftingRecipeResource>.Empty,
            PublicationTable<WorldCraftingRecipeConsumableOutput>.Empty,
            drains ?? PublicationTable<WorldCraftingRecipeDrainBlock>.Empty);
    }

    private static global::UpgradeSO Upgrade()
    {
        var upgrade = new global::UpgradeSO { maxLevel = -1 };
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }

    private static global::ResearchSO ResearchStub(
        int level,
        Guid? id = null,
        int maxLevel = 20)
    {
        var research = new global::ResearchSO
        {
            uuid = (id ?? Guid.NewGuid()).ToString("D"),
            level = level,
            maxLevel = maxLevel,
        };
        global::ResearchSO.All.Add(research);
        return research;
    }

    private static Requirements.ResearchRequirement Require(
        global::ResearchSO target,
        double value) => new()
        {
            item = target,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = value },
        };

    private static global::PrerequisiteLinkSO.LinkDefinition Tier(
        params Requirements.IRequirementCondition[] requirements)
    {
        var tier = new global::PrerequisiteLinkSO.LinkDefinition();
        foreach (var requirement in requirements)
            tier.prerequisites.prerequisites.Add(requirement);
        return tier;
    }

    /// <summary>
    /// A detail page publishes the game's own words for the thing it describes. The description was
    /// reachable only through the evaluated-detail resolver, whose thirteen kinds are the set this
    /// build evaluates predicates for — a shared entry point, not a rule about descriptions — so a
    /// live round walked the agromancy-action and plot-node-action graphs to a detail page and found
    /// no description on either, though both native types carry one and the page already holds the
    /// type name its category declares.
    /// </summary>
    [Fact]
    public void AnActionDetailPageCarriesTheAuthoredDescriptionItsCategoryDeclaresTheTypeFor()
    {
        var mining = Guid.Parse("af40da52-4a75-420c-a88d-008c3f5fc443");
        var watering = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");
        var action = new global::HarvestActionSO
        {
            displayName = "Mining",
            description = "Digs the node for ore.",
        };
        action.SetGuid(mining);
        global::IdScriptableObject.RuntimeLookup[mining] = action;
        var plotAction = new global::PlotNodeActionSO
        {
            displayName = "Water",
            description = "Waters the node so it grows.",
        };
        plotAction.SetGuid(watering);
        global::IdScriptableObject.RuntimeLookup[watering] = plotAction;

        var world = new GameWorldState
        {
            HarvestActions = PublicationTable<WorldHarvestAction>.Create(new[]
            {
                new WorldHarvestAction(
                    mining, new BigDouble(100), new BigDouble(100), new BigDouble(100)),
            }),
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(watering, "PlotNodeActionSO", "Water", "WaterPlotAction"),
                new EntityIdentityName(mining, "HarvestActionSO", "Mining", "MiningHarvestAction"),
            }.OrderBy(row => row.EntityId).ToArray()),
            CollectedAtEpoch = 71,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(971));
        var page = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldQuery.GetRows(
                    GameMcpTestHarness.Context(publisher.ReadLatest()),
                    string.Empty,
                    new[] { mining.ToString("D") })
                .Freeze(),
            world.EntityIdentities));

        Assert.Equal(
            "Digs the node for ore.",
            (string?)Assert.Single(page["results"]!.Values<JObject>())!["description"]);

        // The plot-node-action half of the same fix, at the read the page performs: the category
        // declares the native type, and the type is what the authored text is read through.
        Assert.Equal(
            "Waters the node so it grows.",
            GameMcpEntityExplainer.ReadDescription(
                watering, GameMcpEntityCapabilityMap.ExpectedNativeType("plot-node-actions")));
        Assert.Equal(
            "Digs the node for ore.",
            GameMcpEntityExplainer.ReadDescription(
                mining, GameMcpEntityCapabilityMap.ExpectedNativeType("agromancy-actions")));
    }

    private static GameWorldState Collect()
    {
        var collector = new GameWorldCollector();
        var frame = new GameWorldCycleFrame
        {
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        collector.Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }

    private static void ClearRegistries()
    {
        global::UpgradeSO.All.Clear();
        global::StructureSO.All.Clear();
        global::ResearchSO.All.Clear();
        global::SpellRecipeSO.All.Clear();
        global::AlchemyRecipeSO.All.Clear();
        global::RitualSO.All.Clear();
        global::GlyphSO.All.Clear();
        global::IntVariable.All.Clear();
        global::PrerequisiteLinkSO.All.Clear();
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::GameManager.currentFrame = 0;
    }
}
