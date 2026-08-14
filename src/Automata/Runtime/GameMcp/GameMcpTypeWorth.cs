#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>
/// What a keyword is currently worth, and who paid in. A keyword is a type asset, so this is the
/// half of a type's detail read that its own published row cannot carry: one number per record, and
/// the named sources behind each.
/// </summary>
/// <remarks>
/// <para>
/// <b>The magnitude's name is the guard.</b> Eleven of the fourteen taxonomies hand their bonuses
/// down: the moment a modifier lands on the type, a transformed copy is pushed into every member,
/// so the member value the suite already publishes <i>already contains</i> it. Reading a type total
/// beside a member number and multiplying is how one bonus becomes two, which is why the two cases
/// never share a key: a handed-down record answers under <c>distributedTotalPercent</c> and a record
/// holding a number of its own answers under <c>value</c>. <see cref="WorldTypeModifier.Property"/>
/// cannot carry the distinction — nine of the thirteen structure pairs name the type record and the
/// member record identically.
/// </para>
/// <para>
/// Spell types are the whole of the second case among the classes a detail read reaches: all
/// twenty-two <c>SpellTypeSO</c> records hold values, they hand nothing down, and a spell's power
/// really is multiplied by the product of the types it resonates with. Their numbers appear nowhere
/// else on the wire, so this block is where they are said — for the twenty of them the game can
/// read. The rest of the surface prints only what a purchase could move, which is why a record with
/// no path into any of the game's own computations is absent from here and present in the raw
/// capture; see <see cref="WorldTypeModifierLiveness"/>.
/// </para>
/// </remarks>
internal static class GameMcpTypeWorth
{
    /// <summary>
    /// One sentence saying how the numbers under it apply, in the wording that is true for the
    /// block it sits on. A type that hands everything down never reads about values it has none of,
    /// and a spell type — which hands nothing down — never borrows the handed-down wording.
    /// </summary>
    private const string HandedDown =
        "These totals are already inside each member's own numbers: read them to compare types, " +
        "and never multiply one into a member.";

    private const string OwnNumbers =
        "These are this type's own numbers, and they apply on top of whatever wears the type.";

    private const string Both =
        "A distributedTotalPercent is already inside each member's own numbers and must never be " +
        "multiplied into one again; a value is this type's own number and applies on top of " +
        "whatever wears the type.";

