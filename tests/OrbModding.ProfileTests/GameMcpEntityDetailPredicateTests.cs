using System;
using Newtonsoft.Json.Linq;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpEntityDetailFixture;

namespace OrbModding.ProfileTests;

[Collection(NativeRegistryCollection.Name)]
[OrbModding.TestSupport.ResetGameStubs]
public sealed class GameMcpEntityDetailPredicateTests : IDisposable
{
    public GameMcpEntityDetailPredicateTests() => OrbModding.GameStubs.GameStubState.Reset();

    public void Dispose() => OrbModding.GameStubs.GameStubState.Reset();

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
            EntityIdentities = GameMcpTestHarness.EntityCatalog,
            CollectedAtEpoch = 31,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(original, new WorldGeneration(909));
        var pinned = GameMcpTestHarness.Context(publisher.ReadLatest());
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
        Assert.True(Predicate(readySpell, "canUse"));
        Assert.False(Predicate(waitingSpell, "canUse"));
        // `available` is one question — the game's own availability bit — and only the kinds that
        // hold one answer it. On a discoverable it was an alias of `visible`, which is a different
        // question with a different answer, and the two crossed the wire side by side.
        Assert.Null(readySpell["predicates"]!["available"]);
        Assert.True(Predicate(readyResearch, "available"));
        Assert.False(Predicate(blockedResearch, "available"));
        Assert.True(Predicate(readyCrafting, "visible"));
        Assert.False(Predicate(blockedCrafting, "visible"));
        Assert.Null(blockedCrafting["predicates"]!["available"]);

        // A predicate whose question the row already answers is gone, holding or not: the row's
        // block is the screen's truth and it is where both answers are read. Where the row
        // publishes no such block — a discovered spell has no `discover`, an invisible crafting
        // recipe no `purchase` — the predicate is the only answer and stays: the rule is one
        // verdict per row, not fewer.
        Assert.Null(readySpell["row"]!["discover"]);
        Assert.False(Predicate(readySpell, "canDiscover"));
        Assert.Null(waitingSpell["predicates"]!["canDiscover"]);
        Assert.False((bool)waitingSpell["row"]!["discover"]!["available"]!);
        Assert.Null(waitingSpell["row"]!["discover"]!["reasonCode"]);
        Assert.Null(blockedResearch["predicates"]!["canDevelop"]);
        Assert.False((bool)blockedResearch["row"]!["develop"]!["available"]!);
        Assert.Null(blockedCrafting["row"]!["purchase"]);
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
                // A read's predicates carry no class — the sentence is the whole of the no — so
                // what may never be blank is the sentence.
                if (!(bool)value["available"]!)
                {
                    Assert.Null(value["reasonCode"]);
                    Assert.False(string.IsNullOrWhiteSpace((string?)value["reason"]));
                }
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
        Assert.Null(canUse["reasonCode"]);
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
}
