using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// What one keyword is currently worth, and how many things wear it.
/// </summary>
/// <remarks>
/// <para>
/// A keyword is a type asset, and a type asset carries modifier records, so "what is Workshop worth"
/// and "which structures are Workshops" are the same object asked two ways. This table joins them:
/// one row per keyword, per record, per kind of member, so the question is answered without walking
/// every entity.
/// </para>
/// <para>
/// <b>The count is a reach, not a registration.</b> It comes from
/// <see cref="WorldKeywordMembership"/> — the authored membership every published table carries,
/// closed transitively over the structure subtype edge, so a bonus on <c>PrimalStructures</c>
/// reaches Arcanist, Flameweaver and Stormshaper members too, because
/// <c>StructureTypeSO.Initialize()</c> wires a parent's thirteen records into each child's thirteen.
/// Reading a type's runtime registration list instead would publish one fact twice under two owners
/// and would still miss that edge. Members are counted once no matter how many of a chain's rungs
/// name them, and the same index answers the filter that walks the edge, so the count and the rows
/// are one derivation.
/// </para>
/// <para>
/// <b>The total is still not a factor for a member value.</b> It is
/// <see cref="WorldTypeModifierTotal.DistributedTotalPercent"/> carried across unchanged, and it
/// keeps its name for the reason that name exists.
/// </para>
/// <para>
/// Spell types have no rows here, and that is the shape of the game rather than a gap: all
/// twenty-two <c>SpellTypeSO</c> records hold values rather than distributing, so they have no
/// derived total to index, and a spell's types are published as spell-graph relations rather than as
/// keyword rows. The spell type layer is a product over an effective set, which is
/// <see cref="WorldSpellTypeResonance"/>.
/// </para>
/// </remarks>
internal readonly struct WorldKeywordModifier
{
    internal WorldKeywordModifier(
        Guid keywordId,
        WorldKeywordOwnerKind memberKind,
        string property,
        BigDouble distributedTotalPercent,
        BigDouble distributedTotalMultiplier,
        int memberCount)
    {
        KeywordId = keywordId;
        MemberKind = memberKind;
        Property = property ?? string.Empty;
        DistributedTotalPercent = distributedTotalPercent;
        DistributedTotalMultiplier = distributedTotalMultiplier;
        MemberCount = memberCount;
    }

    /// <summary>The type asset whose display name is the word.</summary>
    internal Guid KeywordId { get; }

    /// <summary>Which class the counted members belong to.</summary>
    internal WorldKeywordOwnerKind MemberKind { get; }

    internal string Property { get; }

    internal BigDouble DistributedTotalPercent { get; }

    internal BigDouble DistributedTotalMultiplier { get; }

    /// <summary>Distinct members of this kind the keyword reaches, subtype chain included.</summary>
    internal int MemberCount { get; }
}

/// <summary>Joins the derived type totals to the authored membership they apply across.</summary>
internal static class WorldKeywordModifierDeriver
{
    internal static PublicationTable<WorldKeywordModifier> Build(
        PublicationTable<WorldTypeModifierTotal> totals,
        PublicationTable<WorldEntityKeyword> keywords,
        PublicationTable<WorldResearch> research,
        PublicationTable<WorldConsumableType> consumableTypes,
        PublicationTable<WorldTypeSubtype> subtypes)
    {
        if (totals.Count == 0) return PublicationTable<WorldKeywordModifier>.Empty;

        var membership =
            WorldKeywordMembership.Build(keywords, research, consumableTypes, subtypes);
        if (membership.IsEmpty) return PublicationTable<WorldKeywordModifier>.Empty;

        var rows = new List<WorldKeywordModifier>();
        var reach = new Dictionary<WorldKeywordOwnerKind, HashSet<Guid>>();
        var kinds = new List<WorldKeywordOwnerKind>();
        var walked = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        var current = Guid.Empty;

        for (var index = 0; index < totals.Count; index++)
        {
            var total = totals[index];
            if (index == 0 || total.TypeId != current)
            {
                current = total.TypeId;
                membership.Reach(current, reach, kinds, walked, pending);
            }

            for (var slot = 0; slot < kinds.Count; slot++)
            {
                var kind = kinds[slot];
                rows.Add(new WorldKeywordModifier(
                    total.TypeId,
                    kind,
                    total.Property,
                    total.DistributedTotalPercent,
                    total.DistributedTotalMultiplier,
                    reach[kind].Count));
            }
        }

        if (rows.Count == 0) return PublicationTable<WorldKeywordModifier>.Empty;

        var published = rows.ToArray();
        Array.Sort(published, static (left, right) =>
        {
            var keyword = left.KeywordId.CompareTo(right.KeywordId);
            if (keyword != 0) return keyword;
            var kind = ((int)left.MemberKind).CompareTo((int)right.MemberKind);
            return kind != 0 ? kind : string.CompareOrdinal(left.Property, right.Property);
        });

        return PublicationTable<WorldKeywordModifier>.Create(published, published.Length);
    }
}

/// <summary>Reaches one keyword's derived worth, which sorts together by keyword.</summary>
internal static class WorldKeywordModifierLookup
{
    internal static bool TryFind(
        PublicationTable<WorldKeywordModifier> table,
        Guid keywordId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        start = LowerBound(rows, keywordId);
        count = 0;
        while (start + count < rows.Length && rows[start + count].KeywordId == keywordId) count++;
        return count > 0;
    }

    private static int LowerBound(ReadOnlySpan<WorldKeywordModifier> rows, Guid keywordId)
    {
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rows[middle].KeywordId.CompareTo(keywordId) < 0) low = middle + 1;
            else high = middle - 1;
        }

        return low;
    }
}
