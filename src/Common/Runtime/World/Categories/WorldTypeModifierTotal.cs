using System;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// What one type-level modifier record currently adds up to, for the records that hold no value of
/// their own.
/// </summary>
/// <remarks>
/// <para>
/// <b>The name is the guard.</b> A record that distributes pushes its modifiers, transformed, into
/// every member record registered with <c>AddRecord</c>, so the member value the suite already
/// publishes <i>already contains</i> this number. The two are one bonus, not two factors, and
/// multiplying them squares it. Nothing here is called <c>Power</c> or <c>Speed</c> for that reason:
/// the member keeps the plain property name, and a type's own total only ever appears under
/// <c>DistributedTotal…</c>. <see cref="WorldTypeModifier.Property"/> cannot carry that distinction
/// on its own — nine of the thirteen structure pairs name the type record and the member record
/// identically.
/// </para>
/// <para>
/// <c>ValueModifierRecord</c> fields have no row here. They carry a <c>baseValue</c> and a memo, they
/// are folded where they are published, and folding them a second time from these entries would
/// produce a second answer to a question that already has one.
/// </para>
/// <para>
/// One record class holds no value and distributes to nothing:
/// <c>ResearchTypeSO.levelRequirementAdjust</c> is a plain <c>ModifierRecord</c>, wired into no
/// member by <c>RegisterResearch</c>, which registers only <c>power</c> and <c>maxLevelCap</c>. Its
/// total is the only one on this table that is not also inside a member value, and
/// <see cref="RecordNativeType"/> is what says so.
/// </para>
/// </remarks>
internal readonly struct WorldTypeModifierTotal
{
    internal WorldTypeModifierTotal(
        Guid typeId,
        WorldTypeModifierOwnerKind ownerKind,
        string property,
        string recordNativeType,
        BigDouble distributedTotalPercent,
        BigDouble distributedTotalMultiplier,
        int contributionCount)
    {
        TypeId = typeId;
        OwnerKind = ownerKind;
        Property = property ?? string.Empty;
        RecordNativeType = recordNativeType ?? string.Empty;
        DistributedTotalPercent = distributedTotalPercent;
        DistributedTotalMultiplier = distributedTotalMultiplier;
        ContributionCount = contributionCount;
    }

    internal Guid TypeId { get; }

    internal WorldTypeModifierOwnerKind OwnerKind { get; }

    /// <summary>The type's own record member name, which is not always the member record's name.</summary>
    internal string Property { get; }

    /// <summary>
    /// <c>MergingModifierRecord</c> and <c>OrderedMultiplierRecord</c> distribute; a plain
    /// <c>ModifierRecord</c> does not.
    /// </summary>
    internal string RecordNativeType { get; }

    /// <summary>
    /// <c>Adjust(100)</c> over the record's entries — the number
    /// <c>OrderedMultiplierRecord.GetTotalPercent()</c> beautifies and suffixes with a percent sign.
    /// </summary>
    internal BigDouble DistributedTotalPercent { get; }

    /// <summary>
    /// The same total as a factor: <c>AsPercent(Adjust(100))</c>, which is what
    /// <c>GetTotalMultiplier()</c> prints behind its <c>x</c>.
    /// </summary>
    internal BigDouble DistributedTotalMultiplier { get; }

    /// <summary>How many entries the fold consumed.</summary>
    internal int ContributionCount { get; }
}

/// <summary>
/// Folds the captured contribution entries into one total per record, off the Unity thread.
/// </summary>
/// <remarks>
/// <para>
/// <c>ModifierRecord.Adjust(BigDouble)</c> is
/// <c>IsEmpty() ? value : ValueModifier.AdjustWith(value, GetAllModifiers())</c>, and
/// <c>GetAllModifiers()</c> is the passive dictionary concatenated with the active one.
/// <see cref="GameModifierStack.AdjustWith(BigDouble, ReadOnlySpan{GameValueModifier})"/> is that
/// port and returns its base untouched for an empty span, so a record carrying nothing totals to a
/// flat 100 percent — a fact, not an absence.
/// </para>
/// <para>
/// The seed is 100 because that is the argument the game passes:
/// <c>OrderedMultiplierRecord.GetTotalPercent()</c> is <c>Adjust((BigDouble)100)</c>. This is a
/// pinned formula rather than a guess, which matters because for a distributor there is no
/// game-side evaluator to defer to — nothing in the game asks one for a number outside its tooltip.
/// <c>MergingModifierRecord</c> has no <c>GetTotal…</c> pair of its own; it inherits the same
/// <c>Adjust</c>, and holds no <c>baseValue</c> to seed with instead.
/// </para>
/// <para>
/// Nothing here crosses a <c>MergeEntry</c> transform. What a distributor hands a member is its
/// modifier after <c>expMod</c>, <c>mod</c> and <c>orderAdjust</c> — delegates created at
/// <c>AddRecord</c> time, unreadable from IL — so this total and a member value are never bridged
/// by a ratio. The transform stays Unresolved.
/// </para>
/// </remarks>
internal static class WorldTypeModifierTotalDeriver
{
    /// <summary>The one record class that carries a value of its own, and is folded where it is published.</summary>
    internal const string ValueRecordNativeType = "ValueModifierRecord";

