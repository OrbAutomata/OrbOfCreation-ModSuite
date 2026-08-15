using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One authored factor a glyph applies — the <c>x1.15 Cost</c> a glyph's own tooltip prints, as the
/// three numbers the game's modifier arithmetic is made of plus the statistic the number is about.
/// </summary>
/// <remarks>
/// <para>
/// A glyph carries fifteen inline <c>ValueModifier</c> slots and almost all of them are empty on any
/// one glyph, so this is a sparse relation rather than fifteen columns on the glyph row: one row per
/// slot the game would print, none for the rest. Until it existed a glyph's published row carried
/// its price and its levels and said nothing whatever about what the glyph does, which is the one
/// question a reader deciding whether to socket it is asking.
/// </para>
/// <para>
/// <see cref="ModifierType"/>, <see cref="Amount"/> and <see cref="Order"/> are the same three
/// fields <see cref="WorldModifierVariable"/> publishes, under the same names, because they are the
/// same arithmetic — <c>type</c> selects the operation, the amount is its magnitude, and the order
/// decides which modifiers merge before any is applied. The type is never folded away: two glyphs on
/// one spell combine by kind, and a single pre-multiplied number would say the wrong thing about
/// every pairing.
/// </para>
/// <para>
/// <see cref="Amount"/> is the modifier's <c>adjustReal</c>, not its authored <c>adjust</c>. They
/// differ on exactly the multiplicative kinds — the game's <c>ConvertToReal</c> adds one for
/// MultiStacking and Exponent — and <c>adjustReal</c> is the one the screen prints and the one the
/// modifier registry beside this table already publishes. Publishing the other would put one
/// modifier magnitude on the wire in two spellings.
/// </para>
/// <para>
/// <see cref="Property"/> is the glyph's own slot name and is on every row, because the statistic is
/// not enough to tell two rows apart: <c>spellCooldown</c> and <c>spellBaseCooldown</c> both name the
/// Cooldown statistic and are different factors with different arithmetic.
/// </para>
/// </remarks>
internal readonly struct WorldGlyphFactor
{
    internal WorldGlyphFactor(
        Guid glyphId,
        string property,
        Guid statisticId,
        int modifierType,
        BigDouble amount,
        int order)
    {
        GlyphId = glyphId;
        Property = property ?? string.Empty;
        StatisticId = statisticId;
        ModifierType = modifierType;
        Amount = amount;
        Order = order;
    }

    /// <summary>The glyph the factor is authored on.</summary>
    internal Guid GlyphId { get; }

    /// <summary>Which of the glyph's authored slots this is, under the game's own field name.</summary>
    internal string Property { get; }

    /// <summary>
    /// The statistic the game itself names for this slot, or <see cref="Guid.Empty"/> where it names
    /// none.
    /// </summary>
    /// <remarks>
    /// Empty is a published fact rather than a dropped row: four slots point the tooltip at a
    /// <c>DoubleVariable</c> on the player instead of at a statistic, and one is applied to a
    /// resource cost list under no name at all. Those rows still carry their slot and their numbers,
    /// and a reader meets the same shape whether or not the edge exists.
    /// </remarks>
    internal Guid StatisticId { get; }

    /// <summary>
    /// The game's <c>ValueModifierType</c> as its underlying integer, exactly as
    /// <see cref="WorldModifierVariable.ModifierType"/> carries it.
    /// </summary>
    internal int ModifierType { get; }

    /// <summary>The modifier's magnitude — the original's <c>adjustReal</c>.</summary>
    internal BigDouble Amount { get; }

    internal int Order { get; }
}

