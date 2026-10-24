#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWorldOverview
{
    internal static JObject Overview(GameMcpFrameContext state)
    {
        if (!GameMcpWorldRead.TryWorld(state, out var publication, out var unavailable))
            return unavailable;

        var world = publication.Snapshot;
        var result = new JObject();
        result["status"] = "available";
        // The run's own clock, under the game's word for it and in the game's own format. Every
        // other number here answers "what can I do next"; this one answers "how long has this taken
        // me", which no surface could answer at all — a round wanting it had to diff wall-clock
        // stamps across its own calls and got the session, not the run.
        // The format is read off the variable's own two flags rather than assumed, so this line and
        // the same variable's row can never print one duration two ways.
        if (WorldLookup.TryFind(world.DoubleVariables, KnownEntities.TimePlayed.Uuid, out var played))
            result["timePlayed"] = GameMcpRowKit.Clock(played.Value, played.IsTimeAccurate);
        // The game keeps a second clock — "Time Played this Reset" — and a run that has reset has
        // two different answers to "how long has this taken me". It is behind TimeResetUnlocked and
        // it does not survive a reset, so a save that has never reset publishes no such variable
        // and this key is simply absent; printing a zero there would claim a reset happened.
        if (WorldLookup.TryFind(
                world.DoubleVariables, KnownEntities.TimePlayedThisReset.Uuid, out var thisReset))
            result["timePlayedThisReset"] = GameMcpRowKit.Clock(thisReset.Value, thisReset.IsTimeAccurate);
        var resources = ResourceBarLines(world);
        if (resources.Count > 0) result["resources"] = resources;
        result["economy"] = new JObject
        {
            ["resourceRows"] = world.Resources.Count,
            ["unlockedStructures"] = CountUnlockedStructures(world),
            ["affordableStructures"] = CountAffordableStructures(world),
            ["affordableUpgrades"] = CountAffordableUpgrades(world),
        };
        result["progression"] = new JObject
        {
            ["discoveredSpellRecipes"] = CountDiscoveredSpells(world),
            ["spellRecipesReadyToLevel"] = CountReadySpells(world),
            // Two counts, because the split is two categories: a number that added the Scholar
            // screen's Concepts to the Alchemy screen's recipes agreed with neither page.
            ["discoveredAlchemyRecipes"] = CountDiscoveredAlchemy(world, concepts: false),
            ["discoveredConcepts"] = CountDiscoveredAlchemy(world, concepts: true),
            ["availableViews"] = CountAvailableViews(world),
            ["visiblePlots"] = CountVisiblePlots(world),
        };
        result["running"] = new JObject
        {
            ["actionQueues"] = ActionQueueOccupancy(world),
            // Two numbers, because the question is "have I a free lane" and one of them cannot
            // answer it. `equippedSpellSlots` was the slot count and read as the equipped count —
            // the field name promised the count and delivered the cap, and it delivered it in the
            // direction that matters, so a strategist sizing its loadout saw a full bar that was
            // five of eight.
            ["equippedSpells"] = GameMcpSpellLoadoutProjection.OccupiedSpellSlots(world),
            ["maximumSpellSlots"] = world.SpellSlots.Count,
            ["activeConceptAssignments"] = world.AlchemyInstances.Count,
        };
        if (world.SpellWorkbench.MaximumOutputLevel > 0)
            // The two dials on Magic > Casting, under the words that screen prints beside them —
            // Output Lv and Reserve Lv — and the ceiling each has been raised to. A bare `output`
            // said neither what it counted nor where the player would see it, and a live round
            // read `casting: output 1/1` beside a mana bar of 133/133 and left the two
            // unreconciled. The floor is the same number on every dial in every save, so it
            // belongs in the casting-dial tool's documentation, not in an answer a caller reads
            // every few calls.
            result["castingDials"] = new JObject
            {
                ["outputLevel"] = new JObject
                {
                    ["current"] = world.SpellWorkbench.OutputLevel,
                    ["maximum"] = world.SpellWorkbench.MaximumOutputLevel,
                },
                ["reserveLevel"] = new JObject
                {
                    ["current"] = world.SpellWorkbench.ReserveLevel,
                    ["maximum"] = world.SpellWorkbench.MaximumReserveLevel,
                },
            };
        if (GameMcpScreenState.IsScreenUnlocked(world, KnownEntities.WorldAgromancyDruidryLvs.Uuid))
            result["druidryLevel"] = new JObject
            {
                ["current"] = world.SpellWorkbench.DruidryLevel,
                ["maximum"] = world.SpellWorkbench.MaximumDruidryLevel,
            };
        if (GameMcpScreenState.IsScreenUnlocked(world, KnownEntities.AlchAlchemyLevel.Uuid))
            result["alchemyLevel"] = new JObject
            {
                ["current"] = world.SpellWorkbench.AlchemyLevel,
                ["maximum"] = world.SpellWorkbench.MaximumAlchemyLevel,
            };
        // Exception-shaped, like every other line that costs a caller a decision: absent while no
        // battle runs, and present with the ritual that is in it and the gate it holds shut while
        // one does. A live round activated a ritual, read `activeBattle: no -> yes`, and twenty
        // minutes later hedged its way into the round's one irreversible action because no surface
        // aggregated "a battle is running" or said what that blocked.
        for (var index = 0; index < world.Rituals.Count; index++)
        {
            if (!world.Rituals[index].InBattle) continue;
            result["ritualBattle"] = new JObject
            {
                ["ritual"] = world.Rituals[index].EntityId.ToString("D"),
                ["gates"] = GameMcpDecisionReason.RitualBattleGate,
            };
            break;
        }
        var requiredRolls = RequiredRolls(world);
        if (requiredRolls.Count > 0) result["requiredRolls"] = requiredRolls;
        result["collection"] = CompactCollectionStatus(world);
        return result;
    }

    /// <summary>
    /// The left resource panel in one line per resource it is drawing: held, the ceiling, the net
    /// rate and the clock its tooltip prints.
    /// </summary>
    /// <remarks>
    /// Every number is the game's own: the panel's quantity, capacity and <c>GetTrueRate()</c>, and
    /// from <c>ResourceSO.GetBaseTooltipNodes</c> the Maxed in pair (time to fill from here, then
    /// from empty), Empty in, and Equilibrium, each under the condition the tooltip draws it — the
    /// two clocks only while no percentage loss runs, Equilibrium only while the flat rate carries a
    /// passive modifier. A resource at its ceiling says full instead of a zero clock.
    /// </remarks>
    internal static JArray ResourceBarLines(GameWorldState world)
    {
        var lines = new JArray();
        for (var index = 0; index < world.ResourceBar.Count; index++)
        {
            var resourceId = world.ResourceBar[index].ResourceId;
            if (!WorldLookup.TryFind(world.Resources, resourceId, out var resource) ||
                !resource.Reading.Visible)
                continue;
            lines.Add(
                EntityIdentityFormatter.PlayerName(resourceId, world.EntityIdentities) + ": " +
                ResourceBarLine(in resource));
        }
        return lines;
    }

    private static string ResourceBarLine(in WorldResource resource)
    {
        var held = WorldResourceCoordinate.DisplayAmount(in resource);
        var rate = resource.TrueRate;
        var text = new StringBuilder(GameScientificNumber.Beautify(held));
        if (resource.IsCapped)
            text.Append('/').Append(GameScientificNumber.Beautify(resource.Reading.Capacity));
        if (rate != BigDouble.Zero)
        {
            text.Append(' ').Append(rate > BigDouble.Zero ? "+" : string.Empty)
                .Append(GameScientificNumber.Beautify(rate)).Append("/s");
        }

        var lossless = resource.Reading.RateInputs.LossPercent == BigDouble.Zero;
        if (resource.IsCapped && resource.IsAtCapacity)
            text.Append(", full");
        else if (resource.IsCapped && rate > BigDouble.Zero && lossless)
        {
            text.Append(", maxed in ")
                .Append(GameDurationText.UltraPreciseWithYears(resource.Headroom / rate))
                .Append('/')
                .Append(GameDurationText.UltraPreciseWithYears(resource.Reading.Capacity / rate));
        }
        else if (rate < BigDouble.Zero && lossless)
        {
            var stored = resource.Reading.Quantity;
            text.Append(stored == BigDouble.Zero ? ", empty" : ", empty in " +
                GameDurationText.UltraPreciseWithYears(stored / -rate));
        }

        if (resource.Reading.RateInputs.RatePassiveModifiers > 0)
            text.Append(", equilibrium ").Append(GameScientificNumber.Beautify(resource.Equilibrium));
        return text.ToString();
    }

    /// <summary>
    /// Every discovery page whose tab carries the game's new-content mark for a required
    /// discovery: the tree, the screen it is drawn on, and the price of the roll it owes.
    /// </summary>
    /// <remarks>
    /// <c>ViewSO.InternalHasActiveTutorial()</c> raises the mark on a page whose
    /// <c>discoveryTree</c> answers <c>HasImmediateRequiredDiscover()</c>, and while it does
    /// <c>GetNextItemCost()</c> prices the required item rather than the pool ladder, so the price
    /// can move to a resource the tree never charged before. A tree whose roll is already pressed
    /// owes no price, so its row carries none. <c>UIViewRadioButton.IsVisible()</c> draws a tab, and
    /// so its mark, only while its view is available, so a page under a locked screen carries no
    /// mark and has no row.
    /// </remarks>
    private static JArray RequiredRolls(GameWorldState world)
    {
        var rows = new JArray();
        Dictionary<Guid, Guid>? parents = null;
        for (var index = 0; index < world.Views.Count; index++)
        {
            var view = world.Views[index];
            if (view.DiscoveryTreeId == Guid.Empty || !view.Available) continue;
            if (!WorldLookup.TryFind(world.DiscoveryTrees, view.DiscoveryTreeId, out var tree) ||
                !tree.HasImmediateRequiredDiscovery)
            {
                continue;
            }
            parents ??= ParentViews(world);
            if (!IsDrawn(world, parents, view.ViewId)) continue;
            var row = new JObject
            {
                ["tree"] = tree.EntityId.ToString("D"),
                ["screen"] = GameScreenPath.For(view.ViewId),
            };
            if (tree.NextItemCosts.Count > 0)
                row["costs"] = GameMcpDiscoveryTreeOfferProjection.DiscoveryTreeCosts(world, in tree, shortfalls: null);
            rows.Add(row);
        }
        return rows;
    }

    private static Dictionary<Guid, Guid> ParentViews(GameWorldState world)
    {
        var parents = new Dictionary<Guid, Guid>();
        for (var index = 0; index < world.Views.Count; index++)
        {
            var view = world.Views[index];
            for (var sub = 0; sub < view.SubViewIds.Count; sub++)
                parents[view.SubViewIds[sub]] = view.ViewId;
        }
        return parents;
    }

    private static bool IsDrawn(GameWorldState world, Dictionary<Guid, Guid> parents, Guid viewId)
    {
        for (var depth = 0; depth <= world.Views.Count; depth++)
        {
            if (!WorldLookup.TryFind(world.Views, viewId, out var view) || !view.Available)
                return false;
            if (!parents.TryGetValue(viewId, out viewId)) return true;
        }
        return false;
    }

    /// <summary>
    /// Each action queue's occupancy under its own identity, with the capacity it is measured
    /// against beside it.
    /// </summary>
    /// <remarks>
    /// This was one unnamed number, <c>occupiedActionQueueSlots</c>, printed beside a queue count of
    /// two. It covered only the plot-action queue — the one whose slots are walked — and silently
    /// omitted the attribute and upgrade queue every purchase's ceiling comes from, so a live round
    /// read <c>0</c> from it five times while asking about the other queue entirely. A number that
    /// answers for one queue and names none is worse than no number at all.
    /// </remarks>
    private static JArray ActionQueueOccupancy(GameWorldState world)
    {
        var rows = new JArray();
        for (var index = 0; index < world.ActionQueues.Count; index++)
        {
            var queue = world.ActionQueues[index];
            var row = new JObject
            {
                ["uuid"] = queue.QueueId.ToString("D"),
                ["name"] = QueueName(queue.QueueId, world.EntityIdentities),
                ["usedSlots"] = queue.UsedSlots,
            };
            if (queue.MaxQueuedItemsId != Guid.Empty &&
                WorldLookup.TryFind(world.IntVariables, queue.MaxQueuedItemsId, out var maximum))
                row["capacity"] = maximum.Value.ToInt();
            else if (queue.SlotCount > 0)
                row["capacity"] = queue.SlotCount;
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// The one queue the game leaves nameless, in the suite's own words.
    /// </summary>
    /// <remarks>
    /// The identity ladder is the game's display name, then the asset name, and
    /// <c>ActivePlotNodeActions</c> has no display name at all — so the Agromancy queue's row
    /// printed its internal spelling on a surface a player reads. No read can recover a label the
    /// game never authored, so the suite authors it, in the word its own Agromancy tool already
    /// speaks. The attribute queue needs no entry: the game names it <c>Active Attributes</c>.
    /// </remarks>
    internal static readonly (Guid Queue, string Name)[] AuthoredQueueNames =
    {
        (KnownEntities.ActivePlotNodeActions.Uuid, "Plot actions"),
    };

    /// <summary>
    /// The word a queue row prints. A name the game authors always wins — the suite fills a hole,
    /// it does not overrule the game — so a build that starts labelling this list is spelled the
    /// game's way from the first capture.
    /// </summary>
    internal static string QueueName(Guid queueId, EntityIdentityCatalogSnapshot? identities)
    {
        var description = EntityIdentityFormatter.Describe(queueId, identities);
        if (description.Source == EntityIdentityNameSource.LiveDisplayName) return description.Name;
        for (var index = 0; index < AuthoredQueueNames.Length; index++)
        {
            if (AuthoredQueueNames[index].Queue == queueId) return AuthoredQueueNames[index].Name;
        }
        return EntityIdentityFormatter.PlayerName(queueId, identities);
    }

    internal static JObject ListCategories(GameMcpFrameContext state)
    {
        if (!GameMcpWorldRead.TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        var result = new JObject();
        result["status"] = "available";
        var rows = new List<(string Name, JObject Row)>(
            GameMcpWorldCategories.Categories.Length +
            publication.Snapshot.CollectionCategories.Count);
        for (var index = 0; index < GameMcpWorldCategories.Categories.Length; index++)
        {
            rows.Add((
                GameMcpWorldCategories.Categories[index].Name,
                DescribeCategory(publication.Snapshot, GameMcpWorldCategories.Categories[index])));
        }
        var unlistable = AddUnlistableCollectors(publication.Snapshot, rows);
        rows.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        var categories = new JArray();
        for (var index = 0; index < rows.Count; index++) categories.Add(rows[index].Row);

        // Said once, above the table, because it is a property of the suite's own code and not of
        // any row: fourteen rows repeating it spent 38% of this whole response on one fact. Each
        // of those rows carries the class, and a row with something of its own to add — a
        // collector that did not bind, one whose pass was partial — still says that on the row.
        if (unlistable)
        {
            result[Unlistable] =
                GameMcpDecisionReason.CollectorNotListable;
        }
        result["categories"] = categories;
        return result;
    }

    /// <summary>
    /// The one word this page says about a collector that publishes no table: the key its standing
    /// sentence is printed under, and the reading every row that cannot be paged carries.
    /// </summary>
    private const string Unlistable = "unlistable";

    /// <summary>
    /// One row per collector this surface cannot list, so the inventory holds every category the
    /// world actually collects.
    /// </summary>
    /// <remarks>
    /// A collector whose rows never become a table was invisible here: the page named the tables it
    /// could page and said nothing at all about the rest, so a reader chasing a category the game
    /// plainly has — the type modifiers behind every worth block, the keywords on every row — found
    /// no row, no reason, and no way to tell an absent collector from an unlistable one. These rows
    /// make no collector listable. They say it runs, how many rows it produced, and why paging it is
    /// refused, in the same availability vocabulary every other row on this page speaks.
    /// </remarks>
    private static bool AddUnlistableCollectors(
        GameWorldState world,
        List<(string Name, JObject Row)> rows)
    {
        var any = false;
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var report = world.CollectionCategories[index];
            var name = GameMcpWorldCategories.Normalize(report.Category);

            // A name this page already answers to keeps the one row it has. Two rows under one
            // category would make the inventory contradict itself, and a reader looking that name
            // up still finds it.
            if (name.Length == 0 ||
                GameMcpWorldCategories.ListedReportCategories.Contains(name) ||
                GameMcpWorldCategories.ByName.ContainsKey(name))
            {
                continue;
            }
            any = true;
            var detail = UnlistableDetail(report);
            rows.Add((name, new JObject
            {
                ["category"] = name,
                ["count"] = report.Sampled,

                // The word the sentence above this table is keyed under, not a class code. A bare
                // `ERR_LOCKED` in this cell meant "this collector has no table", while the same code
                // everywhere else on the surface means "progression has not unlocked this" — one
                // code, two unrelated conditions, with the sentence that told them apart printed
                // once at the top and detached from the fourteen cells that needed it. A live round
                // could not sweep the cell by eye and certified the page clean of exactly this
                // shape. The cell now speaks the reason vocabulary the rest of the column speaks,
                // and the word it says is defined on the page that says it.
                ["reason"] = detail.Length == 0 ? Unlistable : Unlistable + ". " + detail,
            }));
        }
        return any;
    }

    /// <summary>
    /// What this collector adds to the standing reason, or nothing where it adds none. The standing
    /// sentence is the response's, said once; what belongs on a row is what only that row can say.
    /// </summary>
    private static string UnlistableDetail(WorldCollectionCategoryStatus report)
    {
        if (report.Outcome == WorldCategoryOutcome.Unavailable)
        {
            return "It did not bind on this build: " +
                (report.FirstFailure.Length == 0
                    ? "the collector published no failure reason"
                    : report.FirstFailure);
        }
        if (report.Skipped > 0)
        {
            return "Collection is partial: " +
                report.Skipped.ToString(CultureInfo.InvariantCulture) +
                " native rows were skipped; first failure: " +
                (report.FirstFailure.Length == 0
                    ? "the collector did not publish a failure reason"
                    : report.FirstFailure);
        }
        return string.Empty;
    }

    private static JObject CompactCollectionStatus(GameWorldState world)
    {
        var unavailable = new JArray();
        var readRows = 0;
        var skippedRows = 0;
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var category = world.CollectionCategories[index];
            readRows += category.Sampled;
            skippedRows += category.Skipped;
            if (category.IsClean) continue;
            unavailable.Add(new JObject
            {
                ["category"] = GameMcpWorldCategories.Normalize(category.Category),
                ["read"] = category.Sampled,
                ["skipped"] = category.Skipped,
                ["reason"] = category.FirstFailure,
            });
        }
        var result = new JObject
        {
            ["complete"] = IsCollectionComplete(world),
            ["read"] = readRows,
            ["skipped"] = skippedRows,
        };
        if (unavailable.Count > 0) result["unavailableCategories"] = unavailable;
        if (GameMcpWorldRead.TryLocalizedRequirementFailures(world, out var implicated) &&
            implicated.Length > 0)
        {
            // The overview is read every few calls and this evidence is the same bytes every
            // time. It used to name every owner, which on a mature save is a wall of two hundred
            // and forty names on every single call, for a fact that is one fact: this build has a
            // condition the suite does not model, and the entities carrying it cannot be planned.
            // How many, on how many entities, which class, and the two reads that hold the rest.
            // The class name stays: it is the suite saying what it does not model, which is the
            // one thing nobody else can say, and it is not a column on an entity's row.
            var seenOwners = new HashSet<Guid>();
            var nativeTypes = new List<string>();
            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < implicated.Length; index++)
            {
                var leaf = implicated[index];
                seenOwners.Add(leaf.OwnerId);
                if (seenTypes.Add(leaf.ConditionTypeName)) nativeTypes.Add(leaf.ConditionTypeName);
            }
            result["gap"] =
                implicated.Length.ToString(CultureInfo.InvariantCulture) +
                (implicated.Length == 1 ? " requirement leaf on " : " requirement leaves on ") +
                seenOwners.Count.ToString(CultureInfo.InvariantCulture) +
                (seenOwners.Count == 1 ? " entity uses " : " entities use ") +
                "a condition this build authors and this suite does not model (" +
                string.Join(", ", nativeTypes) + "), so those entities are never planned. " +
                "world_list category=entity-requirements lists the leaves, and world_get on one " +
                "of the entities names its own.";
        }
        return result;
    }

    private static int CountUnlockedStructures(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.Structures.Count; index++)
            if (world.Structures[index].Reading.Unlocked) count++;
        return count;
    }

    /// <summary>
    /// Structures whose price is met right now, read the same way the structure rows read it.
    /// Without this the overview named a number of unlocked structures and left the only question a
    /// caller had — how many can I buy — answerable only by paging the whole category.
    /// </summary>
    private static int CountAffordableStructures(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.Structures.Count; index++)
        {
            var structure = world.Structures[index];
            if (structure.Reading.Unlocked &&
                GameMcpPurchaseProjection.TryPurchaseAffordability(world, structure.EntityId, out var affordable) &&
                affordable)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Upgrades whose price is met right now. "Purchasable" was a third word for a fact the rows
    /// already call <c>affordable</c>, next to a per-row <c>available</c> that means something else.
    /// </summary>
    private static int CountAffordableUpgrades(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.Upgrades.Count; index++)
        {
            var upgrade = world.Upgrades[index];
            if (GameMcpScreenState.IsUpgradeOffered(world, in upgrade) && !upgrade.IsExhausted &&
                GameMcpPurchaseProjection.TryPurchaseAffordability(world, upgrade.EntityId, out var affordable) &&
                affordable)
                count++;
        }
        return count;
    }

    private static int CountDiscoveredSpells(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.SpellRecipes.Count; index++)
            if (world.SpellRecipes[index].Discovered) count++;
        return count;
    }

    private static int CountReadySpells(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.SpellRecipes.Count; index++)
            if (world.SpellRecipes[index].MasteryLevelReady) count++;
        return count;
    }

    private static int CountDiscoveredAlchemy(GameWorldState world, bool concepts)
    {
        var count = 0;
        for (var index = 0; index < world.AlchemyRecipes.Count; index++)
        {
            var recipe = world.AlchemyRecipes[index];
            if (recipe.Discovered &&
                GameMcpWorldCategories.IsScholarConcept(recipe.CoreTypeId) == concepts)
            {
                count++;
            }
        }
        return count;
    }

    private static int CountAvailableViews(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.Views.Count; index++)
            if (world.Views[index].Available) count++;
        return count;
    }

    private static int CountVisiblePlots(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.PlotNodes.Count; index++)
            if (world.PlotNodes[index].Reading.Visible) count++;
        return count;
    }

    private static bool IsCollectionComplete(GameWorldState world)
    {
        if (world.CollectionCategories.Count == 0) return false;
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            if (!world.CollectionCategories[index].IsClean) return false;
        }
        return true;
    }

    private static JObject DescribeCategory(GameWorldState world, GameMcpEntityCapabilityDescriptor category)
    {
        var availability = GameMcpWorldCategories.Availability(world, category);

        // No `available` column beside the reason. Every unavailable category writes a reason and
        // every available one writes none, so the two columns disagreed in zero of eighty-one rows
        // across a measured round: the yes/no was the reason cell's own emptiness, spelled a second
        // way. The reason is the column that can say something, so it is the column that stays.
        var result = new JObject
        {
            ["category"] = category.Name,
            ["count"] = CategoryRowCount(world, category),
        };
        if (availability.Reason.Length > 0) result["reason"] = availability.Reason;
        return result;
    }

    /// <summary>
    /// How many rows listing this category answers with.
    /// </summary>
    /// <remarks>
    /// The count is read to decide whether to list a category, so it has to be the number that read
    /// will give. For every category but one it is the publication's own row count. The
    /// mastery-experience ring is the exception: the list says each distinct source once, so the
    /// ring's 256 raw samples were advertised against a page that answers with 17 rows — two true
    /// numbers, one word, and nothing on either page saying they counted different things.
    /// </remarks>
    private static int CategoryRowCount(GameWorldState world, GameMcpEntityCapabilityDescriptor category)
    {
        if (!string.Equals(category.Name, "mastery-experience", StringComparison.Ordinal))
            return category.Count(world);
        var keys = new List<(MasteryExperienceDomain Domain, Guid SourceId, int SourceMastery)>();
        var counts = new List<int>();
        GameMcpSpellLevelProjection.MasteryExperienceSources(world, keys, counts);
        return keys.Count;
    }
}
#endif
