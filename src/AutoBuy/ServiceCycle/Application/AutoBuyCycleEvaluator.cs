using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The pure Auto Buy worker policy: a stateless per-capture batch planner. Given one native-free
/// <see cref="AutoBuyCycleFrame"/> and the pinned <see cref="SuiteRuntimeConfiguration"/> it plans one
/// purchase per eligible candidate in ranked order — the faithful replacement for the legacy
/// engine's admission → reserve/affordability → ranked-pass → grouping pipeline — and waits for the
/// next world or configuration publication. It owns no hidden control state: no
/// ranked-pass cursor, no group/batch counter, no retry/backoff. Fairness and pacing come entirely
/// from one-action-per-pump batch execution plus the wake cadence, so it can never re-plan stale
/// facts. All magnitude math stays on the game's own <see cref="BigDouble"/> end to end — no copy,
/// no conversion — exactly as the raw quantities and costs were captured.
/// </summary>
/// <remarks>
/// Pillar A plans ONE action per eligible candidate per cycle: a structural native rejection
/// cascade-terminates the batch, while an affordability-only refusal skips that candidate and makes
/// Common wait for fresh facts. Planning a candidate twice is therefore wasted work, and an
/// already-queued slot is retained. One action is not one queue slot: a Structure prefers the
/// game's live Bulk Development count, while an Upgrade stays at one level. The worker chooses the
/// largest exactly priced positive Structure count the remaining batch ledger can fund. Nothing
/// here bounds the plan by the queue.
/// The action adapter is
/// the real-time authority that re-validates and submits each call, stopping the cascade the moment
/// one is rejected; pacing comes from world publication, not per-item backoff. The adapter re-reads the
/// live queue room before every submission, rejects — cascade-terminating the batch — once only
/// <see cref="AutoBuyConfiguration.LeaveQueueSlots"/> remain, and clamps a multi-level request to the
/// room above that reserve, since the game queues one entry per level. So the plan is as long as the
/// ranked list and the runtime stops it at the truth. The ledger reserves the exact ported cost-curve
/// sum for the chosen group and never substitutes <c>levels x next-cost</c>.
/// </remarks>
internal static class AutoBuyCycleEvaluator
{
    private static readonly IComparer<Eligible> RankOrder = Comparer<Eligible>.Create(static (left, right) =>
    {
        // CostRatio ascending, then UUID ascending (OrdinalIgnoreCase).
        var ratio = left.CostRatio.CompareTo(right.CostRatio);
        if (ratio != 0) return ratio;
        return string.Compare(left.UuidText, right.UuidText, StringComparison.OrdinalIgnoreCase);
    });

