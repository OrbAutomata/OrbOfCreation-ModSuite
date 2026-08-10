using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Pins what the equipped-loadout reader publishes out of a multi-slot world: which rows, in which
/// order, carrying which values, and what it does instead when the game does not hold up its end.
/// </summary>
/// <remarks>
/// The collector tests cover the per-slot state flags and both cost kinds against their own spell
/// double; what they leave unpinned is the augment table, which is read through the two per-glyph
/// member calls this reader makes inside its per-slot loop. How the reader spells a member read —
/// reflective invocation or a compiled accessor — is an implementation choice underneath these
/// assertions.
/// </remarks>
public sealed class WorldSpellSlotReaderTests : IDisposable
{
    private static readonly Guid RecipeId = new("11111111-aaaa-aaaa-aaaa-111111111111");
    private static readonly Guid InstanceId = new("22222222-aaaa-aaaa-aaaa-222222222222");
    private static readonly Guid FirstGlyphId = new("33333333-aaaa-aaaa-aaaa-333333333333");
    private static readonly Guid SecondGlyphId = new("44444444-aaaa-aaaa-aaaa-444444444444");
    private static readonly Guid FirstResourceId = new("55555555-aaaa-aaaa-aaaa-555555555555");
    private static readonly Guid SecondResourceId = new("66666666-aaaa-aaaa-aaaa-666666666666");

    public WorldSpellSlotReaderTests() => Clear();

    public void Dispose() => Clear();

    [Fact]
    public void CollectsEverySlotInNativeOrderWithItsValues()
    {
        Seed();
        var frame = new GameWorldCycleFrame();

        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(string.Empty, report.FirstFailure);
        Assert.Equal(0, report.Skipped);

        // The hole publishes nothing, so three rows for four positions.
        Assert.Equal(3, report.Sampled);
        Assert.Equal(3, frame.SpellSlots.Count);

        var first = frame.SpellSlots[0];
        Assert.Equal(0, first.SlotIndex);
        Assert.Equal(InstanceId, first.SpellInstanceId);
        Assert.Equal(RecipeId, first.SpellRecipeId);
        Assert.True(first.Occupied);
        Assert.True(first.Casting);
        Assert.True(first.ReadyingCast);
        Assert.False(first.Attuning);
        Assert.True(first.Channeled);
        Assert.True(first.Toggled);
        Assert.True(first.Chargeable);
        Assert.False(first.CastReady);
        Assert.False(first.ChargeAvailable);
        Assert.False(first.CanRemove);
        Assert.True(first.ResourcesCovered);
        Assert.Equal(2, first.CurrentCharges);
        Assert.Equal(3, first.MaximumCharges);
        Assert.Equal(4d, first.CooldownRemaining.ToDouble());
        Assert.Equal(1, first.OutputLevel);
        Assert.Equal(6, first.EffectiveLevel);
        Assert.Equal(7, first.RequiredMasteryLevel);
        Assert.Equal(5, first.RecipeMasteryLevel);
        Assert.True(first.DurationSpell);
        Assert.False(first.UsageRequirementsMet);
        Assert.True(first.CancellationEnabled);
        Assert.True(first.CasterAvailable);
        Assert.Equal(11, first.CastCount);

        // The augment table is one row per distinct glyph, in identity order rather than in the
        // order the loadout stacked them, each carrying the game's own count for that glyph.
        var glyphs = first.AugmentGlyphs.AsSpan().ToArray();
        Assert.Equal(2, glyphs.Length);
        Assert.Equal(FirstGlyphId, glyphs[0].GlyphId);
        Assert.Equal(1, glyphs[0].Quantity);
        Assert.Equal(SecondGlyphId, glyphs[1].GlyphId);
        Assert.Equal(2, glyphs[1].Quantity);

        // An empty position keeps its index rather than sliding down into the hole's place, and
        // publishes the negative of everything — including cancellation, which is a fact about a
        // cast in progress and there is none here.
        var second = frame.SpellSlots[1];
        Assert.Equal(2, second.SlotIndex);
        Assert.False(second.Occupied);
        Assert.Equal(Guid.Empty, second.SpellInstanceId);
        Assert.Equal(Guid.Empty, second.SpellRecipeId);
        Assert.False(second.CastReady);
        Assert.False(second.CanRemove);
        Assert.False(second.CancellationEnabled);
        Assert.True(second.CasterAvailable);
        Assert.Equal(0, second.CurrentCharges);
        Assert.Equal(0, second.MaximumCharges);
        Assert.Empty(second.AugmentGlyphs.AsSpan().ToArray());

        // An occupant with no recipe behind it is still a filled slot, augmented by nothing.
        var third = frame.SpellSlots[2];
        Assert.Equal(3, third.SlotIndex);
        Assert.True(third.Occupied);
        Assert.Equal(Guid.Empty, third.SpellRecipeId);
        Assert.True(third.CastReady);
        Assert.True(third.ChargeAvailable);
        Assert.True(third.CanRemove);
        Assert.Equal(1, third.EffectiveLevel);
        Assert.Equal(0, third.RequiredMasteryLevel);
        Assert.Equal(0, third.RecipeMasteryLevel);
        Assert.True(third.UsageRequirementsMet);
        Assert.Empty(third.AugmentGlyphs.AsSpan().ToArray());

        // Prices come out per slot, in the order the native cost list holds them, and the upkeep
        // this build charges nothing for contributes no rows at all rather than a zero row.
        Assert.Equal(2, frame.SpellCosts.Count);
        AssertCost(frame.SpellCosts[0], 0, WorldSpellCostKind.Immediate, FirstResourceId, 50d);
        AssertCost(frame.SpellCosts[1], 0, WorldSpellCostKind.Immediate, SecondResourceId, 7d);
    }

