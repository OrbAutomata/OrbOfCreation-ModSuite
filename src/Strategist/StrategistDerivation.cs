using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Strategy;

namespace OrbAutomata;

/// <summary>
/// The strategist's rules: a pure function from the facts and the income window to a bulletin.
/// </summary>
internal static class StrategistDerivation
{
    internal const double TrivialSeconds = 10d;
    internal const double CheapSeconds = 60d;
    internal const double NotableSeconds = 3600d;

    internal static StrategistBulletin Derive(StrategistFacts facts, StrategistWindowReading window)
    {
        if (facts is null) throw new ArgumentNullException(nameof(facts));
        if (window is null) throw new ArgumentNullException(nameof(window));
        var context = new Context(facts, window);
        var restoration = Restoration(context, out var chain);
        var goals = Goals(context, chain);
        return new StrategistBulletin(
            facts,
            window,
            PublicationTable<StrategistIncome>.Create(context.IncomeRows),
            restoration,
            PublicationTable<StrategistGoal>.Create(goals),
            PublicationTable<StrategistDemand>.Create(Demand(context, goals)),
            Advice(context, goals));
    }

    /// <summary>
    /// Explains the first ranked goal's costs. A held price is ready even while income is measuring;
    /// only a short price needs an accumulation, capacity, source, measurement or allocation step.
    /// </summary>
    private static StrategistAdvice Advice(Context context, StrategistGoal[] goals)
    {
        if (goals.Length == 0)
            return new StrategistAdvice(
                Guid.Empty, StrategistAdviceKind.NoGoal, Guid.Empty, PublicationTable<StrategistAdviceCost>.Empty);
        var goal = goals[0];
        var costs = new StrategistAdviceCost[goal.Costs.Count];
        for (var index = 0; index < costs.Length; index++)
        {
            var price = goal.Costs[index];
            var kind = !price.IsShort ? StrategistAdviceKind.Ready : price.Tier switch
            {
                StrategistCostTier.OutOfReach => price.OutOfReach == StrategistOutOfReach.AboveCap
                    ? StrategistAdviceKind.IncreaseCapacity
                    : StrategistAdviceKind.ObtainIncome,
                StrategistCostTier.Measuring => StrategistAdviceKind.MeasureIncome,
                StrategistCostTier.Allocation => StrategistAdviceKind.AllocatePoints,
                _ => StrategistAdviceKind.SaveResources,
            };
            costs[index] = new StrategistAdviceCost(price, kind, context.CapacityFor(price.ResourceId));
        }
        var bottleneck = goal.Binder >= 0 ? costs[goal.Binder] : default;
        var next = goal.Binder >= 0 ? bottleneck.Kind : StrategistAdviceKind.Ready;
        return new StrategistAdvice(
            goal.GateId,
            next,
            next == StrategistAdviceKind.Ready ? Guid.Empty : bottleneck.ResourceId,
            PublicationTable<StrategistAdviceCost>.Create(costs));
    }