    public static WakePolicy Evaluate(
        in AutoBuyCycleFrame frame,
        in SuiteRuntimeConfiguration config,
        ServiceActionWriter<AutoBuyCycleAction> actions,
        out AutoBuyDecisionMetrics metrics)
    {
        var wake = WakePolicy.OnPublication;
        metrics = new AutoBuyDecisionMetrics(
            frame.StructureCount,
            frame.UpgradeCount,
            eligibleCandidates: 0,
            plannedActions: 0,
            requestedLevels: 0);

        // Whole-service gates: an inactive/disabled config or a live emergency plans nothing but
        // still reschedules so the service resumes the moment the operator re-enables it.
        if (!AutoBuyConfigurationPolicy.IsOperational(config))
            return wake;

        // The reserve floor is config-only. An empty/malformed AbsoluteReserve falls back to the
        // config's own default of no reserve (0) — the legacy reject-everything path only ever fired
        // on a malformed config that production never produces.
        var absoluteReserve = ResolveAbsoluteReserve(config.Reserves.AbsoluteReserve);
        var relativeMultiplier = Math.Max(0.0, config.Reserves.RelativeReserveMultiplier);

        var candidates = frame.Candidates;
        var resources = frame.Resources;
        var costs = frame.Costs;

        // The worker holds no state between cycles (the framework's worker-definition validator
        // enforces this), so the ranked-pass scratch is a per-cycle local. The evaluation runs off
        // the main thread at the world-publication cadence, where this allocation is immaterial.
        var eligible = new List<Eligible>();

        // One counter per exclusion term. Every candidate that does not reach the plan increments
        // exactly one of them, so the histogram and the eligible count always add back up to the
        // captured count — a term that starts silently swallowing candidates cannot hide in a total.
        var excluded = new int[ExclusionTermCount];
        for (var i = 0; i < candidates.Length; i++)
        {
            ref readonly var candidate = ref candidates[i];
            var admission = Admit(in candidate, config);
            if (admission != AutoBuyExclusion.None)
            {
                excluded[(int)admission]++;
                continue;
            }

            if (!TryEvaluateAffordability(
                    in candidate,
                    costs,
                    resources,
                    in absoluteReserve,
                    relativeMultiplier,
                    config,
                    out var costRatio,
                    out var admissionBelief,
                    out var refusal))
            {
                excluded[(int)refusal]++;
                continue;
            }

            eligible.Add(new Eligible(
                i,
                candidate.Kind,
                candidate.Uuid,
                candidate.Uuid.ToString("D", CultureInfo.InvariantCulture),
                costRatio,
                in admissionBelief));
        }

        if (eligible.Count == 0)
        {
            metrics = new AutoBuyDecisionMetrics(
                frame.StructureCount,
                frame.UpgradeCount,
                eligibleCandidates: 0,
                plannedActions: 0,
                requestedLevels: 0,
                Histogram(excluded));
            return wake;
        }

        eligible.Sort(RankOrder);

        // At most one action per eligible candidate, in ranked order. Each action requests `count`
        // levels: one action may consume several queue slots when its preferred group is affordable,
        // and may shrink to the largest positive exactly-priced count the batch can still fund.
        //
        // Nothing here bounds the plan by the queue. FillAvailableQueue means "keep going until only
        // LeaveQueueSlots remain", and only the action boundary can know when that is — it re-reads
        // the live room before every submission, clamps the request to what fits above the reserve,
        // and the first submission that does not fit cascade-terminates the rest of the batch. So
        // the plan is as long as the ranked list and the runtime stops it at the truth.
        // Eligibility asked "can this candidate be afforded on its own", against untouched quantities.
        // A batch spends for real: the game deducts the cost when a purchase is queued, not when it
        // completes, so by the time the fourth action runs the first three have already been paid for.
        // Without a ledger every action in the batch clears the same floor against the same numbers
        // and the batch as a whole spends straight through it.
        var committed = resources.Length == 0 ? Array.Empty<BigDouble>() : new BigDouble[resources.Length];

        var emitted = 0;
        var requestedLevels = 0;
        var fullGroups = 0;
        var reducedGroups = 0;
        var reducedGroupLevels = 0;
        var ledgerStarved = 0;
        for (var i = 0; i < eligible.Count; i++)
        {
            var decision = eligible[i];
            var preferredCount = PerCandidateAmount(decision.Kind, frame.Global);
            ref readonly var candidate = ref candidates[decision.CandidateIndex];
            var count = 0;
            var belief = default(AutoBuyPlanBelief);
            var plannedSpend = PublicationTable<AutoBuyPlannedSpend>.Empty;
            for (var candidateCount = preferredCount; candidateCount >= 1; candidateCount--)
            {
                if (!TryCommitSpend(
                        in candidate,
                        costs,
                        resources,
                        committed,
                        candidateCount,
                        in absoluteReserve,
                        relativeMultiplier,
                        out belief,
                        out plannedSpend))
                    continue;

                count = candidateCount;
                break;
            }

            if (count == 0)
            {
                // Admission proved one level against untouched quantities. Reaching zero here means
                // earlier ranked actions consumed enough of a shared resource that even that exact
                // one-level spend no longer clears the batch ledger.
                ledgerStarved++;
                continue;
            }

            if (count == preferredCount)
            {
                fullGroups++;
            }
            else
            {
                reducedGroups++;
                reducedGroupLevels = checked(reducedGroupLevels + count);
            }

            var action = new AutoBuyCycleAction(
                decision.Kind,
                decision.Uuid,
                frame.Global.CollectedAtEpoch,
                count,
                preferredCount == 1 ? decision.Belief : belief,
                frame.Global.CollectedAt,
                candidate.OwningListId,
                candidate.OwningViewId,
                plannedSpend);
            actions.Add(in action);
            emitted++;
            requestedLevels = checked(requestedLevels + count);
        }

        metrics = new AutoBuyDecisionMetrics(
            frame.StructureCount,
            frame.UpgradeCount,
            eligible.Count,
            emitted,
            requestedLevels,
            Histogram(excluded),
            new AutoBuyGroupOutcomeHistogram(
                fullGroups,
                reducedGroups,
                reducedGroupLevels,
                ledgerStarved));
        return wake;
    }