/// <summary>
/// Which statistic the game names for each of a glyph's fifteen authored modifier slots.
/// </summary>
/// <remarks>
/// <para>
/// Read off the pinned assembly, from the one method that prints them:
/// <c>GlyphSO.GetQuantityTooltipNodes</c> tests each slot with <c>ValueModifier.IsEmpty()</c> and,
/// for the ones that are not empty, pairs the slot with an accessor that resolves a name. Nine of
/// those accessors are <c>GlobalVariables.Get…Attr()</c>, each of which is a single
/// <c>GlobalVariables.GetAttribute("…")</c> over the game's own global-attribute dictionary — and
/// that dictionary is keyed on <c>AttributeSO.globalDefinition</c>, which is the key
/// <see cref="WorldStatistic.GlobalDefinition"/> captures. The keys here are those literals,
/// copied, never derived from the field name.
/// </para>
/// <para>
/// Five slots have no key and it is not an omission. Four of them —
/// <c>spellCriticalRating</c>, <c>spellCriticalEffect</c>, <c>spellDoubleCastRating</c> and
/// <c>spellDoubleCastEffect</c> — are printed against a <c>DoubleVariable</c> the game reads off the
/// player rather than against a statistic, so no <c>globalDefinition</c> key exists to join on. The
/// fifth, <c>creationCostMod</c>, is never printed as a named factor at all: it is applied straight
/// to a <c>ResourceCostList</c>. Inventing a statistic for any of the five would hand a reader an
/// edge the game does not author.
/// </para>
/// <para>
/// Two slots share one key. <c>spellCooldown</c> and <c>spellBaseCooldown</c> are both printed under
/// the Cooldown statistic, which is why <see cref="WorldGlyphFactor.Property"/> exists as well as
/// the join.
/// </para>
/// </remarks>
internal static class WorldGlyphFactorSlots
{
    /// <summary>The slot's field on <c>GlyphSO</c> and the statistic key the game prints it under.</summary>
    internal readonly struct Slot
    {
        internal Slot(string field, string statisticKey)
        {
            Field = field;
            StatisticKey = statisticKey;
        }

        internal string Field { get; }

        /// <summary>Empty where the game names no statistic for the slot.</summary>
        internal string StatisticKey { get; }
    }

    internal static readonly Slot[] All =
    {
        new("spellPower", "Power"),
        new("spellSpecialEffect", "SpecialEffect"),
        new("spellDuration", "Duration"),
        new("spellCost", "Cost"),
        new("spellDrainCost", "DrainCost"),
        new("spellCooldown", "Cooldown"),
        new("spellBaseCooldown", "Cooldown"),
        new("spellCastSpeed", "CastingSpeed"),
        new("spellExperienceRate", "ExperienceRate"),
        new("spellSpellCharges", "SpellStocks"),
        new("spellCriticalRating", ""),
        new("spellCriticalEffect", ""),
        new("spellDoubleCastRating", ""),
        new("spellDoubleCastEffect", ""),
        new("creationCostMod", ""),
    };
}

/// <summary>Publishes the factor readings, sorted by glyph and then by authored slot order.</summary>
/// <remarks>
/// The statistic join is resolved here rather than during capture: the statistic table is built from
/// the same frame, and the key it is joined on never reaches the wire, so the derived table already
/// carries the identity a reader follows.
/// </remarks>
internal static class WorldGlyphFactorDeriver
{
    internal static PublicationTable<WorldGlyphFactor> Build(
        WorldRelationBuffer<WorldGlyphFactor> buffer,
        PublicationTable<WorldStatistic> statistics)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldGlyphFactor>.Empty;

        var byKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        for (var index = 0; index < statistics.Count; index++)
        {
            var statistic = statistics[index];
            if (statistic.GlobalDefinition.Length == 0) continue;
            byKey[statistic.GlobalDefinition] = statistic.StatisticId;
        }

        var rows = new WorldGlyphFactor[buffer.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            var row = buffer[index];
            var key = KeyFor(row.Property);
            var statisticId = key.Length > 0 && byKey.TryGetValue(key, out var found)
                ? found
                : Guid.Empty;
            rows[index] = new WorldGlyphFactor(
                row.GlyphId,
                row.Property,
                statisticId,
                row.ModifierType,
                row.Amount,
                row.Order);
        }

        Array.Sort(rows, static (left, right) =>
        {
            var glyph = left.GlyphId.CompareTo(right.GlyphId);
            return glyph != 0
                ? glyph
                : SlotOrdinal(left.Property).CompareTo(SlotOrdinal(right.Property));
        });
        return PublicationTable<WorldGlyphFactor>.Create(rows, rows.Length);
    }

    private static string KeyFor(string property)
    {
        var slots = WorldGlyphFactorSlots.All;
        for (var index = 0; index < slots.Length; index++)
            if (string.Equals(slots[index].Field, property, StringComparison.Ordinal))
                return slots[index].StatisticKey;
        return string.Empty;
    }

    private static int SlotOrdinal(string property)
    {
        var slots = WorldGlyphFactorSlots.All;
        for (var index = 0; index < slots.Length; index++)
            if (string.Equals(slots[index].Field, property, StringComparison.Ordinal))
                return index;
        return slots.Length;
    }
}

