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


using static OrbAutomata.GameMcp.GameMcpChallengeProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;

namespace OrbAutomata.GameMcp;

internal static class GameMcpPrestigeProjection
{
    internal static GameMcpValue Project(in NativeActionOutcome submission)
    {
        if (submission.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "next lifecycle";
        return result.Freeze();
    }

    internal static GameMcpValue ProjectPrestigePostState(GameMcpFrameContext state)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var result = new JObject
        {
            ["lifecycleGeneration"] = state.LifecycleGeneration,
            ["scene"] = state.SceneName,
            ["prestigeState"] = ProjectPrestigeState(world),
            ["challengeState"] = ProjectChallengeState(world),
        };
        return result.Freeze();
    }

    /// <summary>
    /// What a reset says when the post-reset world has not landed yet. The reset is the one action
    /// whose own identity is the lifecycle it replaced, so the answer carries that on both sides:
    /// a caller learns the reset happened, and that only the republished world is still owed.
    /// It used to answer the heaviest verb in the game with a bare timeout sentence and nothing to
    /// correlate it against.
    /// </summary>
    internal static GameMcpValue PrestigeSettlementPending(
        long expectedLifecycleGeneration,
        long? observedLifecycleGeneration,
        string? sceneName)
    {
        var result = new JObject
        {
            ["lifecycleGeneration"] = new JObject
            {
                ["before"] = expectedLifecycleGeneration,
                ["after"] = observedLifecycleGeneration,
            },
            ["postStateUnavailable"] = new JObject
            {
                ["reasonCode"] = "post_state_timeout",
                ["reason"] = observedLifecycleGeneration > expectedLifecycleGeneration
                    ? "the reset replaced the lifecycle, but the post-reset world was not " +
                      "republished within fifteen seconds; read it with world_overview"
                    : "the reset was submitted, but neither a new lifecycle nor a post-reset " +
                      "world appeared within fifteen seconds",
            },
        };
        if (!string.IsNullOrEmpty(sceneName)) result["scene"] = sceneName;
        return result.Freeze();
    }

    /// <summary>
    /// The reset decision, in the numbers the Reset screen itself shows, under the names it shows
    /// them under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The game keeps three Time Advancement figures and names all three: "Starting Time
    /// Advancements" (what a reset would start with — a live projection that keeps moving during a
    /// run), "Previous Time Advancements" (what the previous reset actually banked), and "New Time
    /// Advancements" (how many more than that reset, which the game computes itself). The block
    /// carries them under those three words.
    /// </para>
    /// <para>
    /// Both earlier shapes misled. Publishing the projection under the screen's label and
    /// subtracting the wrong pair from it made the one number that decides whether to reset arrive
    /// negative while the screen showed a gain. Naming the pair <c>atStart</c> and
    /// <c>previousStart</c> then read as history — "what the run I just finished started with" —
    /// which is a fact the game overwrites at every reset and no longer holds anywhere. The gain is
    /// read from the game rather than subtracted here for the same reason: the screen prints the
    /// game's figure, and a caller comparing the two must not find a third number.
    /// </para>
    /// </remarks>
    internal static GameMcpValue ProjectPrestigeState(GameWorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var context = world.ChallengeContext;
        if (!context.Available || !context.PrestigeAvailable)
            return new JObject
            {
                ["available"] = false,
                ["reasonCode"] = !context.Available && context.UnavailableReason.Length != 0
                    ? context.UnavailableReason
                    : context.PrestigeUnavailableReason.Length == 0
                    ? "prestige_state_unavailable"
                    : context.PrestigeUnavailableReason,
            }.Freeze();

        var available = context.WorldCycleComplete && context.ChallengesFetched;
        var reset = new JObject { ["available"] = available };
        if (!available)
            reset["reasonCode"] = !context.WorldCycleComplete
                ? "world_cycle_incomplete"
                : "challenges_not_fetched";
        var result = new JObject
        {
            ["timeAdvancements"] = new JObject
            {
                ["starting"] = Anchored(context.PersistenceCurrent, "next reset's start"),
                ["previous"] = Anchored(context.PersistencePrevious, "this run's start"),
                ["new"] = Anchored(
                    context.PersistenceProjected, "gain over this run's start"),
            },
            ["resetCount"] = context.ResetCount,

            // What these two hold: the offers already queued, which is what a reset would start,
            // and the challenges holding a reward the reset carries over. The first was named for
            // selections and read as a third copy of the offer list, because on a save where every
            // offer is queued it is one.
            ["queuedForReset"] = PrestigeChallenges(world, queuedRewards: false),
            ["survivingRewards"] = PrestigeChallenges(world, queuedRewards: true),
            ["reset"] = reset,
        };
        if (context.PersistentResourceId != Guid.Empty)
        {
            var holding = new JObject { ["resourceId"] = context.PersistentResourceId.ToString("D") };
            if (WorldLookup.TryFind(world.Resources, context.PersistentResourceId, out var resource))
            {
                holding["amount"] = new GameMcpDomainValue(
                    WorldResourceCoordinate.DisplayAmount(in resource));
                if (resource.IsCapped)
                    holding["capacity"] = new GameMcpDomainValue(resource.Reading.Capacity);
                holding["atCapacity"] = resource.IsAtCapacity;
            }
            result["persistentResource"] = holding;
        }
        return result.Freeze();
    }

    /// <summary>
    /// One Time Advancement figure with the run it belongs to said beside it.
    /// </summary>
    /// <remarks>
    /// The game's three display words are the block's keys and stay exactly as the screen prints
    /// them, but read cold they point at the wrong runs: a live round read <c>starting</c> as "what
    /// this run started with" and <c>previous</c> as "the run before", declared the wire in
    /// contradiction with a maintained doc, and only unpicked it a paragraph later. The anchor is
    /// what the word is missing, so each number carries its own — and the reader never has to hold
    /// two of them side by side to work out which run either describes.
    /// </remarks>
    internal static string Anchored(int amount, string anchor) =>
        amount.ToString(CultureInfo.InvariantCulture) + " (" + anchor + ")";

    internal static JArray PrestigeChallenges(GameWorldState world, bool queuedRewards)
    {
        var result = new JArray();
        if (queuedRewards)
        {
            for (var index = 0; index < world.Challenges.Count; index++)
                if (world.Challenges[index].RewardQueued)
                    result.Add(world.Challenges[index].EntityId.ToString("D"));
            return result;
        }
        for (var index = 0; index < world.ChallengeContext.PrestigeOffers.Count; index++)
        {
            var id = world.ChallengeContext.PrestigeOffers[index].ChallengeId;
            if (WorldLookup.TryFind(world.Challenges, id, out var challenge) && challenge.State == 1)
                result.Add(id.ToString("D"));
        }
        return result;
    }

}
#endif
