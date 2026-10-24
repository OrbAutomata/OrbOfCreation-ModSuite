using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One retired unlocker: this <c>GlyphSO</c> is the internal half of this Recipe Book.
/// </summary>
/// <remarks>
/// Authored one way, on <c>GlyphSO.associatedRecipeBook</c>, and non-null on exactly the
/// twenty-five the world publishes no glyph row for. It is published so an id a caller still holds
/// from an older wire resolves to the Recipe Book that answers for it, rather than to nothing.
/// </remarks>
internal readonly struct WorldRecipeBookGlyph
{
    internal WorldRecipeBookGlyph(Guid glyphId, Guid recipeBookId)
    {
        GlyphId = glyphId;
        RecipeBookId = recipeBookId;
    }

    internal Guid GlyphId { get; }

    internal Guid RecipeBookId { get; }
}

internal static class WorldRecipeBookGlyphLookup
{
    internal static readonly Comparison<WorldRecipeBookGlyph> Order =
        static (left, right) => left.GlyphId.CompareTo(right.GlyphId);

    /// <summary>The Recipe Book this retired glyph id is the internal half of.</summary>
    internal static bool TryFindBook(
        PublicationTable<WorldRecipeBookGlyph> table,
        Guid glyphId,
        out Guid recipeBookId)
    {
        var found = WorldLookup.TryFindFirst(table.AsSpan(), glyphId,
            static (row, key) => row.GlyphId.CompareTo(key), out var row);
        recipeBookId = found ? row.RecipeBookId : Guid.Empty;
        return found;
    }
}

/// <summary>
/// Reads the authored glyph-to-book link once per pass.
/// </summary>
/// <remarks>
/// Published whole or withheld whole. A partial table cannot be told apart from a build where fewer
/// glyphs carry a book, and the one thing this table is for — answering an id the world publishes
/// no row for — is exactly the question a half-read table would answer wrongly.
/// </remarks>
internal sealed class WorldRecipeBookGlyphReader : IWorldCategoryReader
{
    private readonly Type? _glyphType;
    private readonly string _unavailable;
    private readonly Func<object, Guid>? _glyphIdentity;
    private readonly Func<object, Guid>? _associatedRecipeBook;
    private readonly Func<System.Collections.IList?>? _registry;

    internal WorldRecipeBookGlyphReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _glyphType = resolveType("GlyphSO");
        if (_glyphType is null)
        {
            _unavailable = "the GlyphSO type was not found on this build";
            return;
        }

        var glyph = new WorldMemberBinding(_glyphType, "GlyphSO");
        _glyphIdentity = glyph.StableUuid();
        _associatedRecipeBook = glyph.ReferenceGuid("associatedRecipeBook");
        _registry = NativeAccessorBinder.StaticListAccessor(_glyphType, "All");
        _unavailable = _registry is null
            ? "the GlyphSO registry was unreadable"
            : glyph.Failure;
    }

    public string Category => "recipe book glyphs";

    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.RecipeBookGlyphs;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var glyphs = _registry!();
        if (glyphs is null) return Withheld(buffer, "the GlyphSO registry was unreadable");

        var sampled = 0;
        for (var index = 0; index < glyphs.Count; index++)
        {
            var glyph = glyphs[index];
            if (glyph is null) continue;
            var id = Guid.Empty;
            try
            {
                var book = _associatedRecipeBook!(glyph);
                if (book == Guid.Empty) continue;
                id = _glyphIdentity!(glyph);
                if (id == Guid.Empty)
                    return Withheld(buffer, "a glyph carrying a recipe book had no usable identity");
                buffer.Append(new WorldRecipeBookGlyph(id, book));
                sampled++;
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                return Withheld(
                    buffer, WorldFailures.ReadFault("reading a glyph's recipe book threw", ex, id));
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, 0, string.Empty);
    }

    private WorldCategoryReport Withheld(
        WorldRelationBuffer<WorldRecipeBookGlyph> buffer,
        WorldCollectionFailure reason)
    {
        buffer.Reset();
        return WorldCategoryReport.Missing(Category, "the authored glyph-to-book link was withheld: " + reason);
    }
}
