using System;
using System.Collections.Generic;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.Strategy;

namespace OrbModConfig;

internal readonly record struct StrategistTextCard(string Title, string Body);

/// <summary>Player copy over published judgments; this surface makes no gameplay decisions.</summary>
internal static class StrategistPresentation
{
    internal static IReadOnlyList<StrategistTextCard> Build(StrategistBulletin? bulletin, string statusReason)
    {
        var cards = new List<StrategistTextCard>();
        if (bulletin is null)
        {
            cards.Add(new StrategistTextCard("Strategist", string.IsNullOrEmpty(statusReason)
                ? "Waiting for the first world snapshot. Advice starts when the strategist has a world to judge."
                : statusReason));
            return cards;
        }
        var names = new Names(bulletin);
        var clock = bulletin.Facts.Clock;
        var captured = "Captured at " + Clock(clock.TotalSeconds) + " played" +
            (clock.HasThisReset ? " · " + Clock(clock.ThisResetSeconds) + " this reset" : string.Empty);
        if (!string.IsNullOrEmpty(statusReason)) captured += "\nSnapshot retained: " + statusReason;
        captured += "\nAdvice updates about once a minute. Estimates use captured sustained income, assume other spending stops, and include bursts; they are not purchase promises.\nTo opt in to goal priority for eligible upgrades, open Auto Buy and enable Prioritize strategist goal.";
        cards.Add(new StrategistTextCard("Your next step", captured));
        var advice = bulletin.Advice;
        var body = new StringBuilder();
        if (advice.Kind == StrategistAdviceKind.NoGoal)
            body.Append("No offered progression gate is currently ranked.");
        else
        {
            body.Append(Action(advice.Kind)).Append(" — ").Append(names.Get(advice.GoalId));
            if (advice.BottleneckResourceId != Guid.Empty)
                body.Append("\nBottleneck: ").Append(names.Get(advice.BottleneckResourceId));
            foreach (var cost in advice.Costs.AsSpan())
                body.Append("\n").Append(Cost(cost.Price, names)).Append(" · ").Append(Step(cost));
            var goal = bulletin.Goals[0];
            body.Append("\n").Append(Relevance(goal.Rank));
            AppendOpens(body, goal, names);
        }
        cards.Add(new StrategistTextCard("Active goal", body.ToString()));
        var goals = bulletin.Goals.AsSpan();
        for (var i = 1; i < goals.Length; i++)
        {
            var goal = goals[i];
            body.Clear().Append(Relevance(goal.Rank));
            foreach (var cost in goal.Costs.AsSpan())
                body.Append("\n").Append(Cost(cost, names)).Append(" · ").Append(PriceStatus(cost));
            if (goal.Costs.Count == 0) body.Append("\nNo resource cost in this snapshot.");
            AppendOpens(body, goal, names);
            cards.Add(new StrategistTextCard("Alternative " + (i + 1) + " · " + names.Get(goal.GateId), body.ToString()));
        }
        var restoration = bulletin.Restoration;
        body.Clear();
        if (!restoration.Published) body.Append("Restoration is not present in the published world.");
        else if (restoration.Finished) body.Append(names.Get(bulletin.Facts.ChainRoot)).Append(" is bought or committed.");
        else
        {
            body.Append("Offered gates related to captured Restoration prerequisites: ");
            var frontier = restoration.Frontier.AsSpan();
            if (frontier.Length == 0) body.Append("none in this snapshot");
            for (var i = 0; i < frontier.Length; i++)
                body.Append(i == 0 ? string.Empty : ", ").Append(names.Get(frontier[i]));
            body.Append(".\nPrerequisite references may be alternatives or unknown rules, rather than mandatory steps. They do not estimate the best route or the value of an upgrade's effects.");
        }
        cards.Add(new StrategistTextCard("Finish the world", body.ToString()));
        body.Clear();
        foreach (var income in bulletin.Incomes.AsSpan())
        {
            if (body.Length > 0) body.Append('\n');
            body.Append(names.Get(income.ResourceId)).Append(": ");
            if (!income.Measured)
                body.Append("measuring ").Append(Clock(income.CoveredSeconds)).Append(" of ").Append(Clock(income.RequiredSeconds));
            else
                body.Append(income.LowerBound ? "at least " : string.Empty).Append(Amount(income.PerSecond)).Append("/s sustained");
        }
        foreach (var resource in bulletin.Facts.Resources.AsSpan())
        {
            if (!resource.Bandwidth || !resource.Visible) continue;
            if (body.Length > 0) body.Append('\n');
            body.Append(resource.Name).Append(": ").Append(Amount(resource.Held)).Append(" available allocation points");
        }
        cards.Add(new StrategistTextCard("Income and allocation", body.Length == 0 ? "No income readings in this snapshot." : body.ToString()));
        body.Clear().Append(clock.ActiveChallenges).Append(clock.ActiveChallenges == 1 ? " active challenge" : " active challenges");
        foreach (var open in bulletin.Facts.Open.AsSpan())
        {
            body.Append("\n").Append(open.Name);
            if (open.HasLevel) body.Append(' ').Append(open.Current).Append('/').Append(open.Maximum);
        }
        cards.Add(new StrategistTextCard("Your position", body.ToString()));
        return cards;
    }

