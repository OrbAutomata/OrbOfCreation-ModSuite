using System;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// The spell type layer for one equipped spell: what its types multiply its power, cost and cooldown
/// speed by, together.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one type total that <i>is</i> a factor.</b> Every other taxonomy distributes into
/// its members, so a type total and a member value are one bonus; <c>SpellTypeSO</c> distributes
/// nothing — all twenty-two of its records are values — and <c>Spell.GetPower()</c> multiplies
/// <c>GetSpellTypePowerPercent()</c> in as an independent layer beside the instance record, the
/// player global and the recipe record. Deriving it here is therefore not double counting; it is the
/// only place this layer exists at all.
/// </para>
/// <para>
/// <b>The set is the live one.</b> <c>Spell.GetAllSpellTypes()</c> is
/// <c>SpellRecipeSO.GetNotSpellTypes()</c> concatenated with <c>Spell.augmentedSpellTypes</c>, and
/// the second half is seeded from the recipe's authored <c>spellTypes</c> and then rewritten by
/// <c>Spell.SetupLimitedElementalType()</c> from the equipped glyphs. The authored list is not the
/// set the game multiplies over. It is a concatenation and not a set union: a type named by both
/// halves would multiply twice, and reproducing it as a union would quietly disagree.
/// <c>notSpellTypes</c> is empty on all sixty-five recipes on the audited build, which is a reading
/// and not a rule, so it is folded in like any other half.
/// </para>
/// </remarks>
internal readonly struct WorldSpellTypeResonance
{
    internal WorldSpellTypeResonance(
        int slotIndex,
        Guid spellRecipeId,
        int typeCount,
        BigDouble elementalResonancePercent,
        bool elementalExponentApplied,
        BigDouble typePowerPercent,
        BigDouble typeCostPercent,
        BigDouble typeCooldownSpeedPercent)
    {
        SlotIndex = slotIndex;
        SpellRecipeId = spellRecipeId;
        TypeCount = typeCount;
        ElementalResonancePercent = elementalResonancePercent;
        ElementalExponentApplied = elementalExponentApplied;
        TypePowerPercent = typePowerPercent;
        TypeCostPercent = typeCostPercent;
        TypeCooldownSpeedPercent = typeCooldownSpeedPercent;
    }

    /// <summary>The loadout position, which is the number the fire action takes.</summary>
    internal int SlotIndex { get; }

    internal Guid SpellRecipeId { get; }

    /// <summary>How many types the product ran over, duplicates included.</summary>
    internal int TypeCount { get; }

    /// <summary>
    /// The product of every type's <c>elementalResonance</c> as a percent — the exponent the branch
    /// below turns on.
    /// </summary>
    internal BigDouble ElementalResonancePercent { get; }

    /// <summary>
    /// Whether the resonance exponent was applied at all. False when the resonance is approximately
    /// one, which is the branch <c>GetResonantPercent</c> takes first and the state of a spell no
    /// resonance effect has touched.
    /// </summary>
    internal bool ElementalExponentApplied { get; }

    /// <summary>The factor <c>Spell.GetPower()</c> multiplies in as <c>GetSpellTypePowerPercent()</c>.</summary>
    internal BigDouble TypePowerPercent { get; }

    /// <summary>The same layer for cost, from <c>GetSpellTypeCostPercent()</c>.</summary>
    internal BigDouble TypeCostPercent { get; }

    /// <summary>The same layer for cooldown speed, from <c>GetSpellTypeCdSpeedPercent()</c>.</summary>
    internal BigDouble TypeCooldownSpeedPercent { get; }
}

