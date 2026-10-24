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
using static OrbAutomata.GameMcp.GameMcpDiscoveryTreeOfferProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpRitualLifecycleProjection
{
    internal static GameMcpValue Project(in RitualLifecycleSubmission submission)
    {
        if (submission.Outcome.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested Ritual transition";

        // A refusal whose sentence names a floor and a ceiling carries both as numbers, read from
        // the admission capture the sentence was written from.
        if (submission.MinimumAmount >= 0) result["minimumAmount"] = submission.MinimumAmount;
        if (submission.MaximumAmount >= 0) result["maximumAmount"] = submission.MaximumAmount;
        return result.Freeze();
    }

    /// <summary>
    /// Whether the ritual screen shows the ritual rather than the undiscovered placeholder that
    /// stands in for it. <c>RitualSO.IsAvailable()</c>, <c>IsVisible()</c> and <c>IsDiscovered()</c>
    /// are one member three times over, and it is the field the world already publishes. A ritual is
    /// re-run without end, so nothing about one is ever finished.
    /// </summary>
    internal static string RitualState(in WorldRitual ritual) =>
        ritual.Discovered ? GameMcpListColumns.Available : GameMcpListColumns.Locked;

    internal static GameMcpValue ProjectRitualLifecycleDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        if (!WorldLookup.TryFind(world.Rituals, command.TargetId, out var current))
            return PostStateNotPublished("this ritual");
        var oldWorld = Before(command);
        WorldRitual previous = default;
        var hadBefore = oldWorld is not null &&
            WorldLookup.TryFind(oldWorld.Rituals, command.TargetId, out previous);
        if (command.Mode is "activate" or "end")
        {
            var postState = new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                ["activeBattle"] = new JObject
                {
                    ["before"] = hadBefore ? previous.InBattle : (bool?)null,
                    ["after"] = current.InBattle,
                },
                ["wavesCompleted"] = new JObject
                {
                    ["before"] = hadBefore ? previous.WavesCompleted : (int?)null,
                    ["after"] = current.WavesCompleted,
                },
            };
            if (current.InBattle)
            {
                postState["selectedLevel"] = current.CurrentLevel;
                var drain = ProjectRitualCosts(world, current.Decision.CompletionCosts);
                if (drain.Count > 0) postState["completionCosts"] = drain;

                // The consequence, on the answer that starts it. "Activate a ritual" replies in
                // battle vocabulary and a live round then had to guess what a running battle
                // blocked — guessing its way into the round's one irreversible action.
                postState["gates"] = GameMcpDecisionReason.RitualBattleGate;
            }
            else
            {
                // Ending a battle is the moment its result exists. The settled row carries the level
                // the battle reached and the duration rewards it left running; without them the
                // caller has to guess what the results modal said.
                postState["reachedLevel"] = current.LastReachedLevel;
                postState["activeInstances"] = current.ActiveInstances;

                // The results modal says "Ritual failed." in words and lists the spoils. Both are
                // native facts, so the response carries the same verdict the game showed rather
                // than leaving a caller to infer one from a wave count that cannot distinguish a
                // clean win from a run stopped on the last wave. Only the mode that ends a run
                // reports one: the battle flag would also hand a verdict and an empty spoils list
                // to an activate whose settled capture has not flipped inBattle yet.
                if (command.Mode == "end")
                {
                    postState["result"] = current.FailedRun ? "failed" : "succeeded";
                    postState["spoils"] = ProjectRitualSpoils(current.Spoils);
                }
            }

            return postState.Freeze();
        }
        if (command.Mode == "cancel_duration")
            return Change(command.TargetId,
                hadBefore ? (object)(previous.ActiveInstances > 0) : null,
                current.ActiveInstances > 0,
                "activeDurationReward");

        var result = new JObject { ["uuid"] = command.TargetId.ToString("D") };
        if (command.Mode is "select" or "deselect")
            result["selected"] = new JObject
            {
                ["before"] = hadBefore && previous.Decision.Selected,
                ["after"] = current.Decision.Selected,
            };
        else
            result["startingLevel"] = new JObject
            {
                ["before"] = hadBefore ? previous.CurrentLevel : (int?)null,
                ["after"] = current.CurrentLevel,
            };
        return result.Freeze();
    }

    internal static GameMcpValue ProjectRitual(GameWorldState world, in WorldRitual ritual)
    {
        var result = new JObject
        {
            ["entityId"] = ritual.EntityId.ToString("D"),
            ["category"] = "rituals",
            ["state"] = RitualState(in ritual),
            ["inBattle"] = ritual.InBattle,
            ["activeInstances"] = ritual.ActiveInstances,
            ["reachedLevel"] = ritual.ReachedLevel,
            ["selectedLevel"] = ritual.CurrentLevel,
            ["waveTotal"] = ritual.RequiredWaves,
        };
        AddRitualRun(result, in ritual);
        AddRitualDecision(world, result, in ritual);
        AddDiscoveryDecision(
            world,
            result,
            ritual.Discovery,
            KnownEntities.RitualsDiscover.Uuid,
            ritual.EntityId);
        return result.Freeze();
    }

    /// <summary>
    /// The run record the game retains, in the tense its own fields are in. <c>wavesCompleted</c>
    /// and <c>currentSpoils</c> are one record that <c>Initiate()</c> clears and nothing else does,
    /// so while a battle runs they describe that battle and afterwards they are the finished run's.
    /// A verdict exists only for a finished run — <c>IsFailedRun()</c> is <c>wavesCompleted &lt; 5</c>,
    /// which reads "failed" for a battle still on its second wave and for a ritual nobody has
    /// played, so neither gets one.
    /// </summary>
    internal static void AddRitualRun(JObject result, in WorldRitual ritual)
    {
        if (ritual.InBattle)
        {
            result["wavesCompleted"] = ritual.WavesCompleted;
            if (ritual.Spoils.Count > 0) result["spoils"] = ProjectRitualSpoils(ritual.Spoils);
            return;
        }

        // A cleared count and no spoils is exactly the state a ritual that has never run is in, so
        // there is no record to report rather than a run that banked nothing.
        if (ritual.WavesCompleted == 0 && ritual.Spoils.Count == 0) return;
        result["lastRun"] = new JObject
        {
            ["result"] = ritual.FailedRun ? "failed" : "succeeded",
            ["wavesCompleted"] = ritual.WavesCompleted,
            ["spoils"] = ProjectRitualSpoils(ritual.Spoils),
        };
    }

    internal static void AddRitualDecision(
        GameWorldState world,
        JObject result,
        in WorldRitual ritual)
    {
        var selected = ritual.Decision.Selected;
        var anyBattleActive = false;
        for (var index = 0; index < world.Rituals.Count; index++)
            if (world.Rituals[index].InBattle) { anyBattleActive = true; break; }
        result["selected"] = selected;
        // Selection is no longer a precondition a caller has to satisfy: the verb presses the
        // screen's own selection toggle first. The starting-level bounds are facts of the ritual
        // and the player, so the decision is complete whether or not this ritual is the held one.
        var level = new JObject
        {
            ["available"] = !ritual.ForceLevel && !anyBattleActive,
            ["current"] = ritual.CurrentLevel,
        };
        if (ritual.ForceLevel)
        {
            level["reasonCode"] = "level_locked";
            level["reason"] = RitualLifecycleGameAction.LevelLockedReason(ritual.ForceLevelValue);
        }
        else
        {
            // Both bounds, from the same native control: the jump-start selector is clamped to
            // 1..RitualSO.GetMaxSelectedLevel(), and a caller that only ever saw the ceiling had no
            // way to discover that 0 is not a starting level the game offers. One presence rule —
            // the bounds ride on every ritual whose starting level is the caller's to choose, so
            // selecting one is not how a caller finds out the range exists.
            level["minimum"] = WorldRitualDecision.NativeMinimumStartingLevel;
            level["maximum"] = ritual.Decision.MaximumStartingLevel;
            if (anyBattleActive) level["reasonCode"] = "ritual_battle_active";
        }
        result["setLevel"] = level;

        // Activation presses the selection toggle first, so being the held ritual is not something a
        // caller has to arrange. It was the last fact that only the selection could answer, and it
        // no longer is.
        var activateAvailable = ritual.Discovered && !anyBattleActive &&
            ritual.Decision.UsageRequirementsMet && ritual.Decision.ActivationAffordable;
        // The price is a fact of the ritual, not of the selection, so it rides on every row. Pricing
        // only the held one made "which of these can I afford" a question a caller had to answer by
        // selecting each of them in turn — a mutation, to read.
        var activate = new JObject { ["available"] = activateAvailable };
        activate["affordable"] = ritual.Decision.ActivationAffordable;
        var activationCosts = ProjectRitualCosts(world, ritual.Decision.ActivationCosts);
        if (activationCosts.Count > 0) activate["costs"] = activationCosts;
        var completionCosts = ProjectRitualCosts(world, ritual.Decision.CompletionCosts);
        if (completionCosts.Count > 0) activate["completionCosts"] = completionCosts;
        if (!activateAvailable)
            activate["reasonCode"] = !ritual.Discovered
                ? "not_discovered"
                : anyBattleActive
                    ? "ritual_battle_active"
                    : !ritual.Decision.UsageRequirementsMet
                        ? "usage_requirements_unmet"
                        : "unaffordable";
        result["activate"] = activate;

        var durationAvailable = ritual.DurationRewardBlocks > 0 && ritual.ActiveInstances > 0;
        result["cancelDuration"] = durationAvailable
            ? new JObject { ["available"] = true }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = ritual.DurationRewardBlocks > 0
                    ? "no_active_duration_reward"
                    : "not_a_duration_ritual",
            };
    }

    /// <summary>
    /// What the run banked, exactly as the results screen lists it. An empty list is written, not
    /// omitted: a run that won nothing is a different answer from a response that does not carry
    /// spoils at all.
    /// </summary>
    internal static JArray ProjectRitualSpoils(PublicationTable<WorldRitualSpoil> spoils)
    {
        var result = new JArray();
        for (var index = 0; index < spoils.Count; index++)
        {
            var spoil = spoils[index];
            result.Add(new JObject
            {
                ["resourceId"] = spoil.ResourceId.ToString("D"),
                ["amount"] = new GameMcpDomainValue(spoil.Quantity),
            });
        }
        return result;
    }

    internal static JArray ProjectRitualCosts(
        GameWorldState world,
        PublicationTable<WorldRitualCost> costs)
    {
        var result = new JArray();
        for (var index = 0; index < costs.Count; index++)
        {
            var cost = costs[index];
            var price = PlayerFacingCost(world, cost.ResourceId, cost.Cost);
            var row = new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(price),
            };
            var held = BigDouble.Zero;
            if (WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
            {
                held = resource.Reading.Quantity;
                (row["cost"], row["spendableAmount"]) = GameMcpNumberFormatter.CellsApart(
                    price, SpendableAmount(world, cost.ResourceId, held));
            }

            // Every other cost row in the surface says which line the player is short on; a ritual
            // row left the caller to compare two formatted magnitudes itself.
            row["affordable"] = CanAfford(world, cost.ResourceId, cost.Cost, held);
            result.Add(row);
        }
        return result;
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldRitual listedRitual,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = listedRitual.EntityId.ToString("D"),
                ["state"] = RitualState(in listedRitual),
                ["selected"] = listedRitual.Decision.Selected,
                ["reachedLevel"] = listedRitual.ReachedLevel,
                ["selectedLevel"] = listedRitual.CurrentLevel,
                ["waveTotal"] = listedRitual.RequiredWaves,
                ["affordable"] = listedRitual.Decision.ActivationAffordable,
            }.Freeze();
    }

}
#endif