    /// <summary>One more than the highest <see cref="AutoBuyExclusion"/> member.</summary>
    private const int ExclusionTermCount = (int)AutoBuyExclusion.OwningViewRelationContradictory + 1;

    private static AutoBuyExclusionHistogram Histogram(int[] excluded) =>
        new(
            excluded[(int)AutoBuyExclusion.KindNotSelected],
            excluded[(int)AutoBuyExclusion.Unavailable],
            excluded[(int)AutoBuyExclusion.RequirementsUnmet],
            excluded[(int)AutoBuyExclusion.Terminal],
            excluded[(int)AutoBuyExclusion.Unaffordable],
            excluded[(int)AutoBuyExclusion.Unpriceable],
            excluded[(int)AutoBuyExclusion.OwningViewUnavailable],
            excluded[(int)AutoBuyExclusion.OwningViewRelationMissing],
            excluded[(int)AutoBuyExclusion.OwningViewRelationUnreadable],
            excluded[(int)AutoBuyExclusion.OwningViewRelationContradictory]);

    /// <summary>
    /// Which admission term excluded this candidate, or <see cref="AutoBuyExclusion.None"/> when
    /// none did.
    /// </summary>
    /// <remarks>
    /// The terms are tested in the same order as before and the first one that refuses is the one
    /// reported, so the histogram attributes a candidate to the earliest reason rather than to all of
    /// them. That matches how an operator reads it: a blocklisted candidate that is also maxed out is
    /// blocklisted, and unblocking it is what would change the answer.
    /// </remarks>
    private static AutoBuyExclusion Admit(
        in AutoBuyCandidateRow candidate,
        in SuiteRuntimeConfiguration config)
    {
        // Kind selection — the action adapter revalidates this too, so a config that drops a family
        // between planning and execution can never commit an unwanted purchase.
        if (!AutoBuyConfigurationPolicy.IsSelected(config, candidate.Kind))
            return AutoBuyExclusion.KindNotSelected;

        switch (candidate.OwningView)
        {
            case AutoBuyOwningViewStatus.Unavailable:
                return AutoBuyExclusion.OwningViewUnavailable;
            case AutoBuyOwningViewStatus.RelationMissing:
                return AutoBuyExclusion.OwningViewRelationMissing;
            case AutoBuyOwningViewStatus.RelationUnreadable:
                return AutoBuyExclusion.OwningViewRelationUnreadable;
            case AutoBuyOwningViewStatus.RelationContradictory:
                return AutoBuyExclusion.OwningViewRelationContradictory;
            case AutoBuyOwningViewStatus.Available:
                break;
            default:
                return AutoBuyExclusion.OwningViewRelationUnreadable;
        }

        if (!candidate.IsAvailable)
            return AutoBuyExclusion.Unavailable;

        // The conditions on the level this purchase would actually reach. Unlike availability, which
        // latches once for the entity, this one moves with the level — an upgrade six levels in can be
        // available and still be gated on a research entry nobody has finished. Anything short of met,
        // including a condition the suite cannot evaluate, keeps the candidate out of the plan.
        if (!candidate.MeetsNextLevelRequirements)
            return AutoBuyExclusion.RequirementsUnmet;

        // Finite-level structures/upgrades that are done (or fully queued to their cap) are terminal.
        if (candidate.HasFiniteLevels && candidate.IsMaxLevel && candidate.QueuedLevels == 0)
            return AutoBuyExclusion.Terminal;
        if (candidate.HasFiniteLevels && candidate.IsMaxQueuedLevel && candidate.QueuedLevels > 0)
            return AutoBuyExclusion.Terminal;

        return AutoBuyExclusion.None;
    }

