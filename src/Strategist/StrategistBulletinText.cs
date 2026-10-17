using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.Strategy;

namespace OrbAutomata;

/// <summary>
/// Renders a bulletin as plain text: names with short ids, numbers and clocks as the game prints them.
/// Prose is rendered from the bulletin's data; nothing here judges.
/// </summary>
internal static class StrategistBulletinText
{
    internal const string NoBulletin = "no bulletin — a new game is loading";
    internal const string Sections = "goals|position|demand|facts";
    private const int SummaryGoals = 3;

    internal static string Summary(StrategistBulletin? bulletin)
    {
        if (bulletin is null) return NoBulletin;
        var names = new Names(bulletin);
        var text = new StringBuilder();
        text.Append(Header(bulletin)).Append('\n');
        text.Append("Goals   long: ").Append(LongTermSummary(bulletin, names))
            .Append(" · Cross-run capital\n");
        var goals = bulletin.Goals.AsSpan();
        if (goals.Length == 0) text.Append("        short: no reachable gate\n");
        for (var index = 0; index < Math.Min(SummaryGoals, goals.Length); index++)
        {
            text.Append(index == 0 ? "        short: " : "               ")
                .Append(index + 1).Append(' ').Append(GoalLine(in goals[index], names)).Append('\n');
        }
        if (goals.Length > SummaryGoals)
            text.Append("               … ").Append(goals.Length - SummaryGoals)
                .Append(" more — section=goals\n");
        text.Append("Where   ").Append(Where(bulletin.Facts.Clock)).Append('\n');
        text.Append("Demand  ").Append(DemandSummary(bulletin, names)).Append('\n');
        text.Append("more    section=").Append(Sections).Append('\n');
        return text.ToString();
    }

    internal static string Section(StrategistBulletin? bulletin, string section)
    {
        if (bulletin is null) return NoBulletin;
        return section switch
        {
            "goals" => Header(bulletin) + "\n" + Goals(bulletin),
            "position" => Header(bulletin) + "\n" + Position(bulletin),
            "demand" => Header(bulletin) + "\n" + Demand(bulletin),
            "facts" => StrategistFactsText.Write(bulletin.Facts, bulletin.Window),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown strategist section."),
        };
    }

    private static string Header(StrategistBulletin bulletin)
    {
        var clock = bulletin.Facts.Clock;
        return "bulletin · world " +
            bulletin.Facts.WorldGeneration.ToString(CultureInfo.InvariantCulture) + " · " +
            (clock.HasThisReset
                ? Clock(clock.ThisResetSeconds) + " this reset"
                : Clock(clock.TotalSeconds) + " played");
    }

    private static string Goals(StrategistBulletin bulletin)
    {
        var names = new Names(bulletin);
        var text = new StringBuilder("Goals\n");
        text.Append("  Long-term   Finish the world — ").Append(LongTermSummary(bulletin, names));
        var frontier = bulletin.Restoration.Frontier.AsSpan();
        if (frontier.Length > 0)
        {
            text.Append("  (frontier: ");
            for (var index = 0; index < frontier.Length; index++)
            {
                if (index > 0) text.Append(", ");
                text.Append(names.Handle(frontier[index]));
            }
            text.Append(')');
        }
        text.Append('\n');
        text.Append("              Cross-run capital — mastery → Time Advancements next run; Persistent runes")
            .Append("  (no gate ranks here until effects are evaluated)\n");

        var goals = bulletin.Goals.AsSpan();
        if (goals.Length == 0)
        {
            text.Append("  Short-term  no reachable gate\n");
            return text.ToString();
        }
        for (var index = 0; index < goals.Length; index++)
        {
            text.Append(index == 0 ? "  Short-term  " : "              ")
                .Append(index + 1).Append(' ').Append(GoalLine(in goals[index], names));
            var goal = goals[index];
            text.Append(" · ").Append(Rank(goal.Rank));
            if (goal.Opens.Count > 0)
            {
                text.Append(" · opens ");
                var opens = goal.Opens.AsSpan();
                for (var open = 0; open < opens.Length; open++)
                {
                    if (open > 0) text.Append(", ");
                    text.Append(names.Name(opens[open]));
                }
            }
            for (var cost = 0; cost < goal.Costs.Count; cost++)
            {
                if (cost == goal.Binder) continue;
                text.Append("\n                  also ").Append(PriceText(goal.Costs[cost], names));
            }
            text.Append('\n');
        }
        return text.ToString();
    }