    /// <summary>
    /// What one entry says about this type, when the type has anything to say. Silent otherwise:
    /// every other entity's detail read is byte-unchanged because no published record names it.
    /// </summary>
    internal static void AddWorth(JObject result, GameWorldState world, Guid uuid)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!WorldTypeModifierLookup.TryFind(world.TypeModifiers, uuid, out var start, out var count))
            return;

        var properties = new JArray();
        var unmodified = new JArray();
        var handedDown = false;
        var own = false;
        for (var index = 0; index < count; index++)
        {
            var entry = Property(
                world, world.TypeModifiers[start + index], ref handedDown, ref own, out var neutral);
            if (entry is null) continue;
            if (neutral) unmodified.Add(entry["property"]!);
            else properties.Add(entry);
        }

        if (properties.Count == 0 && unmodified.Count == 0) return;

        var worth = new JObject();

        // Said only about magnitudes that are here. A block carrying none — every record a value
        // whose number this build cannot reach — says nothing about how to read numbers it did not
        // publish, rather than claiming the wrong half of the rule.
        if (handedDown || own)
            worth["howToRead"] = handedDown && own ? Both : handedDown ? HandedDown : OwnNumbers;
        var members = Members(world, uuid);
        if (members is not null) worth["members"] = members;
        if (properties.Count > 0) worth["properties"] = properties;

        // A distributor at a flat hundred percent with nothing sitting on it is the reading "this
        // type publishes this record and nothing modifies it". Forty-one of them cost a live round
        // a three-line stanza each to say that forty-one times over; the two facts a reader takes
        // from them — which records exist, and that none is loaded — both fit on one line, and the
        // moment anything does load one it leaves this line for a stanza of its own.
        if (unmodified.Count > 0) worth["unmodified"] = unmodified;
        result["worth"] = worth;
    }

    /// <summary>
    /// One record: its magnitude under the name that says which kind of magnitude it is, and every
    /// modifier currently sitting on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A record with neither a magnitude nor a source has nothing to say and is omitted, which is
    /// what silence means outside a table. A distributor carrying nothing still has one: its total
    /// is a flat hundred percent, and that is a reading rather than an absence.
    /// </para>
    /// <para>
    /// A record the game itself cannot read is omitted whatever it holds. Worth is what investment
    /// can move, and machinery with no path into any computation — see
    /// <see cref="WorldTypeModifierLiveness"/> — moves for nobody: a number beside it would offer a
    /// lever that is not connected to anything. The record stays captured, because publication
    /// carries what the build holds; it simply is not a property worth has.
    /// </para>
    /// <para>
    /// The name is the game's own word for the record wherever the game authors one; see
    /// <see cref="GameMcpModifierPropertyWords"/>. Rows keep the order the world table holds them
    /// in, which is by internal name: that name is the record's stable identity, so the page's order
    /// does not move when a word is added to the census.
    /// </para>
    /// </remarks>
    private static JObject? Property(
        GameWorldState world,
        in WorldTypeModifier record,
        ref bool handedDown,
        ref bool own,
        out bool neutral)
    {
        neutral = false;
        if (!WorldTypeModifierLiveness.IsLive(record.OwnerKind, record.Property)) return null;

        var entry = new JObject
        {
            ["property"] = GameMcpModifierPropertyWords.Word(record.OwnerKind, record.Property),
        };
        var said = false;
        var flatHundred = false;

        if (string.Equals(
                record.RecordNativeType,
                WorldTypeModifierTotalDeriver.ValueRecordNativeType,
                StringComparison.Ordinal))
        {
            if (TryOwnValue(world, record.TypeId, record.OwnerKind, record.Property, out var value))
            {
                entry["value"] = new GameMcpDomainValue(value);
                own = true;
                said = true;
            }
        }
        else if (WorldTypeModifierTotalLookup.TryFindProperty(
                     world.TypeModifierTotals, record.TypeId, record.Property, out var total))
        {
            entry["distributedTotalPercent"] =
                new GameMcpDomainValue(total.DistributedTotalPercent);
            flatHundred = total.DistributedTotalPercent == NeutralPercent;
            said = true;
        }

        var sources = Sources(world, record.TypeId, record.Property);
        if (sources is not null)
        {
            entry["sources"] = sources;
            said = true;
        }

        if (!said) return null;
        neutral = flatHundred && sources is null && entry["value"] is null;
        if (!neutral && entry["distributedTotalPercent"] is not null) handedDown = true;
        return entry;
    }

    /// <summary>The total a distributor reads when nothing has been added to it.</summary>
    private static readonly BigDouble NeutralPercent = new(100d);

    /// <summary>Every modifier on one record, each under the name of whoever put it there.</summary>
    private static JArray? Sources(GameWorldState world, Guid typeId, string property)
    {
        if (!WorldTypeModifierContributionLookup.TryFind(
                world.TypeModifierContributions, typeId, property, out var start, out var count))
        {
            return null;
        }

        var sources = new JArray();
        for (var index = 0; index < count; index++)
        {
            var contribution = world.TypeModifierContributions[start + index].Contribution;
            sources.Add(new JObject
            {
                ["sourceUuid"] = contribution.SourceId.ToString("D"),
                ["amount"] = new GameMcpDomainValue(contribution.Amount),
                ["effect"] = Effect(contribution.ModifierType),
                ["order"] = contribution.Order,
            });
        }

        return sources;
    }

    /// <summary>
    /// How many things this keyword reaches, per class of thing, with the structure subtype chain
    /// already closed over. Absent for a type no derived total indexes, which is every spell type:
    /// all twenty-two <c>SpellTypeSO</c> records hold values rather than distributing, so there is
    /// no total to key a row off.
    /// </summary>
    /// <remarks>
    /// Research types read this like their sibling taxonomies, off the same index. Their membership
    /// is authored on <c>ResearchSO.researchTypes</c> and published inside the research category,
    /// beside each type's investment levels, rather than in the keyword table;
    /// <see cref="WorldKeywordMembership"/> is where the two tables meet.
    /// </remarks>
    private static JArray? Members(GameWorldState world, Guid keywordId)
    {
        if (!WorldKeywordModifierLookup.TryFind(
                world.KeywordModifiers, keywordId, out var start, out var count))
        {
            return null;
        }

        // The index carries one row per record per kind and the count is a property of the kind, so
        // the kinds are what this says — reading it per record would print one fact once per record.
        var counted = new List<KeyValuePair<WorldKeywordOwnerKind, int>>();
        for (var index = 0; index < count; index++)
        {
            var row = world.KeywordModifiers[start + index];
            var seen = false;
            for (var slot = 0; slot < counted.Count && !seen; slot++)
                seen = counted[slot].Key == row.MemberKind;
            if (seen) continue;
            counted.Add(new KeyValuePair<WorldKeywordOwnerKind, int>(row.MemberKind, row.MemberCount));
        }

        if (counted.Count == 0) return null;

        var members = new JArray();
        for (var index = 0; index < counted.Count; index++)
        {
            members.Add(new JObject
            {
                ["kind"] = Kind(counted[index].Key),
                ["count"] = counted[index].Value,
            });
        }

        return members;
    }

    /// <summary>
    /// The folded number a value record holds, read off the entity's own published row.
    /// </summary>
    /// <remarks>
    /// Explicit per class rather than reflected off the property name: the wire would otherwise
    /// publish whatever member happened to match a record's spelling, and a number under
    /// <c>value</c> that is not that record's value is the one failure this block cannot survive.
    /// A record with no published value says nothing rather than guessing.
    /// </remarks>
    private static bool TryOwnValue(
        GameWorldState world,
        Guid typeId,
        WorldTypeModifierOwnerKind kind,
        string property,
        out BigDouble value)
    {
        value = BigDouble.Zero;
        switch (kind)
        {
            case WorldTypeModifierOwnerKind.SpellType:
                if (!WorldLookup.TryFind(world.SpellTypes, typeId, out var spellType)) return false;
                return TrySpellTypeValue(in spellType, property, out value);
            case WorldTypeModifierOwnerKind.AlchemyType:
                if (!string.Equals(property, "level", StringComparison.Ordinal)) return false;
                if (!WorldLookup.TryFind(world.AlchemyTypes, typeId, out var alchemyType)) return false;
                value = alchemyType.Level;
                return true;
            case WorldTypeModifierOwnerKind.CraftingRecipeType:
                if (!string.Equals(property, "magnitudeIncrement", StringComparison.Ordinal))
                    return false;
                if (!WorldLookup.TryFind(world.CraftingRecipeTypes, typeId, out var craftingType))
                    return false;
                value = craftingType.MagnitudeIncrement;
                return true;
            case WorldTypeModifierOwnerKind.RitualType:
                if (!string.Equals(property, "activeRituals", StringComparison.Ordinal)) return false;
                if (!WorldLookup.TryFind(world.RitualTypes, typeId, out var ritualType)) return false;
                value = ritualType.ActiveRituals;
                return true;
            case WorldTypeModifierOwnerKind.HarvestType:
                if (!string.Equals(property, "level", StringComparison.Ordinal)) return false;
                if (!WorldLookup.TryFind(world.HarvestTypes, typeId, out var harvestType)) return false;
                value = harvestType.Level;
                return true;
            case WorldTypeModifierOwnerKind.TimeRuneType:
                if (!string.Equals(property, "totalLevel", StringComparison.Ordinal)) return false;
                if (!WorldLookup.TryFind(world.TimeRuneTypes, typeId, out var timeRuneType)) return false;
                value = timeRuneType.TotalLevel;
                return true;
            case WorldTypeModifierOwnerKind.ResearchType:
                if (!WorldLookup.TryFind(world.ResearchTypes, typeId, out var researchType))
                    return false;
                switch (property)
                {
                    case "freeBonusLevels":
                        value = researchType.FreeBonusLevels;
                        return true;
                    case "usedBonusLevels":
                        value = researchType.UsedBonusLevels;
                        return true;
                    case "maxInvestmentLevel":
                        value = researchType.MaxInvestmentLevel;
                        return true;
                    default:
                        return false;
                }

            case WorldTypeModifierOwnerKind.EquipmentType:
                if (!string.Equals(property, "maxTypeSlots", StringComparison.Ordinal)) return false;
                if (!WorldLookup.TryFind(world.EquipmentTypes, typeId, out var equipmentType))
                    return false;
                value = equipmentType.MaxTypeSlots;
                return true;
            default:
                return false;
        }
    }

    private static bool TrySpellTypeValue(
        in WorldSpellType spellType,
        string property,
        out BigDouble value)
    {
        switch (property)
        {
            case "augmentResonance": value = spellType.AugmentResonance; return true;
            case "bonusCritRate": value = spellType.BonusCritRate; return true;
            case "bonusDoubleCastRate": value = spellType.BonusDoubleCastRate; return true;
            case "chargeEffectMod": value = spellType.ChargeEffectMod; return true;
            case "chargeSpecialMod": value = spellType.ChargeSpecialMod; return true;
            case "chargeTimeMod": value = spellType.ChargeTimeMod; return true;
            case "cooldownSpeed": value = spellType.CooldownSpeed; return true;
            case "cooldownTime": value = spellType.CooldownTime; return true;
            case "costMod": value = spellType.CostMod; return true;
            case "critDurationMod": value = spellType.CritDurationMod; return true;
            case "critEffectMod": value = spellType.CritEffectMod; return true;
            case "doubleCastEffectMod": value = spellType.DoubleCastEffectMod; return true;
            case "drainCostMod": value = spellType.DrainCostMod; return true;
            case "durationMod": value = spellType.DurationMod; return true;
            case "elementalResonance": value = spellType.ElementalResonance; return true;
            case "maxStacksMod": value = spellType.MaxStacksMod; return true;
            case "power": value = spellType.Power; return true;
            case "scalingMod": value = spellType.ScalingMod; return true;
            case "typeXpMod": value = spellType.TypeXpMod; return true;
            case "usageCostReduction": value = spellType.UsageCostReduction; return true;
            default: value = BigDouble.Zero; return false;
        }
    }

    /// <summary>
    /// What the modifier does to the number, in the vocabulary
    /// <c>docs/game-systems/modifiers.md</c> names the five kinds by. One map for the whole surface
    /// now lives in <see cref="GameMcpNativeVocabulary"/>: the same ordinal was reaching research
    /// adjustments and the modifier-variables rows as a bare number while this block was already
    /// saying the word.
    /// </summary>
    private static string Effect(int modifierType) =>
        GameMcpNativeVocabulary.ModifierEffect(modifierType);

    /// <summary>
    /// The class of thing a keyword reaches, in the word the rest of the surface calls that class
    /// by. A kind with no word is a defect for the same reason an unworded fold kind is.
    /// </summary>
    private static string Kind(WorldKeywordOwnerKind kind) => kind switch
    {
        WorldKeywordOwnerKind.Structure => "structures",
        WorldKeywordOwnerKind.AlchemyRecipe => "alchemy-recipes",
        WorldKeywordOwnerKind.Resource => "resources",
        WorldKeywordOwnerKind.Equipment => "equipment",
        WorldKeywordOwnerKind.PassiveAbility => "passive-abilities",
        WorldKeywordOwnerKind.TimeRune => "time-runes",
        WorldKeywordOwnerKind.Glyph => "glyphs",
        WorldKeywordOwnerKind.PlotNodeAction => "plot-node-actions",
        WorldKeywordOwnerKind.Ritual => "rituals",
        WorldKeywordOwnerKind.Character => "characters",
        WorldKeywordOwnerKind.PlotNode => "plot-nodes",
        WorldKeywordOwnerKind.HarvestElement => "agromancy-elements",
        WorldKeywordOwnerKind.HarvestAction => "agromancy-actions",
        WorldKeywordOwnerKind.Research => "research",
        _ => throw new InvalidOperationException(
            "a keyword reached member kind '" + kind + "' with no word for it."),
    };
}
#endif