/// <summary>Range lookup over the factor table, which is keyed by glyph and then by slot.</summary>
internal static class WorldGlyphFactorLookup
{
    /// <summary>One glyph's factors, as a contiguous range of the sorted table.</summary>
    internal static bool TryFindRange(
        PublicationTable<WorldGlyphFactor> table,
        Guid glyphId,
        out int start,
        out int count)
    {
        var low = 0;
        var high = table.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = table[middle].GlyphId.CompareTo(glyphId);
            if (comparison < 0) low = middle + 1;
            else
            {
                if (comparison == 0) found = middle;
                high = middle - 1;
            }
        }

        if (found < 0)
        {
            start = 0;
            count = 0;
            return false;
        }

        start = found;
        var end = found + 1;
        while (end < table.Count && table[end].GlyphId == glyphId) end++;
        count = end - found;
        return true;
    }
}

/// <summary>
/// A second walk of the glyph registry, for the authored factors each glyph applies. It claims no
/// identities: the glyphs are already claimed by their own category.
/// </summary>
/// <remarks>
/// A slot is published only when the game itself would print it, and the game's own predicate is
/// <c>ValueModifier.IsEmpty()</c> — the amount equals its type's identity, which is one for the
/// multiplicative kinds and zero for the rest. <see cref="GameValueModifier.IsEmpty"/> is that
/// method ported, so the rows are exactly the lines a glyph's tooltip carries rather than a
/// convenient approximation of them.
/// </remarks>
internal sealed class WorldGlyphFactorReader : IWorldCategoryReader
{
    private readonly Type? _glyphType;
    private readonly string _unavailable;
    private readonly Func<object, Guid>? _glyphId;
    private readonly Func<object, int>[] _types;
    private readonly Func<object, BigDouble>[] _amounts;
    private readonly Func<object, int>[] _orders;

    internal WorldGlyphFactorReader(Type? glyphType)
    {
        _glyphType = glyphType;
        var slots = WorldGlyphFactorSlots.All;
        _types = new Func<object, int>[slots.Length];
        _amounts = new Func<object, BigDouble>[slots.Length];
        _orders = new Func<object, int>[slots.Length];
        if (glyphType is null)
        {
            _unavailable = "the GlyphSO type was not found on this build";
            return;
        }

        var bind = new WorldMemberBinding(glyphType, "GlyphSO");
        _glyphId = bind.Call<Guid>("GetGuid");
        for (var index = 0; index < slots.Length; index++)
        {
            var field = slots[index].Field;
            _types[index] = bind.NestedEnumField(field, "type")!;
            _amounts[index] = bind.NestedField<BigDouble>(field, "adjustReal")!;
            _orders[index] = bind.NestedField<int>(field, "order")!;
        }

        _unavailable = bind.Failure;
    }

    public string Category => "glyph effects";

    public bool IsAvailable => _glyphType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.GlyphEffects;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var glyphs = NativeAccessorBinder.StaticList(_glyphType, "All");
        if (glyphs is null)
            return WorldCategoryReport.Missing(Category, "the GlyphSO registry was unreadable");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;

        for (var index = 0; index < glyphs.Count; index++)
        {
            var glyph = glyphs[index];
            if (glyph is null) continue;

            try
            {
                sampled += Read(glyph, buffer);
            }
            catch (Exception ex)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = "reading a glyph's factors threw: " + ex.GetBaseException().Message;
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private int Read(object glyph, WorldRelationBuffer<WorldGlyphFactor> buffer)
    {
        var glyphId = _glyphId!(glyph);
        if (glyphId == Guid.Empty) return 0;

        var slots = WorldGlyphFactorSlots.All;
        var appended = 0;
        for (var index = 0; index < slots.Length; index++)
        {
            var modifierType = _types[index](glyph);
            var amount = _amounts[index](glyph);
            var order = _orders[index](glyph);
            var modifier = new GameValueModifier((GameValueModifierType)modifierType, amount, order);
            if (modifier.IsEmpty()) continue;

            buffer.Append(new WorldGlyphFactor(
                glyphId, slots[index].Field, Guid.Empty, modifierType, amount, order));
            appended++;
        }

        return appended;
    }
}