/// <summary>
/// Reproduces <c>Spell.GetResonantPercent</c> over the captured effective type set.
/// </summary>
/// <remarks>
/// <para>
/// The original is an <c>Enumerable.Aggregate</c> seeded at <c>BigDouble.One</c>, with two closures
/// chosen by one test: when <c>Utils.Approx(elementalRes, One)</c> the accumulator simply multiplies
/// every type's fetched percent, and otherwise every <c>IsElemental()</c> type's percent is raised to
/// <c>elementalRes</c> first. The exponent lands on the percent, not on the record value, because the
/// fetch closure calls <c>AsPercent()</c> before the branch sees it.
/// </para>
/// <para>
/// Fail closed rather than short: a slot naming a type the world did not publish gets no row at all.
/// A product missing one of its factors is a smaller number that looks like an answer, and the
/// consumer this exists for is arithmetic.
/// </para>
/// </remarks>
internal static class WorldSpellTypeResonanceDeriver
{
    internal static PublicationTable<WorldSpellTypeResonance> Build(
        PublicationTable<WorldSpellSlot> slots,
        PublicationTable<WorldSpellSlotType> slotTypes,
        PublicationTable<WorldSpellRelation> relations,
        PublicationTable<WorldSpellType> spellTypes)
    {
        if (slots.Count == 0) return PublicationTable<WorldSpellTypeResonance>.Empty;

        var rows = new WorldSpellTypeResonance[slots.Count];
        var written = 0;
        var effective = Array.Empty<WorldSpellType>();

        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (!slot.Occupied) continue;

            if (!TryGather(slot, slotTypes, relations, spellTypes, ref effective, out var count)) continue;

            var types = new ReadOnlySpan<WorldSpellType>(effective, 0, count);

            var elementalResonance = BigDouble.One;
            for (var type = 0; type < types.Length; type++)
            {
                elementalResonance *= OrbGameMath.AsPercent(types[type].ElementalResonance);
            }

            var exponent = !OrbGameMath.Approx(elementalResonance, BigDouble.One);

            rows[written++] = new WorldSpellTypeResonance(
                slot.SlotIndex,
                slot.SpellRecipeId,
                count,
                elementalResonance,
                exponent,
                Resonant(types, elementalResonance, exponent, Layer.Power),
                Resonant(types, elementalResonance, exponent, Layer.Cost),
                Resonant(types, elementalResonance, exponent, Layer.CooldownSpeed));
        }

        return written == 0
            ? PublicationTable<WorldSpellTypeResonance>.Empty
            : PublicationTable<WorldSpellTypeResonance>.Create(rows, written);
    }

    private enum Layer
    {
        Power,
        Cost,
        CooldownSpeed,
    }

    private static BigDouble Resonant(
        ReadOnlySpan<WorldSpellType> types,
        BigDouble elementalResonance,
        bool exponent,
        Layer layer)
    {
        var product = BigDouble.One;
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            var percent = OrbGameMath.AsPercent(layer switch
            {
                Layer.Power => type.Power,
                Layer.Cost => type.CostMod,
                _ => type.CooldownSpeed,
            });

            product *= exponent && type.IsElemental
                ? BigDouble.Pow(percent, elementalResonance)
                : percent;
        }

        return product;
    }

    /// <summary>
    /// The recipe's <c>notSpellTypes</c> in authored order, then the slot's live augmented types —
    /// the order <c>Concat</c> produces, which is the order the aggregate consumes.
    /// </summary>
    private static bool TryGather(
        in WorldSpellSlot slot,
        PublicationTable<WorldSpellSlotType> slotTypes,
        PublicationTable<WorldSpellRelation> relations,
        PublicationTable<WorldSpellType> spellTypes,
        ref WorldSpellType[] effective,
        out int count)
    {
        count = 0;

        if (WorldSpellGraphLookup.TryFindRelations(relations, slot.SpellRecipeId, out var start, out var length))
        {
            for (var index = 0; index < length; index++)
            {
                var relation = relations[start + index];
                if (relation.Kind != WorldSpellRelationKind.NotSpellType) continue;
                if (!Append(relation.TargetId, spellTypes, ref effective, ref count)) return false;
            }
        }

        if (WorldSpellSlotTypeLookup.TryFind(slotTypes, slot.SlotIndex, out start, out length))
        {
            for (var index = 0; index < length; index++)
            {
                if (!Append(slotTypes[start + index].SpellTypeId, spellTypes, ref effective, ref count))
                    return false;
            }
        }

        return true;
    }

    private static bool Append(
        Guid spellTypeId,
        PublicationTable<WorldSpellType> spellTypes,
        ref WorldSpellType[] effective,
        ref int count)
    {
        if (!WorldLookup.TryFind(spellTypes, spellTypeId, out var published)) return false;

        if (count == effective.Length)
        {
            Array.Resize(ref effective, effective.Length == 0 ? 4 : effective.Length * 2);
        }

        effective[count++] = published;
        return true;
    }
}

/// <summary>Reaches one equipped spell's derived type layer, which sorts by loadout position.</summary>
internal static class WorldSpellTypeResonanceLookup
{
    internal static bool TryFind(
        PublicationTable<WorldSpellTypeResonance> table,
        int slotIndex,
        out WorldSpellTypeResonance resonance)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].SlotIndex.CompareTo(slotIndex);
            if (comparison == 0)
            {
                resonance = rows[middle];
                return true;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        resonance = default;
        return false;
    }
}
