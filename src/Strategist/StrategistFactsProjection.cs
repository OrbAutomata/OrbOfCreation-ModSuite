using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Picks what the strategist's rules read off one published world. It reads the pinned snapshot and
/// nothing else: no game hooks, no native reads.
/// </summary>
internal static class StrategistFactsProjection
{
    private static readonly (Guid ViewId, string Fallback)[] Screens =
    {
        (KnownEntities.ScreenMagic.Uuid, "Magic"),
        (KnownEntities.ScreenWorkshop.Uuid, "Workshop"),
        (KnownEntities.ScreenWorld.Uuid, "World"),
        (KnownEntities.AlchemyScreen.Uuid, "Alchemy"),
        (KnownEntities.ScreenRitual.Uuid, "Rituals"),
        (KnownEntities.ScreenScholar.Uuid, "Scholar"),
        (KnownEntities.ScreenTime.Uuid, "Time"),
    };

    /// <summary>
    /// One full cycle of the slowest running producer, and never under a minute: a planted harvest
    /// element's harvest plus its regrowth, an equipped spell's timed recharge.
    /// </summary>
    internal static double RequiredWindowSeconds(GameWorldState world)
    {
        var required = StrategistIncomeWindow.MinimumSeconds;
        var elements = world.HarvestElements.AsSpan();
        for (var index = 0; index < elements.Length; index++)
        {
            if (elements[index].Instances <= BigDouble.Zero) continue;
            required = Math.Max(required, elements[index].HarvestTime + elements[index].GrowthTime);
        }

        var slots = world.SpellSlots.AsSpan();
        for (var index = 0; index < slots.Length; index++)
        {
            if (!slots[index].Occupied) continue;
            if (!WorldSpellGraphLookup.TryFindAuthoring(
                    world.SpellRecipeAuthoring, slots[index].SpellRecipeId, out var authoring) ||
                authoring.RechargeProcessorType != 0)
            {
                continue;
            }
            required = Math.Max(required, slots[index].Recharge.ToDouble());
        }

        return required;
    }

    /// <summary>The game's clock, which the window and every ETA run on.</summary>
    internal static double ClockSeconds(GameWorldState world)
    {
        if (!WorldLookup.TryFind(world.DoubleVariables, KnownEntities.TimePlayed.Uuid, out var played))
            throw new InvalidOperationException(
                "The published world has resources but no TimePlayed variable; the strategist has no clock.");
        return played.Value.ToDouble();
    }

    internal static StrategistIncomeWindow.Sample[] WindowSamples(GameWorldState world)
    {
        var resources = world.Resources.AsSpan();
        var samples = new List<StrategistIncomeWindow.Sample>(resources.Length);
        for (var index = 0; index < resources.Length; index++)
        {
            var resource = resources[index];
            if (resource.Reading.Traits.BandwidthResource) continue;
            // Storage coordinates: the lifetime counter stops when the stored quantity fills, which
            // for an inverted counter is when its display reads empty.
            var storageFull = resource.IsCapped && resource.Headroom <= BigDouble.Zero;
            samples.Add(new StrategistIncomeWindow.Sample(
                resource.EntityId, resource.Reading.LifetimeQuantity, storageFull));
        }
        return samples.ToArray();
    }

    internal static StrategistFacts Project(GameWorldState world, ulong worldGeneration)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var names = world.EntityIdentities;
        var nodes = new Dictionary<Guid, StrategistNodeFact>();
        var prices = new List<StrategistPriceFact>();
        var referenced = new HashSet<Guid>();

        CollectOfferedPurchases(world, nodes, prices, referenced);
        var edges = CollectEdges(world, nodes, referenced);