    /// <summary>The seed the game's own total passes to <c>Adjust</c>.</summary>
    private static readonly BigDouble PercentSeed = 100;

    internal static PublicationTable<WorldTypeModifierTotal> Build(
        PublicationTable<WorldTypeModifier> records,
        PublicationTable<WorldTypeModifierContribution> contributions)
    {
        if (records.Count == 0) return PublicationTable<WorldTypeModifierTotal>.Empty;

        var rows = new WorldTypeModifierTotal[records.Count];
        var written = 0;
        var scratch = Array.Empty<GameValueModifier>();

        // Both tables sort by type then property, so one walk keeps its place in the entries rather
        // than bisecting for every record.
        var entries = contributions.AsSpan();
        var cursor = 0;

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];

            if (string.Equals(record.RecordNativeType, ValueRecordNativeType, StringComparison.Ordinal))
            {
                continue;
            }

            while (cursor < entries.Length && Compare(entries[cursor], record) < 0) cursor++;

            var start = cursor;
            var count = 0;
            while (start + count < entries.Length && Compare(entries[start + count], record) == 0) count++;

            if (scratch.Length < count) scratch = new GameValueModifier[count];
            for (var entry = 0; entry < count; entry++)
            {
                var contribution = entries[start + entry].Contribution;
                scratch[entry] = new GameValueModifier(
                    (GameValueModifierType)contribution.ModifierType,
                    contribution.Amount,
                    contribution.Order);
            }

            var percent = GameModifierStack.AdjustWith(
                PercentSeed, new ReadOnlySpan<GameValueModifier>(scratch, 0, count));

            rows[written++] = new WorldTypeModifierTotal(
                record.TypeId,
                record.OwnerKind,
                record.Property,
                record.RecordNativeType,
                percent,
                OrbGameMath.AsPercent(percent),
                count);
        }

        return written == 0
            ? PublicationTable<WorldTypeModifierTotal>.Empty
            : PublicationTable<WorldTypeModifierTotal>.Create(rows, written);
    }

    private static int Compare(in WorldTypeModifierContribution entry, in WorldTypeModifier record)
    {
        var type = entry.TypeId.CompareTo(record.TypeId);
        return type != 0 ? type : string.CompareOrdinal(entry.Property, record.Property);
    }
}

/// <summary>Reaches one type's derived totals, which sort together by type then property.</summary>
internal static class WorldTypeModifierTotalLookup
{
    /// <summary>The run of rows belonging to <paramref name="typeId"/>.</summary>
    internal static bool TryFind(
        PublicationTable<WorldTypeModifierTotal> table,
        Guid typeId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        start = LowerBound(rows, typeId);
        count = 0;
        while (start + count < rows.Length && rows[start + count].TypeId == typeId) count++;
        return count > 0;
    }

    /// <summary>One type's total for one named record.</summary>
    internal static bool TryFindProperty(
        PublicationTable<WorldTypeModifierTotal> table,
        Guid typeId,
        string property,
        out WorldTypeModifierTotal total)
    {
        if (TryFind(table, typeId, out var start, out var count))
        {
            for (var index = 0; index < count; index++)
            {
                var row = table[start + index];
                if (!string.Equals(row.Property, property, StringComparison.Ordinal)) continue;
                total = row;
                return true;
            }
        }

        total = default;
        return false;
    }

    private static int LowerBound(ReadOnlySpan<WorldTypeModifierTotal> rows, Guid typeId)
    {
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rows[middle].TypeId.CompareTo(typeId) < 0) low = middle + 1;
            else high = middle - 1;
        }

        return low;
    }
}