    /// <summary>
    /// Whether this row is the first one for its resource. Rows that draw on the same resource are
    /// combined (the stricter-than-native duplicate-resource rule), so each resource is processed
    /// exactly once, at its first row.
    /// </summary>
    private static bool IsFirstRowForItsResource(
        ReadOnlySpan<AutoBuyCostRow> costs,
        int start,
        int row)
    {
        var resourceIndex = costs[row].ResourceRowIndex;
        for (var i = start; i < row; i++)
        {
            if (costs[i].ResourceRowIndex == resourceIndex)
                return false;
        }

        return true;
    }

    /// <summary>How much of a resource a purchase of this cost must leave behind untouched.</summary>
    private static BigDouble RequiredFloor(
        in BigDouble cost,
        in BigDouble absoluteReserve,
        double relativeMultiplier) =>
        BigDouble.Max(absoluteReserve, cost * relativeMultiplier);

    /// <summary>
    /// Charges this purchase to the batch ledger if every resource it draws on still clears the
    /// reserve floor after what the batch has already committed. All-or-nothing: a purchase that
    /// cannot be paid for leaves the ledger untouched, so a candidate behind it that draws on
    /// something else still gets its turn.
    /// </summary>
    /// <remarks>
    /// A multi-level request is charged the published exact sum of each successively priced level.
    /// The same ported game math that prices the next level produces that grouped total during world
    /// derivation, including per-level rounding and the finite-upgrade cap. A row that does not carry
    /// the exact requested group is refused here rather than degraded to <c>levels x next-cost</c>.
    /// </remarks>
    private static bool TryCommitSpend(
        in AutoBuyCandidateRow candidate,
        ReadOnlySpan<AutoBuyCostRow> costs,
        ReadOnlySpan<AutoBuyResourceRow> resources,
        BigDouble[] committed,
        int levels,
        in BigDouble absoluteReserve,
        double relativeMultiplier,
        out AutoBuyPlanBelief belief,
        out PublicationTable<AutoBuyPlannedSpend> plannedSpend)
    {
        belief = default;
        plannedSpend = PublicationTable<AutoBuyPlannedSpend>.Empty;
        var start = candidate.CostRowStart;
        var end = start + candidate.CostRowCount;
        var maxRatio = 0.0;
        var costResourceCount = 0;
        var pricedResourceCount = 0;
        var hasBinding = false;
        var bindingResourceId = Guid.Empty;
        var bindingIsBandwidth = false;
        var bindingCost = default(BigDouble);
        var bindingAvailable = default(BigDouble);
        var bindingFloor = default(BigDouble);

        for (var i = start; i < end; i++)
        {
            if (!IsFirstRowForItsResource(costs, start, i))
                continue;

            costResourceCount++;
            var resourceIndex = costs[i].ResourceRowIndex;
            if (!WorldExactCostMath.TryCombinedExactCost<AutoBuyCostRow, int>(
                    costs, start, end, resourceIndex, levels, out var cost))
                return false;
            if (IsNegative(cost))
                return false;
            if (IsZero(cost))
                continue;
            pricedResourceCount++;
            if ((uint)resourceIndex >= (uint)committed.Length)
                return false;

            var remaining = resources[resourceIndex].Spendable - committed[resourceIndex];
            var floor = RequiredFloor(in cost, in absoluteReserve, relativeMultiplier);
            var requiredBeforeSpend = cost + floor;
            if (remaining.CompareTo(requiredBeforeSpend) < 0)
                return false;

            var ratio = (cost / remaining).ToDouble();
            if (!hasBinding || ratio > maxRatio)
            {
                hasBinding = true;
                bindingResourceId = resources[resourceIndex].ResourceId;
                bindingIsBandwidth = resources[resourceIndex].IsBandwidth;
                bindingCost = cost;
                bindingAvailable = remaining;
                bindingFloor = floor;
            }
            if (ratio > maxRatio)
                maxRatio = ratio;
        }

        if (pricedResourceCount == 0)
            return false;

        var terms = new AutoBuyPlannedSpend[pricedResourceCount];
        var termIndex = 0;
        for (var i = start; i < end; i++)
        {
            if (!IsFirstRowForItsResource(costs, start, i))
                continue;

            var resourceIndex = costs[i].ResourceRowIndex;
            if (!WorldExactCostMath.TryCombinedExactCost<AutoBuyCostRow, int>(
                    costs, start, end, resourceIndex, levels, out var cost))
                return false;
            if (IsZero(cost))
                continue;
            var remaining = resources[resourceIndex].Spendable - committed[resourceIndex];
            terms[termIndex++] = new AutoBuyPlannedSpend(
                resources[resourceIndex].ResourceId,
                cost,
                remaining,
                RequiredFloor(in cost, in absoluteReserve, relativeMultiplier));
            committed[resourceIndex] += cost;
        }

        belief = new AutoBuyPlanBelief(
            candidate.IsAvailable,
            candidate.HasFiniteLevels,
            candidate.IsMaxLevel,
            candidate.IsMaxQueuedLevel,
            candidate.CurrentLevel,
            candidate.QueuedLevels,
            costResourceCount,
            pricedResourceCount,
            maxRatio,
            bindingResourceId,
            bindingIsBandwidth,
            bindingCost,
            bindingAvailable,
            bindingFloor);
        plannedSpend = PublicationTable<AutoBuyPlannedSpend>.Create(terms, termIndex);
        return true;
    }