    private static string Position(StrategistBulletin bulletin)
    {
        var names = new Names(bulletin);
        var text = new StringBuilder("Position\n");
        text.Append("  Where   ").Append(Where(bulletin.Facts.Clock)).Append('\n');
        text.Append("  Open    ");
        var open = bulletin.Facts.Open.AsSpan();
        if (open.Length == 0) text.Append("nothing");
        for (var index = 0; index < open.Length; index++)
        {
            if (index > 0) text.Append(", ");
            text.Append(open[index].Name);
            if (open[index].HasLevel)
                text.Append(' ').Append(open[index].Current).Append('/').Append(open[index].Maximum);
        }
        text.Append('\n');
        text.Append("  Goal    ");
        if (bulletin.Goals.Count == 0) text.Append("no reachable gate");
        else text.Append(GoalLine(bulletin.Goals[0], names));
        text.Append('\n');
        return text.ToString();
    }

    private static string Demand(StrategistBulletin bulletin)
    {
        var names = new Names(bulletin);
        var text = new StringBuilder("Demand\n");
        var demand = bulletin.Demand.AsSpan();
        if (demand.Length == 0) text.Append("  nothing offered is priced\n");
        var goal = bulletin.Goals.Count > 0 ? bulletin.Goals[0].GateId : Guid.Empty;
        for (var index = 0; index < demand.Length; index++)
        {
            var row = demand[index];
            text.Append("  ").Append(names.Name(row.ResourceId)).Append("   ")
                .Append(IncomeText(bulletin, row.ResourceId)).Append('\n');
            var sinks = row.Sinks.AsSpan();
            for (var sink = 0; sink < sinks.Length; sink++)
            {
                text.Append("    ");
                if (sinks[sink].NodeId == goal) text.Append("GOAL ");
                else if (names.IsGate(sinks[sink].NodeId)) text.Append("gate ");
                text.Append(names.Handle(sinks[sink].NodeId)).Append(' ')
                    .Append(Amount(sinks[sink].Amount)).Append(" · ").Append(Judgment(sinks[sink], names))
                    .Append('\n');
            }
        }
        return text.ToString();
    }

    private static string LongTermSummary(StrategistBulletin bulletin, Names names)
    {
        var restoration = bulletin.Restoration;
        var root = bulletin.Facts.ChainRoot;
        if (!restoration.Published) return "Restoration is not in the published world";
        if (restoration.Finished) return names.Name(root) + " bought";
        var text = new StringBuilder(names.Name(root));
        var requires = restoration.Requires.AsSpan();
        for (var index = 0; index < requires.Length; index++)
            text.Append(index == 0 ? " ← " : ", ").Append(names.Name(requires[index]));
        return text.ToString();
    }

    private static string DemandSummary(StrategistBulletin bulletin, Names names)
    {
        var demand = bulletin.Demand.AsSpan();
        var unreachable = new List<string>();
        for (var index = 0; index < demand.Length; index++)
        {
            var sinks = demand[index].Sinks.AsSpan();
            var any = false;
            for (var sink = 0; sink < sinks.Length; sink++)
            {
                if (sinks[sink].Tier != StrategistCostTier.OutOfReach) continue;
                any = true;
                break;
            }
            if (any) unreachable.Add(names.Name(demand[index].ResourceId));
        }
        var text = demand.Length.ToString(CultureInfo.InvariantCulture) + " currencies with sinks";
        if (unreachable.Count > 0) text += " · out of reach in " + string.Join(", ", unreachable);
        return text;
    }

    private static string GoalLine(in StrategistGoal goal, Names names)
    {
        var text = names.Handle(goal.GateId);
        if (goal.Binder < 0) return text + " — free";
        return text + " — binder " + PriceText(goal.Costs[goal.Binder], names);
    }

