#if SERVICE_CYCLE_PROFILE
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// What the collection pass behind the published world spent, category by category.
/// </summary>
/// <remarks>
/// <para>
/// Collection is the suite's largest main-thread cost, and until this page existed the only way to
/// read where a pass went was to record a full trace and open the offline dashboard. A session
/// driving the game through the MCP has neither: the dashboard's cards sit below a fold no verb can
/// scroll, and the numbers on them are below the resolution of the one capture verb. So the fact was
/// measured, published, rendered — and unreachable from the seat that needed it.
/// </para>
/// <para>
/// Nothing here measures anything. Every duration is the one the collector already charged the
/// category on the pass that produced this world, carried on the publication beside that category's
/// availability evidence; this only converts ticks to milliseconds and sorts. The window is
/// therefore one pass rather than a session distribution: a mean, a median and a worst are a fact
/// about many passes, nothing at runtime folds them, and inventing that fold here would add the
/// main-thread bookkeeping this page exists to report on. The session distribution stays the offline
/// dashboard's answer.
/// </para>
/// <para>
/// A category that charged nothing is named rather than dropped. A structural category is read once
/// per lifecycle epoch, so it is absent from the pass rather than free within it, and a page that
/// silently omitted it would let a reader conclude the collector had stopped running it.
/// </para>
/// <para>
/// Every span is named for the world_categories row it feeds rather than for what the collector
/// calls itself. Sixteen of the seventy-four disagreed, so a reader who found a dear collector here
/// had to guess which table it was, and two rounds running the guess was made by matching row
/// counts — wrongly for six pairs, and impossibly for the two that no count separated. Where one
/// collector feeds several rows it names all of them, and where two feed one row each names itself
/// beside it, because a row name printed twice reads as one collector measured twice.
/// </para>
/// <para>
/// The header counts publications. It used to call that count a generation, which is also the word
/// for the lifecycle invalidation token verbs echo, and the two ran three orders of magnitude apart
/// in one session with nothing on the page to say which was which.
/// </para>
/// </remarks>
internal static class GameMcpCollectionSpans
{
    internal static string Describe(GameMcpFrameContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (!GameMcpWorldQuery.IsWorldPublished(context))
        {
            return "collection: unavailable\n" +
                "collection reason: no world is published, so no collection pass has reported " +
                "what it cost";
        }

        var world = context.World!.Snapshot;
        var reports = world.CollectionCategories;
        if (reports.Count == 0)
        {
            return "collection: unavailable\n" +
                "collection reason: the published world carries no collection report";
        }

        var totalTicks = 0L;
        var rows = 0;
        var order = new int[reports.Count];
        for (var index = 0; index < reports.Count; index++)
        {
            var report = reports[index];
            order[index] = index;
            totalTicks += report.ElapsedTicks;
            rows += report.Sampled;
        }

        Array.Sort(order, (left, right) =>
        {
            var first = reports[left];
            var second = reports[right];
            var byCost = second.ElapsedTicks.CompareTo(first.ElapsedTicks);
            return byCost != 0
                ? byCost
                : string.Compare(first.Category, second.Category, StringComparison.Ordinal);
        });

        var text = new StringBuilder("collection: ")
            .Append(Milliseconds(totalTicks))
            .Append(" ms across ")
            .Append(reports.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" categories, ")
            .Append(rows.ToString(CultureInfo.InvariantCulture))
            .Append(" rows, world publication ")
            .Append(context.World.Generation.Value.ToString(CultureInfo.InvariantCulture));

        var free = new StringBuilder();
        var unavailable = new StringBuilder();
        for (var index = 0; index < order.Length; index++)
        {
            var report = reports[order[index]];
            var name = GameMcpWorldQuery.CollectionReportPage(
                GameMcpWorldQuery.Normalize(report.Category));
            if (report.Outcome == WorldCategoryOutcome.Unavailable)
            {
                Name(unavailable, name);
                continue;
            }
            if (report.ElapsedTicks <= 0)
            {
                Name(free, name);
                continue;
            }
            text.Append("\n  ")
                .Append(name)
                .Append(": ")
                .Append(Milliseconds(report.ElapsedTicks))
                .Append(" ms, ")
                .Append(report.Sampled.ToString(CultureInfo.InvariantCulture))
                .Append(" rows");
        }

        if (free.Length > 0)
            text.Append("\n  charged nothing this pass: ").Append(free);
        if (unavailable.Length > 0)
            text.Append("\n  unavailable: ").Append(unavailable);
        return text.ToString();
    }

    private static void Name(StringBuilder text, string category)
    {
        if (text.Length > 0) text.Append(", ");
        text.Append(category);
    }

    private static string Milliseconds(long ticks) =>
        (ticks * 1000.0 / Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture);
}
#endif
