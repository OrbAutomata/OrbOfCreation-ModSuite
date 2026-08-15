#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Why one entity answered a search, in the order a reader wants the answers.
/// </summary>
/// <remarks>
/// A caller searching a word they heard wants the thing called that first, then the things the game
/// files under that word, then everything in a heap the word happens to name. The tiers are that
/// sentence: they are read as a sort key and never printed, because a row that says why it matched
/// is a row spending bytes on the search rather than on the world.
/// </remarks>
internal enum GameMcpSearchTier
{
    /// <summary>This entity's own identity — its player-facing name, its internal asset name, or its id.</summary>
    Name = 0,

    /// <summary>A word the game prints on this entity's tooltip type line.</summary>
    Keyword = 1,

    /// <summary>The name of the category this entity lives in, or the native type behind it.</summary>
    Category = 2,

    /// <summary>
    /// A word one of this entity's authored effects carries — the property it moves, or the name of
    /// the thing it moves it on.
    /// </summary>
    /// <remarks>
    /// Last, so that adding it moved no hit this surface already returned: every existing band keeps
    /// its rows and its order, and an entity that answers only by what it does joins the page after
    /// them rather than displacing anything.
    /// </remarks>
    Effect = 3,
}

/// <summary>
/// Every keyword the published world carries, resolved to the words the game actually prints.
/// </summary>
/// <remarks>
/// <para>
/// The player-visible word line is <c>ITooltipable.GetDisplayType()</c>, and sixteen classes author
/// it from lists of type assets. Thirteen of them publish those lists as the <c>entity keywords</c>
/// table; the other three carry their type membership inside their own category, because each
/// publishes more than the keyword there — research its investment levels, consumables their carry
/// load, spell recipes their graph. So the keyword surface is the union of four published tables and
/// nothing here reads the game: a word is the display name of the type asset the membership edge
/// points at, looked up in the same identity catalog every other name comes from.
/// </para>
/// <para>
/// A type asset with no display name contributes no word. That is the one place in the suite where a
/// missing name is a missing fact rather than a missing diagnostic: the seven <c>ChallengeTypeSO</c>
/// are effect-targetable but deliberately wordless, and the asset-name fallback every other surface
/// may walk would print seven keywords the game never shows a player.
/// </para>
/// <para>
/// Word order is the game's own, which is not the table's: capture publishes each authored member
/// under its own source and ordinal because the two composite classes compose in opposite directions
/// — <c>StructureSO.GetAllTypes()</c> prepends its primary type, <c>EquipmentSO.GetAllEquipmentTypes()</c>
/// appends it. Assembling the line is therefore this derivation's job, and equipment is the one kind
/// that reads its primary type last.
/// </para>
/// </remarks>
internal sealed class GameMcpKeywordIndex
{
    private static readonly string[] NoWords = Array.Empty<string>();

    private readonly Dictionary<Guid, string[]> _words;

    private GameMcpKeywordIndex(Dictionary<Guid, string[]> words) => _words = words;

    /// <summary>How the words of one entity are joined into the cell a page prints.</summary>
    /// <remarks>
    /// Not the space the game joins them with: six authored type names contain a space of their own
    /// (<c>Spell Augment</c>, <c>All Capped</c>, …), so a space-joined line cannot be split back into
    /// the words it was built from. A comma can, and a cell holding one is already understood — the
    /// share line declines to hoist it and it stays where it is.
    /// </remarks>
    internal const string Separator = ", ";

    internal static GameMcpKeywordIndex Build(GameWorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var catalog = world.EntityIdentities;
        var collected = new Dictionary<Guid, List<string>>();

        var keywords = world.EntityKeywords;
        var index = 0;
        while (index < keywords.Count)
        {
            var owner = keywords[index].OwnerId;
            var end = index;
            while (end < keywords.Count && keywords[end].OwnerId == owner) end++;

            // Equipment reads its primary type last and everything else reads it first, and the
            // table is sorted primary-first — so equipment alone is walked from the far end.
            var primaryLast = keywords[index].OwnerKind == WorldKeywordOwnerKind.Equipment;
            if (primaryLast)
            {
                for (var row = end - 1; row >= index; row--)
                    Append(collected, catalog, owner, keywords[row].KeywordId);
            }
            else
            {
                for (var row = index; row < end; row++)
                    Append(collected, catalog, owner, keywords[row].KeywordId);
            }
            index = end;
        }

        var research = world.Research;
        for (var row = 0; row < research.Count; row++)
        {
            var types = research[row].Decision.ResearchTypes;
            for (var type = 0; type < types.Count; type++)
                Append(collected, catalog, research[row].EntityId, types[type].ResearchTypeId);
        }

        var consumables = world.ConsumableTypes;
        for (var row = 0; row < consumables.Count; row++)
        {
            Append(
                collected, catalog, consumables[row].ConsumableId, consumables[row].TypeId);
        }

        var spells = world.SpellRelations;
        for (var row = 0; row < spells.Count; row++)
        {
            if (spells[row].Kind != WorldSpellRelationKind.SpellType) continue;
            Append(collected, catalog, spells[row].RecipeId, spells[row].TargetId);
        }

        var result = new Dictionary<Guid, string[]>(collected.Count);
        foreach (var pair in collected) result.Add(pair.Key, pair.Value.ToArray());
        return new GameMcpKeywordIndex(result);
    }