    private static string PriceText(in StrategistPrice price, Names names) =>
        names.Name(price.ResourceId) + ": " + Amount(price.Held) + " of " + Amount(price.Amount) +
        " → " + Judgment(in price, names);

    private static string Judgment(in StrategistPrice price, Names names)
    {
        switch (price.Tier)
        {
            case StrategistCostTier.OutOfReach:
                return price.OutOfReach == StrategistOutOfReach.AboveCap
                    ? "out of reach: ×" + Amount(price.Amount / names.Cap(price.ResourceId)) + " over cap"
                    : "out of reach: no income";
            case StrategistCostTier.Measuring:
                return price.IsShort ? "measuring income" : "now (measuring income)";
            case StrategistCostTier.Allocation:
                return price.IsShort ? Amount(price.Amount - price.Held) + " points short" : "allocate now";
            default:
                return (price.IsShort ? Clock(price.EtaSeconds) : "now") + " (" + TierWord(price.Tier) + ")";
        }
    }

    private static string IncomeText(StrategistBulletin bulletin, Guid resourceId)
    {
        foreach (var resource in bulletin.Facts.Resources.AsSpan())
        {
            if (resource.Id == resourceId && resource.Bandwidth) return "points " + Amount(resource.Held) + " free";
        }
        foreach (var income in bulletin.Incomes.AsSpan())
        {
            if (income.ResourceId != resourceId) continue;
            if (!income.Measured)
                return "measuring " + Clock(income.CoveredSeconds) + " of " + Clock(income.RequiredSeconds);
            return (income.LowerBound ? "≥ +" : "+") + Amount(income.PerSecond) + "/s sustained";
        }
        return "no income reading";
    }

    private static string Where(in StrategistClock clock)
    {
        var text = clock.HasThisReset
            ? Clock(clock.ThisResetSeconds) + " this reset · " + Clock(clock.TotalSeconds) + " total"
            : Clock(clock.TotalSeconds) + " played";
        if (clock.ActiveChallenges > 0)
            text += " · " + clock.ActiveChallenges.ToString(CultureInfo.InvariantCulture) +
                (clock.ActiveChallenges == 1 ? " challenge active" : " challenges active");
        return text;
    }

    internal static string TierWord(StrategistCostTier tier) => tier switch
    {
        StrategistCostTier.Trivial => "trivial",
        StrategistCostTier.Cheap => "cheap",
        StrategistCostTier.Notable => "notable",
        StrategistCostTier.Expensive => "expensive",
        StrategistCostTier.OutOfReach => "out of reach",
        StrategistCostTier.Measuring => "measuring",
        StrategistCostTier.Allocation => "allocation",
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    private static string Rank(StrategistGoalRank rank) => rank switch
    {
        StrategistGoalRank.Chain => "on the Restoration chain",
        _ => "off the chain",
    };

    private static string Amount(BigDouble value) => GameScientificNumber.Beautify(value);

    private static string Clock(double seconds) => GameDurationText.UltraPrecise(new BigDouble(seconds));

    private static string Clock(BigDouble seconds) => GameDurationText.UltraPrecise(seconds);

    private sealed class Names
    {
        private readonly Dictionary<Guid, string> _names = new();
        private readonly HashSet<Guid> _gates = new();
        private readonly Dictionary<Guid, BigDouble> _caps = new();

        internal Names(StrategistBulletin bulletin)
        {
            foreach (var resource in bulletin.Facts.Resources.AsSpan())
            {
                _names[resource.Id] = resource.Name;
                _caps[resource.Id] = resource.Cap;
            }
            foreach (var node in bulletin.Facts.Nodes.AsSpan())
            {
                _names[node.Id] = node.Name;
                if (node.Gate) _gates.Add(node.Id);
            }
        }

        internal string Name(Guid id) => _names.TryGetValue(id, out var name) ? name : id.ToString("D");

        internal string Handle(Guid id) =>
            Name(id) + " " + id.ToString("D").Substring(0, EntityIdentityFormatter.HandleLength);

        internal bool IsGate(Guid id) => _gates.Contains(id);

        internal BigDouble Cap(Guid id) => _caps[id];
    }
}