    /// <summary>
    /// Sustained income: lifetime gain over a window at least one full producer cycle long. A window
    /// still filling has measured nothing yet.
    /// </summary>
    internal static StrategistIncome Income(
        in StrategistResourceFact resource,
        StrategistWindowReading window)
    {
        var rows = window.Rows.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index].ResourceId != resource.Id) continue;
            var row = rows[index];
            var measured = StrategistIncomeWindow.Covers(row.SpanSeconds, window.RequiredSeconds);
            return new StrategistIncome(
                resource.Id,
                measured,
                measured ? row.Gained / new BigDouble(row.SpanSeconds) : BigDouble.Zero,
                row.ClippedAtCap,
                row.SpanSeconds,
                window.RequiredSeconds);
        }
        return new StrategistIncome(
            resource.Id, false, BigDouble.Zero, false, 0d, window.RequiredSeconds);
    }

    /// <summary>
    /// One price, judged against the currency's sustained income. The tier reads price ÷ income; the
    /// ETA reads what is still missing ÷ income.
    /// </summary>
    internal static StrategistPrice Judge(
        Guid nodeId,
        in StrategistResourceFact resource,
        in StrategistIncome income,
        BigDouble amount)
    {
        var resourceId = resource.Id;
        var held = resource.Held;
        var missing = held < amount ? amount - held : BigDouble.Zero;
        if (resource.Bandwidth)
            return Price(StrategistCostTier.Allocation, StrategistOutOfReach.None, BigDouble.Zero);
        if (resource.Capped && amount > resource.Cap)
            return Price(StrategistCostTier.OutOfReach, StrategistOutOfReach.AboveCap, BigDouble.Zero);
        if (!income.Measured)
            return Price(StrategistCostTier.Measuring, StrategistOutOfReach.None, BigDouble.Zero);
        if (income.PerSecond <= BigDouble.Zero)
        {
            // A window spent at the cap reads zero because the game discarded every gain: a floor,
            // not an absence of income. Held, spending it costs nothing that would have been kept;
            // short, only the next unclipped window can say how fast it refills.
            if (income.LowerBound)
            {
                return Price(
                    missing > BigDouble.Zero ? StrategistCostTier.Measuring : StrategistCostTier.Trivial,
                    StrategistOutOfReach.None,
                    BigDouble.Zero);
            }
            return missing > BigDouble.Zero
                ? Price(StrategistCostTier.OutOfReach, StrategistOutOfReach.NoIncome, BigDouble.Zero)
                : Price(StrategistCostTier.Expensive, StrategistOutOfReach.None, BigDouble.Zero);
        }

        return Price(
            Tier(amount / income.PerSecond), StrategistOutOfReach.None, missing / income.PerSecond);

        StrategistPrice Price(
            StrategistCostTier tier,
            StrategistOutOfReach outOfReach,
            BigDouble etaSeconds) =>
            new(nodeId, resourceId, amount, held, tier, outOfReach, etaSeconds);
    }

    internal static StrategistCostTier Tier(BigDouble costSeconds) =>
        costSeconds < new BigDouble(TrivialSeconds) ? StrategistCostTier.Trivial :
        costSeconds < new BigDouble(CheapSeconds) ? StrategistCostTier.Cheap :
        costSeconds < new BigDouble(NotableSeconds) ? StrategistCostTier.Notable :
        StrategistCostTier.Expensive;

    /// <summary>
    /// How long a price keeps a goal waiting, for ordering: affordable or timed first by ETA, then
    /// short allocations, then still measuring, then out of reach.
    /// </summary>
    internal static int WaitClass(in StrategistPrice price) =>
        !price.IsShort ? 0 :
        price.Tier switch
        {
            StrategistCostTier.OutOfReach => 3,
            StrategistCostTier.Measuring => 2,
            StrategistCostTier.Allocation => 1,
            _ => 0,
        };

    internal static int CompareWait(in StrategistPrice left, in StrategistPrice right)
    {
        var byClass = WaitClass(in left).CompareTo(WaitClass(in right));
        if (byClass != 0 || WaitClass(in left) != 0) return byClass;
        var leftEta = left.IsShort ? left.EtaSeconds : BigDouble.Zero;
        var rightEta = right.IsShort ? right.EtaSeconds : BigDouble.Zero;
        return leftEta.CompareTo(rightEta);
    }

    /// <summary>
    /// Walks supported prerequisite leaves back from Restoration. The flattened edges express
    /// relevance, not mandatory steps: alternative grouping and unevaluable conditions are not kept.
    /// </summary>
    private static StrategistRestoration Restoration(Context context, out HashSet<Guid> chain)
    {
        var facts = context.Facts;
        var root = facts.ChainRoot;
        chain = new HashSet<Guid>();
        if (!context.Nodes.TryGetValue(root, out var rootNode))
        {
            return new StrategistRestoration(
                false, false, PublicationTable<Guid>.Empty, PublicationTable<Guid>.Empty);
        }

        var requires = new List<Guid>();
        var pending = new Queue<Guid>();
        chain.Add(root);
        pending.Enqueue(root);
        var edges = facts.Edges.AsSpan();
        while (pending.Count > 0)
        {
            var owner = pending.Dequeue();
            for (var index = 0; index < edges.Length; index++)
            {
                if (edges[index].OwnerId != owner) continue;
                if (owner == root && !requires.Contains(edges[index].TargetId))
                    requires.Add(edges[index].TargetId);
                if (!edges[index].Met && chain.Add(edges[index].TargetId))
                    pending.Enqueue(edges[index].TargetId);
            }
        }

        var frontier = new List<Guid>();
        foreach (var id in chain)
        {
            if (context.Nodes.TryGetValue(id, out var node) && node.Offered && !node.Owned)
                frontier.Add(id);
        }
        frontier.Sort(context.CompareByName);
        return new StrategistRestoration(
            true,
            rootNode.Owned,
            PublicationTable<Guid>.Create(requires.ToArray()),
            PublicationTable<Guid>.Create(frontier.ToArray()));
    }

    /// <summary>
    /// Every offered gate, ranked by captured relevance: Restoration-related first, then everything
    /// else; sooner first within a rank.
    /// </summary>
    private static StrategistGoal[] Goals(Context context, HashSet<Guid> chain)
    {
        var goals = new List<StrategistGoal>();
        var nodes = context.Facts.Nodes.AsSpan();
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            if (!node.Gate || !node.Offered || node.Owned) continue;
            var costs = context.PricesFor(node.Id);
            var binder = -1;
            for (var cost = 0; cost < costs.Length; cost++)
            {
                if (binder < 0 || CompareWait(in costs[cost], in costs[binder]) > 0) binder = cost;
            }
            goals.Add(new StrategistGoal(
                node.Id,
                chain.Contains(node.Id) ? StrategistGoalRank.Chain : StrategistGoalRank.Other,
                binder,
                PublicationTable<StrategistPrice>.Create(costs),
                PublicationTable<Guid>.Create(context.Opens(node.Id))));
        }

        goals.Sort((left, right) =>
        {
            var byRank = left.Rank.CompareTo(right.Rank);
            if (byRank != 0) return byRank;
            if (left.Binder >= 0 && right.Binder >= 0)
            {
                var byWait = CompareWait(left.Costs[left.Binder], right.Costs[right.Binder]);
                if (byWait != 0) return byWait;
            }
            else if (left.Binder != right.Binder)
            {
                return left.Binder < 0 ? -1 : 1;
            }
            return context.CompareByName(left.GateId, right.GateId);
        });
        return goals.ToArray();
    }

    /// <summary>
    /// Every currency with an offered purchase priced in it, the goal's currencies first; within a
    /// currency, gates first, then by price.
    /// </summary>
    private static StrategistDemand[] Demand(Context context, StrategistGoal[] goals)
    {
        var byResource = new Dictionary<Guid, List<StrategistPrice>>();
        var prices = context.Facts.Prices.AsSpan();
        for (var index = 0; index < prices.Length; index++)
        {
            var node = context.Nodes[prices[index].NodeId];
            if (!node.Offered || node.Owned) continue;
            if (!byResource.TryGetValue(prices[index].ResourceId, out var sinks))
                byResource[prices[index].ResourceId] = sinks = new List<StrategistPrice>();
            sinks.Add(context.Judge(in prices[index]));
        }

        var order = new List<Guid>();
        if (goals.Length > 0)
        {
            var costs = goals[0].Costs.AsSpan();
            for (var index = 0; index < costs.Length; index++)
            {
                if (byResource.ContainsKey(costs[index].ResourceId) && !order.Contains(costs[index].ResourceId))
                    order.Add(costs[index].ResourceId);
            }
        }
        var rest = new List<Guid>();
        foreach (var id in byResource.Keys)
        {
            if (!order.Contains(id)) rest.Add(id);
        }
        rest.Sort(context.CompareResourceByName);
        order.AddRange(rest);

        var demand = new StrategistDemand[order.Count];
        for (var index = 0; index < order.Count; index++)
        {
            var sinks = byResource[order[index]];
            sinks.Sort((left, right) =>
            {
                var leftGate = context.Nodes[left.NodeId].Gate;
                var rightGate = context.Nodes[right.NodeId].Gate;
                if (leftGate != rightGate) return leftGate ? -1 : 1;
                var byAmount = left.Amount.CompareTo(right.Amount);
                return byAmount != 0 ? byAmount : context.CompareByName(left.NodeId, right.NodeId);
            });
            demand[index] = new StrategistDemand(
                order[index], PublicationTable<StrategistPrice>.Create(sinks.ToArray()));
        }
        return demand;
    }

    private sealed class Context
    {
        private readonly Dictionary<Guid, StrategistResourceFact> _resources = new();
        private readonly Dictionary<Guid, StrategistIncome> _incomes = new();

        internal Context(StrategistFacts facts, StrategistWindowReading window)
        {
            Facts = facts;
            var nodes = facts.Nodes.AsSpan();
            for (var index = 0; index < nodes.Length; index++) Nodes[nodes[index].Id] = nodes[index];
            var resources = facts.Resources.AsSpan();
            var incomes = new List<StrategistIncome>();
            for (var index = 0; index < resources.Length; index++)
            {
                _resources[resources[index].Id] = resources[index];
                var income = Income(in resources[index], window);
                _incomes[resources[index].Id] = income;
                if (!resources[index].Bandwidth) incomes.Add(income);
            }
            IncomeRows = incomes.ToArray();
        }

        internal StrategistFacts Facts { get; }
        internal Dictionary<Guid, StrategistNodeFact> Nodes { get; } = new();
        internal StrategistIncome[] IncomeRows { get; }

        internal BigDouble CapacityFor(Guid resourceId) => _resources[resourceId].Cap;

        internal StrategistPrice Judge(in StrategistPriceFact price)
        {
            if (!_resources.TryGetValue(price.ResourceId, out var resource))
                throw new InvalidOperationException(
                    $"A price names resource {price.ResourceId:D}, which the facts do not carry.");
            var income = _incomes[price.ResourceId];
            return StrategistDerivation.Judge(price.NodeId, in resource, in income, price.Amount);
        }

        internal StrategistPrice[] PricesFor(Guid nodeId)
        {
            var judged = new List<StrategistPrice>();
            var prices = Facts.Prices.AsSpan();
            for (var index = 0; index < prices.Length; index++)
            {
                if (prices[index].NodeId == nodeId) judged.Add(Judge(in prices[index]));
            }
            return judged.ToArray();
        }

        internal Guid[] Opens(Guid gateId)
        {
            var owners = new List<Guid>();
            var edges = Facts.Edges.AsSpan();
            for (var index = 0; index < edges.Length; index++)
            {
                if (edges[index].TargetId == gateId && Nodes.ContainsKey(edges[index].OwnerId) &&
                    !owners.Contains(edges[index].OwnerId))
                {
                    owners.Add(edges[index].OwnerId);
                }
            }
            owners.Sort(CompareByName);
            return owners.ToArray();
        }

        internal int CompareByName(Guid left, Guid right)
        {
            var byName = string.CompareOrdinal(NameOf(left), NameOf(right));
            return byName != 0 ? byName : left.CompareTo(right);
        }

        internal int CompareResourceByName(Guid left, Guid right)
        {
            var byName = string.CompareOrdinal(_resources[left].Name, _resources[right].Name);
            return byName != 0 ? byName : left.CompareTo(right);
        }

        private string NameOf(Guid id) =>
            Nodes.TryGetValue(id, out var node) ? node.Name :
            _resources.TryGetValue(id, out var resource) ? resource.Name :
            id.ToString("D");
    }
}