    private static bool TryEvaluateAffordability(
        in AutoBuyCandidateRow candidate,
        ReadOnlySpan<AutoBuyCostRow> costs,
        ReadOnlySpan<AutoBuyResourceRow> resources,
        in BigDouble absoluteReserve,
        double relativeMultiplier,
        in SuiteRuntimeConfiguration config,
        out double costRatio,
        out AutoBuyPlanBelief belief,
        out AutoBuyExclusion refusal)
    {
        costRatio = 0.0;
        belief = default;
        refusal = AutoBuyExclusion.Unaffordable;
        var start = candidate.CostRowStart;
        var end = start + candidate.CostRowCount;
        var maxRatio = 0.0;
        var costResourceCount = 0;
        var pricedResourceCount = 0;
        var hasBinding = false;
        var bindingResourceId = Guid.Empty;
        var bindingIsBandwidth = false;
        var bindingCost = default(BigDouble);
        var bindingAvailable = default(BigDouble);
        var bindingFloor = default(BigDouble);

        for (var i = start; i < end; i++)
        {
            if (!IsFirstRowForItsResource(costs, start, i))
                continue;

            costResourceCount++;
            var resourceIndex = costs[i].ResourceRowIndex;
            if (!WorldExactCostMath.TryCombinedExactCost<AutoBuyCostRow, int>(
                    costs, start, end, resourceIndex, levels: 1, out var cost))
            {
                refusal = AutoBuyExclusion.Unpriceable;
                return false;
            }

            if (IsNegative(cost))
            {
                refusal = AutoBuyExclusion.Unpriceable;
                return false; // invalid resource snapshot
            }

            if (IsZero(cost))
                continue; // free on this resource

            pricedResourceCount++;
            if ((uint)resourceIndex >= (uint)resources.Length)
            {
                refusal = AutoBuyExclusion.Unpriceable;
                return false; // defensive: cost references a resource the frame did not capture
            }


            // Holdings for an ordinary resource, room below the ceiling for a bandwidth one — the
            // game charges bandwidth against the gap, so comparing a cost against a full pool's
            // quantity says "affordable" about a purchase the game will refuse outright.
            var quantity = resources[resourceIndex].Spendable;
            if (IsNegative(quantity))
            {
                refusal = AutoBuyExclusion.Unpriceable;
                return false;
            }

            var floor = RequiredFloor(in cost, in absoluteReserve, relativeMultiplier);
            var requiredBeforeSpend = cost + floor;
            if (quantity.CompareTo(requiredBeforeSpend) < 0)
                return false; // reserve floor blocks the purchase

            // quantity >= requiredBeforeSpend > 0 here, so the ratio never divides by zero.
            var ratio = (cost / quantity).ToDouble();
            if (!hasBinding || ratio > maxRatio)
            {
                hasBinding = true;
                bindingResourceId = resources[resourceIndex].ResourceId;
                bindingIsBandwidth = resources[resourceIndex].IsBandwidth;
                bindingCost = cost;
                bindingAvailable = quantity;
                bindingFloor = floor;
            }
            if (ratio > maxRatio)
                maxRatio = ratio;
        }

        // Nothing was priced, so nothing was tested. A candidate whose every cost row is zero has not
        // been shown to be affordable — it has been shown to be unpriceable, and the skip above
        // stepped over the one comparison that would have said otherwise. Answering true here made
        // such a candidate eligible with no affordability test at all, and that is exactly what
        // happened on the first cold collection after a load: the global structure-cost multiplier
        // read as an uncalculated zero, all 180 structures priced at nothing, all 180 were planned,
        // and the game refused 147 of them.
        //
        // W5 — a value that could not be read is not evidence. A single free row on an otherwise
        // priced candidate still skips, because that one really is free relative to the rows that
        // did price.
        if (pricedResourceCount == 0)
        {
            refusal = AutoBuyExclusion.Unpriceable;
            return false;
        }

        var affordabilityMode = candidate.Kind == AutoBuyCandidateKind.Upgrade
            ? config.AutoBuy.UpgradeAffordability
            : config.AutoBuy.StructureAffordability;
        if (maxRatio > MaximumAllowedCostRatio(affordabilityMode))
            return false;

        costRatio = maxRatio;
        belief = new AutoBuyPlanBelief(
            candidate.IsAvailable,
            candidate.HasFiniteLevels,
            candidate.IsMaxLevel,
            candidate.IsMaxQueuedLevel,
            candidate.CurrentLevel,
            candidate.QueuedLevels,
            costResourceCount,
            pricedResourceCount,
            maxRatio,
            bindingResourceId,
            bindingIsBandwidth,
            bindingCost,
            bindingAvailable,
            bindingFloor);
        return true;
    }