        var resources = new List<StrategistResourceFact>();
        var rows = world.Resources.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            var resource = rows[index];
            if (!resource.Reading.Visible && !referenced.Contains(resource.EntityId)) continue;
            resources.Add(new StrategistResourceFact(
                resource.EntityId,
                EntityIdentityFormatter.PlayerName(resource.EntityId, names),
                resource.Reading.Visible,
                WorldResourceCoordinate.SpendableAmount(in resource),
                resource.IsCapped,
                resource.Reading.Capacity,
                resource.Reading.Traits.BandwidthResource));
        }

        var nodeRows = new StrategistNodeFact[nodes.Count];
        nodes.Values.CopyTo(nodeRows, 0);
        Array.Sort(nodeRows, static (left, right) => left.Id.CompareTo(right.Id));
        return new StrategistFacts(
            worldGeneration,
            Clock(world),
            KnownEntities.Restoration.Uuid,
            PublicationTable<StrategistOpenFact>.Create(Open(world).ToArray()),
            PublicationTable<StrategistResourceFact>.Create(resources.ToArray()),
            PublicationTable<StrategistNodeFact>.Create(nodeRows),
            PublicationTable<StrategistPriceFact>.Create(prices.ToArray()),
            PublicationTable<StrategistEdgeFact>.Create(edges.ToArray()));
    }

    private static StrategistClock Clock(GameWorldState world)
    {
        var total = ClockSeconds(world);
        var hasThisReset = WorldLookup.TryFind(
            world.DoubleVariables, KnownEntities.TimePlayedThisReset.Uuid, out var thisReset);
        var active = 0;
        var challenges = world.Challenges.AsSpan();
        for (var index = 0; index < challenges.Length; index++)
        {
            if (challenges[index].State == CurrentlyActiveChallenge) active++;
        }
        return new StrategistClock(
            total, hasThisReset, hasThisReset ? thisReset.Value.ToDouble() : 0d, active);
    }

    /// <summary><c>ChallengeSO.ChallengeState.CurrentlyActive</c>.</summary>
    private const int CurrentlyActiveChallenge = 2;

    private static List<StrategistOpenFact> Open(GameWorldState world)
    {
        var open = new List<StrategistOpenFact>();
        for (var index = 0; index < Screens.Length; index++)
        {
            if (!IsViewAvailable(world, Screens[index].ViewId)) continue;
            open.Add(new StrategistOpenFact(ScreenName(world, Screens[index]), false, 0, 0));
        }

        var bench = world.SpellWorkbench;
        if (bench.MaximumOutputLevel > 0)
        {
            open.Add(new StrategistOpenFact("Output Lv", true, bench.OutputLevel, bench.MaximumOutputLevel));
            open.Add(new StrategistOpenFact("Reserve Lv", true, bench.ReserveLevel, bench.MaximumReserveLevel));
        }
        if (IsViewAvailable(world, KnownEntities.WorldAgromancyDruidryLvs.Uuid))
            open.Add(new StrategistOpenFact("Druidry Lv", true, bench.DruidryLevel, bench.MaximumDruidryLevel));
        if (IsViewAvailable(world, KnownEntities.AlchAlchemyLevel.Uuid))
            open.Add(new StrategistOpenFact("Alchemy Lv", true, bench.AlchemyLevel, bench.MaximumAlchemyLevel));
        return open;
    }

    private static string ScreenName(GameWorldState world, (Guid ViewId, string Fallback) screen)
    {
        var name = EntityIdentityFormatter.PlayerName(screen.ViewId, world.EntityIdentities);
        return name == screen.ViewId.ToString("D") ? screen.Fallback : name;
    }

    private static bool IsViewAvailable(GameWorldState world, Guid viewId) =>
        WorldLookup.TryFind(world.Views, viewId, out var view) && view.Available;

    /// <summary>Every offered upgrade and structure with a next level to buy, and its cost rows.</summary>
    private static void CollectOfferedPurchases(
        GameWorldState world,
        Dictionary<Guid, StrategistNodeFact> nodes,
        List<StrategistPriceFact> prices,
        HashSet<Guid> referenced)
    {
        var upgrades = world.Upgrades.AsSpan();
        for (var index = 0; index < upgrades.Length; index++)
        {
            var node = UpgradeNode(world, in upgrades[index]);
            if (!node.Offered || node.Owned) continue;
            if (AddPrices(world, node.Id, prices, referenced)) nodes[node.Id] = node;
        }

        var structures = world.Structures.AsSpan();
        for (var index = 0; index < structures.Length; index++)
        {
            var node = StructureNode(world, in structures[index]);
            if (!node.Offered) continue;
            if (AddPrices(world, node.Id, prices, referenced)) nodes[node.Id] = node;
        }
    }

    private static bool AddPrices(
        GameWorldState world,
        Guid nodeId,
        List<StrategistPriceFact> prices,
        HashSet<Guid> referenced)
    {
        if (!WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, nodeId, out var start, out var count))
            return false;
        for (var index = 0; index < count; index++)
        {
            var cost = world.PurchaseCosts[start + index];
            if (!WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
                throw new InvalidOperationException(
                    $"{EntityIdentityFormatter.Format(nodeId)} is priced in " +
                    $"{EntityIdentityFormatter.Format(cost.ResourceId)}, which the world does not publish.");
            var nominal = cost.AffordabilityEvaluated ? cost.CombinedEffectiveAmount : cost.EffectiveExactAmount;
            prices.Add(new StrategistPriceFact(
                nodeId, cost.ResourceId, WorldResourceCoordinate.PlayerFacingCost(in resource, nominal)));
            referenced.Add(cost.ResourceId);
        }
        return count > 0;
    }

    /// <summary>
    /// Restoration's own requirements, every unmet requirement behind them, and every requirement that
    /// names an offered gate.
    /// </summary>
    private static List<StrategistEdgeFact> CollectEdges(
        GameWorldState world,
        Dictionary<Guid, StrategistNodeFact> nodes,
        HashSet<Guid> referenced)
    {
        var edges = new List<StrategistEdgeFact>();
        var seen = new HashSet<(Guid, Guid)>();
        var root = KnownEntities.Restoration.Uuid;
        if (!TryNode(world, root, out var rootNode)) return edges;
        nodes[root] = rootNode;

        var walked = new HashSet<Guid> { root };
        var pending = new Queue<Guid>();
        pending.Enqueue(root);
        var rows = world.EntityRequirements.AsSpan();
        while (pending.Count > 0)
        {
            var owner = pending.Dequeue();
            if (!WorldEntityRequirementLookup.TryFindRange(
                    world.EntityRequirements, owner, out var start, out var count))
            {
                continue;
            }

            var level = NextLevel(world, owner);
            for (var index = start; index < start + count; index++)
            {
                if (!IsChainEdge(in rows[index])) continue;
                var target = rows[index].TargetId;
                var met = WorldRequirementEvaluator.Evaluate(world, in rows[index], level) ==
                    WorldRequirementVerdict.Met;
                if (met && owner != root) continue;
                if (!TryTarget(world, target, nodes, referenced)) continue;
                if (seen.Add((owner, target))) edges.Add(new StrategistEdgeFact(owner, target, met));
                if (!met && walked.Add(target)) pending.Enqueue(target);
            }
        }

        var gates = new HashSet<Guid>();
        foreach (var node in nodes.Values)
        {
            if (node.Gate && node.Offered && !node.Owned) gates.Add(node.Id);
        }
        for (var index = 0; index < rows.Length; index++)
        {
            if (!IsChainEdge(in rows[index]) || !gates.Contains(rows[index].TargetId)) continue;
            var owner = rows[index].OwnerId;
            if (!seen.Add((owner, rows[index].TargetId))) continue;
            if (!nodes.ContainsKey(owner))
            {
                if (!TryNode(world, owner, out var ownerNode)) continue;
                nodes[owner] = ownerNode;
            }
            edges.Add(new StrategistEdgeFact(owner, rows[index].TargetId, met: false));
        }

        return edges;
    }

    private static bool IsChainEdge(in WorldEntityRequirement row) =>
        row.NodeKind == WorldRequirementNodeKind.Leaf &&
        row.Program is WorldRequirementProgramKind.NextLevel or WorldRequirementProgramKind.Unlock &&
        row.Kind is WorldRequirementConditionKind.Upgrade or WorldRequirementConditionKind.Research or
            WorldRequirementConditionKind.Structure or WorldRequirementConditionKind.Resource &&
        row.TargetId != Guid.Empty;

    private static bool TryTarget(
        GameWorldState world,
        Guid target,
        Dictionary<Guid, StrategistNodeFact> nodes,
        HashSet<Guid> referenced)
    {
        if (nodes.ContainsKey(target)) return true;
        if (WorldLookup.TryFind(world.Resources, target, out _))
        {
            referenced.Add(target);
            return true;
        }
        if (!TryNode(world, target, out var node)) return false;
        nodes[target] = node;
        return true;
    }

    private static long NextLevel(GameWorldState world, Guid owner)
    {
        if (WorldLookup.TryFind(world.Upgrades, owner, out var upgrade)) return upgrade.Reading.Level + 1L;
        if (WorldLookup.TryFind(world.Research, owner, out var research)) return research.Level + 1L;
        if (WorldLookup.TryFind(world.Structures, owner, out var structure))
            return (long)structure.Reading.Level.ToDouble() + 1L;
        return 1L;
    }

    private static bool TryNode(GameWorldState world, Guid id, out StrategistNodeFact node)
    {
        if (WorldLookup.TryFind(world.Upgrades, id, out var upgrade))
        {
            node = UpgradeNode(world, in upgrade);
            return true;
        }
        if (WorldLookup.TryFind(world.Research, id, out var research))
        {
            node = new StrategistNodeFact(
                id,
                EntityIdentityFormatter.PlayerName(id, world.EntityIdentities),
                StrategistNodeKind.Research,
                research.Visible && research.Available && !research.Complete,
                research.Complete,
                gate: false);
            return true;
        }
        if (WorldLookup.TryFind(world.Structures, id, out var structure))
        {
            node = StructureNode(world, in structure);
            return true;
        }
        node = default;
        return false;
    }

    private static StrategistNodeFact UpgradeNode(GameWorldState world, in WorldUpgrade upgrade) =>
        new(
            upgrade.EntityId,
            EntityIdentityFormatter.PlayerName(upgrade.EntityId, world.EntityIdentities),
            StrategistNodeKind.Upgrade,
            WorldUpgradeScreenLists.IsOffered(world, in upgrade),
            upgrade.IsBounded && upgrade.CommittedLevel >= upgrade.Reading.MaxLevel,
            upgrade.Reading.MaxLevel == 1);

    private static StrategistNodeFact StructureNode(GameWorldState world, in WorldStructure structure) =>
        new(
            structure.EntityId,
            EntityIdentityFormatter.PlayerName(structure.EntityId, world.EntityIdentities),
            StrategistNodeKind.Structure,
            structure.Reading.Unlocked && !structure.Reading.Disabled,
            owned: false,
            gate: false);
}