    /// <summary>
    /// The manager's readiness answer and the cancellation setting are sampled once and reach every
    /// row of the same pass, because both gate the whole loadout rather than one slot.
    /// </summary>
    [Fact]
    public void TheTwoWholeLoadoutAnswersReachEveryOccupiedRow()
    {
        Seed();
        global::SettingsManager.CancellableSpells = false;
        global::SpellManager.NativeCanCast = false;

        var frame = new GameWorldCycleFrame();
        Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(3, frame.SpellSlots.Count);
        for (var index = 0; index < frame.SpellSlots.Count; index++)
        {
            Assert.False(frame.SpellSlots[index].CancellationEnabled);
            Assert.False(frame.SpellSlots[index].CasterAvailable);
        }
    }

    /// <summary>An entry that is not a spell is skipped by its position rather than read.</summary>
    [Fact]
    public void AnEntryThatIsNotASpellIsSkippedByItsPosition()
    {
        Seed();
        Loadout().value.Insert(0, new NotQuiteASpell());

        var frame = new GameWorldCycleFrame();
        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(3, report.Sampled);
        Assert.Equal(1, report.Skipped);
        Assert.Equal("slot 0 held an entry that is not a spell", report.FirstFailure);
        Assert.Equal(3, frame.SpellSlots.Count);
    }

    /// <summary>
    /// A slot whose reading throws costs that slot and no other: the pass keeps going and says which
    /// position failed and why.
    /// </summary>
    [Fact]
    public void ASlotThatThrowsCostsOnlyItsOwnRow()
    {
        Seed();
        Loadout().value[0]!.augmentGlyphRefs = null!;

        var frame = new GameWorldCycleFrame();
        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(2, report.Sampled);
        Assert.Equal(1, report.Skipped);
        Assert.StartsWith("reading slot 0 threw: ", report.FirstFailure);
        Assert.Equal(2, frame.SpellSlots.Count);
        Assert.Equal(0, frame.SpellCosts.Count);
    }

    /// <summary>
    /// A loadout the identity registry does not hold yet is a fact about the save rather than a
    /// shortfall to report.
    /// </summary>
    [Fact]
    public void AnUnregisteredLoadoutCollectsNothingWithoutComplaint()
    {
        var frame = new GameWorldCycleFrame();
        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(0, report.Sampled);
        Assert.Equal(string.Empty, report.FirstFailure);
        Assert.Equal(0, frame.SpellSlots.Count);
    }

    /// <summary>A member the build does not expose leaves the category unavailable and says so.</summary>
    [Fact]
    public void AMemberThatCannotBindLeavesTheCategoryUnavailableAndNamesIt()
    {
        var reader = new WorldSpellSlotReader(
            Resolve("IdScriptableObject"),
            Resolve("SpellListVariable"),
            name => name == "GlyphSO" ? null : Resolve(name));

        Assert.False(reader.IsAvailable);
        Assert.Equal(
            "Spell did not expose its complete cast, level, augment, cancellation, or manager " +
            "readiness state on this build",
            reader.Collect(new HashSet<Guid>(), new GameWorldCycleFrame()).FirstFailure);
    }

