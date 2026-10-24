#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWorldCategories
{
    internal static readonly GameMcpEntityCapabilityDescriptor[] Categories = CreateCategories();
    internal static readonly Dictionary<string, GameMcpEntityCapabilityDescriptor> ByName = IndexCategories();

    /// <summary>
    /// Every collection report at least one listable category is built from.
    /// </summary>
    /// <remarks>
    /// The collector runs more categories than this surface lists, and a listable category is often
    /// built from several of them, so the two counts never matched and nothing said which collectors
    /// the difference was. A collector missing from this set publishes no table of its own, and
    /// <see cref="GameMcpWorldOverview.ListCategories"/> gives it a row saying exactly that rather than
    /// leaving it to be inferred from arithmetic in a diagnostic nobody runs.
    /// </remarks>
    internal static readonly HashSet<string> ListedReportCategories = CollectReportCategories();

    /// <summary>
    /// What a diagnostic calls each collection report, in the words this surface answers to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both surfaces describe the same collectors, and until this join was published they described
    /// them under two vocabularies: six collectors named for a table since renamed, two more no row
    /// count could separate, and two whose names appeared on no row at all. The join was already
    /// declared on the categories themselves — every listable page names the reports it is built
    /// from — and read by nothing but a census. Reading it here is the whole of the fix, because
    /// world_categories is where a name becomes an action and the acting surface's words win.
    /// </para>
    /// <para>
    /// A report that is itself a row name keeps it. Other pages read it too, and naming them here
    /// would put three names on the dearest span of a pass to answer a question nobody asked of it.
    /// A report that feeds several pages and names none of them says all of them, because choosing
    /// one would be this code's opinion rather than the world's structure. And where two reports
    /// feed one page, each says which of the two it is: a row name printed twice reads as one
    /// collector measured twice.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> CollectionReportPages = MapReportsToPages();

    /// <summary>
    /// Every page a collection report is read into, so a caller who named the report can be sent to
    /// the pages that carry its rows rather than told the name means nothing.
    /// </summary>
    internal static readonly Dictionary<string, string[]> ListedReportPageNames = MapReportsToPageNames();

    /// <summary>
    /// The world_categories row <paramref name="report"/> feeds, or its own name where no listable
    /// page is built from it and the page therefore gives it a row of that name.
    /// </summary>
    internal static string CollectionReportPage(string report) =>
        CollectionReportPages.TryGetValue(report, out var page) ? page : report;

    /// <summary>Which listable pages each collection report is read into.</summary>
    private static Dictionary<string, List<string>> FedReports()
    {
        var fed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 0; index < Categories.Length; index++)
        {
            var category = Categories[index];
            for (var required = 0; required < category.ReportCategories.Length; required++)
                Feeds(fed, category.ReportCategories[required], category.Name);
            for (var only = 0; only < category.FailureOnlyReportCategories.Length; only++)
                Feeds(fed, category.FailureOnlyReportCategories[only], category.Name);
        }
        return fed;
    }

    private static Dictionary<string, string[]> MapReportsToPageNames()
    {
        var fed = FedReports();
        var result = new Dictionary<string, string[]>(fed.Count, StringComparer.Ordinal);
        foreach (var pair in fed) result.Add(pair.Key, pair.Value.ToArray());
        return result;
    }

    private static Dictionary<string, string> MapReportsToPages()
    {
        var fed = FedReports();
        var alone = new Dictionary<string, string>(fed.Count, StringComparer.Ordinal);
        foreach (var pair in fed)
        {
            if (ByName.ContainsKey(pair.Key)) alone.Add(pair.Key, pair.Key);
            else if (pair.Value.Count == 1) alone.Add(pair.Key, pair.Value[0]);
        }

        var feeders = new Dictionary<string, int>(alone.Count, StringComparer.Ordinal);
        foreach (var pair in alone)
        {
            feeders.TryGetValue(pair.Value, out var count);
            feeders[pair.Value] = count + 1;
        }

        var result = new Dictionary<string, string>(fed.Count, StringComparer.Ordinal);
        foreach (var pair in fed)
        {
            if (!alone.TryGetValue(pair.Key, out var page))
                result.Add(pair.Key, string.Join("+", pair.Value.ToArray()));
            else if (page == pair.Key || feeders[page] == 1)
                result.Add(pair.Key, page);
            else
                result.Add(pair.Key, page + " (" + pair.Key + ")");
        }
        return result;
    }

    /// <summary>
    /// Records that <paramref name="page"/> is built from <paramref name="report"/>. The pages
    /// arrive in the order <see cref="GameMcpWorldOverview.ListCategories"/> prints them, so a report
    /// feeding several names them in the order a reader will find them on that page.
    /// </summary>
    private static void Feeds(
        Dictionary<string, List<string>> fed,
        string report,
        string page)
    {
        if (!fed.TryGetValue(report, out var pages)) fed.Add(report, pages = new List<string>());
        if (!pages.Contains(page)) pages.Add(page);
    }

    private static HashSet<string> CollectReportCategories()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Categories.Length; index++)
        {
            var category = Categories[index];
            for (var required = 0; required < category.ReportCategories.Length; required++)
                result.Add(category.ReportCategories[required]);
            for (var only = 0; only < category.FailureOnlyReportCategories.Length; only++)
                result.Add(category.FailureOnlyReportCategories[only]);
        }
        return result;
    }

    internal static GameMcpCategoryAvailability Availability(
        GameWorldState world,
        GameMcpEntityCapabilityDescriptor category)
    {
        for (var requiredIndex = 0;
             requiredIndex < category.ReportCategories.Length;
             requiredIndex++)
        {
            var reportName = category.ReportCategories[requiredIndex];
            var found = false;
            for (var index = 0; index < world.CollectionCategories.Count; index++)
            {
                var report = world.CollectionCategories[index];
                if (!string.Equals(
                        Normalize(report.Category),
                        reportName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                found = true;
                if (report.Outcome == WorldCategoryOutcome.Unavailable)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName + " unavailable"
                            : report.FirstFailure);
                }
                if (report.Skipped > 0)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        "collection is partial: " +
                        report.Skipped.ToString(CultureInfo.InvariantCulture) +
                        " native rows were skipped from " + reportName +
                        "; first failure: " +
                        (report.FirstFailure.Length == 0
                            ? "the collector did not publish a failure reason"
                            : report.FirstFailure));
                }
                break;
            }
            if (!found)
            {
                return new GameMcpCategoryAvailability(
                    false,
                    "the publication carries no collection report for " + reportName);
            }
        }

        for (var blockerIndex = 0;
             blockerIndex < category.FailureOnlyReportCategories.Length;
             blockerIndex++)
        {
            var reportName = category.FailureOnlyReportCategories[blockerIndex];
            for (var index = 0; index < world.CollectionCategories.Count; index++)
            {
                var report = world.CollectionCategories[index];
                if (!string.Equals(
                        Normalize(report.Category),
                        reportName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (report.Outcome == WorldCategoryOutcome.Unavailable ||
                    report.Skipped > 0)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName +
                              " degraded without a failure reason"
                            : report.FirstFailure);
                }
                break;
            }
        }

        return new GameMcpCategoryAvailability(true, string.Empty);
    }

    /// <summary>
    /// The page the Scholar screen's Concept list is read as. A Concept is an
    /// <c>AlchemyRecipeSO</c> and is not an Alchemy recipe in any sense a player meets: it is
    /// discovered on its own tree, slotted into development slots, and drains while it levels.
    /// Concepts are the seventh discovery surface, so they are a category beside the other six
    /// rather than rows inside one of them.
    /// </summary>
    internal const string ConceptsCategory = "concepts";

    /// <summary>
    /// Whether an alchemy recipe is one of the forty-six Concepts, from the recipe's own core
    /// alchemy type. Same audited mapping the discovery boundary classifies the press with, so the
    /// category a row prints and the screen that draws it cannot disagree.
    /// </summary>
    internal static bool IsScholarConcept(Guid coreTypeId) =>
        AlchemyGameplayDomainClassifier.ClassifyTypeUuid(coreTypeId) ==
            AlchemyGameplayDomain.ScholarConcept;

    /// <summary>
    /// Which of the two categories an alchemy recipe files under. A type in neither audited set
    /// stays on <c>alchemy-recipes</c>, where every recipe the world publishes has always been
    /// readable, rather than vanishing from both pages.
    /// </summary>
    internal static string AlchemyRecipeCategory(Guid coreTypeId) =>
        IsScholarConcept(coreTypeId) ? ConceptsCategory : "alchemy-recipes";

    internal static bool TryCategory(
        string name,
        out GameMcpEntityCapabilityDescriptor category,
        out string reason)
    {
        var normalized = Normalize(name);
        if (ByName.TryGetValue(normalized, out category!))
        {
            reason = string.Empty;
            return true;
        }
        category = null!;
        reason = "unknown category '" + (name ?? string.Empty) + "'; " +
            (RetiredCategoryNames.TryGetValue(normalized, out var moved)
                ? moved
                : "call world_categories for the exact discoverable names");
        return false;
    }

    /// <summary>
    /// `glyphs` published two player concepts under one native class, and each half is its own
    /// category now, so the refusal names both rather than sending the caller back to the list.
    /// </summary>
    private static readonly Dictionary<string, string> RetiredCategoryNames =
        new(StringComparer.Ordinal)
        {
            ["glyphs"] =
                "it named two things and is now two categories. 'augment-glyphs' is the " +
                "twenty-two a caster sockets into a spell, whose level buys slots. " +
                "'recipe-books' is the thirty-four tiles that widen a discovery pool",
            ["concept-recipes"] =
                "the forty-six Concepts are a category of their own now: list 'concepts'. This " +
                "name republished them beside the seventy-nine Alchemy recipes, and the Scholar " +
                "screen draws them nowhere near the Alchemy screen",
            ["structures"] =
                "the screen calls these attributes and so does the wire: list 'attributes'. " +
                "StructureSO is the game's class name for them, not a word any screen draws",
        };

    internal readonly struct GameMcpCategoryAvailability
    {
        internal GameMcpCategoryAvailability(bool available, string reason)
        {
            Available = available;
            Reason = reason ?? string.Empty;
        }

        internal bool Available { get; }
        internal string Reason { get; }
    }

    private static GameMcpEntityCapabilityDescriptor[] CreateCategories()
    {
        var entries = GameMcpEntityCapabilityMap.Entries;
        var result = new GameMcpEntityCapabilityDescriptor[entries.Count];
        for (var index = 0; index < entries.Count; index++) result[index] = entries[index];
        Array.Sort(result, static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return result;
    }

    private static Dictionary<string, GameMcpEntityCapabilityDescriptor> IndexCategories()
    {
        var result = new Dictionary<string, GameMcpEntityCapabilityDescriptor>(StringComparer.Ordinal);
        foreach (var category in Categories) result.Add(category.Name, category);
        return result;
    }

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new System.Text.StringBuilder(value.Length + 8);
        var previousWasSeparator = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '-' || character == '_' || char.IsWhiteSpace(character))
            {
                if (!previousWasSeparator) result.Append('-');
                previousWasSeparator = true;
                continue;
            }
            if (char.IsUpper(character) && !previousWasSeparator && result.Length > 0)
                result.Append('-');
            result.Append(char.ToLowerInvariant(character));
            previousWasSeparator = false;
        }
        if (result.Length > 0 && result[result.Length - 1] == '-')
            result.Length--;
        return result.ToString();
    }

    internal static string[] RegisteredCategoryNames()
    {
        var result = new string[Categories.Length];
        for (var index = 0; index < Categories.Length; index++)
            result[index] = Categories[index].Name;
        return result;
    }

    /// <summary>
    /// The collection reports a listable category is built from, so a census can state which
    /// collectors this surface reaches through a table and which need a row of their own.
    /// </summary>
    internal static string[] ListedCollectionReportNames()
    {
        var result = new string[ListedReportCategories.Count];
        ListedReportCategories.CopyTo(result);
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }
}

#endif
