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


using static OrbAutomata.GameMcp.GameMcpCastProjection;
using static OrbAutomata.GameMcp.GameMcpPrestigeProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpWorldRead;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpChallengeProjection
{
    internal static GameMcpValue Project(in ChallengeSubmission submission)
    {
        if (submission.Outcome.Verified) return new GameMcpObjectBuilder().Freeze();
        var result = new GameMcpObjectBuilder();

        // A press that was attempted spends before it asks for offers, so its failure carries the
        // settled budget on both sides — the pair a committed press publishes, on the one path a
        // caller would otherwise have to infer the spend from. A refusal that never pressed names
        // the one axis that failed and hands over the number its sentence read, so a planner learns
        // the live budget instead of a hand-written rule that can drift.
        if (submission.RerollsLeft >= 0 && submission.RerollsLeftAfter >= 0)
        {
            var budget = new GameMcpObjectBuilder();
            budget["before"] = submission.RerollsLeft;
            budget["after"] = submission.RerollsLeftAfter;
            result["rerollsLeft"] = budget;
        }
        else if (submission.RerollsLeft >= 0) result["rerollsLeft"] = submission.RerollsLeft;
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested challenge transition";
        return result.Freeze();
    }

    /// <summary>
    /// How far the player has come with one challenge — the one category here that reaches all
    /// three words.
    /// </summary>
    /// <remarks>
    /// <c>ChallengeSO.IsMaxLevel()</c> is <c>HasMaxLevel() &amp;&amp; GetQueuedLevel() &gt;=
    /// maxLevel</c>, a real ceiling, and it is asked first for the same reason exhaustion is asked
    /// first for an upgrade: <c>IsAvailableToRun()</c> opens with <c>if (IsMaxLevel()) return
    /// false</c>, so the game's own predicate composes in that order. <c>IsAvailableToRun()</c> is
    /// then <c>availabilityPrerequisites.Check(level)</c> and every previous challenge completed —
    /// a genuine lock the player meets, which is why this category gets the word at all. It is not
    /// the same question as the run column beside it, and it is not the same question as
    /// <c>level</c> either: <c>PassChallenge()</c> increments <c>level</c> and sets the run word to
    /// <c>passed</c>, and <c>ChallengeListVariable.CycleOut()</c> calls <c>EmptyState()</c>, which
    /// returns that word to <c>idle</c> at the boundary that ends the run. So the run word is
    /// in-run status alone, <c>level</c> is the durable count of wins, and a challenge that has been
    /// beaten is <see cref="GameMcpListColumns.Available"/> again at the next level.
    /// </remarks>
    internal static string ChallengeLifecycle(in WorldChallenge challenge) =>
        challenge.MaximumLevelReached ? GameMcpListColumns.Completed :
        challenge.AvailableToRun ? GameMcpListColumns.Available :
        GameMcpListColumns.Locked;

    internal static GameMcpValue ProjectChallengePostState(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        if (command.TargetId == Guid.Empty)
            return ProjectChallengeRerollDelta(world, Before(command));
        if (!WorldLookup.TryFind(world.Challenges, command.TargetId, out var current))
            return PostStateNotPublished("this challenge");
        if (command.Mode == "select")
        {
            var oldWorld = Before(command);
            var selected = new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                ["selected"] = new JObject
                {
                    ["before"] =
                        oldWorld is not null && ChallengeSelected(oldWorld, command.TargetId),
                    ["after"] = ChallengeSelected(world, command.TargetId),
                },
            };

            // The press the caller did not ask for, named — in the same shape a displaced spell
            // already answers in, because it is the same fact.
            if (command.SecondaryId != Guid.Empty)
                selected["displaced"] = new JObject
                {
                    ["uuid"] = command.SecondaryId.ToString("D"),
                    ["selected"] = new JObject
                    {
                        ["before"] = true,
                        ["after"] = ChallengeSelected(world, command.SecondaryId),
                    },
                };
            return selected.Freeze();
        }
        var priorWorld = Before(command);
        var beforeState = priorWorld is not null && WorldLookup.TryFind(
            priorWorld.Challenges, command.TargetId, out var previous)
            ? ChallengeRun(previous.State)
            : null;

        // A queue press moves the run, never the lifecycle — reporting it under `state` would be
        // the one place on the surface where that word meant the other question.
        return Change(
            command.TargetId,
            beforeState,
            ChallengeRun(current.State),
            "run");
    }

    /// <summary>
    /// What one press of the challenge-offer button did. The game's own button decrements
    /// <c>challengeRerollsLeft</c> when <c>hasFetchedChallenges</c> is already set and otherwise sets
    /// that flag, so both facts ship as pairs on every commit: a caller must never have to infer
    /// from an absent key whether a scarce reroll left the budget. <c>changed</c> answers the other
    /// question a press asks — whether the redraw moved the offers — because the game promises the
    /// spend, not a different set: a pool small enough to redraw itself is a legitimate outcome, and
    /// the caller reads it here instead of diffing two offer lists itself.
    /// </summary>
    /// <remarks>
    /// The press is one button and it is two presses' worth of weight. The cycle's first press is
    /// the offer fetch: it costs no reroll, and besides redrawing it arms every challenge it
    /// fetched for the reset and unlocks the reset decision that was refusing until then. A live
    /// round pressed it, read <c>changed: yes</c> beside a fresh five, and had to go to a second
    /// verb to discover that five challenges were now queued and the reset had opened — a commit
    /// of that size hiding behind a word about whether a list moved. So the answer says which of
    /// the two presses this was, what it armed, and what it unlocked, from the same world the
    /// state read answers from.
    /// </remarks>
    internal static GameMcpValue ProjectChallengeRerollDelta(
        GameWorldState world,
        GameWorldState? before)
    {
        var after = world.ChallengeContext;
        var prior = before?.ChallengeContext;
        var firstOfCycle = prior is { Available: true } && !prior.Value.ChallengesFetched;
        var result = new JObject
        {
            ["press"] = firstOfCycle ? "offer_fetch" : "reroll",
            ["rerollsLeft"] = new JObject
            {
                ["before"] = prior is { Available: true } ? Number(prior.Value.RerollsLeft) : (int?)null,
                ["after"] = Number(after.RerollsLeft),
            },
            ["challengesFetched"] = new JObject
            {
                ["before"] = prior is { Available: true } ? prior.Value.ChallengesFetched : (bool?)null,
                ["after"] = after.ChallengesFetched,
            },
        };
        if (prior is { Available: true } settled && after.Available)
            result["changed"] = !SameChallengeOffers(settled.TimeOffers, after.TimeOffers);
        result["offers"] = ChallengeReferences(after.TimeOffers);
        result["queuedForReset"] = PrestigeChallenges(world, queuedRewards: false);
        result["reset"] = new JObject
        {
            ["before"] = prior is { Available: true }
                ? prior.Value.WorldCycleComplete && prior.Value.ChallengesFetched
                : (bool?)null,
            ["after"] = after.WorldCycleComplete && after.ChallengesFetched,
        };
        return result.Freeze();
    }

    internal static bool SameChallengeOffers(
        PublicationTable<WorldChallengeReference> before,
        PublicationTable<WorldChallengeReference> after)
    {
        if (before.Count != after.Count) return false;
        for (var index = 0; index < before.Count; index++)
            if (before[index].ChallengeId != after[index].ChallengeId) return false;
        return true;
    }

    /// <summary>
    /// The selection a select press has to give up first when the screen holds no free slot.
    /// </summary>
    /// <remarks>
    /// Selecting past a full list is two presses on the screen: drop one, then take the one you
    /// want. A caller naming a challenge means the second press, so the tool makes the first one
    /// itself whenever the choice is forced. It answers nothing when there is room, when the caller
    /// already holds the target, or when more than one selection could be the one to drop — that
    /// last one is the caller's choice, and the action boundary refuses it by name.
    /// </remarks>
    internal static Guid ChallengeSelectionToReplace(GameWorldState world, Guid targetId)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var context = world.ChallengeContext;
        if (!context.Available || context.SelectionMaximum <= 0) return Guid.Empty;
        var selected = context.Selected;
        if (selected.Count < context.SelectionMaximum || selected.Count != 1) return Guid.Empty;
        var held = selected[0].ChallengeId;
        return held == targetId ? Guid.Empty : held;
    }

    internal static bool ChallengeSelected(GameWorldState world, Guid challengeId)
    {
        var selected = world.ChallengeContext.Selected;
        for (var index = 0; index < selected.Count; index++)
            if (selected[index].ChallengeId == challengeId) return true;
        return false;
    }

    internal static GameMcpValue ProjectChallenge(GameWorldState world, in WorldChallenge challenge)
    {
        var context = world.ChallengeContext;
        var selected = Contains(context.Selected, challenge.EntityId, out _);
        var inTime = Contains(context.TimeOffers, challenge.EntityId, out var timeRestricted);
        var inPrestige = Contains(context.PrestigeOffers, challenge.EntityId, out var prestigeRestricted);
        var restricted = timeRestricted || prestigeRestricted;
        var result = new JObject
        {
            ["entityId"] = challenge.EntityId.ToString("D"),
            ["category"] = "challenges",
            ["state"] = ChallengeLifecycle(in challenge),
            ["run"] = ChallengeRun(challenge.State),
            ["level"] = new GameMcpDomainValue(new BigDouble(challenge.Level)),
            ["seen"] = challenge.Seen,
            ["rewardQueued"] = challenge.RewardQueued,
            ["completedOnce"] = challenge.CompletedOnce,
            ["nextDifficulty"] = new GameMcpDomainValue(challenge.NextDifficulty),
            ["nextReward"] = new GameMcpDomainValue(challenge.NextReward),
            ["selected"] = selected,
            ["inTimeOffers"] = inTime,
            ["inPrestigeOffers"] = inPrestige,
        };
        if (challenge.MaxLevel >= 0) result["maximumLevel"] = challenge.MaxLevel;

        // What a timed challenge is racing. The game's own check is
        // `Player.GetResetTimePassed() > ChallengeCondition.GetTimeLimit(level)`, and the wire
        // carried the elapsed half only — `world_overview`'s `timePlayedThisReset` — so a caller
        // could see the clock running and not what it ran out against. The limit is the game's
        // answer for this challenge's own level, and it prints through `RunClock` because
        // `GetTimeBeforeNodes` draws it with `Utils.BeautifyTimeUltraPrecise`: the same rule that
        // formats the elapsed variable, so the two numbers a caller compares are in one notation. A
        // condition that races nothing says nothing here, and an unpublished limit is absent rather
        // than a zero that would read as "no time left".
        if (challenge.ConditionBeforeType == WorldChallenge.TimedCondition)
            result["timeLimit"] = RunClock(challenge.TimeLimit);

        // The selection budget is the game's own gate and the published world carries no reading of
        // it: `SelectionMaximum` is the list's declared maximum, and the press asks
        // `HasEmptySpot()`, which on a full list is still a yes for the swap the verb performs.
        // The page said no where the verb then said yes — a predicted refusal a caller planned
        // around for nothing — so the budget is no longer a clause here. What this page refuses on
        // is what the world states outright: an unoffered challenge, and one the offer set itself
        // marks restricted. Everything else is the verb's answer to give.
        var selectable = context.Available && (selected || inTime || inPrestige) &&
            (selected || !restricted);
        var select = new JObject { ["available"] = selectable };
        if (!selectable)
            select["reasonCode"] = !context.Available
                ? "challenge_state_unavailable"
                : !selected && !inTime && !inPrestige
                    ? "not_offered"
                    : "selection_restricted";
        result["select"] = select;

        // The button reads "activate" on the screen but it queues: the challenge starts at the next
        // reset, not now. Naming the verb after the press taught callers to expect a running
        // challenge and to read the queued state as a failure. The gate says whether the press is
        // open and why not; the row beside it already says where the challenge stands.
        var queueAvailable = context.Available && (inTime || inPrestige) && challenge.State is 0 or 1;
        var queue = new JObject { ["available"] = queueAvailable };
        if (!queueAvailable)
            queue["reasonCode"] = !inTime && !inPrestige ? "not_offered" : "already_ran";
        result["queue"] = queue;
        if (challenge.State == 2)
            result["abandon"] = new JObject { ["available"] = true };
        return result.Freeze();
    }

    /// <summary>
    /// What this challenge's own run did, in the game's own five words for it. Not a lifecycle:
    /// <c>passed</c> is one attempt cleared, after which the challenge is offered again a level
    /// higher, so it says nothing about how far the player has come.
    /// </summary>
    internal static string ChallengeRun(int state) => state switch
    {
        0 => GameMcpListColumns.RunIdle,
        1 => GameMcpListColumns.RunQueued,
        2 => GameMcpListColumns.RunActive,
        3 => GameMcpListColumns.RunPassed,
        4 => GameMcpListColumns.RunFailed,
        _ => "unknown",
    };

    /// <summary>
    /// Everything the challenge screen shows, answered when a caller asks for it.
    /// </summary>
    /// <remarks>
    /// It also said the same list three times — the two offer arrays and the surviving-selection
    /// filter were byte-identical on every live sample — under two reroll decisions that were one
    /// budget, one list, and one button. It is one answer now, and a caller asks for it.
    /// </remarks>
    internal static JObject ChallengeStateRead(GameMcpFrameContext state)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        // The reset state is published once, at the top level, on both verbs that carry it. Nesting
        // a second byte-identical copy inside the challenge block gave a caller two blocks with
        // nothing to tell them apart, on the two responses that were already the largest.
        var result = new JObject();
        result["status"] = "available";
        result["prestigeState"] = ProjectPrestigeState(publication.Snapshot);
        result["challengeState"] = ProjectChallengeState(publication.Snapshot);
        return result;
    }

    internal static GameMcpValue ProjectChallengeState(GameWorldState world)
    {
        var context = world.ChallengeContext;
        if (!context.Available)
            return new JObject
            {
                ["available"] = false,
                ["reasonCode"] = context.UnavailableReason.Length == 0
                    ? "challenge_state_unavailable"
                    : context.UnavailableReason,
            }.Freeze();
        var fetchAvailable = context.WorldCycleComplete &&
            (!context.ChallengesFetched || context.RerollsLeft > 0);
        var result = new JObject
        {
            ["available"] = true,
            ["worldCycleComplete"] = context.WorldCycleComplete,
            ["challengesFetched"] = context.ChallengesFetched,
            ["rerollsLeft"] = Number(context.RerollsLeft),
            ["rerollsMaximum"] = Number(context.RerollsMaximum),
            ["selectionMaximum"] = Number(context.SelectionMaximum),
            ["selected"] = ChallengeReferences(context.Selected),
        };

        // An offer is an offer of this cycle's, and the game does not clear the list when a cycle
        // ends: after a reset the same five names sat under `offers:` beside
        // `challengesFetched: no` while every one of them was running, and before the first fetch
        // they were the previous cycle's draw. Both readings are the same lie in two places, so
        // the list is published exactly while it is the offer set: what the running challenges are
        // is the `run` column's answer, on the rows that own it.
        //
        // The Reset modal draws from the same list asset the Time screen does, so it is said once.
        // A build where the two ever part company says the second one out loud rather than quietly
        // planning off the first.
        if (context.ChallengesFetched)
        {
            result["offers"] = ChallengeReferences(context.TimeOffers);
            if (!SameChallengeOffers(context.TimeOffers, context.PrestigeOffers))
                result["resetOffers"] = ChallengeReferences(context.PrestigeOffers);
        }
        result["reroll"] = RerollDecision(fetchAvailable, context);
        return result.Freeze();
    }

    /// <summary>
    /// Whether the challenge-offer button can be pressed, and what it costs. The game relabels one
    /// button: the first press is free and sets the fetched flag, every later press spends a reroll,
    /// so the decision block says which of the two this press would be.
    /// </summary>
    internal static JObject RerollDecision(bool available, in WorldChallengeContext context)
    {
        var result = new JObject
        {
            ["available"] = available,
            ["costsReroll"] = context.ChallengesFetched,
        };
        if (!available)
            result["reasonCode"] = !context.WorldCycleComplete
                ? "world_cycle_incomplete"
                : "no_rerolls";
        return result;
    }

    internal static JArray ChallengeReferences(PublicationTable<WorldChallengeReference> references)
    {
        var result = new JArray();
        for (var index = 0; index < references.Count; index++)
            result.Add(references[index].ChallengeId.ToString("D"));
        return result;
    }

    internal static bool Contains(PublicationTable<WorldChallengeReference> references,
        Guid id, out bool restricted)
    {
        restricted = false;
        for (var index = 0; index < references.Count; index++)
        {
            if (references[index].ChallengeId != id) continue;
            restricted = references[index].SelectionRestricted;
            return true;
        }
        return false;
    }

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldChallenge challenge,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = challenge.EntityId.ToString("D"),
                ["state"] = ChallengeLifecycle(in challenge),
                ["run"] = ChallengeRun(challenge.State),
                ["level"] = new GameMcpDomainValue(new BigDouble(challenge.Level)),
            }.Freeze();
    }

}
#endif