    /// <summary>A build without the loadout list type at all names that instead.</summary>
    [Fact]
    public void AnAbsentListTypeIsNamedSeparately()
    {
        var reader = new WorldSpellSlotReader(Resolve("IdScriptableObject"), null, Resolve);

        Assert.False(reader.IsAvailable);
        Assert.Equal(
            "the SpellListVariable type was not found on this build",
            reader.Collect(new HashSet<Guid>(), new GameWorldCycleFrame()).FirstFailure);
    }

    private sealed class NotQuiteASpell : global::Spell
    {
    }

    private static void AssertCost(
        in WorldSpellCost row,
        int slotIndex,
        WorldSpellCostKind kind,
        Guid resourceId,
        double amount)
    {
        Assert.Equal(slotIndex, row.SlotIndex);
        Assert.Equal(kind, row.Kind);
        Assert.Equal(resourceId, row.ResourceId);
        Assert.Equal(amount, row.Amount.ToDouble());
    }

    private static WorldSpellSlotReader Reader() =>
        new(Resolve("IdScriptableObject"), Resolve("SpellListVariable"), Resolve);

    private static Type? Resolve(string name) =>
        typeof(global::AlchemyRecipeSO).Assembly.GetType(name, throwOnError: false);

    private static global::SpellListVariable Loadout() =>
        (global::SpellListVariable)global::IdScriptableObject.RuntimeLookup[KnownEntities.ActiveSpells.Uuid];

    private static void Clear()
    {
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::SpellRecipeSO.All.Clear();
        global::GlyphSO.All.Clear();
        global::SettingsManager.CancellableSpells = true;
        global::SpellManager.NativeCanCast = true;
        global::UnityEngine.Resources.Objects.Clear();
    }

    private static void Seed()
    {
        var recipe = new global::SpellRecipeSO { uuid = RecipeId.ToString(), masteryLevel = 5 };
        var firstGlyph = Glyph(FirstGlyphId, masteryRequirement: 3);
        var secondGlyph = Glyph(SecondGlyphId, masteryRequirement: 7);

        var equipped = new global::Spell(recipe)
        {
            guidContainer = new global::GuidContainer(InstanceId),
            Channeled = true,
            ToggledSpell = true,
            NativeCasting = true,
            NativeReadyingCast = true,
            NativeCanCharge = true,
            NativeCanCast = false,
            NativeChargeAvailable = false,
            CurrentCharges = 2,
            MaximumCharges = 3,
            CooldownRemaining = new BigDouble(4d, 0),
            BaseEffectLevel = 6,
            DurationSpell = true,
            NativeUsageRequirementsMet = false,
            NumCasts = 11,
        };

        // Stacked in the order the loadout applied them, which is not identity order.
        equipped.SetAugmentGlyphs(new global::Stacked.StackedIdRecord<global::GlyphSO>(
            new List<global::GlyphSO> { secondGlyph, secondGlyph, firstGlyph }));
        equipped.Cost.costs.Add(
            new global::ResourceTuple(Resource(FirstResourceId), new BigDouble(50d, 0)));
        equipped.Cost.costs.Add(
            new global::ResourceTuple(Resource(SecondResourceId), new BigDouble(7d, 0)));

        var loadout = new global::SpellListVariable();
        loadout.value.Add(equipped);
        loadout.value.Add(null!);
        loadout.value.Add(new global::Spell { NativeEmpty = true });
        loadout.value.Add(new global::Spell());
        loadout.SetGuid(KnownEntities.ActiveSpells.Uuid);
        global::IdScriptableObject.RuntimeLookup[KnownEntities.ActiveSpells.Uuid] = loadout;
    }

    private static global::GlyphSO Glyph(Guid id, int masteryRequirement)
    {
        var glyph = new global::GlyphSO { masteryReqCount = masteryRequirement };
        glyph.SetGuid(id);
        return glyph;
    }

    private static global::ResourceSO Resource(Guid id) => new() { uuid = id.ToString() };
}
