using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The shared one-step policy for level dials. Quotes and resource state come from the same immutable
/// world; the caller decides when a fresh publication permits another step and the native boundary
/// revalidates the actual control. No quote is inferred from a level multiplier.
/// </summary>
internal static class AutoLevelsPolicy
{
    internal static AutoLevelsDecision Evaluate(
        GameWorldState world, LevelDial dial, AutoLevelsConfiguration config)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (config is null) throw new ArgumentNullException(nameof(config));
        var (current, maximum) = Levels(world.SpellWorkbench, dial);
        if (config.Mode != AutoLevelsOperationMode.Active)
            return Hold(dial, current, AutoLevelsDecisionReason.Disabled);
        if (!double.IsFinite(config.SpellCostSeconds) || config.SpellCostSeconds < 0d ||
            !double.IsFinite(config.DrainBufferSeconds) || config.DrainBufferSeconds < 0d ||
            current < WorldSpellWorkbench.MinimumDialLevel || maximum < current)
            return Hold(dial, current, AutoLevelsDecisionReason.InvalidEvidence);
        if (dial == LevelDial.Reserve)
            return current < maximum
                ? Decision(dial, current, 1, AutoLevelsDecisionReason.ReserveMaximum)
                : Hold(dial, current, AutoLevelsDecisionReason.AtMaximum);

        var controlId = dial switch
        {
            LevelDial.Druidry => KnownEntities.WorldAgromancyDruidryLvs.Uuid,
            LevelDial.Alchemy => KnownEntities.AlchAlchemyLevel.Uuid,
            _ => Guid.Empty,
        };
        if (controlId != Guid.Empty)
        {
            if (!WorldLookup.TryFind(world.Views, controlId, out var control))
                return Hold(dial, current, AutoLevelsDecisionReason.EvidenceUnavailable,
                    detail: "The " + dial + " level control's availability was not published.");
            if (!control.Available)
                return Hold(dial, current, AutoLevelsDecisionReason.ControlLocked,
                    detail: "The game's " + dial + " level control is locked.");
        }

        var quote = dial switch
        {
            LevelDial.Output => world.LevelEconomy.Output,
            LevelDial.Druidry => world.LevelEconomy.Druidry,
            LevelDial.Alchemy => world.LevelEconomy.Alchemy,
            _ => throw new ArgumentOutOfRangeException(nameof(dial)),
        };
        if (!quote.Complete)
            return Hold(dial, current, AutoLevelsDecisionReason.EvidenceUnavailable, detail: quote.Reason);
        if (quote.CurrentLevel != current || quote.MaximumLevel != maximum ||
            !ValidPrices(world, quote.Current))
            return Hold(dial, current, AutoLevelsDecisionReason.InvalidEvidence);
        if (!quote.HasActiveWork)
            return Hold(dial, current, AutoLevelsDecisionReason.NoActiveWork);

        // Output's per-spell budget is independent of the sustained-drain dominance rule. One
        // expensive spell cannot hide behind another cheap one or a shared per-loadout average.
        if (dial == LevelDial.Output && TryImmediateBlock(world, quote, next: false,
                config.SpellCostSeconds, out var immediate))
            return Decision(dial, current, current > WorldSpellWorkbench.MinimumDialLevel ? -1 : 0,
                current > WorldSpellWorkbench.MinimumDialLevel
                    ? AutoLevelsDecisionReason.ImmediateCost : AutoLevelsDecisionReason.AtMinimum,
                immediate.ResourceId, immediate.Required, immediate.Budget);