    /// <summary>
    /// How many levels one action requests for a candidate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Structures use the live Bulk Development count. Upgrades always request one level.
    /// </para>
    /// <para>
    /// The hundred-level ceiling is the legacy engine's and is kept: it bounds one action to
    /// something a person can still read in a log, and the live queue room bounds it again at the
    /// action boundary, which is the only place that knows what actually fits. The frame carries one
    /// when either count is unreadable, which collapses to a single level on its own.
    /// </para>
    /// </remarks>
    private static int PerCandidateAmount(
        AutoBuyCandidateKind kind,
        in AutoBuyGlobalRow global) =>
        kind == AutoBuyCandidateKind.Structure ? Clamp(global.BulkDevelopment) : 1;

    /// <summary>The most levels one action may ask for, whatever the game's count says.</summary>
    private const int MaximumGroupedLevels = WorldPurchaseGrouping.MaximumLevels;

    private static int Clamp(int levels) => Math.Max(1, Math.Min(MaximumGroupedLevels, levels));

    private static double MaximumAllowedCostRatio(AutoBuyAffordabilityMode mode) =>
        mode switch
        {
            AutoBuyAffordabilityMode.BuyAll => double.PositiveInfinity,
            AutoBuyAffordabilityMode.Excess10 => 0.1,
            AutoBuyAffordabilityMode.Excess100 => 0.01,
            AutoBuyAffordabilityMode.Excess1000 => 0.001,
            _ => 0.01,
        };

    // Parse AbsoluteReserve the way legacy did (invariant double, per BigAmount.TryParse →
    // TryFromDouble) and widen to BigDouble by value; but treat empty/malformed/non-finite as the
    // config default of no reserve (0) rather than rejecting every candidate.
    private static BigDouble ResolveAbsoluteReserve(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            !double.IsNaN(parsed) && !double.IsInfinity(parsed))
        {
            return parsed;
        }

        return default;
    }

    private static bool IsZero(BigDouble value) => value.Mantissa == 0.0;

    private static bool IsNegative(BigDouble value) => value.Mantissa < 0.0;

    private readonly struct Eligible
    {
        public Eligible(
            int candidateIndex,
            AutoBuyCandidateKind kind,
            Guid uuid,
            string uuidText,
            double costRatio,
            in AutoBuyPlanBelief belief)
        {
            CandidateIndex = candidateIndex;
            Kind = kind;
            Uuid = uuid;
            UuidText = uuidText;
            CostRatio = costRatio;
            Belief = belief;
        }

        /// <summary>Where the candidate's rows live, so the batch ledger can re-read its costs.</summary>
        public int CandidateIndex { get; }
        public AutoBuyCandidateKind Kind { get; }
        public Guid Uuid { get; }
        public string UuidText { get; }
        public double CostRatio { get; }

        /// <summary>The one-level admission belief preserved for exact single-level parity.</summary>
        public AutoBuyPlanBelief Belief { get; }
    }
}
