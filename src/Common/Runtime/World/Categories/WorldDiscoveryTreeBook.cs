using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One authored edge: this discovery tree may offer what this Recipe Book carries.
/// </summary>
/// <remarks>
/// <c>DiscoveryTreeSO.availableRecipeBooks</c> is a <c>RecipeBookListVariable</c>, and
/// <c>UIDiscoveryTreePage.UIStart()</c> is the only code that reads it — the page binds that list
/// and draws one <c>UIRecipeBookItem</c> per book. So this one edge answers both questions a caller
/// asks about a book: which pool it widens, and where the game draws it.
/// </remarks>
internal readonly struct WorldDiscoveryTreeBook
{
    internal WorldDiscoveryTreeBook(Guid recipeBookId, Guid treeId)
    {
        RecipeBookId = recipeBookId;
        TreeId = treeId;
    }

    internal Guid RecipeBookId { get; }

    internal Guid TreeId { get; }
}

internal static class WorldDiscoveryTreeBookDeriver
{
    internal static PublicationTable<WorldDiscoveryTreeBook> Build(
        WorldRelationBuffer<WorldDiscoveryTreeBook> buffer) =>
        WorldScribeRelationDeriver.Build(
            buffer,
            static (left, right) =>
            {
                var book = left.RecipeBookId.CompareTo(right.RecipeBookId);
                return book != 0 ? book : left.TreeId.CompareTo(right.TreeId);
            });
}

internal static class WorldDiscoveryTreeBookLookup
{
    /// <summary>The trees one book widens, as a contiguous range of the sorted table.</summary>
    internal static bool TryFindRange(
        PublicationTable<WorldDiscoveryTreeBook> table,
        Guid recipeBookId,
        out int start,
        out int count)
    {
        var low = 0;
        var high = table.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = table[middle].RecipeBookId.CompareTo(recipeBookId);
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
        while (end < table.Count && table[end].RecipeBookId == recipeBookId) end++;
        count = end - found;
        return true;
    }
}

/// <summary>
/// Reads every discovery tree's authored book list once per lifecycle.
/// </summary>
/// <remarks>
/// Published whole or withheld whole, like the memberships beside it: a partial table cannot be told
/// apart from a build where a tree draws on fewer books, and "this book widens nothing" is a fact a
/// consumer is entitled to state. A tree with an empty list contributes no edge and is not a
/// failure — the pinned build ships one, the Time Rune tree.
/// </remarks>
internal sealed class WorldDiscoveryTreeBookReader : IWorldCategoryReader
{
    private readonly Type? _treeType;
    private readonly Type? _bookType;
    private readonly string _unavailable;
    private readonly Func<object, Guid>? _treeIdentity;
    private readonly Func<object, Guid>? _bookIdentity;
    private readonly Func<object, object?>? _bookList;
    private readonly Func<object, System.Collections.IList?>? _members;
    private readonly Func<System.Collections.IList?>? _registry;

    internal WorldDiscoveryTreeBookReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _treeType = resolveType("DiscoveryTreeSO");
        _bookType = resolveType("RecipeBookSO");
        var listType = resolveType("RecipeBookListVariable");
        if (_treeType is null || _bookType is null || listType is null)
        {
            _unavailable = _treeType is null
                ? "the DiscoveryTreeSO type was not found on this build"
                : _bookType is null
                    ? "the RecipeBookSO type was not found on this build"
                    : "the RecipeBookListVariable type was not found on this build";
            return;
        }

        var tree = new WorldMemberBinding(_treeType, "DiscoveryTreeSO");
        _treeIdentity = tree.Call<Guid>("GetGuid");
        var book = new WorldMemberBinding(_bookType, "RecipeBookSO");
        _bookIdentity = book.Call<Guid>("GetGuid");
        _bookList = NativeAccessorBinder.Reference(_treeType, "availableRecipeBooks");
        _members = NativeAccessorBinder.CollectionField(listType, "value");
        _registry = NativeAccessorBinder.StaticListAccessor(_treeType, "All");
        _unavailable = _bookList is null
            ? "DiscoveryTreeSO did not expose availableRecipeBooks on this build"
            : _members is null
                ? "RecipeBookListVariable did not expose its authored membership on this build"
                : _registry is null
                    ? "the DiscoveryTreeSO registry was unreadable"
                    : tree.Failure.Length > 0
                        ? tree.Failure
                        : book.Failure;
    }

    public string Category => "discovery tree books";

    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.DiscoveryTreeBooks;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var trees = _registry!();
        if (trees is null) return Withheld(buffer, "the DiscoveryTreeSO registry was unreadable");

        var sampled = 0;
        for (var index = 0; index < trees.Count; index++)
        {
            var tree = trees[index];
            if (tree is null) continue;
            try
            {
                var treeId = _treeIdentity!(tree);
                if (treeId == Guid.Empty)
                    return Withheld(buffer, "a discovery tree had no usable identity");

                var list = _bookList!(tree);
                if (list is null) continue;

                var members = _members!(list);
                for (var member = 0; member < (members?.Count ?? 0); member++)
                {
                    var entry = members![member];
                    if (entry is null || entry.GetType() != _bookType)
                    {
                        return Withheld(
                            buffer,
                            "discovery tree " + treeId.ToString("D") +
                            " carried a book list member that is not a RecipeBookSO");
                    }

                    var bookId = _bookIdentity!(entry);
                    if (bookId == Guid.Empty)
                    {
                        return Withheld(
                            buffer,
                            "discovery tree " + treeId.ToString("D") +
                            " carried a book with no usable identity");
                    }

                    buffer.Append(new WorldDiscoveryTreeBook(bookId, treeId));
                    sampled++;
                }
            }
            catch (Exception ex)
            {
                return Withheld(
                    buffer,
                    "reading a discovery tree's book list threw: " + ex.GetBaseException().Message);
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, 0, string.Empty);
    }

    private WorldCategoryReport Withheld(
        WorldRelationBuffer<WorldDiscoveryTreeBook> buffer,
        string reason)
    {
        buffer.Reset();
        return new WorldCategoryReport(
            Category,
            WorldCategoryOutcome.Collected,
            sampled: 0,
            skipped: 0,
            "the authored tree-to-book link was withheld: " + reason);
    }
}
