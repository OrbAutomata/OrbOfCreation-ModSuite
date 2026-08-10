using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One category's availability evidence carried inside the same immutable publication as its rows.
/// </summary>
/// <remarks>
/// An empty category is otherwise ambiguous: it can mean the save owns no rows or that a native
/// type/member could not be bound. External readers must not turn the latter into a gameplay fact.
/// This row makes the distinction atomic with the world it describes.
/// </remarks>
internal readonly struct WorldCollectionCategoryStatus
{
    internal WorldCollectionCategoryStatus(
        string category,
        WorldCategoryOutcome outcome,
        int sampled,
        int skipped,
        string firstFailure,
        long elapsedTicks)
    {
        Category = category;
        Outcome = outcome;
        Sampled = sampled;
        Skipped = skipped;
        FirstFailure = firstFailure;
        ElapsedTicks = elapsedTicks;
    }

    internal string Category { get; }
    internal WorldCategoryOutcome Outcome { get; }
    internal int Sampled { get; }
    internal int Skipped { get; }
    internal string FirstFailure { get; }

    /// <summary>
    /// Raw <see cref="System.Diagnostics.Stopwatch"/> ticks the pass that published this world spent
    /// reading this category.
    /// </summary>
    /// <remarks>
    /// What a category cost is a fact about the collection that produced the world, and it travels
    /// with the world for the same reason its availability does: a reader holding the snapshot can
    /// say where the pass went without a second artifact and without inferring it from a total.
    /// </remarks>
    internal long ElapsedTicks { get; }

    internal bool IsClean => Outcome == WorldCategoryOutcome.Collected && Skipped == 0;

    internal static PublicationTable<WorldCollectionCategoryStatus> Build(
        WorldCollectionReport report)
    {
        var categories = report.Categories;
        if (categories.Length == 0)
            return PublicationTable<WorldCollectionCategoryStatus>.Empty;
        var rows = new WorldCollectionCategoryStatus[categories.Length];
        for (var index = 0; index < categories.Length; index++)
        {
            var category = categories[index];
            rows[index] = new WorldCollectionCategoryStatus(
                category.Category,
                category.Outcome,
                category.Sampled,
                category.Skipped,
                category.FirstFailure,
                category.ElapsedTicks);
        }
        return PublicationTable<WorldCollectionCategoryStatus>.Create(rows, rows.Length);
    }
}