    private static string Action(StrategistAdviceKind kind) => kind switch
    {
        StrategistAdviceKind.Ready => "Ready in this snapshot",
        StrategistAdviceKind.SaveResources => "Save resources",
        StrategistAdviceKind.IncreaseCapacity => "Increase storage capacity",
        StrategistAdviceKind.ObtainIncome => "Find a source of income",
        StrategistAdviceKind.MeasureIncome => "Wait for income measurement",
        StrategistAdviceKind.AllocatePoints => "Gain allocation points",
        _ => "No ranked goal",
    };

    private static string Step(in StrategistAdviceCost cost) => cost.Kind switch
    {
        StrategistAdviceKind.Ready => "cost covered",
        StrategistAdviceKind.IncreaseCapacity => "capacity " + Amount(cost.Capacity) + "; raise it to at least " + Amount(cost.Price.Amount),
        StrategistAdviceKind.ObtainIncome => "no income measured in this window; find or activate a source",
        StrategistAdviceKind.MeasureIncome => "income is still being measured",
        StrategistAdviceKind.AllocatePoints => "allocation points required; no time estimate",
        _ => "estimated wait " + Clock(cost.Price.EtaSeconds),
    };

    private static string Cost(in StrategistPrice price, Names names) => names.Get(price.ResourceId) + ": " +
        Amount(price.Held) + " / " + Amount(price.Amount) +
        (price.IsShort ? " · missing " + Amount(price.Amount - price.Held) : " · covered");

    private static string PriceStatus(in StrategistPrice price)
    {
        if (!price.IsShort) return "cost covered";
        return price.Tier switch
        {
            StrategistCostTier.OutOfReach => price.OutOfReach == StrategistOutOfReach.AboveCap ? "above storage capacity" : "no measured income",
            StrategistCostTier.Measuring => "measuring income",
            StrategistCostTier.Allocation => "allocation points required",
            _ => "estimated wait " + Clock(price.EtaSeconds),
        };
    }

    private static string Relevance(StrategistGoalRank rank) => rank == StrategistGoalRank.Chain
        ? "Relevant to captured Restoration prerequisites."
        : "An offered progression gate outside the captured Restoration references.";

    private static void AppendOpens(StringBuilder text, in StrategistGoal goal, Names names)
    {
        var opens = goal.Opens.AsSpan();
        if (opens.Length == 0) return;
        text.Append("\nReferenced by prerequisite rules: ");
        for (var i = 0; i < opens.Length; i++) text.Append(i == 0 ? string.Empty : ", ").Append(names.Get(opens[i]));
    }

    private static string Amount(BigDouble value) => GameScientificNumber.Beautify(value);
    private static string Clock(double seconds) => Clock(new BigDouble(seconds));
    private static string Clock(BigDouble seconds) => GameDurationText.UltraPrecise(seconds);

    private sealed class Names
    {
        private readonly Dictionary<Guid, string> _names = new();
        internal Names(StrategistBulletin bulletin)
        {
            foreach (var node in bulletin.Facts.Nodes.AsSpan()) _names[node.Id] = node.Name;
            foreach (var resource in bulletin.Facts.Resources.AsSpan()) _names[resource.Id] = resource.Name;
        }
        internal string Get(Guid id) => _names.TryGetValue(id, out var name) ? name : "Unknown published entity";
    }
}
