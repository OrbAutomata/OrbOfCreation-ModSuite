#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;


using static OrbAutomata.GameMcp.GameMcpDecisionReason;
using static OrbAutomata.GameMcp.GameMcpRowKit;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpResearchProjection
{
    internal static GameMcpValue Project(in ResearchSubmission submission)
    {
        if (submission.Outcome.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested research transition";

        // A refusal whose sentence names a ceiling carries that same ceiling as a number, read from
        // the admission capture the sentence was written from.
        if (submission.MaximumAmount >= 0) result["maximumAmount"] = submission.MaximumAmount;
        return result.Freeze();
    }

    /// <summary>
    /// The same lifecycle for research, off the same kind of captured members:
    /// <c>ResearchSO.IsVisible()</c> is its prerequisite gate and <c>IsComplete()</c> is
    /// <c>IsMaxLevel()</c>. Research splits visibility across two authored containers where an
    /// upgrade uses one, but the game has already collapsed both into the single <c>visible</c>
    /// the world captures.
    /// </summary>
    internal static string ResearchLifecycle(in WorldResearch research) =>
        research.Complete ? GameMcpListColumns.Completed :
        research.Visible ? GameMcpListColumns.Available :
        GameMcpListColumns.Locked;

    internal static GameMcpValue ProjectResearchDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null ||
            !WorldLookup.TryFind(state.World.Snapshot.Research, command.TargetId, out var current))
            return PostStateNotPublished("this research");
        var oldWorld = Before(command);
        WorldResearch previous = default;
        var hasPrevious = oldWorld is not null && WorldLookup.TryFind(
            oldWorld.Research, command.TargetId, out previous);
        if (command.Mode == "bonus")
            return Change(command.TargetId,
                hasPrevious ? previous.SelfBonusLevels : (int?)null,
                current.SelfBonusLevels,
                "bonusLevel");

        // A develop does not finish a level, it buys research time: the levels go into the queue and
        // the game drains them over the minutes that follow. Narrating that hop was three facts and
        // every one of them was stale by the time a caller could read it — `development: idle ->
        // active` and `queuedLevels: 0 -> 1` both read back as their own before-value seconds
        // later, so the honest reading of a develop that worked was that it had not. The press
        // queued levels; the settlement above already proved it queued exactly the count that was
        // asked for, and where the queue stands now is a read.
        if (command.Mode == "develop")
            return QueuedMutation(command.TargetId, command.Amount, command.Amount);

        // Cancel, pause, and resume apply when they are pressed, so each says the one fact it moved.
        // A cancel empties the queue it was asked about; a pause and a resume move what the queue is
        // doing, which is never the lifecycle — a paused research is exactly as available as it was.
        if (command.Mode == "cancel")
            return Change(command.TargetId,
                hasPrevious ? ResearchQueuedLevels(previous) : (int?)null,
                ResearchQueuedLevels(current),
                "queuedLevels");
        return Change(command.TargetId,
            hasPrevious ? ResearchDevelopment(in previous) : null,
            ResearchDevelopment(in current),
            "development");
    }

    /// <summary>
    /// The queue count a research row publishes: the decision's own number where the decision was
    /// collected, and otherwise the game's own <c>GetQueuedLevels()</c> composition — the queued
    /// levels plus the one in flight.
    /// </summary>
    internal static int ResearchQueuedLevels(in WorldResearch research) =>
        research.Decision.Available
            ? research.Decision.QueuedLevels
            : Math.Max(research.QueuedLevels + (research.IsDeveloping ? 1 : 0), 0);

    /// <summary>
    /// What the development queue is doing with this research right now. Completion is no longer
    /// one of these words: it is a lifecycle state, <see cref="ResearchLifecycle"/> says it, and a
    /// finished research is developing nothing, which is what <c>idle</c> already means.
    /// </summary>
    internal static string ResearchDevelopment(in WorldResearch research) =>
        !research.IsDeveloping ? "idle" :
        research.IsActive ? "active" : "paused";

    internal static GameMcpValue ProjectResearch(GameWorldState world, in WorldResearch research)
    {
        var decision = research.Decision;
        var queued = ResearchQueuedLevels(in research);
        var result = new JObject
        {
            ["entityId"] = research.EntityId.ToString("D"),
            ["category"] = "research",

            // `visible`, `available` and `complete` were the three inputs to one lifecycle word,
            // published beside it. The word is the answer; the inputs stay on the raw-fact scan,
            // where reading the members the game exposes is the whole point.
            ["state"] = ResearchLifecycle(in research),
            ["development"] = ResearchDevelopment(in research),
            ["purchasedLevel"] = Number(research.PurchasedLevels),
            ["baseLevel"] = Number(research.BaseLevel),
            ["bonusLevel"] = Number(research.BonusLevel),
            ["totalLevel"] = Number(research.TotalLevel),
            ["queuedLevels"] = Number(queued),
            ["baseRequirementLevel"] = Number(research.BaseRequirementLevel),
            ["effectiveRequirementLevel"] = Number(research.EffectiveRequirementLevel),
            ["requirementLevelAdjustment"] = Number(research.RequirementLevelAdjustment),
        };
        if (research.MaxLevel > 0) result["maximumLevel"] = Number(research.MaxLevel);
        if (research.ArtificialMaxLevel > 0)
            result["artificialMaximumLevel"] = Number(research.ArtificialMaxLevel);
        if (research.Flagged) result["flagged"] = true;
        if (research.HiddenLevel) result["levelHidden"] = true;
        if (research.RequirementAdjustments.Count > 0)
        {
            var adjustments = new JArray();
            for (var index = 0; index < research.RequirementAdjustments.Count; index++)
            {
                var value = research.RequirementAdjustments[index];
                var adjustment = new JObject
                {
                    ["modifierId"] = value.ModifierId.ToString("D"),
                    ["sourceId"] = value.SourceId.ToString("D"),
                    ["amount"] = GameMcpNativeVocabulary.ModifierMagnitude(
                        value.ModifierType, value.Amount),
                    ["order"] = value.Order,
                };
                if (value.Passive) adjustment["passive"] = true;
                adjustments.Add(adjustment);
            }
            result["requirementAdjustments"] = adjustments;
        }
        if (!decision.Available)
        {
            result["develop"] = new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "research_decision_unavailable",
            };
            return result.Freeze();
        }

        if (research.IsDeveloping)
        {
            var progress = new JObject
            {
                ["stage"] = Number(research.ResearchStage),
                ["requiredStages"] = Number(research.RequiredStagesCached),
                ["elapsedSeconds"] = new GameMcpDomainValue(decision.CurrentTime),
                ["requiredSeconds"] = new GameMcpDomainValue(research.RequiredTimeCached),
                ["remainingSeconds"] = new GameMcpDomainValue(decision.RemainingTime),
                ["completionRatio"] = new GameMcpDomainValue(decision.TimeRatio),
            };
            if (research.IsActive)
                progress["etaSeconds"] = new GameMcpDomainValue(decision.RemainingTime);
            else progress["etaUnavailableReason"] = "paused";
            result["progress"] = progress;
        }
        result["investmentLevel"] = Number(decision.CurrentInvestmentLevel);
        if (decision.Investment.Count > 0)
        {
            // The development drain, not the price: Develop() pays `develop.costs` and then drains
            // this over the level's research time. `remaining` is what the bar still takes, after
            // the resource's own quality conversion; `invested` and `required` are the native fill
            // bar, both raw. Neither `cost` nor `amount`: the wire spells a price `cost` and renames
            // an `amount` beside `spendableAmount` into one.
            var drain = new JArray();
            for (var index = 0; index < decision.Investment.Count; index++)
            {
                var value = decision.Investment[index];
                var row = new JObject
                {
                    ["resourceId"] = value.ResourceId.ToString("D"),
                    ["remaining"] = new GameMcpDomainValue(value.Remaining),
                    ["invested"] = new GameMcpDomainValue(value.Invested),
                    ["required"] = new GameMcpDomainValue(value.Required),
                };
                if (TryFindResource(world, value.ResourceId, out var pool))
                {
                    (row["remaining"], row["spendableAmount"]) = GameMcpNumberFormatter.CellsApart(
                        value.Remaining, WorldResourceCoordinate.SpendableAmount(in pool));
                }
                drain.Add(row);
            }
            result["drain"] = drain;
        }
        if (decision.ResearchTypes.Count > 0)
        {
            var types = new JArray();
            for (var index = 0; index < decision.ResearchTypes.Count; index++)
            {
                var value = decision.ResearchTypes[index];
                var type = new JObject
                {
                    ["researchTypeId"] = value.ResearchTypeId.ToString("D"),
                    ["remainingBonusLevels"] = Number(value.RemainingBonusLevels),
                    ["investmentLevel"] = Number(value.CurrentInvestmentLevel),
                };
                if (value.MaximumInvestmentLevel > 0)
                    type["maximumInvestmentLevel"] = Number(value.MaximumInvestmentLevel);
                types.Add(type);
            }
            result["researchTypes"] = types;
        }

        var queueRoom = research.MaxLevel <= 0
            ? int.MaxValue
            : Math.Max(research.MaxLevel - research.Level - queued, 0);
        var developAvailable = research.Visible && decision.LevelsAvailable > 0;
        var develop = new JObject
        {
            ["available"] = developAvailable,
            ["route"] = decision.QueueMode ? "queue" : "immediate",
        };
        if (decision.QueueMode)
            develop["maximumBatch"] = Number(Math.Min(decision.MultiBuy, queueRoom));
        develop["levels"] = Number(research.Visible ? decision.LevelsAvailable : 0);
        if (developAvailable) develop["affordable"] = decision.DevelopmentCostAffordable;
        var blockedOnPrice = false;
        if (!developAvailable)
        {
            // The queue-mode gates come first because they are about the batch, not the level.
            // Everything after them asks ResearchSO.IsWithinDevelopRange's own gates in its own
            // order — completion, cost, level requirements, then leeway falling back to both caps
            // together. Leeway is one gate with the caps, not three: native develops on leeway OR
            // on being below both caps, so an exhausted leeway beside an open cap blocks nothing.
            var leewayBlocked = !research.StillHasLeeway &&
                !(research.BelowArtificialMaxLevel && research.BelowMaxInvestmentLevel);
            var reasonCode = !research.Visible
                ? "research_locked"
                : research.Complete
                ? "already_maxed"
                : decision.QueueMode && decision.MultiBuy <= 0
                    ? "multi_buy_unavailable"
                    : decision.QueueMode && queueRoom <= 0
                        ? "research_queue_full"
                        : !decision.DevelopmentCostAffordable
                            ? "unaffordable"
                            : !research.MeetsLevelRequirements
                                ? "requirements_unmet"
                                : leewayBlocked
                                    ? "research_leeway_exhausted"
                                    : research.IsDeveloping && !decision.QueueMode
                                        ? "already_developing"
                                        : !research.WithinDevelopRange
                                            ? "develop_range_refused"
                                            : "native_develop_refused";
            develop["reasonCode"] = reasonCode;
            blockedOnPrice = reasonCode == "unaffordable";
            if (reasonCode == "research_locked")
                develop["reason"] = GameMcpRequirementProjection.LockedResearchReason(
                    world, research.EntityId);

            // The cost verdict is published exactly when the cost is what decides. A row refused
            // for being maxed, queued out, or short of requirements has no next price to afford.
            if (blockedOnPrice)
            {
                develop["affordable"] = decision.DevelopmentCostAffordable;
                develop["reason"] = ShortfallReason(world, decision.DevelopmentCosts);
            }
        }

        // The price of the next level is a fact of the node until it is complete, whatever gate is
        // shut: the screen prints it on every node it draws.
        if (!research.Complete && decision.DevelopmentCosts.Count > 0)
        {
            var costs = new JArray();
            for (var index = 0; index < decision.DevelopmentCosts.Count; index++)
            {
                var value = decision.DevelopmentCosts[index];
                var playerCost = PlayerFacingCost(world, value.ResourceId, value.Price);
                var spendable = SpendableAmount(world, value.ResourceId, value.Held);
                var (costCell, amountCell) = GameMcpNumberFormatter.CellsApart(playerCost, spendable);
                costs.Add(new JObject
                {
                    ["resourceId"] = value.ResourceId.ToString("D"),
                    ["cost"] = costCell,
                    ["spendableAmount"] = amountCell,
                    ["affordable"] = CanAfford(
                        world, value.ResourceId, value.Price, value.Held),
                });
            }
            develop["costs"] = costs;
        }
        result["develop"] = develop;

        if (research.IsDeveloping)
        {
            result["cancel"] = new JObject { ["available"] = true };
            if (!decision.QueueMode)
                result[research.IsActive ? "pause" : "resume"] =
                    new JObject { ["available"] = true };
        }
        else if (research.Visible && decision.CanApplyBonusLevel && decision.FreeBonusLevels > 0)
            result["bonus"] = new JObject
            {
                ["available"] = true,
                ["remainingLevels"] = Number(decision.FreeBonusLevels),
            };
        return result.Freeze();
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldResearch listedResearch,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = listedResearch.EntityId.ToString("D"),
                ["state"] = ResearchLifecycle(in listedResearch),
                ["paused"] = !listedResearch.IsActive,
                ["totalLevel"] = listedResearch.TotalLevel,
                ["queuedLevels"] = ResearchQueuedLevels(in listedResearch),
                ["nextLevelRequirements"] = listedResearch.MeetsLevelRequirements
                    ? GameMcpListColumns.Met
                    : GameMcpListColumns.Unmet,
                ["canDevelop"] = listedResearch.Visible && listedResearch.Decision.Available &&
                    listedResearch.Decision.LevelsAvailable > 0,
                ["affordable"] = listedResearch.Decision.DevelopmentCostAffordable,
            }.Freeze();
    }

}
#endif