        var currentDrainBlock = DrainBlock(world, quote, next: false, config.DrainBufferSeconds);
        if (currentDrainBlock.Reason == AutoLevelsDecisionReason.DominantDrain)
            return Decision(dial, current, current > WorldSpellWorkbench.MinimumDialLevel ? -1 : 0,
                current > WorldSpellWorkbench.MinimumDialLevel
                    ? AutoLevelsDecisionReason.DominantDrain : AutoLevelsDecisionReason.AtMinimum,
                currentDrainBlock.ResourceId, currentDrainBlock.Required, currentDrainBlock.Budget);
        if (currentDrainBlock.Reason != AutoLevelsDecisionReason.Sustainable)
            return Hold(dial, current, currentDrainBlock.Reason, currentDrainBlock.ResourceId,
                currentDrainBlock.Required, currentDrainBlock.Budget);
        if (current >= maximum)
            return Hold(dial, current, AutoLevelsDecisionReason.AtMaximum);
        if (!quote.NextComplete)
            return Hold(dial, current, AutoLevelsDecisionReason.EvidenceUnavailable, detail: quote.NextReason);
        if (!ValidPrices(world, quote.Next))
            return Hold(dial, current, AutoLevelsDecisionReason.InvalidEvidence);
        if (dial == LevelDial.Output && TryImmediateBlock(world, quote, next: true,
                config.SpellCostSeconds, out var nextImmediate))
            return Hold(dial, current, AutoLevelsDecisionReason.NextImmediateCost,
                nextImmediate.ResourceId, nextImmediate.Required, nextImmediate.Budget);
        var nextDrainBlock = DrainBlock(world, quote, next: true, config.DrainBufferSeconds);
        if (nextDrainBlock.Reason != AutoLevelsDecisionReason.Sustainable)
            return Hold(dial, current, nextDrainBlock.Reason, nextDrainBlock.ResourceId,
                nextDrainBlock.Required, nextDrainBlock.Budget);
        return Decision(dial, current, 1, AutoLevelsDecisionReason.Sustainable);
    }

    private static (int Current, int Maximum) Levels(in WorldSpellWorkbench workbench, LevelDial dial) => dial switch
    {
        LevelDial.Output => (workbench.OutputLevel, workbench.MaximumOutputLevel),
        LevelDial.Reserve => (workbench.ReserveLevel, workbench.MaximumReserveLevel),
        LevelDial.Druidry => (workbench.DruidryLevel, workbench.MaximumDruidryLevel),
        LevelDial.Alchemy => (workbench.AlchemyLevel, workbench.MaximumAlchemyLevel),
        _ => (0, 0),
    };

    private static bool ValidPrices(GameWorldState world, PublicationTable<WorldLevelPrice> table)
    {
        foreach (var price in table.AsSpan())
        {
            if (price.ResourceId == Guid.Empty || !Nonnegative(price.Amount) ||
                price.Kind is not (WorldLevelPriceKind.Immediate or WorldLevelPriceKind.Drain or
                    WorldLevelPriceKind.Allocation) ||
                !WorldLookup.TryFind(world.Resources, price.ResourceId, out var resource)) return false;
            if (!Nonnegative(WorldResourceCoordinate.SpendableAmount(in resource))) return false;
            if (resource.Reading.Traits.BandwidthResource)
            {
                if (price.Kind == WorldLevelPriceKind.Drain) return false;
            }
            else if (!Finite(resource.TrueRate) || !Finite(resource.UnclampedNetRate) ||
                !Nonnegative(resource.Reading.Drain) || !Finite(resource.Reading.Quality) ||
                resource.Reading.Quality <= BigDouble.Zero || price.Kind == WorldLevelPriceKind.Allocation)
                return false;
        }
        return true;
    }

    private static bool TryImmediateBlock(GameWorldState world, WorldLevelDomainQuote quote,
        bool next, double seconds, out Comparison block)
    {
        var rows = (next ? quote.Next : quote.Current).AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            var price = rows[index];
            if (!Immediate(in price) || Seen(rows, index, in price)) continue;
            var amount = BigDouble.Zero;
            for (var cost = index; cost < rows.Length; cost++)
                if (Immediate(in rows[cost]) && rows[cost].SlotIndex == price.SlotIndex &&
                    rows[cost].ResourceId == price.ResourceId) amount += rows[cost].Amount;
            WorldLookup.TryFind(world.Resources, price.ResourceId, out var resource);
            if (resource.Reading.Traits.BandwidthResource)
            {
                if (WorldResourceCoordinate.HasAmount(in resource, amount)) continue;
                block = new Comparison(price.ResourceId, amount, resource.Headroom);
                return true;
            }
            var required = WorldResourceCoordinate.PlayerFacingCost(in resource, amount);
            var net = resource.UnclampedNetRate;
            if (next) net -= DrainDelta(quote, price.ResourceId) / OrbGameMath.AsPercent(resource.Reading.Quality);
            var budget = BigDouble.Max(net, BigDouble.Zero) * new BigDouble(seconds);
            if (required <= budget) continue;
            block = new Comparison(price.ResourceId, required, budget);
            return true;
        }
        block = default;
        return false;
    }

    private static bool Immediate(in WorldLevelPrice price) =>
        price.Kind == WorldLevelPriceKind.Immediate ||
        price.Kind == WorldLevelPriceKind.Allocation && price.SlotIndex >= 0;

    private static bool Seen(ReadOnlySpan<WorldLevelPrice> rows, int index, in WorldLevelPrice price)
    {
        for (var before = 0; before < index; before++)
            if (Immediate(in rows[before]) && rows[before].SlotIndex == price.SlotIndex &&
                rows[before].ResourceId == price.ResourceId) return true;
        return false;
    }

    private static AutoLevelsDecision DrainBlock(GameWorldState world, WorldLevelDomainQuote quote,
        bool next, double seconds)
    {
        var otherPressure = default(AutoLevelsDecision);
        var count = quote.Current.Count + (next ? quote.Next.Count : 0);
        for (var index = 0; index < count; index++)
        {
            var price = index < quote.Current.Count ? quote.Current[index] : quote.Next[index - quote.Current.Count];
            if (Immediate(in price)) continue;
            WorldLookup.TryFind(world.Resources, price.ResourceId, out var resource);
            if (resource.Reading.Traits.BandwidthResource)
            {
                if (!next) continue; // Existing allocation is already charged, not a recurring rate.
                var delta = Sum(quote.Next, price.ResourceId, WorldLevelPriceKind.Allocation) -
                    Sum(quote.Current, price.ResourceId, WorldLevelPriceKind.Allocation);
                if (delta <= BigDouble.Zero || WorldResourceCoordinate.HasAmount(in resource, delta)) continue;
                return Hold(default, 0, AutoLevelsDecisionReason.Allocation,
                    price.ResourceId, delta, resource.Headroom);
            }
            var currentOwn = Sum(quote.Current, price.ResourceId, WorldLevelPriceKind.Drain);
            var own = next ? Sum(quote.Next, price.ResourceId, WorldLevelPriceKind.Drain) : currentOwn;
            var total = resource.Reading.Drain;
            if (currentOwn > total)
                return Hold(default, 0, AutoLevelsDecisionReason.InvalidEvidence, price.ResourceId);
            var depletion = -resource.TrueRate;
            if (next && depletion > BigDouble.Zero &&
                WorldResourceCoordinate.SpendableAmount(in resource) < depletion * new BigDouble(seconds) &&
                currentOwn * new BigDouble(2) <= total)
                return Hold(default, 0, AutoLevelsDecisionReason.OtherDrainPressure,
                    price.ResourceId, depletion * new BigDouble(seconds),
                    WorldResourceCoordinate.SpendableAmount(in resource));
            if (next)
            {
                total += own - currentOwn;
                // The present cap may discard production. Replacing this domain's drain must
                // start before that clamp, or a full resource invents depletion from a safe raise.
                depletion = -resource.UnclampedNetRate +
                    (own - currentOwn) / OrbGameMath.AsPercent(resource.Reading.Quality);
            }
            var required = BigDouble.Max(depletion, BigDouble.Zero) * new BigDouble(seconds);
            var held = WorldResourceCoordinate.SpendableAmount(in resource);
            if (depletion <= BigDouble.Zero || held >= required) continue;
            if (own * new BigDouble(2) > total)
                return Hold(default, 0, next ? AutoLevelsDecisionReason.NextDrain :
                    AutoLevelsDecisionReason.DominantDrain, price.ResourceId, required, held);
            if (!next)
                otherPressure = Hold(default, 0, AutoLevelsDecisionReason.OtherDrainPressure,
                    price.ResourceId, required, held);
        }
        return otherPressure.Reason == AutoLevelsDecisionReason.OtherDrainPressure
            ? otherPressure : Hold(default, 0, AutoLevelsDecisionReason.Sustainable);
    }

    private static BigDouble DrainDelta(WorldLevelDomainQuote quote, Guid resourceId) =>
        Sum(quote.Next, resourceId, WorldLevelPriceKind.Drain) -
        Sum(quote.Current, resourceId, WorldLevelPriceKind.Drain);

    private static BigDouble Sum(PublicationTable<WorldLevelPrice> rows, Guid resourceId,
        WorldLevelPriceKind kind)
    {
        var sum = BigDouble.Zero;
        foreach (var row in rows.AsSpan())
            if (row.ResourceId == resourceId && row.Kind == kind &&
                (kind != WorldLevelPriceKind.Allocation || row.SlotIndex < 0)) sum += row.Amount;
        return sum;
    }

    private static AutoLevelsDecision Hold(LevelDial dial, int current, AutoLevelsDecisionReason reason,
        Guid resourceId = default, BigDouble required = default, BigDouble budget = default, string detail = "") =>
        new(dial, current, current, AutoLevelsDecisionKind.Hold, reason, resourceId, required, budget, detail);

    private static AutoLevelsDecision Decision(LevelDial dial, int current, int step,
        AutoLevelsDecisionReason reason, Guid resourceId = default, BigDouble required = default,
        BigDouble budget = default) => new(dial, current, current + step,
            step > 0 ? AutoLevelsDecisionKind.Raise : step < 0 ? AutoLevelsDecisionKind.Lower : AutoLevelsDecisionKind.Hold,
            reason, resourceId, required, budget);

    private static bool Nonnegative(BigDouble value) => Finite(value) && value >= BigDouble.Zero;
    private static bool Finite(BigDouble value) => !BigDouble.IsNaN(value) && !BigDouble.IsInfinity(value);

    private readonly struct Comparison
    {
        internal Comparison(Guid resourceId, BigDouble required, BigDouble budget)
        { ResourceId = resourceId; Required = required; Budget = budget; }
        internal Guid ResourceId { get; }
        internal BigDouble Required { get; }
        internal BigDouble Budget { get; }
    }
}