    internal IReadOnlyList<string> Words(Guid ownerId) =>
        _words.TryGetValue(ownerId, out var words) ? words : NoWords;

    /// <summary>The entity's word line, or the empty string where the game authors no words for it.</summary>
    internal string Line(Guid ownerId) =>
        _words.TryGetValue(ownerId, out var words) ? string.Join(Separator, words) : string.Empty;

    private static void Append(
        Dictionary<Guid, List<string>> collected,
        EntityIdentityCatalogSnapshot catalog,
        Guid ownerId,
        Guid keywordId)
    {
        if (ownerId == Guid.Empty || keywordId == Guid.Empty) return;
        if (!catalog.IsBound || !catalog.TryGet(keywordId, out var row)) return;
        if (row.DisplayName.Length == 0) return;
        if (!collected.TryGetValue(ownerId, out var words))
        {
            words = new List<string>(3);
            collected.Add(ownerId, words);
        }
        if (!words.Contains(row.DisplayName)) words.Add(row.DisplayName);
    }
}

/// <summary>
/// Every word the published world's authored effects carry, by the entity that authors them.
/// </summary>
/// <remarks>
/// <para>
/// "What raises my Druidry cap" is a question about an effect, and until this existed the only way
/// to ask it was to know the name of the thing that does it. The words are the ones the effect rows
/// already publish: the property the modifier moves, and the player-facing name of the entity it
/// moves it on. Nothing is synthesised and nothing is read from the game — a target's name comes
/// from the same identity catalog every other name on this surface comes from, and an unnamed target
/// contributes no word rather than a stub.
/// </para>
/// <para>
/// Both effect tables feed it, because both answer the same question about their owner: a glyph's
/// inline factors are what it does at any level, and the six per-level holders' tuples are what one
/// more level buys. A caller searching "Cooldown" wants the glyph and the upgrade alike.
/// </para>
/// </remarks>
internal sealed class GameMcpEffectWordIndex
{
    private static readonly string[] NoWords = Array.Empty<string>();

    private readonly Dictionary<Guid, string[]> _words;

    private GameMcpEffectWordIndex(Dictionary<Guid, string[]> words) => _words = words;

    internal static GameMcpEffectWordIndex Build(GameWorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var catalog = world.EntityIdentities;
        var collected = new Dictionary<Guid, List<string>>();

        var factors = world.GlyphEffects;
        for (var index = 0; index < factors.Count; index++)
        {
            var factor = factors[index];
            Append(collected, factor.GlyphId, factor.Property);
            Append(collected, catalog, factor.GlyphId, factor.StatisticId);
            Append(collected, catalog, factor.GlyphId, factor.VariableId);
        }

        var effects = world.LevelEffects;
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            Append(collected, effect.OwnerId, effect.Property);
            Append(collected, catalog, effect.OwnerId, effect.TargetId);
        }

        var result = new Dictionary<Guid, string[]>(collected.Count);
        foreach (var pair in collected) result.Add(pair.Key, pair.Value.ToArray());
        return new GameMcpEffectWordIndex(result);
    }

    internal IReadOnlyList<string> Words(Guid ownerId) =>
        _words.TryGetValue(ownerId, out var words) ? words : NoWords;

    /// <summary>Whether one of this entity's effect words contains the query.</summary>
    internal bool Matches(Guid ownerId, string query)
    {
        if (!_words.TryGetValue(ownerId, out var words)) return false;
        for (var index = 0; index < words.Length; index++)
        {
            if (words[index].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }

    private static void Append(
        Dictionary<Guid, List<string>> collected,
        EntityIdentityCatalogSnapshot catalog,
        Guid ownerId,
        Guid targetId)
    {
        if (targetId == Guid.Empty) return;
        if (!catalog.IsBound || !catalog.TryGet(targetId, out var row)) return;
        Append(collected, ownerId, row.DisplayName);
    }

    private static void Append(Dictionary<Guid, List<string>> collected, Guid ownerId, string word)
    {
        if (ownerId == Guid.Empty || word.Length == 0) return;
        if (!collected.TryGetValue(ownerId, out var words))
        {
            words = new List<string>(4);
            collected.Add(ownerId, words);
        }

        if (!words.Contains(word)) words.Add(word);
    }
}
#endif
