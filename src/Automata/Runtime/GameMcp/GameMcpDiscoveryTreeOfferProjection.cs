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

internal static class GameMcpDiscoveryTreeOfferProjection
{
    internal static GameMcpValue Project(
        DiscoveryTreeOfferActionKind kind,
        in DiscoveryTreeOfferSubmission submission)
    {
        if (submission.Outcome.Verified) return new JObject().Freeze();
        var result = new JObject();

        // The refusal names the one axis that failed and hands over the number its sentence read,
        // so a planner learns the live budget instead of a hand-written rule that can drift.
        if (submission.RerollsLeft >= 0) result["rerollsLeft"] = submission.RerollsLeft;
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
        {
            result["missingOutcome"] = kind switch
            {
                DiscoveryTreeOfferActionKind.Initiate => "crafting mode",
                DiscoveryTreeOfferActionKind.Select => "requested offer selected",
                DiscoveryTreeOfferActionKind.Confirm => "requested offer discovered",
                DiscoveryTreeOfferActionKind.Reroll => "crafting mode",
                _ => "requested transition",
            };
        }
        return result.Freeze();
    }

    /// <summary>
    /// What a confirmed offer produced. The press permanently spends a discovery choice, and the
    /// answer used to be a bare count with nothing naming what was taken — so a caller could not
    /// confirm the discovery landed, and read the tree again to learn what it had got. The tool
    /// held that identity in its own request the whole time.
    /// </summary>
    internal static GameMcpValue ProjectDiscoveryOfferConfirmDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        if (!WorldLookup.TryFind(world.DiscoveryTrees, command.TargetId, out var after))
            return PostStateNotPublished("this discovery tree");
        var before = Before(command);
        WorldDiscoveryTree previous = default;
        var hadBefore = before is not null &&
            WorldLookup.TryFind(before.DiscoveryTrees, command.TargetId, out previous);
        var result = new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["discovered"] = command.SecondaryId.ToString("D"),
            ["treeDiscovered"] = new JObject
            {
                ["before"] = hadBefore ? previous.TotalDiscoveredCount : (int?)null,
                ["after"] = after.TotalDiscoveredCount,
            },
            // The count that just moved means nothing on its own: what a caller asked by confirming
            // is how much of this tree is left, and one number cannot say.
            ["discoverableCount"] = after.TotalDiscoverableCount,
            ["mode"] = new JObject
            {
                ["before"] = hadBefore ? DiscoveryMode(previous.ActionMode) : null,
                ["after"] = DiscoveryMode(after.ActionMode),
            },
            ["hasRemainingDiscoveries"] = after.HasRemainingDiscovery ||
                after.HasImmediateRequiredDiscovery,
        };
        // A spell taken off a tree is discovered by the same press as one taken off a row, and the
        // game loads it the same way. The tree route answered with the tree alone, so a caller that
        // confirmed an offer had to read the loadout again to learn where its new spell was.
        if (WorldLookup.TryFind(world.SpellRecipes, command.SecondaryId, out var recipe) &&
            recipe.Discovered)
            result["loadout"] = ProjectDiscoveredSpellLoadout(world, before, command.SecondaryId);
        return result.Freeze();
    }

    /// <summary>
    /// What a spent discovery reroll bought: the budget it cost, whether the offers actually moved,
    /// and the offers themselves. It answered with a bare post-value and no offers, so the caller
    /// re-read the whole tree to learn whether the press had done anything.
    /// </summary>
    internal static GameMcpValue ProjectDiscoveryRerollDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        if (!WorldLookup.TryFind(world.DiscoveryTrees, command.TargetId, out var after))
            return PostStateNotPublished("this discovery tree");
        var before = Before(command);
        WorldDiscoveryTree previous = default;
        var hadBefore = before is not null &&
            WorldLookup.TryFind(before.DiscoveryTrees, command.TargetId, out previous);
        var result = new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["rerollsLeft"] = new JObject
            {
                ["before"] = hadBefore ? previous.RerollsLeft : (int?)null,
                ["after"] = after.RerollsLeft,
            },
        };
        if (hadBefore)
            result["changed"] = !SameOfferIds(previous.CurrentOfferIds, after.CurrentOfferIds);
        var offers = new JArray();
        for (var index = 0; index < after.CurrentOfferIds.Count; index++)
            offers.Add(EntityReference(world, after.CurrentOfferIds[index]));
        if (offers.Count > 0) result["offers"] = offers;
        return result.Freeze();
    }

    internal static bool SameOfferIds(
        PublicationTable<Guid> before,
        PublicationTable<Guid> after)
    {
        if (before.Count != after.Count) return false;
        for (var index = 0; index < before.Count; index++)
            if (before[index] != after[index]) return false;
        return true;
    }

    /// <summary>
    /// A selection changes which offer is selected, not what is on offer. The settled tree still
    /// says its mode, its budget, and the offer it now holds by name; the list the caller just
    /// picked from is the one thing it already has.
    /// </summary>
    internal static GameMcpValue WithoutOffers(GameMcpValue projected)
    {
        if (projected is not GameMcpObject row) return projected;
        var result = new JObject();
        for (var index = 0; index < row.Properties.Count; index++)
        {
            var property = row.Properties[index];
            if (string.Equals(property.Name, "offers", StringComparison.Ordinal)) continue;
            result[property.Name] = property.Value;
        }
        return result.Freeze();
    }

    internal static bool HasDiscoveryPostState(
        GameMcpFrameContext state,
        Guid treeId,
        string mode,
        Guid offerId)
    {
        var world = state.World?.Snapshot;
        if (world is null) return false;
        for (var index = 0; index < world.DiscoveryTrees.Count; index++)
        {
            var tree = world.DiscoveryTrees[index];
            if (tree.EntityId != treeId) continue;
            return mode switch
            {
                // Initiate and reroll both end in DiscoveryTreeSO.EnterCraftingMode, and the offer
                // list only appears three seconds of game time later, when IncrementCrafting rolls
                // the tree into choice mode. Waiting for the offers made every one of these calls
                // report a timeout for a mutation that had already landed; crafting mode is what
                // the press itself produces.
                "initiate" or "reroll" => tree.ActionMode == 1,
                "select" => tree.ActionMode == 2 && tree.SelectedChoiceId == offerId,
                "confirm" => tree.ActionMode == 0 && tree.SelectedChoiceId == Guid.Empty,
                _ => true,
            };
        }
        return false;
    }

    /// <summary>
    /// Why this tree offers nothing, told apart by the two counts the row already prints.
    /// </summary>
    /// <remarks>
    /// One sentence — "This tree has nothing left to discover." — stood beside
    /// <c>discoveredCount: 12</c> and <c>discoverableCount: 65</c> and contradicted both. The game
    /// keeps two separate facts here: <c>RemainingDiscoverableItems()</c> is
    /// <c>allDiscoverableItems.Count - totalDiscoveredCount</c>, and <c>hasRemainingDiscovery</c>
    /// additionally requires a pool item that is undiscovered <em>and</em>
    /// <c>IsDiscoverVisible()</c>. So a tree with 53 undiscovered items and nothing currently in
    /// reach fails the second test while plainly passing the first, and the two answers are not the
    /// same news: one is a finished tree, the other is a tree waiting on a Recipe Book.
    /// </remarks>
    internal static void AddNoDiscoveryReason(JObject initiate, in WorldDiscoveryTree tree)
    {
        var discovered = tree.TotalDiscoveredCount;
        var discoverable = tree.TotalDiscoverableCount;
        var remaining = discoverable - discovered;
        if (remaining <= 0)
        {
            initiate["reasonCode"] = "no_discoveries";
            initiate["reason"] =
                "Every one of this tree's " +
                discoverable.ToString(CultureInfo.InvariantCulture) +
                " discoveries is made.";
            return;
        }
        initiate["reasonCode"] = "no_discoveries_in_reach";
        initiate["reason"] =
            "Nothing in this tree can be discovered right now: " +
            discovered.ToString(CultureInfo.InvariantCulture) + " of its " +
            discoverable.ToString(CultureInfo.InvariantCulture) +
            " are discovered, and none of the other " +
            remaining.ToString(CultureInfo.InvariantCulture) +
            " is in reach — a tree's pool is widened by its Recipe Books, and an item in the pool " +
            "is offered only once the game shows it.";
    }

    /// <summary>
    /// The price of a tree's next roll, one row per resource, with every unmet one added to
    /// <paramref name="shortfalls"/> when a caller words them.
    /// </summary>
    internal static JArray DiscoveryTreeCosts(
        GameWorldState world,
        in WorldDiscoveryTree tree,
        List<(string, BigDouble, BigDouble)>? shortfalls)
    {
        var costs = new JArray();
        for (var index = 0; index < tree.NextItemCosts.Count; index++)
        {
            var cost = tree.NextItemCosts[index];
            var amount = SpendableAmount(world, cost.ResourceId, cost.Held);
            var affordable = CanAfford(world, cost.ResourceId, cost.Price, cost.Held);
            var price = PlayerFacingCost(world, cost.ResourceId, cost.Price);
            var (priceCell, amountCell) = GameMcpNumberFormatter.CellsApart(price, amount);
            costs.Add(new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["cost"] = priceCell,
                ["spendableAmount"] = amountCell,
                ["affordable"] = affordable,
            });
            if (affordable || shortfalls is null) continue;
            var identity = EntityIdentityFormatter.Describe(cost.ResourceId, world.EntityIdentities);
            shortfalls.Add((
                identity.HasName ? identity.Name : cost.ResourceId.ToString("D"),
                price,
                amount));
        }
        return costs;
    }

    internal static GameMcpValue ProjectDiscoveryTree(
        GameWorldState world,
        in WorldDiscoveryTree tree)
    {
        var result = new JObject
        {
            ["entityId"] = tree.EntityId.ToString("D"),
            ["category"] = "discovery-trees",
            ["mode"] = DiscoveryMode(tree.ActionMode),
            ["rerollsLeft"] = tree.RerollsLeft,
            // The game's own cached counter, and the tree's own route is the only thing that
            // moves it: DiscoveryTreeSO.DiscoverItem is the sole writer, so a discovery made from
            // an entity's own row never reaches it. The name now says whose count it is — how many
            // this tree handed you, not how many of its things you have.
            ["treeDiscovered"] = tree.TotalDiscoveredCount,
            // The count the game caches is a count of the tree's own discoverable list, and without
            // that list's size no caller could tell an early tree from a nearly finished one.
            ["discoverableCount"] = tree.TotalDiscoverableCount,
            ["hasRemainingDiscoveries"] = tree.HasRemainingDiscovery ||
                tree.HasImmediateRequiredDiscovery,
        };

        // Seconds, in the format every other duration on this surface reads in. It shipped as a
        // bare magnitude and a live round watched it climb 0.22 → 2.72 with nothing saying what
        // the number counted, then kept `actionTime` — `DiscoveryTreeSO`'s own field name, which
        // says neither which clock nor which direction. The key is the mode the row is already in
        // and the direction the number runs: this tree has been crafting for that long.
        if (tree.ActionMode == 1)
        {
            result["craftingFor"] = CoarseClock(tree.ActionTime);

            // The press starts a roll, it does not produce offers: the game rolls them three
            // seconds of game time later. A round read the empty list as a press that had failed.
            result["rolling"] =
                "This roll is still running. The game rolls its offers three seconds after the " +
                "press, and this tree has been rolling " + CoarseClock(tree.ActionTime) + ".";
        }
        if (tree.SelectedChoiceId != Guid.Empty)
            result["selectedOfferUuid"] = tree.SelectedChoiceId.ToString("D");

        if (tree.ActionMode == 0)
        {
            var hasNext = tree.HasRemainingDiscovery || tree.HasImmediateRequiredDiscovery;
            var available = tree.Visible && hasNext && tree.NextItemAffordable;
            var initiate = new JObject
            {
                ["available"] = available,
            };
            if (!available)
            {
                if (!tree.Visible) initiate["reasonCode"] = "tree_unavailable";
                else if (hasNext) initiate["reasonCode"] = "unaffordable";
                else AddNoDiscoveryReason(initiate, tree);
            }
            // Priced only where the price is the thing left to decide. A tree the game is not
            // showing quoted "400 of 100 Mana affordable=no" underneath "The game is not showing
            // this discovery tree.", which sent a caller to go and earn a price for a press that
            // would still not exist.
            if (tree.Visible && hasNext && tree.NextItemCosts.Count > 0)
            {
                var shortfalls = new List<(string, BigDouble, BigDouble)>();
                initiate["costs"] = DiscoveryTreeCosts(world, in tree, shortfalls);

                // The price row reads in the screen's rounding, and a round read `cost: 5 of 5
                // Knowledge affordable=no` off 4.6 Knowledge and went looking for a bug. The
                // sentence carries the same two numbers game_purchase's does, so the near miss is
                // legible as a near miss.
                var sentence = GameMcpDecisionReason.Shortfall(shortfalls);
                if (sentence.Length > 0 &&
                    (string?)initiate["reasonCode"] == "unaffordable")
                {
                    initiate["reason"] = sentence;
                }
            }
            result["initiate"] = initiate;
        }

        if (tree.ActionMode == 2)
        {
            if (tree.CurrentOfferIds.Count > 0)
            {
                var offers = new JArray();
                for (var index = 0; index < tree.CurrentOfferIds.Count; index++)
                {
                    var id = tree.CurrentOfferIds[index];
                    var offer = new JObject
                    {
                        ["uuid"] = id.ToString("D"),
                    };
                    if (GameMcpEntityIdentity.TryDescribePublishedEntity(
                            world, id, out var category, out _, out _))
                    {
                        offer["category"] = category;
                    }

                    // A tree with a required discovery outstanding offers that one thing and
                    // nothing else, even when it is already owned — so an offer can name something
                    // the player already has. That is a fact about the offer, not a reason to
                    // refuse the press the game draws.
                    if (world.EntityIdentities.TryGet(id, out var identity) &&
                        TryReadDiscoveryState(world, id, identity.RuntimeType, out var owned) &&
                        owned)
                    {
                        offer["discovered"] = true;
                    }
                    offers.Add(offer);
                }
                result["offers"] = offers;
            }

        }
        if (tree.ActionMode == 2)
        {
            // The challenge surface answers the same question as a decision block with a code and a
            // sentence. A naked `rerollAvailable: false` beside `rerollsLeft: 0` said the same
            // thing to a machine and nothing at all to the caller that had to act on it.
            var rerollAvailable = tree.Visible &&
                tree.RerollsLeft > 0 && tree.CurrentOfferIds.Count > 0 &&
                !tree.UsedRerollsLastDiscover;
            var reroll = new JObject { ["available"] = rerollAvailable };
            if (!rerollAvailable)
            {
                reroll["reasonCode"] = !tree.Visible
                    ? "tree_unavailable"
                    : tree.UsedRerollsLastDiscover
                        ? "reroll_already_used"
                        : tree.RerollsLeft <= 0
                            ? "no_rerolls"
                            : "no_current_offers";
            }

            // The game's reroll button asks only whether a reroll is left. A required discovery
            // makes a reroll a waste — the tree rolls the same one thing again — which is advice
            // about what the press buys, not a rule about whether it is drawn.
            if (tree.HasImmediateRequiredDiscovery)
            {
                reroll["note"] =
                    "This tree owes a required discovery, so it is offering that one thing: a " +
                    "reroll spends a reroll and offers it again.";
            }
            result["reroll"] = reroll;
        }
        return result.Freeze();
    }

    /// <summary>
    /// The discovery verdict, present wherever the concept applies to the row's kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No screen discovers one thing: the only callers of <c>IDiscoverable.Discover()</c> are
    /// <c>DiscoveryTreeSO.DiscoverItem</c> (the roll), the developer console and
    /// <c>UIDiscoverablePage</c>, a page the game no longer draws. So an undiscovered row is never
    /// a press. It says which screen's roll gives it and whether it is in that roll's pool — the
    /// game's <c>!IsDiscovered() &amp;&amp; IsDiscoverVisible()</c>. The row once read
    /// <c>discover: yes</c> with a price, and the press behind it either refused or discovered a
    /// spell without any button.
    /// </para>
    /// <para>
    /// <c>alchemy-recipes</c> spans two screens: concepts are drawn on Scholar &gt; Concepts &gt;
    /// Discover rather than Alchemy &gt; Alchemy &gt; Learn, so that page passes the screen it read
    /// off the recipe's own alchemy type, and an empty id names no screen.
    /// </para>
    /// </remarks>
    internal static void AddDiscoveryDecision(
        GameWorldState world,
        JObject result,
        WorldDiscoverableDecision decision,
        Guid screenId,
        Guid entityId,
        bool nativeDiscoverable = true)
    {
        if (decision.Discovered)
        {
            result["discover"] = new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "already_discovered",
            };
            return;
        }
        result["discover"] = TryFindHoldingDiscoveryTree(world, entityId, out var holdingTree)
            ? HeldOfferDiscovery(world, holdingTree)
            : RollOnlyDiscovery(
                world,
                screenId,
                nativeDiscoverable && decision.Visible,
                string.Empty,
                IsCurrentDiscoveryOffer(world, entityId));
    }

    internal static JObject RollOnlyDiscovery(
        GameWorldState world,
        Guid screenId,
        bool inPool,
        string missingBook,
        bool offered)
    {
        var discover = new JObject
        {
            ["available"] = false,
            ["reasonCode"] = "discovered_by_roll_only",
            ["reason"] = GameMcpDecisionReason.RollOnly(screenId, inPool, missingBook, offered),
        };
        if (offered) discover["offered"] = true;
        return discover;
    }

    /// <summary>
    /// What a row says about discovering something a tree is already holding for the player.
    /// </summary>
    internal static JObject HeldOfferDiscovery(GameWorldState world, Guid treeId) => new()
    {
        ["available"] = false,
        ["reasonCode"] = "tree_holds_this_offer",
        ["reason"] =
            EntityIdentityFormatter.PlayerName(treeId, world.EntityIdentities) +
            " is holding this as the offer you picked, and confirming it there costs nothing " +
            "more — the roll already paid. Use game_discover mode=offer_confirm on that tree.",
        ["offeredBy"] = EntityReference(world, treeId),
        ["offered"] = true,
    };

    /// <summary>
    /// The discovery tree holding this entity as the choice the player has selected, if one is.
    /// </summary>
    /// <remarks>
    /// The roll that offered it has already been paid for — <c>UIDiscoveryTreePage.OnConfirmClick</c>
    /// reaches <c>DiscoveryTreeSO.DiscoverSelectedItem</c>, which charges nothing.
    /// </remarks>
    internal static bool TryFindHoldingDiscoveryTree(GameWorldState world, Guid id, out Guid treeId)
    {
        for (var index = 0; index < world.DiscoveryTrees.Count; index++)
        {
            var tree = world.DiscoveryTrees[index];
            if (tree.SelectedChoiceId != id) continue;
            treeId = tree.TreeId;
            return true;
        }
        treeId = Guid.Empty;
        return false;
    }

    /// <summary>
    /// Whether one of the discovery trees is offering this entity a slot right now — the fact that
    /// separates "you could discover this next" from "this is in the pool somewhere".
    /// </summary>
    internal static bool IsCurrentDiscoveryOffer(GameWorldState world, Guid id)
    {
        for (var treeIndex = 0; treeIndex < world.DiscoveryTrees.Count; treeIndex++)
        {
            var offers = world.DiscoveryTrees[treeIndex].CurrentOfferIds;
            for (var offerIndex = 0; offerIndex < offers.Count; offerIndex++)
                if (offers[offerIndex] == id) return true;
        }
        return false;
    }

    internal static string DiscoveryMode(int mode) => mode switch
    {
        0 => "idle",
        1 => "crafting",
        2 => "choice",
        _ => "unknown_" + mode.ToString(CultureInfo.InvariantCulture),
    };

    internal static GameMcpValue List(
        GameWorldState world,
        in WorldDiscoveryTree listedTree,
        int nestedOffset,
        int nestedLimit)
    {
        return new JObject
            {
                ["entityId"] = listedTree.EntityId.ToString("D"),
                ["mode"] = DiscoveryMode(listedTree.ActionMode),
            }.Freeze();
    }

}
#endif
