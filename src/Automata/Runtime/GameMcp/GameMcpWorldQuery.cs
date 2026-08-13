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

namespace OrbAutomata.GameMcp;

/// <summary>Read-only queries over one pinned world publication.</summary>
internal static class GameMcpWorldQuery
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 200;

    /// <summary>
    /// A category holding at most this many rows is read whole, in one call, when the caller named
    /// no page size of its own.
    /// </summary>
    /// <remarks>
    /// Ruled with the durable-row doctrine (verb-surface spec §2, 2026-08-10): a category this small
    /// is one screen of the game, and paging it is ceremony charged for nothing — a caller pays a
    /// second call, and every reader pays the thinking that a `next=` demands, to learn that there
    /// was never a second page. The count line still says <c>rows N/N</c>, because how many rows
    /// there are is a fact whether or not any of them were withheld. A caller that asks for fewer
    /// gets fewer: this raises no page above what was asked for, it only stops lowering one below
    /// what the whole category is.
    /// </remarks>
    internal const int WholeCategoryRows = 25;

    /// <summary>One page budget for every paged read, so short pages mean the same thing on all of them.</summary>
    internal const int MaximumListResponseBytes = 12 * 1024;
    internal const int MaximumBatchSize = 200;
    private static readonly GameMcpWorldCategory[] Categories = CreateCategories();
    private static readonly Dictionary<string, GameMcpWorldCategory> ByName = IndexCategories();

    internal static JObject Overview(GameMcpFrameContext state)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;

        var world = publication.Snapshot;
        var result = Envelope(publication);
        result["status"] = "available";
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
            ["discoveredAlchemyRecipes"] = CountDiscoveredAlchemy(world),
            ["availableViews"] = CountAvailableViews(world),
            ["visiblePlots"] = CountVisiblePlots(world),
        };
        result["running"] = new JObject
        {
            ["actionQueues"] = world.ActionQueues.Count,
            ["occupiedActionQueueSlots"] = CountOccupiedActionQueueSlots(world),
            // Two numbers, because the question is "have I a free lane" and one of them cannot
            // answer it. `equippedSpellSlots` was the slot count and read as the equipped count —
            // the field name promised the count and delivered the cap, and it delivered it in the
            // direction that matters, so a strategist sizing its loadout saw a full bar that was
            // five of eight.
            ["equippedSpells"] = CountEquippedSpellSlots(world),
            ["maximumSpellSlots"] = world.SpellSlots.Count,
            ["activeConceptAssignments"] = world.AlchemyInstances.Count,
        };
        if (world.SpellWorkbench.MaximumOutputLevel > 0)
            // The floor is the same number on every dial in every save, so it belongs in the
            // casting-dial tool's documentation, not in an answer a caller reads every few calls.
            result["casting"] = new JObject
            {
                ["output"] = new JObject
                {
                    ["current"] = world.SpellWorkbench.OutputLevel,
                    ["maximum"] = world.SpellWorkbench.MaximumOutputLevel,
                },
                ["reserve"] = new JObject
                {
                    ["current"] = world.SpellWorkbench.ReserveLevel,
                    ["maximum"] = world.SpellWorkbench.MaximumReserveLevel,
                },
            };
        result["collection"] = CompactCollectionStatus(world);
        return result;
    }

    private static int CountOccupiedActionQueueSlots(GameWorldState world)
    {
        var occupied = 0;
        for (var index = 0; index < world.ActionQueueSlots.Count; index++)
        {
            if (!world.ActionQueueSlots[index].Empty) occupied++;
        }
        return occupied;
    }

    private static int CountEquippedSpellSlots(GameWorldState world)
    {
        var occupied = 0;
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            if (world.SpellSlots[index].Occupied) occupied++;
        }
        return occupied;
    }

    internal static JObject ListCategories(GameMcpFrameContext state)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        var result = Envelope(publication);
        result["status"] = "available";
        var categories = new JArray();
        for (var index = 0; index < Categories.Length; index++)
            categories.Add(DescribeCategory(publication.Snapshot, Categories[index]));
        result["categories"] = categories;
        return result;
    }

    internal static JObject ListRows(
        GameMcpFrameContext state,
        string categoryName,
        int offset,
        int limit,
        bool affordableOnly = false,
        bool limitFromCaller = true)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        if (!TryCategory(categoryName, out var category, out var reason))
            return NotAvailable(publication, "unknown_category", reason);
        if (affordableOnly && !SupportsAffordableFilter(category))
        {
            return NotAvailable(
                publication,
                "filter_not_supported",
                "category " + category.Name + " has no price, so affordable does not apply to " +
                "it; the priced categories are " + string.Join(", ", PricedCategories));
        }
        if (offset < 0)
            return NotAvailable(publication, "invalid_offset", "offset must be zero or greater");
        if (limit <= 0 || limit > MaximumPageSize)
        {
            return NotAvailable(
                publication,
                "invalid_limit",
                "limit must be between 1 and " +
                MaximumPageSize.ToString(CultureInfo.InvariantCulture));
        }

        var availability = Availability(publication.Snapshot, category);
        var localizedRequirementCategory = !availability.Available &&
            string.Equals(category.Name, "entity-requirements", StringComparison.Ordinal) &&
            TryLocalizedRequirementFailures(publication.Snapshot, out _, out _);
        if (!availability.Available && !localizedRequirementCategory)
        {
            return NotAvailable(
                publication,
                "category_not_collected",
                availability.Reason.Length == 0
                    ? "the category was not collected"
                    : availability.Reason);
        }

        var world = publication.Snapshot;
        if (string.Equals(category.Name, "mastery-experience", StringComparison.Ordinal))
            return MasteryExperienceSummary(publication, offset, limit);

        var count = category.Count(world);

        // A small category is the whole answer or it is not an answer. Nobody named a page size, and
        // the category fits in one, so neither the default nor the byte budget cuts it in two.
        var wholeCategory = !limitFromCaller && count <= WholeCategoryRows;
        if (wholeCategory) limit = Math.Max(count, 1);

        var rows = new JArray();
        var total = 0;
        var full = false;
        var estimatedBytes = 128;
        for (var index = 0; index < count; index++)
        {
            var row = category.Row(world, index);
            if (affordableOnly && !IsAffordableRow(world, category, row)) continue;

            // The ordinal counts matching rows, so offset and nextOffset mean the same thing on a
            // filtered page as on an unfiltered one, and total is what the filter actually matched.
            var ordinal = total++;
            if (ordinal < offset || rows.Count >= limit || full) continue;
            var projected = ProjectListRow(world, category, row);
            var rowBytes = EstimateListRowBytes(world, category, row, projected);
            if (rows.Count > 0 && !wholeCategory &&
                estimatedBytes + rowBytes > MaximumListResponseBytes)
            {
                full = true;
                continue;
            }
            var identity = category.TryIdentity(row, out var stableIdentity)
                ? stableIdentity
                : row is WorldEntityRequirement requirement
                    ? requirement.OwnerId
                    : Guid.Empty;
            var local = identity == Guid.Empty
                ? new JArray()
                : LocalizedRequirementImplications(
                    world, new HashSet<Guid> { identity });
            var localOffers = identity == Guid.Empty
                ? new JArray()
                : LocalizedDiscoveryOfferImplications(
                    world, new HashSet<Guid> { identity });
            if (local.Count == 0 && localOffers.Count == 0)
            {
                rows.Add(projected);
            }
            else
            {
                var incompleteRow = new JObject
                {
                    ["status"] = "not_available",
                    ["code"] = local.Count > 0
                        ? "entity_data_incomplete"
                        : "discovery_offer_read_incomplete",
                    ["reason"] = local.Count > 0
                        ? "this row has incomplete published requirement evidence"
                        : "this discovery tree has an offer absent from the published entity rows",
                    ["partialRow"] = projected,
                };
                if (local.Count > 0) incompleteRow["implicatedSkippedRows"] = local;
                if (localOffers.Count > 0) incompleteRow["implicatedOffers"] = localOffers;
                rows.Add(incompleteRow);
            }
            estimatedBytes += rowBytes;
        }

        // One pagination rule: nextOffset present means more rows remain and names where to
        // resume. A page shorter than the limit with a nextOffset is the byte budget; a page
        // shorter than the limit without one is the end of the category.
        var result = Envelope(publication);

        // A page with rows shows the shape of a row of this category by showing one. A page with
        // none has to say it, or the one read where a caller most needs to know what they were
        // looking for answers with a count and nothing else.
        if (rows.Count == 0) result["columns"] = ListColumns(category);
        result["rows"] = rows;
        result["total"] = total;
        var end = checked(offset + rows.Count);
        if (end < total) result["nextOffset"] = end;
        return result;
    }

    private static string[] ListColumns(GameMcpWorldCategory category) =>
        GameMcpEntityWireNormalizer.WireColumns(
            GameMcpListColumns.TryDeclared(category.Name, out var declared)
                ? declared
                : ListFields(category));

    /// <summary>
    /// What the mastery-experience ring actually says, rather than the ring.
    /// </summary>
    /// <remarks>
    /// The buffer is a fixed-size window the game overwrites, and the sources feeding it repeat on a
    /// short cycle: reading it row by row cost four full pages to deliver about fifteen distinct
    /// facts, and the only column that varied down the page was a monotone counter. The page says
    /// each distinct source once with how many of the window's samples it earned, and names the
    /// window itself so a caller can tell one read's window from the next.
    /// </remarks>
    /// <summary>
    /// The distinct sources in the ring's current window, and how many samples each earned.
    /// </summary>
    private static void MasteryExperienceSources(
        GameWorldState world,
        List<(MasteryExperienceDomain Domain, Guid SourceId, int SourceMastery)> keys,
        List<int> counts)
    {
        var samples = world.MasteryExperience;
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var key = (sample.Domain, sample.SourceId, sample.SourceMastery);
            var found = false;
            for (var slot = 0; slot < keys.Count; slot++)
            {
                if (!keys[slot].Equals(key)) continue;
                counts[slot]++;
                found = true;
                break;
            }
            if (found) continue;
            keys.Add(key);
            counts.Add(1);
        }
    }

    private static JObject MasteryExperienceSummary(
        WorldPublication<GameWorldState> publication,
        int offset,
        int limit)
    {
        var world = publication.Snapshot;
        var samples = world.MasteryExperience;
        var keys = new List<(MasteryExperienceDomain Domain, Guid SourceId, int SourceMastery)>();
        var counts = new List<int>();
        MasteryExperienceSources(world, keys, counts);

        var rows = new JArray();
        var end = Math.Min(keys.Count, checked(offset + limit));
        for (var index = Math.Min(offset, keys.Count); index < end; index++)
        {
            rows.Add(new JObject
            {
                ["count"] = counts[index],
                ["domain"] = keys[index].Domain.ToString(),
                ["sourceMastery"] = keys[index].SourceMastery,
                ["sourceId"] = keys[index].SourceId.ToString("D"),
                ["category"] = "mastery-experience",
                ["addressable"] = false,
            });
        }

        var result = Envelope(publication);
        if (samples.Count > 0)
        {
            result["window"] = new JObject
            {
                ["samples"] = samples.Count,
                ["firstSequence"] = samples[0].Sequence,
                ["lastSequence"] = samples[samples.Count - 1].Sequence,
            };
        }
        if (rows.Count == 0)
        {
            result["columns"] = GameMcpEntityWireNormalizer.WireColumns(
                new[] { "count", "domain", "sourceMastery", "sourceId" });
        }
        result["rows"] = rows;
        result["total"] = keys.Count;
        if (end < keys.Count) result["nextOffset"] = end;
        return result;
    }

    /// <summary>
    /// One search hit: which entity it is, what kind of thing it is, and the words the game prints
    /// on it.
    /// </summary>
    /// <remarks>
    /// The same four columns for every category, because a search page holds hits from all of them
    /// at once and borrowing each category's own scan columns unioned seventeen headings onto one
    /// table with about nine cells in ten empty. Widening is <c>world_list</c>'s job, on a page whose
    /// columns all apply to every row. The keywords cell is empty where the game authors no words —
    /// upgrades, challenges, views, achievements, advancements, recipe books and crafting recipes all
    /// spell their type line as a constant — and an empty cell prints as the absence mark rather than
    /// borrowing a word from somewhere the player would not recognise it.
    /// </remarks>
    private static GameMcpValue ProjectSearchMatch(
        GameMcpWorldCategory category,
        Guid identity,
        string keywords) =>
        new JObject
        {
            ["entityId"] = identity.ToString("D"),
            ["category"] = category.Name,
            ["keywords"] = keywords,
        }.Freeze();

    /// <summary>What a search page promises to say about every hit, whatever category it came from.</summary>
    private static readonly string[] SearchColumns = { "entityId", "category", "keywords" };

    private static GameMcpValue ProjectListRow(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row) =>
        WithOwnIdentity(category, Declared(category, ProjectListRowFields(world, category, row)));

    /// <summary>
    /// A hand-written projection answers to its category's declared column set. A category with no
    /// hand-written projection is already rendered from its declared field list, so it is total by
    /// construction and has nothing to check here.
    /// </summary>
    private static GameMcpValue Declared(GameMcpWorldCategory category, GameMcpValue projected)
    {
        if (projected is GameMcpObject built) GameMcpListColumns.Verify(category.Name, built);
        return projected;
    }

    /// <summary>
    /// A composite row's identity is its own. It has no addressable UUID, so it says so and
    /// declares the category it really belongs to, instead of borrowing whichever nested entity it
    /// happens to reference — an offer a caller took, spending a call on a refusal for one category
    /// and silently fetching the wrong entity for another.
    /// </summary>
    private static GameMcpValue WithOwnIdentity(
        GameMcpWorldCategory category,
        GameMcpValue projected)
    {
        if (string.Equals(category.IdentityMode, "stable_entity_uuid", StringComparison.Ordinal))
            return projected;
        if (projected is GameMcpProjectedDomainValue domain)
            return domain.WithoutAddressableIdentity();
        if (projected is not GameMcpObject frozen) return projected;
        var result = new JObject();
        result.CopyFrom(frozen);
        if (result["uuid"] is not null) return projected;
        result["category"] = category.Name;
        result["addressable"] = false;
        return result.Freeze();
    }

    private static GameMcpValue ProjectListRowFields(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row)
    {
        // The list row spells a level exactly as the row and the reference do: the badge the screen
        // shows, as the exact count it is rather than through the large-magnitude renderer, with
        // work in flight named separately and always present.
        if (row is WorldStructure structure)
        {
            var projected = new JObject
            {
                ["entityId"] = structure.EntityId.ToString("D"),
                ["level"] = structure.Reading.Level.ToInt(),
                ["queuedLevels"] = structure.Reading.QueuedLevels.ToInt(),
                ["state"] = StructureState(in structure),
                ["enabled"] = !structure.Reading.Disabled,
                ["affordable"] =
                    TryPurchaseAffordability(world, structure.EntityId, out var affordable)
                        ? affordable
                        : (object)GameMcpListColumns.Unpriced,
            };
            return projected.Freeze();
        }
        if (row is WorldUpgrade upgrade)
        {
            var projected = new JObject
            {
                ["entityId"] = upgrade.EntityId.ToString("D"),
                ["level"] = upgrade.Reading.Level,
                ["queuedLevels"] = upgrade.Reading.QueuedLevels,
                ["screen"] = UpgradeScreen(world, upgrade.EntityId),
                ["state"] = UpgradeState(in upgrade),
                ["maximum"] = UpgradeCeiling(in upgrade),
                ["requirements"] = RequirementWord(
                    WorldRequirementEvaluator.Evaluate(
                        world,
                        upgrade.EntityId,
                        WorldRequirementEvaluator.UpgradeCheckLevel(in upgrade))),
                ["affordable"] = UpgradeAffordability(world, in upgrade),
            };
            // Being maxed used to be four columns saying one thing: `available` said no,
            // `remainingLevels` said none, `affordable` said `already_maxed`, and the verdict pair
            // spelled it out in a sentence. `state` says it once. `available` was the same
            // predicate as `state == available` and is gone with it; `remainingLevels` is
            // `maximum` minus `level` wherever a maximum exists and said `uncapped` where one does
            // not, which is what `maximum` is for.
            return projected.Freeze();
        }
        // `created` is what separates the two things a zero equipped count meant: owning none of an
        // artifact, and owning some with none equipped. Without it the only way to tell them apart
        // was to attempt an equip and read the refusal, once per row.
        if (row is WorldEquipment equipment)
            return new JObject
            {
                ["entityId"] = equipment.EntityId.ToString("D"),
                ["created"] = equipment.IsCreated,
                ["equippedCount"] = equipment.EquippedLevel,
            }.Freeze();

        // The facts the ritual screen is scanned by: which one is held, how far it has been taken,
        // what a run would start at, how long a run is, and whether it can be paid for. Reading them
        // one ritual at a time cost a detail page per candidate to compare a column the row had.
        // Every field is unconditional, so the column set is the same page after a prestige.
        //
        // `discovered` folded into `state`: RitualSO.IsAvailable() and IsVisible() are both
        // IsDiscovered(), so the column said the lifecycle in a second grammar of its own.
        if (row is WorldRitual listedRitual)
            return new JObject
            {
                ["entityId"] = listedRitual.EntityId.ToString("D"),
                ["state"] = RitualState(in listedRitual),
                ["selected"] = listedRitual.Decision.Selected,
                ["reachedLevel"] = listedRitual.ReachedLevel,
                ["selectedLevel"] = listedRitual.SelectedLevel,
                ["waveTotal"] = listedRitual.RequiredWaves,
                ["affordable"] = listedRitual.Decision.ActivationAffordable,
            }.Freeze();

        // What a caller picks the next research by. `state` is the lifecycle the whole surface
        // shares, so `visible`, `available` and `complete` — its three inputs — do not each say a
        // third of it again. `affordable` is the published cost verdict for the next development,
        // which is a fact of the row rather than of the develop gate, so the scan row carries it on
        // every row instead of only where the gate happened to be open.
        //
        // `paused` is the player's own switch on this entry, the game's saved `isActive` field
        // inverted, and it is the durable half of what the retired `development` column said. The
        // other half was not durable: `idle`-versus-`active` is only "is a level in flight", which
        // drains to `idle` while nobody plays and which `queuedLevels` already counts on the same
        // row. What the queue is doing this moment is still the whole three-word answer on the
        // detail row and on the pause response — the surfaces a caller asks that question of.
        if (row is WorldResearch listedResearch)
            return new JObject
            {
                ["entityId"] = listedResearch.EntityId.ToString("D"),
                ["state"] = ResearchLifecycle(in listedResearch),
                ["paused"] = !listedResearch.IsActive,
                ["totalLevel"] = listedResearch.TotalLevel,
                ["queuedLevels"] = ResearchQueuedLevels(in listedResearch),
                ["requirements"] = listedResearch.MeetsLevelRequirements
                    ? GameMcpListColumns.Met
                    : GameMcpListColumns.Unmet,
                ["canDevelop"] = listedResearch.Decision.Available &&
                    listedResearch.Decision.LevelsAvailable > 0,
                ["affordable"] = listedResearch.Decision.DevelopmentCostAffordable,
            }.Freeze();

        // The gate the detail row already publishes. A hidden resource type refuses every level
        // purchase, so a list that omits it is a list a caller must probe row by row.
        if (row is WorldResourceType resourceType)
            return new JObject
            {
                ["entityId"] = resourceType.EntityId.ToString("D"),
                ["level"] = resourceType.LevelDecision.TotalLevel,
                ["hidden"] = resourceType.SpecialHidden,
            }.Freeze();
        // `available` folded into `state`: it was `Learned`, which is `GlyphSO.IsAvailable()`, which
        // is exactly the predicate the lifecycle word is. `discovered` stays because it is a
        // different fact — a pool unlocker is available off an authored prerequisite while never
        // having been discovered at all — and `population` is what says which of those two a row is
        // without the reader having to infer it from that pair.
        if (row is WorldGlyph glyph)
            return new JObject
            {
                ["entityId"] = glyph.EntityId.ToString("D"),
                ["population"] = GlyphPopulation(in glyph),
                ["screen"] = GlyphScreen(world, glyph.EntityId),
                ["state"] = GlyphState(in glyph),
                ["discovered"] = glyph.Discovered,
                ["paidLevel"] = glyph.LevelDecision.TotalLevel - glyph.LevelDecision.BonusLevels,
                ["bonusLevel"] = glyph.LevelDecision.BonusLevels,
                ["totalLevel"] = glyph.LevelDecision.TotalLevel,
            }.Freeze();
        // How many of this node exist and how many are uncommitted are the two counts a plan is made
        // from. How many sit in the Idle phase is not one of them: it is the game's `GetQuantity()`,
        // a phase timer's count, and it moves while nobody plays. A caller who needs the phase reads
        // the scan or the node's own detail, both of which still carry it.
        //
        // `visible` folded into `state`: PlotNodeSO.IsVisible() is that field, and it is what
        // UIPlotNode renders the row on, so the two columns were one fact under two names.
        if (row is WorldPlotNode plot)
            return new JObject
            {
                ["entityId"] = plot.EntityId.ToString("D"),
                ["state"] = PlotNodeState(in plot),
                ["masteryLevel"] = plot.Reading.MasteryLevel,
                ["quantity"] = plot.Reading.TotalQuantity,
                ["availableQuantity"] = plot.RemainingTotalQuantity,
            }.Freeze();
        if (row is WorldPurchaseCost purchaseCost)
        {
            var projected = ProjectPurchaseCost(world, in purchaseCost, asRow: true);
            projected["targetId"] = purchaseCost.EntityId.ToString("D");
            return projected.Freeze();
        }
        if (row is WorldTargetingRequest targeting)
            return ProjectTargeting(world, in targeting);
        // Two questions, two columns, and they used to share a name. `state` is how far the player
        // has come, the word every other category says it in; `run` is what this challenge's own
        // attempt did, which the game keeps in `ChallengeSO.state` and which never moves the
        // lifecycle — passing one run raises the level and hands the challenge straight back.
        if (row is WorldChallenge challenge)
            return new JObject
            {
                ["entityId"] = challenge.EntityId.ToString("D"),
                ["state"] = ChallengeLifecycle(in challenge),
                ["run"] = ChallengeRun(challenge.State),
                ["level"] = new GameMcpDomainValue(new BigDouble(challenge.Level)),
            }.Freeze();
        // `discovered` folded into `state`: it is what AlchemyRecipeSO.IsAvailable() reads, and
        // that is the member UIAlchemyRecipe renders each row on, so the recipe a player cannot
        // click and the recipe this page calls locked are one set.
        if (row is WorldAlchemyRecipe listedAlchemyRecipe)
            return new JObject
            {
                ["entityId"] = listedAlchemyRecipe.EntityId.ToString("D"),
                ["state"] = AlchemyRecipeState(in listedAlchemyRecipe),
                ["masteryLevel"] = listedAlchemyRecipe.MasteryLevel,
            }.Freeze();
        if (row is WorldCraftingStation station)
        {
            var identity = EntityIdentityFormatter.Describe(
                station.StructureTypeId, world.EntityIdentities);
            return new JObject
            {
                ["entityId"] = station.StationId.ToString("D"),
                ["name"] = identity.HasName ? identity.Name : station.StationId.ToString("D"),
                ["loaded"] = station.Loaded,
                ["active"] = station.Active,
            }.Freeze();
        }
        if (row is WorldCraftingQueueEntry queueEntry)
            return ProjectCraftingQueueEntry(in queueEntry);
        // A loadout and a snapshot list are live objects the asset catalog never publishes, so their
        // ids resolve for nobody: printing one hands the caller an address every tool refuses. The
        // name is what the screen says, and the position is what the verbs take.
        if (row is WorldPlayerLoadout playerLoadout)
            return new JObject
            {
                ["name"] = playerLoadout.Name,
                ["selected"] = playerLoadout.Selected,
            }.Freeze();
        if (row is WorldSnapshotLoadout snapshotLoadout)
        {
            var identity = EntityIdentityFormatter.Describe(
                snapshotLoadout.EntityId, world.EntityIdentities);
            return new JObject
            {
                ["name"] = identity.HasName
                    ? identity.Name
                    : SnapshotKind(snapshotLoadout.Kind) + " snapshots",
                ["kind"] = SnapshotKind(snapshotLoadout.Kind),
                ["slots"] = snapshotLoadout.Slots,
            }.Freeze();
        }
        // Scan rows that fall through to the reflected projector below inherit raw native property
        // paths and raw native values, which is how one fact ended up with two names and two
        // shapes: a list said reading.startingQuantity and actionMode where the detail row said
        // startingAmount and a named mode. These two say what the detail row says.
        if (row is WorldCraftingRecipe listedRecipe)
            return new JObject
            {
                ["entityId"] = listedRecipe.EntityId.ToString("D"),
                ["startingAmount"] =
                    new GameMcpDomainValue(listedRecipe.Reading.StartingQuantity),
            }.Freeze();
        if (row is WorldDiscoveryTree listedTree)
            return new JObject
            {
                ["entityId"] = listedTree.EntityId.ToString("D"),
                ["mode"] = DiscoveryMode(listedTree.ActionMode),
            }.Freeze();
        if (row is WorldResource resource)
            return ProjectResource(world, in resource);
        if (row is WorldAlchemyInstance alchemyInstance)
            return ProjectAlchemyInstance(world, in alchemyInstance);
        if (row is WorldActionQueueSlot processingSlot)
            return ProjectAgromancyProcessing(world, in processingSlot);
        if (row is WorldPlotAction plotAction)
            return ProjectPlotAction(world, in plotAction, asRow: true);
        // Every row that carries a position says it the way the verbs take it. The reflected
        // projector below copies the native member under its native name, and every native list is
        // indexed from zero — so a row printed its internal index while the verb addressed by that
        // number acted on the row above it. These five say slot, one-based, like the screen.
        if (row is WorldSpellSlot listedSpellSlot)
            return ProjectSpellSlotSummary(in listedSpellSlot);
        if (row is WorldSpellCost spellCost)
            return ProjectSpellCostRow(in spellCost);
        if (row is WorldSnapshotSlot snapshotSlot)
            return ProjectSnapshotSlotRow(in snapshotSlot);
        if (row is WorldSnapshotEntry snapshotEntry)
            return ProjectSnapshotEntryRow(in snapshotEntry);
        if (row is WorldAlchemyLoadoutDecision alchemyLoadout)
            return ProjectAlchemyLoadoutSummary(in alchemyLoadout);
        return new GameMcpProjectedDomainValue(
            row,
            ListFields(category),
            category.Name,
            category.ExpectedNativeType);
    }

    /// <summary>
    /// The spell-slot list row, addressed the way <c>game_cast</c> and <c>game_spell_loadout</c>
    /// take it. The equipped spell's own runtime id stays off the row: it is in no catalog, so it
    /// resolves for nobody, and the recipe carries the same name.
    /// </summary>
    /// <remarks>
    /// Whether the slot is casting right now is not on the row. It is the transient this table's
    /// rule was written from: a live round caught the column present on one page of a scan and
    /// absent on the next, with nothing about the request changed, because the cast started between
    /// two reads. The fact keeps every surface that answers for the instant — the scan, the
    /// <c>world_get</c> detail, and the <c>game_cast</c> response, which is the one a caller
    /// deciding its next press should be reading anyway.
    /// </remarks>
    private static GameMcpValue ProjectSpellSlotSummary(in WorldSpellSlot slot)
    {
        // An empty slot names itself under the column that would name its spell, so `occupied` is
        // not a second column for the same bit — and the id of an empty slot is the game's zero
        // Guid, which the wire drops rather than handing back an address nothing answers to.
        var result = new JObject
        {
            ["slot"] = GameMcpSlotNumbering.Wire(slot.SlotIndex),
            ["spellRecipeId"] = slot.Occupied
                ? slot.SpellRecipeId
                : (object)GameMcpListColumns.Empty,
        };
        return result.Freeze();
    }

    private static GameMcpValue ProjectSpellCostRow(in WorldSpellCost cost) =>
        new JObject
        {
            ["slot"] = GameMcpSlotNumbering.Wire(cost.SlotIndex),
            ["kind"] = cost.Kind.ToString(),
            ["resourceId"] = Named(cost.ResourceId),
            ["amount"] = new GameMcpDomainValue(cost.Amount),
        }.Freeze();

    private static GameMcpValue ProjectSnapshotSlotRow(in WorldSnapshotSlot slot) =>
        new JObject
        {
            ["ownerId"] = Named(slot.OwnerId),
            ["slot"] = GameMcpSlotNumbering.Wire(slot.Slot),
            ["populated"] = slot.Populated,
        }.Freeze();

    private static GameMcpValue ProjectSnapshotEntryRow(in WorldSnapshotEntry entry) =>
        new JObject
        {
            ["ownerId"] = Named(entry.OwnerId),
            ["slot"] = GameMcpSlotNumbering.Wire(entry.Slot),
            ["entryId"] = Named(entry.EntryId),
            ["quantity"] = entry.Quantity,
        }.Freeze();

    /// <summary>
    /// A reference column's value: the entity, or the word for naming none. The zero identity is
    /// an address nothing answers to, and dropping it would take the column with it on a page
    /// where no row names one.
    /// </summary>
    private static object Named(Guid entityId) =>
        entityId == Guid.Empty ? GameMcpListColumns.Absent : entityId;

    /// <summary>
    /// A recipe the loadout does not hold has no position at all — the game stores that as a
    /// negative index, which one-based arithmetic would print as a plausible slot 0, so the column
    /// says which recipes are out rather than going quiet about them.
    /// </summary>
    private static GameMcpValue ProjectAlchemyLoadoutSummary(
        in WorldAlchemyLoadoutDecision decision)
    {
        var result = new JObject
        {
            ["recipeId"] = Named(decision.RecipeId),
            ["slot"] = decision.Position >= 0
                ? GameMcpSlotNumbering.Wire(decision.Position)
                : (object)GameMcpListColumns.Unslotted,
            ["slotCount"] = decision.SlotCount,
            ["amount"] = decision.Amount,
        };
        return result.Freeze();
    }

    private static GameMcpValue ProjectCraftingQueueEntry(
        in WorldCraftingQueueEntry entry)
    {
        // Only an automated entry repeats, so the repetition count is where "this one is manual"
        // belongs; a separate `automatic` flag said the same bit a second time.
        var result = new JObject
        {
            ["queueId"] = Named(entry.QueueId),
            ["slot"] = GameMcpSlotNumbering.Wire(entry.Slot),
            ["recipeId"] = Named(entry.RecipeId),
            ["amount"] = new GameMcpDomainValue(entry.Amount),
            ["repetitions"] = entry.Automatic
                ? entry.Repetitions
                : (object)GameMcpListColumns.Manual,
        };
        return result.Freeze();
    }

    /// <summary>The categories whose rows publish a price, and therefore an affordability.</summary>
    private static readonly string[] PricedCategories = { "structures", "upgrades" };

    private static bool SupportsAffordableFilter(GameMcpWorldCategory category)
    {
        for (var index = 0; index < PricedCategories.Length; index++)
            if (string.Equals(category.Name, PricedCategories[index], StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// Whether one priced row can be bought right now, read exactly as its own list row reads it —
    /// an exhausted upgrade has no next level and so has no price to be short of.
    /// </summary>
    private static bool IsAffordableRow(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row)
    {
        if (row is WorldStructure structure)
        {
            return structure.Reading.Unlocked &&
                TryPurchaseAffordability(world, structure.EntityId, out var affordable) &&
                affordable;
        }
        if (row is WorldUpgrade upgrade)
        {
            return !upgrade.IsExhausted && upgrade.Reading.Available &&
                TryPurchaseAffordability(world, upgrade.EntityId, out var affordable) &&
                affordable;
        }
        throw new InvalidOperationException(
            "category " + category.Name + " accepted the affordable filter without a priced row");
    }

    private static bool TryPurchaseAffordability(
        GameWorldState world,
        Guid entityId,
        out bool affordable)
    {
        affordable = false;
        if (!WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, entityId, out var start, out var count) || count <= 0)
            return false;
        affordable = true;
        for (var index = start; index < start + count; index++)
        {
            var cost = world.PurchaseCosts[index];
            if (!cost.AffordabilityEvaluated) return false;
            if (!cost.Affordable) affordable = false;
        }
        return true;
    }

    /// <summary>
    /// The upgrade's ceiling, or the word for having none. The game marks "no ceiling" with a
    /// negative native maximum, and every candidate number for it either lies or inverts the
    /// meaning — <c>0</c> reads as a cap of zero. The word is the only spelling that is true.
    /// </summary>
    private static object UpgradeCeiling(in WorldUpgrade upgrade) =>
        upgrade.IsBounded ? upgrade.Reading.MaxLevel : GameMcpListColumns.Uncapped;

    /// <summary>
    /// Which screen's upgrade panel shows this row, from the authored list it is a member of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The panel a player opens is one <c>UIUpgradeList</c> whose contents swap with the active
    /// screen, so membership in an authored list <em>is</em> the screen fact. Every upgrade is on
    /// the catch-all list, which is what makes the catch-all worthless as an answer wherever a
    /// screen list also carries the row — and exactly right where none does.
    /// </para>
    /// <para>
    /// Three things fail closed to <see cref="GameMcpListColumns.Unreadable"/> rather than to a
    /// plausible word: a withheld membership publication, a row the publication does not mention,
    /// and a row two screen panels both claim. The last is impossible on the pinned build — the
    /// eight screen lists are disjoint — which is the point: if it ever stops being impossible, the
    /// column says so instead of picking a winner.
    /// </para>
    /// </remarks>
    private static string UpgradeScreen(GameWorldState world, Guid upgradeId)
    {
        if (!WorldUpgradeListMembershipLookup.TryFindRange(
                world.UpgradeListMemberships, upgradeId, out var start, out var count))
        {
            return GameMcpListColumns.Unreadable;
        }

        var screen = string.Empty;
        var everyUpgradeList = false;
        for (var index = 0; index < count; index++)
        {
            var listId = world.UpgradeListMemberships[start + index].ListId;
            if (listId == GameMcpListColumns.EveryUpgradeList)
            {
                everyUpgradeList = true;
                continue;
            }

            var word = string.Empty;
            for (var screenIndex = 0; screenIndex < GameMcpListColumns.Screens.Length; screenIndex++)
            {
                if (GameMcpListColumns.Screens[screenIndex].ListId != listId) continue;
                word = GameMcpListColumns.Screens[screenIndex].Word;
                break;
            }
            if (word.Length == 0 || (screen.Length > 0 && screen != word))
                return GameMcpListColumns.Unreadable;
            screen = word;
        }

        return screen.Length > 0
            ? screen
            : everyUpgradeList
                ? GameMcpListColumns.ScreenAll
                : GameMcpListColumns.Unreadable;
    }

    /// <summary>
    /// How far the player has come with this purchase, in the one vocabulary every purchasable
    /// row shares.
    /// </summary>
    /// <remarks>
    /// The game's own members answer this and the suite already captures both of them.
    /// <c>UpgradeSO.IsAvailable()</c> — which the binder reads, and which is exactly
    /// <c>!IsMaxLevel() &amp;&amp; prerequisites.Check()</c> — is the middle state, and it is also
    /// what <c>UIUpgradeButton</c> renders its row on, so a row the player can see and a row that
    /// is <see cref="GameMcpListColumns.Available"/> are the same set. Exhaustion is asked first
    /// because a finished upgrade is finished whatever its prerequisites went on to do, and
    /// because that is the order the game's own predicate uses. Nothing here reads
    /// <c>UpgradeSO.IsVisible()</c>: despite the name it is the prerequisite gate alone and stays
    /// true for a maxed upgrade, so it would call a completed row locked.
    /// </remarks>
    private static string UpgradeState(in WorldUpgrade upgrade) =>
        upgrade.IsExhausted ? GameMcpListColumns.Completed :
        upgrade.Reading.Available ? GameMcpListColumns.Available :
        GameMcpListColumns.Locked;

    /// <summary>
    /// The same lifecycle for research, off the same kind of captured members:
    /// <c>ResearchSO.IsVisible()</c> is its prerequisite gate and <c>IsComplete()</c> is
    /// <c>IsMaxLevel()</c>. Research splits visibility across two authored containers where an
    /// upgrade uses one, but the game has already collapsed both into the single <c>visible</c>
    /// the world captures.
    /// </summary>
    private static string ResearchLifecycle(in WorldResearch research) =>
        research.Complete ? GameMcpListColumns.Completed :
        research.Visible ? GameMcpListColumns.Available :
        GameMcpListColumns.Locked;

    /// <summary>
    /// A structure's whole lifecycle, which is two words. <c>StructureSO</c> carries no
    /// <c>maxLevel</c> field at all, so there is no level at which one is finished and no third
    /// word to reach. Its soft prerequisites are a development penalty rather than a gate — a
    /// structure builds worse with them unmet, not never — so they belong to the can-purchase
    /// axis and never to this column.
    /// </summary>
    private static string StructureState(in WorldStructure structure) =>
        structure.Reading.Unlocked
            ? GameMcpListColumns.Available
            : GameMcpListColumns.Locked;

    /// <summary>
    /// A recipe the alchemy screen will not show is one the player cannot reach, and that is the
    /// whole of this category's lifecycle: <c>UIAlchemyRecipe.IsVisible()</c> is
    /// <c>AlchemyRecipeSO.IsAvailable()</c>, and a recipe is never finished — <c>maxLevel</c> is the
    /// level it has been taken to, not a ceiling it stops at.
    /// </summary>
    /// <remarks>
    /// <c>IsAvailable()</c> reads <c>discovered</c> for a <c>Discover</c> recipe and
    /// <c>visibilityPrerequisites.Check()</c> for a <c>Prerequisite</c> one, so the published
    /// <c>Discovered</c> is the answer only on the first branch. The gate is captured rather than
    /// assumed, and the second branch is a lock this suite cannot read without making the game latch
    /// a prerequisite container during collection — so it says
    /// <see cref="GameMcpListColumns.Unreadable"/> rather than guessing. All 125 authored recipes on
    /// the pinned build are <c>Discover</c>, so no row reaches that word today; the branch exists so
    /// that a build where one did would say so instead of quietly calling it locked.
    /// </remarks>
    private static string AlchemyRecipeState(in WorldAlchemyRecipe recipe) =>
        recipe.VisibilityGate != WorldAlchemyRecipe.DiscoverGate
            ? GameMcpListColumns.Unreadable
            : recipe.Discovered
                ? GameMcpListColumns.Available
                : GameMcpListColumns.Locked;

    /// <summary>
    /// Whether the glyph picker offers this glyph. <c>UIGlyphListItem.IsVisible()</c> is
    /// <c>GlyphSO.IsAvailable()</c>, which the binder already reads as <c>Learned</c>, and
    /// <c>GlyphSO.IsVisible()</c> is the same method again. A glyph has no ceiling —
    /// <c>CanLevel()</c> is the constant <c>true</c> — so two words are its whole lifecycle.
    /// </summary>
    private static string GlyphState(in WorldGlyph glyph) =>
        glyph.Learned ? GameMcpListColumns.Available : GameMcpListColumns.Locked;

    /// <summary>
    /// Which family of glyph this is, off the authored boolean that splits them exactly. See
    /// <see cref="GameMcpListColumns.PopulationAugment"/> for why it is this field and not
    /// <c>augmentsSpells</c>.
    /// </summary>
    private static string GlyphPopulation(in WorldGlyph glyph) =>
        glyph.Discoverable
            ? GameMcpListColumns.PopulationAugment
            : GameMcpListColumns.PopulationUnlocker;

    /// <summary>
    /// Which page shows this glyph, from the authored lists it is a member of. See
    /// <see cref="GameMcpListColumns.ScreenAugments"/> for why several destinations is an answer
    /// here and a refusal in the upgrade column beside it.
    /// </summary>
    private static string GlyphScreen(GameWorldState world, Guid glyphId)
    {
        if (!WorldGlyphListMembershipLookup.TryFindRange(
                world.GlyphListMemberships, glyphId, out var start, out var count))
        {
            return world.GlyphListMemberships.Count == 0
                ? GameMcpListColumns.Unreadable
                : GameMcpListColumns.ScreenNoPage;
        }

        var screens = string.Empty;
        for (var pinned = 0; pinned < GameMcpListColumns.GlyphScreens.Length; pinned++)
        {
            var candidate = GameMcpListColumns.GlyphScreens[pinned];
            for (var offset = 0; offset < count; offset++)
            {
                if (world.GlyphListMemberships[start + offset].ListId != candidate.ListId) continue;
                screens = screens.Length == 0 ? candidate.Word : screens + ", " + candidate.Word;
                break;
            }
        }

        return screens.Length > 0 ? screens : GameMcpListColumns.ScreenNoPage;
    }

    /// <summary>
    /// The authored condition holding an unlocker shut, named. Each of the twenty-five carries one
    /// condition in <c>GlyphSO.prerequisites</c> — a research, an upgrade, or a prerequisite link —
    /// and <c>Prerequisites.Container.Check()</c> asks all of them at level zero, which is the level
    /// this evaluates at. The first unmet one is the answer: they are a flat AND, so any of them
    /// refusing is a complete reason, and naming the first keeps one sentence per row.
    /// </summary>
    private static bool TryNameGlyphBlocker(
        GameWorldState world,
        Guid glyphId,
        out string name,
        out Guid blockerId)
    {
        name = string.Empty;
        blockerId = Guid.Empty;
        if (!WorldEntityRequirementLookup.TryFindRange(
                world.EntityRequirements, glyphId, out var start, out var count))
        {
            return false;
        }

        var rows = world.EntityRequirements.AsSpan();
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var row = ref rows[start + offset];
            if (row.NodeKind != WorldRequirementNodeKind.Leaf) continue;
            if (WorldRequirementEvaluator.Evaluate(world, in row, level: 0) !=
                WorldRequirementVerdict.Unmet)
            {
                continue;
            }
            var identity = EntityIdentityFormatter.Describe(row.TargetId, world.EntityIdentities);
            if (!identity.HasName) continue;
            name = identity.Name;
            blockerId = row.TargetId;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the ritual screen shows the ritual rather than the undiscovered placeholder that
    /// stands in for it. <c>RitualSO.IsAvailable()</c>, <c>IsVisible()</c> and <c>IsDiscovered()</c>
    /// are one member three times over, and it is the field the world already publishes. A ritual is
    /// re-run without end, so nothing about one is ever finished.
    /// </summary>
    private static string RitualState(in WorldRitual ritual) =>
        ritual.Discovered ? GameMcpListColumns.Available : GameMcpListColumns.Locked;

    /// <summary>
    /// Whether the harvest screen lists this node. <c>UIPlotNode.IsVisible()</c> is
    /// <c>PlotNodeSO.IsVisible()</c>, which is the <c>visible</c> field the game latches from the
    /// node's own visibility prerequisite. Mastery grows without a ceiling, so there is no third
    /// word.
    /// </summary>
    private static string PlotNodeState(in WorldPlotNode plot) =>
        plot.Reading.Visible ? GameMcpListColumns.Available : GameMcpListColumns.Locked;

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
    /// the same question as the run column beside it: a challenge whose last run passed is
    /// <see cref="GameMcpListColumns.Available"/> again at the next level.
    /// </remarks>
    private static string ChallengeLifecycle(in WorldChallenge challenge) =>
        challenge.MaximumLevelReached ? GameMcpListColumns.Completed :
        challenge.AvailableToRun ? GameMcpListColumns.Available :
        GameMcpListColumns.Locked;

    /// <summary>
    /// The per-level conditions on the next purchase, as the second half of the can-purchase axis.
    /// </summary>
    /// <remarks>
    /// This is the branch <c>UIUpgradeButton.RenderContent()</c> takes when it replaces the price
    /// with a requirements notice, and it is a fact about the next press rather than about how far
    /// the row has come: a row whose requirements are unmet is still
    /// <see cref="GameMcpListColumns.Available"/>, on the panel, one condition away. An
    /// unevaluable verdict keeps its own word rather than borrowing <c>unmet</c>, because the two
    /// resolve differently — one waits for the save to progress, the other waits for this suite.
    /// </remarks>
    private static string RequirementWord(WorldRequirementVerdict verdict) => verdict switch
    {
        WorldRequirementVerdict.Met => GameMcpListColumns.Met,
        WorldRequirementVerdict.Unmet => GameMcpListColumns.Unmet,
        _ => GameMcpListColumns.Unmodelled,
    };

    /// <summary>
    /// Whether the next level can be paid for. A completed upgrade has no next level, so the world
    /// publishes no price for one — the same absence <see cref="GameMcpListColumns.Unpriced"/>
    /// already names. It used to answer <c>already_maxed</c> here, which was the completed state
    /// wearing an affordability column's clothes; <c>state</c> says that now, once.
    /// </summary>
    private static object UpgradeAffordability(GameWorldState world, in WorldUpgrade upgrade)
    {
        if (upgrade.IsExhausted) return GameMcpListColumns.Unpriced;
        return TryPurchaseAffordability(world, upgrade.EntityId, out var affordable)
            ? affordable
            : GameMcpListColumns.Unpriced;
    }

    private static string[] ListFields(GameMcpWorldCategory category) => category.Name switch
    {
        "resources" => new[] { "entityId", "trueQuantity" },
        "structures" => new[] { "entityId", "level", "reading.disabled" },
        "upgrades" => new[] { "entityId", "level" },
        "spell-recipes" => new[] { "entityId", "masteryLevel", "discovered" },
        "alchemy-recipes" => new[] { "entityId", "masteryLevel", "discovered" },
        "equipment" => new[] { "entityId", "equippedLevel" },
        "glyphs" => new[] { "entityId", "level" },
        "consumables" => new[] { "entityId", "quantity" },
        "crafting-queue-entries" => new[]
        {
            "queueId", "slot", "recipeId", "amount", "automatic", "repetitions",
        },
        "plot-nodes" => new[] { "entityId", "reading.masteryLevel" },
        "challenges" => new[] { "entityId", "level", "state" },
        _ => FirstDecisionFields(category),
    };

    private static string[] FirstDecisionFields(GameMcpWorldCategory category)
    {
        var take = string.Equals(
            category.IdentityMode, "stable_entity_uuid", StringComparison.Ordinal) ? 2 : 4;
        if (category.ScanFields.Length <= take) return category.ScanFields;
        var result = new string[take];
        Array.Copy(category.ScanFields, result, take);
        return result;
    }

    /// <summary>
    /// What one built row will weigh on the wire, measured from the row itself.
    /// </summary>
    /// <remarks>
    /// The estimate used to be a flat 512 bytes plus 64 per declared field, which is roughly three
    /// times what a row actually encodes to, so every page stopped at about a quarter of the byte
    /// budget and callers paged four times for one page's worth of rows. The document is already
    /// built when this runs, so the fields it really carries are countable; only the two things the
    /// transport adds later are charged as constants — the named-entity expansion the wire
    /// normalizer performs, and the large-magnitude values the JSON encoder renders.
    /// </remarks>
    private static int EstimateListRowBytes(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row,
        GameMcpValue projected)
    {
        var bytes = MeasureDocumentBytes(projected);
        if (!category.TryIdentity(row, out var identity)) return bytes;
        var name = EntityIdentityFormatter.Describe(identity, world.EntityIdentities).Name;

        // The row's own id shortens to its handle and gains the player's name for it; the asset name
        // and the runtime type it used to carry no longer ride any identity.
        return checked(
            bytes + NameFieldBytes + Encoding.UTF8.GetByteCount(name) - HandleSavingBytes);
    }

    /// <summary>The key, quotes, and comma one <c>name</c> field costs beside an id.</summary>
    private const int NameFieldBytes = 10;

    /// <summary>What a canonical UUID gives back when the wire says it as a handle.</summary>
    private const int HandleSavingBytes = 36 - GameMcpEntityHandle.Length;

    /// <summary>One large-magnitude value, which renders as a short scientific string.</summary>
    private const int DomainValueBytes = 10;

    /// <summary>One path copied out of a reflected row: its key, its punctuation, and its value.</summary>
    private const int ProjectedPathBytes = 48;

    /// <summary>A whole reflected row, whose shape is not known until the transport projects it.</summary>
    private const int UnprojectedRowBytes = 512;

    private static int MeasureDocumentBytes(GameMcpValue value)
    {
        switch (value)
        {
            case GameMcpObject item:
            {
                var bytes = 2;
                for (var index = 0; index < item.Properties.Count; index++)
                {
                    var property = item.Properties[index];
                    bytes += Encoding.UTF8.GetByteCount(property.Name) + 4;
                    bytes += MeasureDocumentBytes(property.Value);
                }
                return bytes;
            }
            case GameMcpArray array:
            {
                var bytes = 2;
                for (var index = 0; index < array.Items.Count; index++)
                    bytes += MeasureDocumentBytes(array.Items[index]) + 1;
                return bytes;
            }
            case GameMcpScalar scalar:
                return scalar.Value switch
                {
                    string text => Encoding.UTF8.GetByteCount(text) + 2,
                    bool boolean => boolean ? 4 : 5,
                    _ => 20,
                };
            case GameMcpProjectedDomainValue projection:
                return projection.Paths.Length == 0
                    ? UnprojectedRowBytes
                    : checked(2 + projection.Paths.Length * ProjectedPathBytes);
            case GameMcpDomainValue:
                return DomainValueBytes;
            default:
                return 4;
        }
    }

    internal static JObject GetRow(
        GameMcpFrameContext state,
        string categoryName,
        string uuidText)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        if (!TryCategory(categoryName, out var category, out var reason))
            return NotAvailable(publication, "unknown_category", reason);
        if (!Guid.TryParseExact(uuidText ?? string.Empty, "D", out var uuid))
            return NotAvailable(publication, "invalid_uuid", "That is not a valid id.");
        if (!string.Equals(
                category.IdentityMode,
                "stable_entity_uuid",
                StringComparison.Ordinal))
        {
            return NotAvailable(
                publication,
                "composite_identity_required",
                "Rows in " + category.Name + " are not addressed by one id; " +
                "read them with world_list.");
        }

        var availability = Availability(publication.Snapshot, category);
        if (!availability.Available)
        {
            return NotAvailable(
                publication,
                "category_not_collected",
                availability.Reason.Length == 0
                    ? "the category was not collected"
                    : availability.Reason);
        }

        var count = category.Count(publication.Snapshot);
        for (var index = 0; index < count; index++)
        {
            var row = category.Row(publication.Snapshot, index);
            if (!category.TryIdentity(row, out var rowIdentity) || rowIdentity != uuid) continue;
            var result = Envelope(publication);
            result["status"] = "available";
            var implicated = LocalizedRequirementImplications(
                publication.Snapshot,
                new HashSet<Guid> { uuid });
            var implicatedOffers = LocalizedDiscoveryOfferImplications(
                publication.Snapshot,
                new HashSet<Guid> { uuid });
            if (implicated.Count == 0 && implicatedOffers.Count == 0)
            {
                result["row"] = ProjectRow(publication.Snapshot, category, row);
            }
            else
            {
                result["status"] = "not_available";
                result["code"] = implicated.Count > 0
                    ? "entity_data_incomplete"
                    : "discovery_offer_read_incomplete";
                result["reason"] =
                    implicated.Count > 0
                        ? "The game did not report everything this entry requires."
                        : "This tree is offering something the game did not report anywhere else.";
                result["partialRow"] = ProjectRow(publication.Snapshot, category, row);
                if (implicated.Count > 0) result["implicatedSkippedRows"] = implicated;
                if (implicatedOffers.Count > 0) result["implicatedOffers"] = implicatedOffers;
            }
            return result;
        }

        return NotAvailable(
            publication,
            "unknown_uuid",
            "There is no " + category.Name + " entry with that id.");
    }

    /// <summary>
    /// The detail read: one id, or a batch of them, answered from the one immutable publication the
    /// router already pinned. Each block says what the thing is, what the game calls it, its
    /// published row, and — where this build evaluates them — the decisions, requirements, price and
    /// blockers behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Results preserve input order, so nothing has to echo an index back; only a failing block
    /// repeats its UUID, because that identity is what a caller needs to act on the failure. One id
    /// failing refuses that block alone and the rest still answer.
    /// </para>
    /// <para>
    /// <paramref name="categoryName"/> is optional and names which table the row is read from. An id
    /// carries its own category, so a caller who has an id from a search or a refusal never has to
    /// know one; naming it addresses a specific table — which is the only way to reach a row whose
    /// native type belongs to more than one of them.
    /// </para>
    /// <para>
    /// No publication token or state survives this call.
    /// </para>
    /// </remarks>
    internal static JObject GetRows(
        GameMcpFrameContext state,
        string categoryName,
        IReadOnlyList<string> uuidTexts)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        if (uuidTexts is null || uuidTexts.Count == 0 || uuidTexts.Count > MaximumBatchSize)
        {
            return NotAvailable(
                publication,
                "invalid_batch_size",
                "uuids must contain between 1 and " +
                MaximumBatchSize.ToString(CultureInfo.InvariantCulture) + " entries");
        }

        GameMcpWorldCategory? requested = null;
        if (!string.IsNullOrEmpty(categoryName))
        {
            if (!TryCategory(categoryName, out var named, out var reason))
                return NotAvailable(publication, "unknown_category", reason);
            if (!string.Equals(
                    named.IdentityMode,
                    "stable_entity_uuid",
                    StringComparison.Ordinal))
            {
                return NotAvailable(
                    publication,
                    "composite_identity_required",
                    "Rows in " + named.Name + " are not addressed by one id; " +
                    "read them with world_list.");
            }
            var namedAvailability = Availability(publication.Snapshot, named);
            if (!namedAvailability.Available)
            {
                return NotAvailable(
                    publication,
                    "category_not_collected",
                    namedAvailability.Reason.Length == 0
                        ? "the category was not collected"
                        : namedAvailability.Reason);
            }
            requested = named;
        }

        // Built once for the whole call. It is an index over the world's whole keyword table, and a
        // 200-id batch that rebuilt it per block would pay for that table two hundred times.
        var keywordIndex = GameMcpKeywordIndex.Build(publication.Snapshot);
        var results = new JArray();
        for (var inputIndex = 0; inputIndex < uuidTexts.Count; inputIndex++)
            results.Add(GetOne(
                publication.Snapshot, requested, uuidTexts[inputIndex], keywordIndex));

        var result = Envelope(publication);
        result["results"] = results;
        return result;
    }

    /// <summary>One id's block of the detail read, refusing on its own without failing the batch.</summary>
    private static JObject GetOne(
        GameWorldState world,
        GameMcpWorldCategory? requested,
        string? uuidText,
        GameMcpKeywordIndex keywordIndex)
    {
        if (!Guid.TryParseExact(uuidText ?? string.Empty, "D", out var uuid) || uuid == Guid.Empty)
        {
            return new JObject
            {
                ["status"] = "not_available",
                ["code"] = "invalid_uuid",
                ["reason"] = "That is not a valid id, and the all-zero id names nothing.",
                ["uuid"] = uuidText ?? string.Empty,
            };
        }

        // A category the caller named is gated as a whole above, because naming a page asks about
        // the page. A category an id resolved into is not: partial collection is answered per id by
        // the localized evidence below, and refusing every upgrade because one unrelated upgrade row
        // was skipped is the blunt answer that mechanism exists to replace.
        var category = requested;
        if (category is null && !TryEntityCategory(world, uuid, out category))
            return GameMcpEntityExplainer.UnresolvedEntity(world, uuid);

        object? matched = null;
        var count = category.Count(world);
        for (var rowIndex = 0; rowIndex < count; rowIndex++)
        {
            var row = category.Row(world, rowIndex);
            if (!category.TryIdentity(row, out var rowIdentity) || rowIdentity != uuid) continue;
            matched = row;
            break;
        }
        if (matched is null)
        {
            return new JObject
            {
                ["status"] = "not_available",
                ["code"] = "unknown_uuid",
                ["reason"] = "There is no " + category.Name + " entry with that id.",
                ["uuid"] = uuid.ToString("D"),
                ["readWith"] = new JObject
                {
                    ["tool"] = "world_list",
                    ["category"] = category.Name,
                },
            };
        }

        // A block that answered says nothing about having answered. Silence is the yes on every
        // other read on this surface, and inside a batch it is also what separates the blocks that
        // answered from the one that refused beside them.
        var item = new JObject();

        // Identity rides the detail read and only the detail read. What the asset is called and what
        // native type answers for it are catalog-browsing facts, so they are published where someone
        // browsing asks for them, and never stamped on a table's rows.
        if (GameMcpEntityExplainer.HasDetail(world, uuid) &&
            world.EntityIdentities.TryGet(uuid, out _))
        {
            item.CopyFrom(GameMcpEntityCatalog.Lookup(world.EntityIdentities, uuid));

            // The category the world actually publishes this id in, not the one the catalog's
            // runtime type implies: they disagree exactly where a caller most needs the truth.
            item["category"] = category.Name;
            var description = GameMcpEntityExplainer.ReadDescription(world, uuid);
            if (description.Length > 0) item["description"] = description;

            // The words the game prints on this thing's type line, under the same name and in the
            // same spelling `world_search` prints them. One fact, one name, both verbs.
            //
            // This was the round's costliest miss. A glyph's detail block published state,
            // discovery, visibility and price and never said `keywords: Elemental` — the one fact
            // that decides which family the glyph is in and therefore which page it renders on — so
            // a reader working from the detail block concluded the read surface contradicted the
            // screen, and held that finding for two hours. The search row ten minutes earlier had
            // carried the word plainly. Where a category also publishes a richer relation block
            // (`belongsTo` on spell recipes) that block stays; this line rides beside it, because a
            // reader should not have to know which of three treatments a category happens to give
            // one fact.
            var keywords = keywordIndex.Line(uuid);
            if (keywords.Length > 0) item["keywords"] = keywords;
        }

        var implicated = LocalizedRequirementImplications(world, new HashSet<Guid> { uuid });
        var implicatedOffers = LocalizedDiscoveryOfferImplications(world, new HashSet<Guid> { uuid });
        if (implicated.Count == 0 && implicatedOffers.Count == 0)
        {
            item["row"] = ProjectRow(world, category, matched);
        }
        else
        {
            item["status"] = "not_available";
            item["code"] = implicated.Count > 0
                ? "entity_data_incomplete"
                : "discovery_offer_read_incomplete";
            item["reason"] =
                implicated.Count > 0
                    ? "this entity has incomplete published requirement evidence"
                    : "this discovery tree has an offer UUID absent from all published " +
                      "explainable entity categories";
            item["partialRow"] = ProjectRow(world, category, matched);
            if (implicated.Count > 0) item["implicatedSkippedRows"] = implicated;
            if (implicatedOffers.Count > 0) item["implicatedOffers"] = implicatedOffers;
        }

        GameMcpEntityExplainer.AddDetail(item, world, uuid);

        // A keyword is a type asset, and what a type is worth is the one thing its own row cannot
        // say. Silent for every id no type record names, which is every id the explainer answers
        // for — the two blocks never meet on one entity.
        GameMcpTypeWorth.AddWorth(item, world, uuid);
        return item;
    }

    /// <summary>
    /// The published table an id belongs to, without the caller naming one. What the world itself
    /// publishes decides it; the live catalog's runtime type answers for the categories this build
    /// evaluates no detail for.
    /// </summary>
    private static bool TryEntityCategory(
        GameWorldState world,
        Guid uuid,
        out GameMcpWorldCategory category)
    {
        if (GameMcpEntityExplainer.TryDescribePublishedEntity(
                world, uuid, out var published, out _, out _) &&
            TryCategory(published, out category, out _))
        {
            return true;
        }
        if (world.EntityIdentities.TryGet(uuid, out var identity) &&
            GameMcpEntityCapabilityMap.TryCategoryForNativeType(
                identity.RuntimeType, out var implied) &&
            TryCategory(implied, out category, out _) &&
            string.Equals(category.IdentityMode, "stable_entity_uuid", StringComparison.Ordinal))
        {
            return true;
        }
        category = null!;
        return false;
    }

    /// <summary>
    /// Projects the state a successful mutation's next read would expose, without wrapping it in a
    /// read envelope. The caller already owns one main-thread frame context and waits for a newer
    /// immutable world before invoking this method.
    /// </summary>
    internal static GameMcpValue ProjectPostState(
        GameMcpFrameContext state,
        string categoryName,
        Guid uuid)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        if (!TryCategory(categoryName, out var category, out var reason))
            return PostStateUnavailable("unknown_category", reason);
        var world = state.World.Snapshot;
        var count = category.Count(world);
        for (var index = 0; index < count; index++)
        {
            var row = category.Row(world, index);
            if (category.TryIdentity(row, out var identity) && identity == uuid)
                return ProjectRow(world, category, row);
        }
        return PostStateUnavailable(
            "post_state_not_published",
            "the newer world has no " + category.Name + " row for the committed target");
    }

    /// <summary>
    /// Projects the smallest changed fact after any gameplay mutation. This is the only
    /// command-to-world-projection switch; transport code only waits and delegates here.
    /// </summary>
    /// <remarks>
    /// A committed answer used to carry what it charged and what the next level asks. Both were
    /// wire-only bookkeeping: the paid rows priced the game's multi-buy variable rather than the
    /// count the call committed, so a two-level buy reported one level's price as though it were the
    /// whole charge, and the caller that most needed the number got the one running in the
    /// dangerous direction. The world publication still holds every cost curve — Auto Buy plans off
    /// it — and a refusal that cannot afford something still names what it needs and what is held.
    /// A live caller gets the outcome.
    /// </remarks>
    internal static GameMcpValue ProjectGameplayPostState(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed) => ProjectChangedFact(state, command, committed);

    private static GameMcpValue ProjectChangedFact(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed) => command.Kind switch
        {
            GameMcpCommandKind.SpellWorkbench when string.Equals(
                command.Mode,
                "create",
                StringComparison.Ordinal) => ProjectSpellLoadoutDelta(state, command),
            GameMcpCommandKind.SpellWorkbench => ProjectDiscoveryDelta(state, command),
            GameMcpCommandKind.SpellComposition =>
                ProjectCastingDialDelta(state, command),
            GameMcpCommandKind.SpellLoadout => ProjectSpellLoadoutDelta(state, command),
            GameMcpCommandKind.Targeting => ProjectTargetingPostState(
                state,
                GameMcpTargetingProjection.SubmittedTarget(committed.Details)),
            GameMcpCommandKind.Consumable =>
                ProjectConsumableDelta(state, command),
            GameMcpCommandKind.Challenge =>
                ProjectChallengePostState(state, command),
            GameMcpCommandKind.Prestige => ProjectPrestigePostState(state),
            GameMcpCommandKind.SpellLevel when string.Equals(
                command.Mode,
                "all",
                StringComparison.Ordinal) => ProjectSpellLevelAllPostState(state, command),
            GameMcpCommandKind.Cast => ProjectCastDelta(state, command),
            GameMcpCommandKind.Concept => ProjectConceptDelta(state, command),
            GameMcpCommandKind.Harvest => ProjectHarvestDelta(state, command, committed),
            GameMcpCommandKind.SpellLevel => ProjectSpellLevelDelta(state, command),
            GameMcpCommandKind.Crafting => ProjectCraftingDelta(state, command, committed),
            GameMcpCommandKind.EquipmentLoadout => ProjectEquipmentDelta(state, command),
            GameMcpCommandKind.AlchemyLoadout => ProjectAlchemyLoadoutDelta(state, command),
            GameMcpCommandKind.RitualLifecycle => ProjectRitualLifecycleDelta(state, command),
            GameMcpCommandKind.GenericLevel => ProjectGenericLevelDelta(state, command),
            GameMcpCommandKind.CraftingStation => ProjectCraftingStationDelta(state, command),
            GameMcpCommandKind.Loadout => ProjectLoadoutDelta(state, command),
            GameMcpCommandKind.HarvestLifecycle => ProjectHarvestLifecycleDelta(state, command),
            GameMcpCommandKind.StructureLifecycle => ProjectStructureLifecycleDelta(state, command),
            GameMcpCommandKind.Research => ProjectResearchDelta(state, command),
            GameMcpCommandKind.GenericDiscovery => ProjectDiscoveryDelta(state, command),
            GameMcpCommandKind.DiscoveryTreeOffer when string.Equals(
                command.Mode,
                "select",
                StringComparison.Ordinal) => WithoutOffers(
                    ProjectPostState(state, PostStateCategory(command), command.TargetId)),
            GameMcpCommandKind.DiscoveryTreeOffer when string.Equals(
                command.Mode,
                "offer_confirm",
                StringComparison.Ordinal) => ProjectDiscoveryOfferConfirmDelta(state, command),
            GameMcpCommandKind.DiscoveryTreeOffer when string.Equals(
                command.Mode,
                "offer_reroll",
                StringComparison.Ordinal) => ProjectDiscoveryRerollDelta(state, command),
            _ => ProjectPostState(state, PostStateCategory(command), command.TargetId),
        };

    /// <summary>
    /// What a confirmed offer produced. The press permanently spends a discovery choice, and the
    /// answer used to be a bare count with nothing naming what was taken — so a caller could not
    /// confirm the discovery landed, and read the tree again to learn what it had got. The tool
    /// held that identity in its own request the whole time.
    /// </summary>
    private static GameMcpValue ProjectDiscoveryOfferConfirmDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        if (!WorldLookup.TryFind(world.DiscoveryTrees, command.TargetId, out var after))
            return PostStateUnavailable(
                "post_state_not_published",
                "the settled world has no discovery tree row for the committed target");
        var before = Before(command);
        WorldDiscoveryTree previous = default;
        var hadBefore = before is not null &&
            WorldLookup.TryFind(before.DiscoveryTrees, command.TargetId, out previous);
        var result = new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["discovered"] = command.SecondaryId.ToString("D"),
            ["discoveredCount"] = new JObject
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
        return result.Freeze();
    }

    /// <summary>
    /// What a spent discovery reroll bought: the budget it cost, whether the offers actually moved,
    /// and the offers themselves. It answered with a bare post-value and no offers, so the caller
    /// re-read the whole tree to learn whether the press had done anything.
    /// </summary>
    private static GameMcpValue ProjectDiscoveryRerollDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        if (!WorldLookup.TryFind(world.DiscoveryTrees, command.TargetId, out var after))
            return PostStateUnavailable(
                "post_state_not_published",
                "the settled world has no discovery tree row for the committed target");
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

    private static bool SameOfferIds(
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
    private static GameMcpValue WithoutOffers(GameMcpValue projected)
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

    private static GameWorldState? Before(GameMcpCommand command) =>
        command.FrameContext?.World?.Snapshot;

    private static GameMcpValue ProjectCastDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        var slotIndex = command.Amount - 1;
        if (state.World is null ||
            !WorldSpellSlotLookup.TryFind(
                state.World.Snapshot.SpellSlots,
                slotIndex,
                out var after) ||
            !after.Occupied ||
            after.SpellRecipeId != command.TargetId)
        {
            return PostStateUnavailable(
                "post_state_not_published",
                "the settled loadout no longer contains that spell in the requested slot");
        }
        var prior = default(WorldSpellSlot);
        var hasBefore = Before(command) is { } before &&
            WorldSpellSlotLookup.TryFind(before.SpellSlots, slotIndex, out prior) &&
            prior.Occupied && prior.SpellRecipeId == command.TargetId;
        var result = new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["slot"] = GameMcpSlotNumbering.Wire(slotIndex),
        };
        var fire = string.Equals(command.Mode, "fire", StringComparison.Ordinal);
        if (fire)
        {
            var costs = ProjectEquippedSpellCosts(
                state.World.Snapshot, slotIndex, WorldSpellCostKind.Immediate);
            if (costs.Count > 0) result["costs"] = costs;
            // What a committed fire moved: a cast that was not running is running now. The boundary
            // verifies the press against the game's own fire hook, and it refuses a spell that is
            // already casting, so a commit is a cast this call started.
            //
            // The game's finished-cast counter used to stand here and could not do this job. The
            // game increments it when a cast completes, not when one starts, so a cast shorter than
            // the world cadence and a cast that never happened produced the same number — a landed
            // press and a dropped press read byte-identical.
            //
            // Fire is the only mode that carries it, because it is the only mode with a native
            // delta behind it: a release is a native call with nothing to verify a start against,
            // and a toggle-off's own sentinel is a cast ending. Neither may claim one either way.
            result["casting"] = true;
        }
        // Whether this press left a charge held — an input the caller has to pick back up, which no
        // loadout row records. It rides on every mode as yes or no rather than vanishing between
        // them: `release` exists precisely to end a hold, and a response that drops the key instead
        // of saying `no` leaves a caller unable to tell "the hold is over" from "this mode does not
        // speak about holds", which is the same absence-as-value the surface bans everywhere else.
        result["charging"] = fire &&
            string.Equals(command.PayloadValue, "charge", StringComparison.Ordinal);
        // Whether the spell is running, as the boolean the read surface publishes. Every mode says
        // it: the settled slot always knows, and an idle one-shot that dropped the key made its
        // silence carry the answer.
        result["active"] = after.Casting;
        if (hasBefore && prior.CurrentCharges != after.CurrentCharges)
        {
            result["charges"] = new JObject
            {
                ["before"] = prior.CurrentCharges,
                ["after"] = after.CurrentCharges,
            };
        }
        // Named after the term it reads, because that term is not a prediction that the next press
        // lands. The game's own Spell.Fire asks IsCasting() BEFORE it asks CanCast(), so a running
        // spell answers CanCast() with true and answers the press with nothing — which is how a
        // response saying `ready: yes` invited the very press this tool then refused. What a press
        // would do additionally needs the live target selector, which no published world holds, so
        // the field says what it measures and `casting` beside it says the rest.
        result["castReady"] = after.CastReady;
        result["cooldown"] = new GameMcpDomainValue(
            BigDouble.Max(after.CooldownRemaining, BigDouble.Zero));
        return result.Freeze();
    }

    private static GameMcpValue ProjectCastingDialDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var after = state.World.Snapshot.SpellWorkbench;
        var before = Before(command)?.SpellWorkbench;
        var output = command.Mode == "set_output_level";
        return new JObject
        {
            ["dial"] = command.PayloadKey,
            ["before"] = output ? before?.OutputLevel : before?.ReserveLevel,
            ["after"] = output ? after.OutputLevel : after.ReserveLevel,
            // The floor of both dials is the constant 1 and lives in the tool documentation. The
            // ceiling is the one end of the range that moves, so it is the one end worth sending.
            ["maximum"] = output ? after.MaximumOutputLevel : after.MaximumReserveLevel,
        }.Freeze();
    }

    private static GameMcpValue ProjectStructureLifecycleDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var after = state.World.Snapshot;
        if (!WorldLookup.TryFind(after.Structures, command.TargetId, out var current))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no structure row for that attribute");
        var before = Before(command);
        WorldStructure previous = default;
        var hadBefore = before is not null &&
            WorldLookup.TryFind(before.Structures, command.TargetId, out previous);
        return Change(command.TargetId,
            hadBefore ? !previous.Reading.Disabled : (bool?)null,
            !current.Reading.Disabled,
            "enabled");
    }

    private static GameMcpValue ProjectConceptDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var after = WorldAlchemyInstanceLookup.TryFind(
            state.World.Snapshot.AlchemyInstances, command.TargetId, out var current)
            ? current.Quantity
            : 0;
        var oldWorld = Before(command);
        var before = oldWorld is not null && WorldAlchemyInstanceLookup.TryFind(
            oldWorld.AlchemyInstances, command.TargetId, out var previous)
            ? previous.Quantity
            : 0;
        return Change(command.TargetId, before, after, "activeCount");
    }

    private static GameMcpValue ProjectAlchemyLoadoutDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        if (!WorldAlchemyLoadoutLookup.TryFind(
                state.World.Snapshot.AlchemyLoadout, command.TargetId, out var current))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no ordinary Alchemy row for that recipe");
        var oldWorld = Before(command);
        WorldAlchemyLoadoutDecision previous = default;
        var hadBefore = oldWorld is not null && WorldAlchemyLoadoutLookup.TryFind(
            oldWorld.AlchemyLoadout, command.TargetId, out previous);
        if (command.Mode == "move")
        {
            if (!current.IsActive || current.Position != command.Amount - 1)
                return PostStateUnavailable("requested_state_not_reached",
                    "the settled Alchemy loadout does not show the requested slot");
            return new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                ["slot"] = new JObject
                {
                    ["before"] = hadBefore
                        ? GameMcpSlotNumbering.Wire(previous.Position)
                        : (int?)null,
                    ["after"] = GameMcpSlotNumbering.Wire(current.Position),
                },
            }.Freeze();
        }
        return Change(command.TargetId,
            hadBefore ? (object)previous.TargetAmount : null,
            current.TargetAmount,
            "activeCount");
    }

    private static GameMcpValue ProjectRitualLifecycleDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        if (!WorldLookup.TryFind(world.Rituals, command.TargetId, out var current))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no Ritual row for that ritual");
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
                postState["selectedLevel"] = current.SelectedLevel;
                var drain = ProjectRitualCosts(world, current.Decision.CompletionCosts);
                if (drain.Count > 0) postState["completionCosts"] = drain;
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
                ["before"] = hadBefore ? previous.SelectedLevel : (int?)null,
                ["after"] = current.SelectedLevel,
            };
        return result.Freeze();
    }

    private static GameMcpValue ProjectHarvestLifecycleDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var oldWorld = Before(command);
        if (command.Mode is "add_element" or "remove_element")
        {
            if (!TryFindHarvestElementControl(
                    world.HarvestElementControls, command.TargetId, out var current))
                return PostStateUnavailable("post_state_not_published",
                    "the settled world has no harvest-list row for that element");
            WorldHarvestElementControl previous = default;
            var hadBefore = oldWorld is not null && TryFindHarvestElementControl(
                oldWorld.HarvestElementControls, command.TargetId, out previous);
            var result = new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                ["active"] = new JObject
                {
                    ["before"] = hadBefore ? previous.Active : (int?)null,
                    ["after"] = current.Active,
                },
            };
            return result.Freeze();
        }

        if (!TryFindHarvestActionControl(world.HarvestActionControls,
                command.TargetId, command.SecondaryId, out var action))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no harvest-list row for that element and action");
        WorldHarvestActionControl previousAction = default;
        var hadActionBefore = oldWorld is not null && TryFindHarvestActionControl(
            oldWorld.HarvestActionControls, command.TargetId, command.SecondaryId,
            out previousAction);
        return new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["actionUuid"] = command.SecondaryId.ToString("D"),
            ["active"] = new JObject
            {
                ["before"] = hadActionBefore ? previousAction.Active : (int?)null,
                ["after"] = action.Active,
            },
        }.Freeze();
    }

    private static GameMcpValue ProjectGenericLevelDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        if (!TryFindLevelDecision(
                state.World.Snapshot, command.TargetId, command.DerivedNativeType,
                out var current))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no level row for that entity");
        var oldWorld = Before(command);
        WorldLevelableDecision previous = default;
        var hadBefore = oldWorld is not null && TryFindLevelDecision(
            oldWorld, command.TargetId, command.DerivedNativeType, out previous);
        var currentPaid = current.TotalLevel - current.BonusLevels;
        var previousPaid = hadBefore ? previous.TotalLevel - previous.BonusLevels : (int?)null;
        var result = new JObject { ["uuid"] = command.TargetId.ToString("D") };
        if (command.Mode == "bonus")
        {
            if (hadBefore && current.BonusLevels <= previous.BonusLevels)
                return PostStateUnavailable("requested_state_not_reached",
                    "the settled world does not show a higher bonus level");
            result["bonusLevel"] = new JObject
            {
                ["before"] = hadBefore ? previous.BonusLevels : (int?)null,
                ["after"] = current.BonusLevels,
            };
        }
        else
        {
            if (hadBefore && currentPaid <= previousPaid)
                return PostStateUnavailable("requested_state_not_reached",
                    "the settled world does not show a higher paid level");
            result["paidLevel"] = new JObject
            {
                ["before"] = previousPaid,
                ["after"] = currentPaid,
            };
            // The game's headroom can be below the ask, and one level bought against an ask of two
            // settled into an answer indistinguishable from a satisfied amount=1. What was asked is
            // said only when it differs from what arrived.
            if (hadBefore && command.Amount > currentPaid - previousPaid!.Value)
            {
                result["requestedAmount"] = command.Amount;
                result["deliveredAmount"] = currentPaid - previousPaid.Value;
            }
        }
        result["totalLevel"] = new JObject
        {
            ["before"] = hadBefore ? previous.TotalLevel : (int?)null,
            ["after"] = current.TotalLevel,
        };

        // A level that asks for nothing is worth saying, because it changes what a caller does next.
        // What it asked and what the next one asks are not: the world publication carries both cost
        // curves for anything that plans off them, and a caller acting on this answer has bought
        // the levels the pair above names.
        var bonus = command.Mode == "bonus";
        if ((bonus ? current.BonusCosts : current.PaidCosts).Count == 0 &&
            hadBefore && (bonus ? previous.BonusCosts : previous.PaidCosts).Count == 0)
        {
            result["free"] = true;
        }

        // A glyph screen counts uses, not levels — levels buy uses through the mastery requirement,
        // so the number the player watched move is the one the row already publishes as usableCount.
        if (command.DerivedNativeType == "GlyphSO" &&
            WorldLookup.TryFind(state.World.Snapshot.Glyphs, command.TargetId, out var glyph))
        {
            WorldGlyph priorGlyph = default;
            var hadGlyph = oldWorld is not null &&
                WorldLookup.TryFind(oldWorld.Glyphs, command.TargetId, out priorGlyph);
            result["usableCount"] = new JObject
            {
                ["before"] = hadGlyph ? priorGlyph.MaximumUsages : (int?)null,
                ["after"] = glyph.MaximumUsages,
            };
        }
        return result.Freeze();
    }

    private static GameMcpValue ProjectCraftingStationDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        if (!WorldCraftingStationLookup.TryFind(
                state.World.Snapshot.CraftingStations, command.TargetId, out var station))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no Brewing Station row for that station");
        var oldWorld = Before(command);
        WorldCraftingStation previous = default;
        var hadBefore = oldWorld is not null && WorldCraftingStationLookup.TryFind(
            oldWorld.CraftingStations, command.TargetId, out previous);
        var result = new JObject { ["uuid"] = command.TargetId.ToString("D") };
        switch (command.Mode)
        {
            case "set_ingredient":
                var oldIngredient = command.Amount == 1
                    ? previous.FirstIngredientId
                    : previous.SecondIngredientId;
                var newIngredient = command.Amount == 1
                    ? station.FirstIngredientId
                    : station.SecondIngredientId;
                result["ingredient"] = new JObject
                {
                    ["slot"] = command.Amount,
                    ["before"] = hadBefore && oldIngredient != Guid.Empty
                        ? oldIngredient.ToString("D")
                        : null,
                    ["after"] = newIngredient != Guid.Empty
                        ? newIngredient.ToString("D")
                        : null,
                };
                break;
            case "set_output":
                result["output"] = new JObject
                {
                    ["before"] = hadBefore && previous.OutputId != Guid.Empty
                        ? previous.OutputId.ToString("D")
                        : null,
                    ["after"] = station.OutputId != Guid.Empty
                        ? station.OutputId.ToString("D")
                        : null,
                };
                break;
            case "set_level":
                result["level"] = new JObject
                {
                    ["before"] = hadBefore ? previous.Level : (int?)null,
                    ["after"] = station.Level,
                };
                break;
            default:
                result["active"] = new JObject
                {
                    ["before"] = hadBefore && previous.Active,
                    ["after"] = station.Active,
                };
                break;
        }
        return result.Freeze();
    }

    private static GameMcpValue ProjectLoadoutDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var before = Before(command);
        if (command.DerivedNativeType == "PlayerLoadout")
        {
            if (!WorldLoadoutLookup.TryFindPlayer(world.PlayerLoadouts,
                    command.TargetId, out var current))
                return PostStateUnavailable("post_state_not_published",
                    "the settled world has no player loadout with that UUID");
            WorldPlayerLoadout previous = default;
            var hadBefore = before is not null && WorldLoadoutLookup.TryFindPlayer(
                before.PlayerLoadouts, command.TargetId, out previous);
            // The loadout is a live object the asset catalog never publishes, so its id resolves for
            // nobody and every tool refuses it; the read rows dropped it for the same reason, and a
            // mutation answering with one would be the one place a caller could still find it.
            var result = new JObject { ["name"] = current.Name };
            switch (command.Mode)
            {
                case "select":
                    result["selected"] = new JObject
                    {
                        ["before"] = hadBefore && previous.Selected,
                        ["after"] = current.Selected,
                    };
                    // Swapping loadouts re-equips the spell bar, and the bar belongs to the player
                    // rather than to the loadout being described. Selecting an empty loadout
                    // unequipped five spells and the only trace was `spells: none` inside the
                    // loadout's own contents, which reads as what that loadout stores.
                    var bar = ProjectSpellBarEffect(world, before);
                    if (bar is not null) result["spellBar"] = bar;
                    result["loadout"] = ProjectPlayerLoadout(world, in current);
                    break;
                case "set_equipment":
                    result["equipment"] = new JObject
                    {
                        ["before"] = hadBefore && previous.SavesEquipment,
                        ["after"] = current.SavesEquipment,
                    };
                    break;
                case "set_alchemy":
                    result["alchemy"] = new JObject
                    {
                        ["before"] = hadBefore && previous.SavesAlchemy,
                        ["after"] = current.SavesAlchemy,
                    };
                    break;
                case "rename":
                    result["label"] = new JObject
                    {
                        ["before"] = hadBefore ? previous.Name : null,
                        ["after"] = current.Name,
                    };
                    break;
                case "next_icon":
                    result["icon"] = new JObject
                    {
                        ["before"] = hadBefore ? previous.Icon : (int?)null,
                        ["after"] = current.Icon,
                    };
                    break;
                case "next_color":
                    result["color"] = new JObject
                    {
                        ["before"] = hadBefore ? previous.Color : (int?)null,
                        ["after"] = current.Color,
                    };
                    break;
            }
            return result.Freeze();
        }

        if (!WorldLoadoutLookup.TryFindSnapshot(world.SnapshotLoadouts,
                command.TargetId, out var owner))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no Equipment or Alchemy snapshot list with that UUID");
        var slot = command.Amount - 1;
        var snapshot = ProjectSnapshotSlot(world, in owner, slot);
        if (snapshot is null)
            return PostStateUnavailable("post_state_not_published",
                "the settled snapshot list has no requested slot");
        var response = new JObject
        {
            ["name"] = SnapshotOwnerName(world, in owner),
            ["kind"] = SnapshotKind(owner.Kind),
            ["snapshot"] = snapshot,
        };
        if (command.Mode == "snapshot_load")
            response["active"] = ProjectActiveLoadoutSection(world, owner.Kind);
        return response.Freeze();
    }

    /// <summary>
    /// What a loadout swap did to the spell bar, or nothing when the bar did not move.
    /// </summary>
    /// <remarks>
    /// The bar is player state, not loadout contents, so it is named at the top of the answer with
    /// the spells that left and the spells that arrived. Recovery needs the names that went away,
    /// and the settled world no longer holds them.
    /// </remarks>
    private static GameMcpValue? ProjectSpellBarEffect(
        GameWorldState world,
        GameWorldState? before)
    {
        if (before is null) return null;
        var removed = new JArray();
        var added = new JArray();
        for (var index = 0; index < before.SpellSlots.Count; index++)
        {
            var slot = before.SpellSlots[index];
            if (!slot.Occupied) continue;
            if (ContainsSpellInstance(world.SpellSlots, slot.SpellInstanceId)) continue;
            removed.Add(EntityReference(before, slot.SpellRecipeId));
        }
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied) continue;
            if (ContainsSpellInstance(before.SpellSlots, slot.SpellInstanceId)) continue;
            added.Add(EntityReference(world, slot.SpellRecipeId));
        }
        if (removed.Count == 0 && added.Count == 0) return null;
        var result = new JObject
        {
            ["equipped"] = new JObject
            {
                ["before"] = OccupiedSpellSlots(before),
                ["after"] = OccupiedSpellSlots(world),
            },
        };
        if (removed.Count > 0) result["unequipped"] = removed;
        if (added.Count > 0) result["equippedNow"] = added;
        return result.Freeze();
    }

    private static int OccupiedSpellSlots(GameWorldState world)
    {
        var occupied = 0;
        for (var index = 0; index < world.SpellSlots.Count; index++)
            if (world.SpellSlots[index].Occupied) occupied++;
        return occupied;
    }

    private static GameMcpValue ProjectPlayerLoadout(
        GameWorldState world,
        in WorldPlayerLoadout loadout)
    {
        var spells = new JArray();
        var equipment = new JArray();
        var alchemy = new JArray();
        for (var index = 0; index < world.PlayerLoadoutEntries.Count; index++)
        {
            var entry = world.PlayerLoadoutEntries[index];
            if (entry.OwnerId != loadout.EntityId) continue;
            if (entry.Kind == WorldLoadoutEntryKind.Spell)
            {
                // The saved spell is a runtime instance, so its id is in no catalog and resolves for
                // nobody. Its recipe does, and it is the only identity here a caller can look up.
                spells.Add(new JObject { ["spell"] = EntityReference(world, entry.ReferenceId) });
            }
            else
            {
                var row = EntityReference(world, entry.EntryId, entry.Quantity);
                (entry.Kind == WorldLoadoutEntryKind.Equipment ? equipment : alchemy).Add(row);
            }
        }
        // Every section is always present with its own list, empty or not: a loadout that saves
        // nothing is a fact the caller needs, and dropping the key made it read the same as a
        // section nobody projected.
        var sections = new JObject { ["spells"] = spells };
        sections["equipment"] = new JObject
        {
            ["saved"] = loadout.SavesEquipment,
            ["entries"] = equipment,
        };
        sections["alchemy"] = new JObject
        {
            ["saved"] = loadout.SavesAlchemy,
            ["entries"] = alchemy,
        };
        var result = new JObject
        {
            ["name"] = loadout.Name,
            ["category"] = "player-loadouts",
            ["selected"] = loadout.Selected,
            ["sections"] = sections,
            ["label"] = new JObject
            {
                ["icon"] = loadout.Icon,
                ["color"] = loadout.Color,
            },
        };

        // One predicate serves the read and the mutation: CanSwapLoadouts() is the game's whole
        // admission for a swap, so a false read carries the code select itself would return.
        if (!loadout.Selected)
        {
            result["canSelect"] = loadout.CanSwitchNow;
            if (!loadout.CanSwitchNow) result["reasonCode"] = "switch_blocked";
        }
        return result.Freeze();
    }

    private static GameMcpValue ProjectSnapshotLoadout(
        GameWorldState world,
        in WorldSnapshotLoadout owner)
    {
        var slots = new JArray();
        for (var slot = 0; slot < owner.Slots; slot++)
        {
            var row = ProjectSnapshotSlot(world, in owner, slot);
            if (row is not null) slots.Add(row);
        }
        var result = new JObject
        {
            ["name"] = SnapshotOwnerName(world, in owner),
            ["category"] = "snapshot-loadouts",
            ["kind"] = SnapshotKind(owner.Kind),
            ["slots"] = slots,
        };
        return result.Freeze();
    }

    private static GameMcpValue? ProjectSnapshotSlot(
        GameWorldState world,
        in WorldSnapshotLoadout owner,
        int slot)
    {
        WorldSnapshotSlot value = default;
        var found = false;
        for (var index = 0; index < world.SnapshotSlots.Count; index++)
        {
            var candidate = world.SnapshotSlots[index];
            if (candidate.OwnerId != owner.EntityId || candidate.Slot != slot) continue;
            value = candidate;
            found = true;
            break;
        }
        if (!found) return null;
        var result = new JObject
        {
            ["slot"] = GameMcpSlotNumbering.Wire(slot),
            ["populated"] = value.Populated,
        };
        if (value.Populated)
        {
            var entries = new JArray();
            for (var index = 0; index < world.SnapshotEntries.Count; index++)
            {
                var entry = world.SnapshotEntries[index];
                if (entry.OwnerId == owner.EntityId && entry.Slot == slot)
                    entries.Add(EntityReference(world, entry.EntryId, entry.Quantity));
            }
            result["entries"] = entries;
        }
        return result.Freeze();
    }

    private static GameMcpValue ProjectActiveLoadoutSection(
        GameWorldState world,
        WorldSnapshotLoadoutKind kind)
    {
        var entries = new JArray();
        if (kind == WorldSnapshotLoadoutKind.Equipment)
        {
            for (var index = 0; index < world.Equipment.Count; index++)
            {
                var equipment = world.Equipment[index];
                if (equipment.EquippedLevel > 0)
                    entries.Add(EntityReference(world, equipment.EntityId,
                        equipment.EquippedLevel));
            }
        }
        else
        {
            for (var index = 0; index < world.AlchemyLoadout.Count; index++)
            {
                var alchemy = world.AlchemyLoadout[index];
                if (alchemy.IsActive && alchemy.Amount > 0)
                    entries.Add(EntityReference(world, alchemy.RecipeId, alchemy.Amount));
            }
        }
        return entries.Freeze();
    }

    private static GameMcpValue EntityReference(
        GameWorldState world,
        Guid id,
        int quantity = 0)
    {
        var identity = EntityIdentityFormatter.Describe(id, world.EntityIdentities);
        var result = new JObject { ["uuid"] = id.ToString("D") };
        if (identity.HasName) result["name"] = identity.Name;
        if (quantity > 0) result["amount"] = quantity;
        return result.Freeze();
    }

    private static string SnapshotOwnerName(
        GameWorldState world,
        in WorldSnapshotLoadout owner)
    {
        var identity = EntityIdentityFormatter.Describe(owner.EntityId, world.EntityIdentities);
        return identity.Source is EntityIdentityNameSource.LiveDisplayName or
            EntityIdentityNameSource.KnownEntityBootstrap
            ? identity.Name
            : SnapshotKind(owner.Kind) + " snapshots";
    }

    private static string SnapshotKind(WorldSnapshotLoadoutKind kind) =>
        kind == WorldSnapshotLoadoutKind.Alchemy ? "alchemy" : "equipment";

    private static bool TryFindLevelDecision(
        GameWorldState world,
        Guid target,
        string nativeType,
        out WorldLevelableDecision decision)
    {
        if (nativeType == "EquipmentTypeSO" &&
            WorldLookup.TryFind(world.EquipmentTypes, target, out var equipment))
        {
            decision = equipment.LevelDecision;
            return true;
        }
        if (nativeType == "GlyphSO" &&
            WorldLookup.TryFind(world.Glyphs, target, out var glyph))
        {
            decision = glyph.LevelDecision;
            return true;
        }
        if (nativeType == "ResourceTypeSO" &&
            WorldLookup.TryFind(world.ResourceTypes, target, out var resourceType))
        {
            decision = resourceType.LevelDecision;
            return true;
        }
        if (nativeType == "TimeRuneSO" &&
            WorldLookup.TryFind(world.TimeRunes, target, out var timeRune))
        {
            decision = timeRune.LevelDecision;
            return true;
        }
        decision = default;
        return false;
    }

    private static GameMcpValue ProjectHarvestDelta(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed)
    {
        if (state.World is null ||
            !WorldPlotActionLookup.TryFind(state.World.Snapshot.PlotActions,
                command.TargetId, command.SecondaryId, out var current))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no requested plot-action row");
        var world = state.World.Snapshot;
        var oldWorld = Before(command);
        var before = oldWorld is null ? 0 : PlotActionQuantity(
            oldWorld.ActionQueueSlots, command.TargetId, command.SecondaryId);
        var after = PlotActionQuantity(
            world.ActionQueueSlots, command.TargetId, command.SecondaryId);
        if (command.Mode == "add_plot_action" ? after <= before : after >= before)
        {
            var observed = Property(committed.Details, "active");
            if (observed is null)
                return PostStateUnavailable("post_state_not_published",
                    "the plot action changed before the next world could publish it");
            return new JObject
            {
                ["plot"] = EntityReference(world, command.TargetId),
                ["action"] = EntityReference(world, command.SecondaryId),
                ["active"] = observed,
            }.Freeze();
        }
        return new JObject
        {
            ["plot"] = EntityReference(world, command.TargetId),
            ["action"] = EntityReference(world, command.SecondaryId),
            ["active"] = new JObject
            {
                ["before"] = before,
                ["after"] = after,
            },
        }.Freeze();
    }

    private static GameMcpValue? Property(GameMcpValue? value, string name)
    {
        if (value is not GameMcpObject instance) return null;
        for (var index = 0; index < instance.Properties.Count; index++)
        {
            var property = instance.Properties[index];
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
                return property.Value;
        }
        return null;
    }

    private static int PlotActionQuantity(
        PublicationTable<WorldActionQueueSlot> instances,
        Guid plotId,
        Guid actionId)
    {
        var quantity = 0;
        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            if (!instance.Empty && instance.PlotNodeId == plotId &&
                instance.PlotNodeActionId == actionId)
                quantity += Math.Max(instance.Quantity, 0);
        }
        return quantity;
    }

    private static GameMcpValue ProjectSpellLevelDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null ||
            !WorldLookup.TryFind(state.World.Snapshot.SpellRecipes, command.TargetId, out var current))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no leveled spell row");
        var oldWorld = Before(command);
        var before = oldWorld is not null && WorldLookup.TryFind(
            oldWorld.SpellRecipes, command.TargetId, out var previous)
            ? previous.MasteryLevel
            : (int?)null;
        return Change(command.TargetId, before, current.MasteryLevel, "mastery");
    }

    private static GameMcpValue ProjectEquipmentDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null ||
            !WorldLookup.TryFind(state.World.Snapshot.Equipment, command.TargetId, out var current))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no equipment row");
        var oldWorld = Before(command);
        var before = oldWorld is not null && WorldLookup.TryFind(
            oldWorld.Equipment, command.TargetId, out var previous)
            ? previous.EquippedLevel
            : (int?)null;
        return Change(command.TargetId, before, current.EquippedLevel, "equippedCount");
    }

    private static GameMcpValue ProjectResearchDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null ||
            !WorldLookup.TryFind(state.World.Snapshot.Research, command.TargetId, out var current))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no research row");
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
    private static string ResearchDevelopment(in WorldResearch research) =>
        !research.IsDeveloping ? "idle" :
        research.IsActive ? "active" : "paused";

    private static GameMcpValue ProjectDiscoveryDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        if (!TryReadDiscoveryState(
                state.World.Snapshot,
                command.TargetId,
                command.DerivedNativeType,
                out var after))
            return PostStateUnavailable(
                "post_state_not_published",
                "the settled world has no discovery state for the requested target");
        bool? before = null;
        var oldWorld = Before(command);
        if (oldWorld is not null && TryReadDiscoveryState(
                oldWorld,
                command.TargetId,
                command.DerivedNativeType,
                out var previous))
            before = previous;
        var result = new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["discovered"] = new JObject { ["before"] = before, ["after"] = after },
        };
        if (command.PayloadKey.Length > 0) result["surface"] = command.PayloadKey;
        return result.Freeze();
    }

    private static bool TryReadDiscoveryState(
        GameWorldState world,
        Guid targetId,
        string nativeType,
        out bool discovered)
    {
        switch (nativeType)
        {
            case "SpellRecipeSO" when WorldLookup.TryFind(
                world.SpellRecipes, targetId, out var spell):
                discovered = spell.Discovered;
                return true;
            case "GlyphSO" when WorldLookup.TryFind(world.Glyphs, targetId, out var glyph):
                discovered = glyph.Discovered;
                return true;
            case "RitualSO" when WorldLookup.TryFind(world.Rituals, targetId, out var ritual):
                discovered = ritual.Discovered;
                return true;
            case "TimeRuneSO" when WorldLookup.TryFind(
                world.TimeRunes, targetId, out var timeRune):
                discovered = timeRune.Discovered;
                return true;
            case "AlchemyRecipeSO" when WorldLookup.TryFind(
                world.AlchemyRecipes, targetId, out var alchemy):
                discovered = alchemy.Discovered;
                return true;
            case "EquipmentSO" when WorldLookup.TryFind(
                world.Equipment, targetId, out var equipment):
                discovered = equipment.IsCreated;
                return true;
            default:
                discovered = false;
                return false;
        }
    }

    private static GameMcpValue ProjectConsumableDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null ||
            !WorldLookup.TryFind(state.World.Snapshot.Consumables, command.TargetId, out var current))
            return PostStateUnavailable("post_state_not_published",
                "the settled world has no consumable row");
        var oldWorld = Before(command);
        WorldConsumable previous = default;
        var hasPrevious = oldWorld is not null && WorldLookup.TryFind(
            oldWorld.Consumables, command.TargetId, out previous);
        return command.Mode switch
        {
            "set_randomization" => Change(
                command.TargetId,
                hasPrevious ? previous.Randomized : (bool?)null,
                current.Randomized,
                "randomized"),
            // A use takes the item into the game's preparation queue and it is consumed later, so
            // the press answers that it queued. A cancel takes entries back out, which is a settled
            // count moving now.
            "use" => QueuedMutation(
                command.TargetId,
                command.Amount,
                hasPrevious ? current.QueuedQuantity - previous.QueuedQuantity : null),
            "cancel" => Change(
                command.TargetId,
                hasPrevious ? previous.QueuedQuantity : (int?)null,
                current.QueuedQuantity,
                "queued"),
            "move" => ProjectConsumableMoveDelta(state, command),
            _ => Change(
                command.TargetId,
                hasPrevious ? previous.Quantity : (int?)null,
                current.Quantity,
                "amount"),
        };
    }

    private static GameMcpValue ProjectConsumableMoveDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        var oldWorld = Before(command);
        var list = string.Equals(command.PayloadKey, "hotbar", StringComparison.Ordinal)
            ? WorldConsumableListKind.Hotbar
            : WorldConsumableListKind.Inventory;
        var before = oldWorld is not null
            ? FindConsumablePosition(oldWorld.ConsumableInventory.Slots, command.TargetId, list)
            : null;
        var after = FindConsumablePosition(
            state.World!.Snapshot.ConsumableInventory.Slots, command.TargetId, list);
        return Change(command.TargetId, before, after, "slot");
    }

    private static int? FindConsumablePosition(
        PublicationTable<WorldConsumableSlot> slots,
        Guid consumableId,
        WorldConsumableListKind list)
    {
        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (slot.List == list && slot.ConsumableId == consumableId)
                return slot.Position;
        }
        return null;
    }

    private static GameMcpValue ProjectCraftingDelta(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var oldWorld = Before(command);
        WorldCraftingDecision previous = default;
        var hasBefore = oldWorld is not null && WorldCraftingDecisionLookup.TryFind(
            oldWorld.CraftingDecisions, command.TargetId, out previous);
        if (!WorldCraftingDecisionLookup.TryFind(
                state.World.Snapshot.CraftingDecisions,
                command.TargetId,
                out var current))
            return PostStateUnavailable(
                command.TargetId,
                command.Amount,
                "post_state_not_published",
                "the settled world has no crafting decision for the committed recipe");
        if (command.Mode is "automate" or "cancel_automation")
        {
            // The screen's number, not the repetition count behind it: one cancel on a doubled
            // entry moves the badge 8 to 4 while the repetitions move 4 to 3. A side whose queue
            // entry was not collected while the recipe still repeats says so rather than reporting
            // the zero the lookup returned.
            var result = new JObject { ["uuid"] = command.TargetId.ToString("D") };
            var priorBadge = BigDouble.Zero;
            var hasAfter = TryAutomationBadge(state.World.Snapshot, current.AutomationQueueId,
                command.TargetId, current.AutomationRepetitions, out var afterBadge);
            var hasPrior = !hasBefore || oldWorld is null ||
                TryAutomationBadge(oldWorld, previous.AutomationQueueId, command.TargetId,
                    previous.AutomationRepetitions, out priorBadge);
            if (hasAfter && hasPrior)
                result["amount"] = new JObject
                {
                    ["before"] = hasBefore && oldWorld is not null
                        ? new GameMcpDomainValue(priorBadge)
                        : null,
                    ["after"] = new GameMcpDomainValue(afterBadge),
                };
            else
                result["amountUnavailable"] = AutomationAmountUnavailable();
            return result.Freeze();
        }
        if (command.Mode == "cancel_manual")
        {
            return Change(
                command.TargetId,
                hasBefore ? previous.QueuedAmount.ToInt() : null,
                current.QueuedAmount.ToInt(),
                "queued");
        }
        if (GameMcpCraftingProjection.ProvedCompletion(committed.Details))
        {
            return new JObject
            {
                ["uuid"] = command.TargetId.ToString("D"),
                ["completed"] = true,
            }.Freeze();
        }
        var settled = current.QueuedAmount.ToInt();

        // A craft is taken into the game's crafting queue and finishes later, so the answer is the
        // press: crafts are queued. Without a before world the count cannot be told apart from what
        // was already waiting, and inventing one would be worse than saying only what is known.
        if (!hasBefore) return QueuedMutation(command.TargetId, command.Amount, null);
        var started = previous.QueuedAmount.ToInt();
        if (settled > started)
            return QueuedMutation(command.TargetId, command.Amount, settled - started);
        // An unmoved queue count is the same number before and after the craft, which proves
        // nothing about it. Say what the settled world shows instead of publishing the pre-state.
        return PostStateUnavailable(
            command.TargetId,
            command.Amount,
            "post_state_not_observed",
            GameMcpCraftingProjection.ProvedQueueEntry(committed.Details)
                ? "the craft entered the game's crafting queue, but the settled queue still shows " +
                  settled + " queued for this recipe, so the entry it created is no longer observable"
                : "the settled crafting queue still shows " + settled +
                  " queued for this recipe, so the committed craft left no observable change");
    }

    private static GameMcpValue Change(
        Guid uuid,
        object? before,
        object? after,
        string field) => new JObject
    {
        ["uuid"] = uuid.ToString("D"),
        [field] = new JObject { ["before"] = before, ["after"] = after },
    }.Freeze();

    /// <summary>
    /// The one answer a mutation the game queues is allowed to give: it worked, and this much is
    /// queued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A queue hop is not a settled fact. The counts move again while the answer is being written and
    /// have moved back by the time the caller reads it, so a <c>{before, after}</c> pair over them
    /// reports a transition nobody can confirm — and the round that shipped one reported a purchase
    /// that landed on level 1 as <c>level: 0 -&gt; 0</c>, whose honest reading is "it failed". A loop
    /// driven off that either spins forever or buys twice. The press is what is knowable, so the
    /// press is what is said, and where the queue stands now is a read.
    /// </para>
    /// <para>
    /// A delivery short of the ask says both numbers on one line, because the difference is the whole
    /// fact and a partial that looks like a satisfied <c>amount=1</c> is the shape a caller cannot
    /// act on. An observed count is always said as that number, including the count of one: these
    /// verbs promise "how many", and answering the commonest press with a bare <c>yes</c> spends the
    /// one word that could carry the observation on saying nothing, leaving a caller to re-read the
    /// entity to learn what its own commit did. An unknown count says only that the press queued
    /// something: naming a number the evidence does not carry would be the same defect wearing a
    /// confident face. A count observed as nought or less is unknown rather than nought — this is
    /// only reached once the mutation's own sentinel has proved at least one entry was made, so a
    /// queue showing no growth has drained already, and reporting "0 of 1 asked" for a press that
    /// landed is the very lie being fixed.
    /// </para>
    /// </remarks>
    internal static GameMcpValue QueuedMutation(Guid uuid, int asked, int? queued) => new JObject
    {
        ["uuid"] = uuid.ToString("D"),
        ["queued"] = QueuedValue(asked, queued),
    }.Freeze();

    private static object QueuedValue(int asked, int? queued) =>
        queued is null or < 1
            ? true
            : queued < asked
                ? queued + " of " + asked + " asked; the game took no more this press."
                : queued.Value;

    private static GameMcpValue ProjectSpellLoadoutDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var after = state.World.Snapshot;
        var before = Before(command);
        if (command.Mode is "add" or "create")
        {
            for (var index = 0; index < after.SpellSlots.Count; index++)
            {
                var slot = after.SpellSlots[index];
                if (!slot.Occupied || slot.SpellRecipeId != command.TargetId) continue;
                if (before is not null && ContainsSpellInstance(
                        before.SpellSlots, slot.SpellInstanceId)) continue;
                return new JObject
                {
                    ["uuid"] = command.TargetId.ToString("D"),
                    ["slot"] = new JObject
                    {
                        ["before"] = null,
                        ["after"] = GameMcpSlotNumbering.Wire(slot.SlotIndex),
                    },
                    ["loadBudget"] = new JObject
                    {
                        ["used"] = new JObject
                        {
                            ["before"] = before?.SpellWorkbench.EquippedCount,
                            ["after"] = after.SpellWorkbench.EquippedCount,
                        },
                        ["maximum"] = after.SpellWorkbench.MaximumEquipped,
                    },
                }.Freeze();
            }
        }
        else
        {
            WorldSpellSlot oldSlot = default;
            var hadBefore = before is not null && TryFindSpellInstance(
                before.SpellSlots, command.TargetId, out oldSlot);
            var hasAfter = TryFindSpellInstance(
                after.SpellSlots, command.TargetId, out var newSlot);
            if (command.Mode == "remove" && hadBefore && !hasAfter)
                return new JObject
                {
                    ["uuid"] = oldSlot.SpellRecipeId.ToString("D"),
                    ["slot"] = new JObject
                    {
                        ["before"] = GameMcpSlotNumbering.Wire(oldSlot.SlotIndex),
                        ["after"] = null,
                    },
                }.Freeze();
            if (command.Mode == "move" && hadBefore && hasAfter)
            {
                var moved = new JObject
                {
                    ["uuid"] = newSlot.SpellRecipeId.ToString("D"),
                    ["slot"] = new JObject
                    {
                        ["before"] = GameMcpSlotNumbering.Wire(oldSlot.SlotIndex),
                        ["after"] = GameMcpSlotNumbering.Wire(newSlot.SlotIndex),
                    },
                };
                // A move onto an occupied slot is a swap. Reporting only the half the caller named
                // left the other spell somewhere the caller's model did not have it, and the next
                // cast at the old address refused.
                var displaced = ProjectDisplacedSpell(
                    after, before!, newSlot.SlotIndex, newSlot.SpellInstanceId);
                if (displaced is not null) moved["displaced"] = displaced;
                return moved.Freeze();
            }
        }
        return PostStateUnavailable("requested_state_not_reached",
            "the settled loadout does not show the requested add, remove, or move");
    }

    /// <summary>
    /// The spell the move pushed out of the destination, and where the game put it.
    /// </summary>
    private static GameMcpValue? ProjectDisplacedSpell(
        GameWorldState after,
        GameWorldState before,
        int destinationIndex,
        Guid movedInstanceId)
    {
        var occupant = default(WorldSpellSlot);
        var occupied = false;
        for (var index = 0; index < before.SpellSlots.Count; index++)
        {
            var slot = before.SpellSlots[index];
            if (slot.SlotIndex != destinationIndex || !slot.Occupied) continue;
            if (slot.SpellInstanceId == movedInstanceId) continue;
            occupant = slot;
            occupied = true;
            break;
        }
        if (!occupied) return null;
        if (!TryFindSpellInstance(after.SpellSlots, occupant.SpellInstanceId, out var settled))
            return null;
        if (settled.SlotIndex == destinationIndex) return null;
        return new JObject
        {
            ["uuid"] = settled.SpellRecipeId.ToString("D"),
            ["slot"] = new JObject
            {
                ["before"] = GameMcpSlotNumbering.Wire(destinationIndex),
                ["after"] = GameMcpSlotNumbering.Wire(settled.SlotIndex),
            },
        }.Freeze();
    }

    private static bool ContainsSpellInstance(
        PublicationTable<WorldSpellSlot> slots,
        Guid instanceId) => TryFindSpellInstance(slots, instanceId, out _);

    private static bool TryFindSpellInstance(
        PublicationTable<WorldSpellSlot> slots,
        Guid instanceId,
        out WorldSpellSlot slot)
    {
        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index].SpellInstanceId != instanceId) continue;
            slot = slots[index];
            return true;
        }
        slot = default;
        return false;
    }

    private static string PostStateCategory(GameMcpCommand command) => command.Kind switch
    {
        GameMcpCommandKind.Purchase => command.Mode == "structure" ? "structures" : "upgrades",
        GameMcpCommandKind.Cast => "spell-recipes",
        GameMcpCommandKind.Concept => "alchemy-recipes",
        GameMcpCommandKind.Harvest => "plot-nodes",
        GameMcpCommandKind.SpellLevel => "spell-recipes",
        GameMcpCommandKind.DiscoveryTreeOffer => "discovery-trees",
        GameMcpCommandKind.SpellWorkbench => "spell-recipes",
        GameMcpCommandKind.Crafting => "crafting-recipes",
        GameMcpCommandKind.GenericDiscovery => command.DerivedNativeType switch
        {
            "AlchemyRecipeSO" => "alchemy-recipes",
            "EquipmentSO" => "equipment",
            "GlyphSO" => "glyphs",
            "RitualSO" => "rituals",
            "TimeRuneSO" => "time-runes",
            _ => throw new ArgumentOutOfRangeException(nameof(command.DerivedNativeType)),
        },
        GameMcpCommandKind.EquipmentLoadout => "equipment",
        GameMcpCommandKind.AlchemyLoadout => "alchemy-recipes",
        GameMcpCommandKind.RitualLifecycle => "rituals",
        GameMcpCommandKind.GenericLevel => command.DerivedNativeType switch
        {
            "EquipmentTypeSO" => "equipment-types",
            "GlyphSO" => "glyphs",
            "ResourceTypeSO" => "resource-types",
            "TimeRuneSO" => "time-runes",
            _ => string.Empty,
        },
        GameMcpCommandKind.CraftingStation => "crafting-stations",
        GameMcpCommandKind.Loadout => command.DerivedNativeType == "PlayerLoadout"
            ? "player-loadouts"
            : "snapshot-loadouts",
        GameMcpCommandKind.Research => "research",
        _ => throw new ArgumentOutOfRangeException(nameof(command.Kind)),
    };

    private static GameMcpValue ProjectSpellLevelAllPostState(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var oldWorld = Before(command);
        var spells = new JArray();
        for (var index = 0; index < world.SpellRecipes.Count; index++)
        {
            var recipe = world.SpellRecipes[index];
            if (!recipe.Discovered) continue;
            var before = oldWorld is not null && WorldLookup.TryFind(
                oldWorld.SpellRecipes, recipe.EntityId, out var previous)
                ? previous.MasteryLevel
                : (int?)null;
            if (before.HasValue && before.Value == recipe.MasteryLevel) continue;
            spells.Add(new JObject
            {
                ["spellRecipeId"] = recipe.EntityId.ToString("D"),
                ["mastery"] = new JObject
                {
                    ["before"] = before,
                    ["after"] = recipe.MasteryLevel,
                },
            });
        }
        return new JObject { ["changedSpells"] = spells }.Freeze();
    }

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
            return PostStateUnavailable(
                "post_state_not_published",
                "the settled world has no challenge row for the committed target");
        if (command.Mode == "select")
        {
            var oldWorld = Before(command);
            return Change(
                command.TargetId,
                oldWorld is not null && ChallengeSelected(oldWorld, command.TargetId),
                ChallengeSelected(world, command.TargetId),
                "selected");
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
    /// What one press of the challenge-offer button spent. The game's own button decrements
    /// <c>challengeRerollsLeft</c> when <c>hasFetchedChallenges</c> is already set and otherwise sets
    /// that flag, so both facts ship as pairs on every commit: a caller must never have to infer
    /// from an absent key whether a scarce reroll left the budget. <c>changed</c> answers the other
    /// question a press asks — whether the redraw moved the offers — because the game promises the
    /// spend, not a different set: a pool small enough to redraw itself is a legitimate outcome, and
    /// the caller reads it here instead of diffing two offer lists itself.
    /// </summary>
    private static GameMcpValue ProjectChallengeRerollDelta(
        GameWorldState world,
        GameWorldState? before)
    {
        var after = world.ChallengeContext;
        var prior = before?.ChallengeContext;
        var result = new JObject
        {
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
        return result.Freeze();
    }

    private static bool SameChallengeOffers(
        PublicationTable<WorldChallengeReference> before,
        PublicationTable<WorldChallengeReference> after)
    {
        if (before.Count != after.Count) return false;
        for (var index = 0; index < before.Count; index++)
            if (before[index].ChallengeId != after[index].ChallengeId) return false;
        return true;
    }

    /// <summary>
    /// The spell a caller means when it names a slot on the loadout bar.
    /// </summary>
    /// <remarks>
    /// A runtime spell instance has an id the game never shows and the asset catalog never
    /// publishes, so it has no place on the wire at all. The bar itself is what the player sees and
    /// what the screen numbers, and a refusal names both what was asked for and what is there.
    /// </remarks>
    internal static bool TryEquippedSpellSlot(
        GameWorldState world,
        int slot,
        out Guid spellInstanceId,
        out string reason)
    {
        spellInstanceId = Guid.Empty;
        var slots = world.SpellSlots;
        if (slots.Count == 0)
        {
            reason = "The loadout bar has no slots yet.";
            return false;
        }
        if (slot < 1 || slot > slots.Count)
        {
            reason = "There is no slot " + slot + "; the loadout bar has slots 1 to " +
                slots.Count + ".";
            return false;
        }
        var value = slots[GameMcpSlotNumbering.Index(slot)];
        if (!value.Occupied || value.SpellInstanceId == Guid.Empty)
        {
            reason = "Slot " + slot + " is empty; " + OccupiedSlotsSentence(slots);
            return false;
        }
        spellInstanceId = value.SpellInstanceId;
        reason = string.Empty;
        return true;
    }

    private static string OccupiedSlotsSentence(PublicationTable<WorldSpellSlot> slots)
    {
        var occupied = new System.Text.StringBuilder();
        var count = 0;
        for (var index = 0; index < slots.Count; index++)
        {
            if (!slots[index].Occupied) continue;
            if (count > 0) occupied.Append(", ");
            occupied.Append(GameMcpSlotNumbering.Wire(slots[index].SlotIndex)
                .ToString(CultureInfo.InvariantCulture));
            count++;
        }
        return count == 0
            ? "no slot on the bar holds a spell."
            : count == 1
                ? "the one spell you have equipped is in slot " + occupied + "."
                : "the spells you have equipped are in slots " + occupied + ".";
    }

    /// <summary>
    /// The player loadout a caller means when it names a position on the loadout bar.
    /// </summary>
    internal static bool TryPlayerLoadout(
        GameWorldState world,
        int position,
        out Guid loadoutId,
        out string reason)
    {
        loadoutId = Guid.Empty;
        var loadouts = world.PlayerLoadouts;
        if (loadouts.Count == 0)
        {
            reason = "This save has no player loadouts.";
            return false;
        }
        if (position < 1 || position > loadouts.Count)
        {
            reason = "There is no loadout " + position + "; you have loadouts 1 to " +
                loadouts.Count + ".";
            return false;
        }
        loadoutId = loadouts[GameMcpSlotNumbering.Index(position)].EntityId;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The snapshot list a caller means when it names the Equipment or Alchemy section.
    /// </summary>
    internal static bool TrySnapshotList(
        GameWorldState world,
        string section,
        out Guid listId,
        out string reason)
    {
        listId = Guid.Empty;
        var wanted = string.Equals(section, "alchemy", StringComparison.Ordinal)
            ? WorldSnapshotLoadoutKind.Alchemy
            : WorldSnapshotLoadoutKind.Equipment;
        for (var index = 0; index < world.SnapshotLoadouts.Count; index++)
        {
            if (world.SnapshotLoadouts[index].Kind != wanted) continue;
            listId = world.SnapshotLoadouts[index].EntityId;
            reason = string.Empty;
            return true;
        }
        reason = "The game is not showing the " +
            (wanted == WorldSnapshotLoadoutKind.Alchemy ? "Alchemy" : "Equipment") +
            " snapshots right now.";
        return false;
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

    private static bool ChallengeSelected(GameWorldState world, Guid challengeId)
    {
        var selected = world.ChallengeContext.Selected;
        for (var index = 0; index < selected.Count; index++)
            if (selected[index].ChallengeId == challengeId) return true;
        return false;
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

    internal static GameMcpValue ProjectEntityState(
        GameWorldState world,
        string categoryName,
        object row)
    {
        if (!TryCategory(categoryName, out var category, out _))
            return new GameMcpDomainValue(row);
        return ProjectRow(world, category, row);
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

    internal static GameMcpValue PostStateUnavailable(string reasonCode, string reason) =>
        new JObject
        {
            ["postStateUnavailable"] = new JObject
            {
                ["reasonCode"] = reasonCode,
                ["reason"] = reason,
            },
        }.Freeze();

    /// <summary>
    /// An unobservable post-state still names what was acted on and how much was asked for. Being
    /// honest that the settled world proves nothing is right; dropping the identity with it made
    /// two commits on two different recipes byte-identical.
    /// </summary>
    private static GameMcpValue PostStateUnavailable(
        Guid uuid,
        int requestedAmount,
        string reasonCode,
        string reason)
    {
        var result = new JObject
        {
            ["uuid"] = uuid.ToString("D"),
            ["postStateUnavailable"] = new JObject
            {
                ["reasonCode"] = reasonCode,
                ["reason"] = reason,
            },
        };
        if (requestedAmount > 0) result["requestedAmount"] = requestedAmount;
        return result.Freeze();
    }

    /// <summary>
    /// One search hit before it is projected: enough to sort the whole result set, and nothing else.
    /// </summary>
    private readonly struct GameMcpSearchHit
    {
        internal GameMcpSearchHit(GameMcpSearchTier tier, Guid identity, int category, int row)
        {
            Tier = tier;
            Identity = identity;
            Category = category;
            Row = row;
        }

        internal GameMcpSearchTier Tier { get; }
        internal Guid Identity { get; }
        internal int Category { get; }
        internal int Row { get; }
    }

    internal static JObject Search(
        GameMcpFrameContext state,
        string query,
        int offset,
        int limit,
        string categoryName = "",
        string stateFilter = "",
        bool limitFromCaller = true)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return NotAvailable(publication, "query_required", "query must not be empty");
        if (offset < 0)
            return NotAvailable(publication, "invalid_offset", "offset must be zero or greater");
        if (limit <= 0 || limit > MaximumPageSize)
        {
            return NotAvailable(
                publication,
                "invalid_limit",
                "limit must be between 1 and " +
                MaximumPageSize.ToString(CultureInfo.InvariantCulture));
        }

        var scope = (categoryName ?? string.Empty).Trim();
        GameMcpWorldCategory? only = null;
        if (scope.Length > 0)
        {
            if (!TryCategory(scope, out var scoped, out var reason))
                return NotAvailable(publication, "unknown_category", reason);
            if (!IsSearchable(scoped))
            {
                return NotAvailable(
                    publication,
                    "category_not_searchable",
                    "category " + scoped.Name + " has no stable entity identity of its own, so " +
                    "search cannot address its rows; read it with world_list");
            }
            only = scoped;
        }

        var wanted = (stateFilter ?? string.Empty).Trim();
        if (wanted.Length > 0 &&
            wanted is not (GameMcpListColumns.Locked or GameMcpListColumns.Available
                or GameMcpListColumns.Completed))
        {
            return NotAvailable(
                publication,
                "invalid_state",
                "state must be one of " + GameMcpListColumns.Locked + ", " +
                GameMcpListColumns.Available + ", " + GameMcpListColumns.Completed);
        }

        // Search is deliberately an entity-catalog surface. Composite diagnostic categories are
        // readable through world_list, where their full identity and localized partiality survive.
        // One entity is one match however many categories publish it: identity is deduplicated
        // before the sort, so a repeat can never eat a slot the caller paid for.
        var world = publication.Snapshot;
        var keywords = GameMcpKeywordIndex.Build(world);
        var hits = new List<GameMcpSearchHit>();
        var keywordHits = new List<KeyValuePair<string, int>>();
        var seen = new HashSet<Guid>();
        var unavailableCategories = new JArray();
        for (var categoryIndex = 0; categoryIndex < Categories.Length; categoryIndex++)
        {
            var category = Categories[categoryIndex];
            if (!IsSearchable(category)) continue;
            if (only is not null && !ReferenceEquals(category, only)) continue;
            var availability = Availability(world, category);
            if (!availability.Available)
            {
                unavailableCategories.Add(new JObject
                {
                    ["category"] = category.Name,
                    ["reason"] = availability.Reason,
                });
                continue;
            }
            var count = category.Count(world);
            for (var rowIndex = 0; rowIndex < count; rowIndex++)
            {
                var row = category.Row(world, rowIndex);
                if (!category.TryIdentity(row, out var identity)) continue;
                if (wanted.Length > 0 &&
                    !string.Equals(SearchLifecycle(row), wanted, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!TryTier(world, category, identity, keywords, normalized, out var tier))
                    continue;
                if (!seen.Add(identity)) continue;
                hits.Add(new GameMcpSearchHit(tier, identity, categoryIndex, rowIndex));
                CountKeywordHits(keywords.Words(identity), normalized, keywordHits);
            }
        }

        // Relevance first, then the id — the thing called that, then the things filed under that
        // word, then the heap the word merely names, and inside each band an order that does not
        // move between two pages of one result.
        hits.Sort(static (left, right) =>
        {
            var tier = ((int)left.Tier).CompareTo((int)right.Tier);
            return tier != 0 ? tier : left.Identity.CompareTo(right.Identity);
        });

        // A small result is the whole answer or it is not an answer, exactly as a small category is.
        var whole = !limitFromCaller && hits.Count <= WholeCategoryRows;
        var rows = new JArray();
        var estimatedBytes = 128;
        for (var index = offset; index < hits.Count; index++)
        {
            if (rows.Count >= limit && !whole) break;
            var hit = hits[index];
            var category = Categories[hit.Category];
            var row = category.Row(world, hit.Row);
            var projected = ProjectSearchMatch(
                category, hit.Identity, keywords.Line(hit.Identity));
            var local = LocalizedRequirementImplications(
                world, new HashSet<Guid> { hit.Identity });
            var localOffers = LocalizedDiscoveryOfferImplications(
                world, new HashSet<Guid> { hit.Identity });
            var matchBytes = EstimateListRowBytes(world, category, row, projected);
            GameMcpValue match;
            if (local.Count == 0 && localOffers.Count == 0)
            {
                match = projected;
            }
            else
            {
                var incomplete = new JObject
                {
                    ["status"] = "not_available",
                    ["code"] = local.Count > 0
                        ? "entity_data_incomplete"
                        : "discovery_offer_read_incomplete",
                    ["reason"] = local.Count > 0
                        ? "this match has incomplete published requirement evidence"
                        : "this discovery tree has an offer absent from the published entity rows",
                    ["partialRow"] = projected,
                };
                if (local.Count > 0) incomplete["implicatedSkippedRows"] = local;
                if (localOffers.Count > 0) incomplete["implicatedOffers"] = localOffers;
                match = incomplete.Freeze();
                matchBytes = checked(
                    matchBytes + 192 + local.Count * 128 + localOffers.Count * 128);
            }
            if (rows.Count > 0 && !whole &&
                estimatedBytes + matchBytes > MaximumListResponseBytes)
            {
                break;
            }
            estimatedBytes += matchBytes;
            rows.Add(match);
        }

        var result = Envelope(publication);
        result["total"] = hits.Count;
        if (unavailableCategories.Count > 0)
            result["unavailableCategories"] = unavailableCategories;
        var summary = KeywordHitSummary(keywordHits);
        if (summary.Length > 0) result["keywordHits"] = summary;
        if (rows.Count == 0)
            result["columns"] = GameMcpEntityWireNormalizer.WireColumns(SearchColumns);
        result["rows"] = rows;
        if (offset + rows.Count < hits.Count) result["nextOffset"] = offset + rows.Count;
        return result;
    }

    /// <summary>
    /// Whether search can address this category's rows at all: it indexes entities, and a composite
    /// diagnostic row has no identity of its own to return.
    /// </summary>
    private static bool IsSearchable(GameMcpWorldCategory category) =>
        string.Equals(category.IdentityMode, "stable_entity_uuid", StringComparison.Ordinal);

    /// <summary>
    /// The lifecycle word this row says on its own list page, or nothing where its category has no
    /// lifecycle to say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This reads the word the row's own list page says and never derives one of its own, so the
    /// filter's reach is a consequence of which categories carry the column rather than a second
    /// list maintained beside them. Every category the player can meet a locked thing in now carries
    /// it, which is what the filter reaches: the three purchasables plus alchemy recipes, glyphs,
    /// rituals, plot nodes and challenges.
    /// </para>
    /// <para>
    /// A category with no lifecycle model still does not match a state filter, and is still not
    /// excluded from an unfiltered search — inventing a word here for a row whose page never speaks
    /// one is exactly what one vocabulary per fact forbids. <c>challenges</c> is now read, because
    /// it now has a lifecycle to read: its run words moved to <c>run</c>, which this never consults.
    /// </para>
    /// </remarks>
    private static string SearchLifecycle(object row) => row switch
    {
        WorldUpgrade upgrade => UpgradeState(in upgrade),
        WorldResearch research => ResearchLifecycle(in research),
        WorldStructure structure => StructureState(in structure),
        WorldAlchemyRecipe recipe => AlchemyRecipeState(in recipe),
        WorldGlyph glyph => GlyphState(in glyph),
        WorldRitual ritual => RitualState(in ritual),
        WorldPlotNode plot => PlotNodeState(in plot),
        WorldChallenge challenge => ChallengeLifecycle(in challenge),
        _ => string.Empty,
    };

    /// <summary>
    /// Why this entity answers the query, or that it does not. Case-insensitive substring, which is
    /// the rule the game's own search box uses: <c>FilterVariable.MatchesSearchStrings</c> lowercases
    /// both sides and asks <c>Contains</c>, with no tokenising and no whole-word test, so a caller
    /// who learned what "cant" finds in the game learns nothing new here.
    /// </summary>
    private static bool TryTier(
        GameWorldState world,
        GameMcpWorldCategory category,
        Guid identity,
        GameMcpKeywordIndex keywords,
        string query,
        out GameMcpSearchTier tier)
    {
        if (GameMcpEntityCatalog.MatchesIdentity(world.EntityIdentities, identity, query))
        {
            tier = GameMcpSearchTier.Name;
            return true;
        }
        var words = keywords.Words(identity);
        for (var index = 0; index < words.Count; index++)
        {
            if (words[index].IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            tier = GameMcpSearchTier.Keyword;
            return true;
        }
        if (category.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            category.RowTypeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            category.ExpectedNativeType.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            tier = GameMcpSearchTier.Category;
            return true;
        }
        tier = GameMcpSearchTier.Name;
        return false;
    }

    private static void CountKeywordHits(
        IReadOnlyList<string> words,
        string query,
        List<KeyValuePair<string, int>> counts)
    {
        for (var index = 0; index < words.Count; index++)
        {
            if (words[index].IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var found = false;
            for (var slot = 0; slot < counts.Count; slot++)
            {
                if (!string.Equals(counts[slot].Key, words[index], StringComparison.Ordinal))
                    continue;
                counts[slot] = new KeyValuePair<string, int>(
                    counts[slot].Key, counts[slot].Value + 1);
                found = true;
                break;
            }
            if (!found) counts.Add(new KeyValuePair<string, int>(words[index], 1));
        }
    }

    /// <summary>
    /// How the whole result set splits across the keywords the query itself hit, when it split at
    /// all.
    /// </summary>
    /// <remarks>
    /// The line answers the question a cross-category search actually raises — a word the player
    /// heard once turns out to name two families, and which one they meant is the next thing they
    /// need. It counts only the keywords the query matched, so it is short by construction rather
    /// than by a cap, and one keyword is no split at all: a line saying the count already on the
    /// count line is a line that says nothing.
    /// </remarks>
    private static string KeywordHitSummary(List<KeyValuePair<string, int>> counts)
    {
        if (counts.Count < 2) return string.Empty;
        counts.Sort(static (left, right) =>
        {
            var byCount = right.Value.CompareTo(left.Value);
            return byCount != 0
                ? byCount
                : string.Compare(left.Key, right.Key, StringComparison.Ordinal);
        });
        var line = new StringBuilder();
        for (var index = 0; index < counts.Count; index++)
        {
            if (index > 0) line.Append(", ");
            line.Append(counts[index].Key).Append('=')
                .Append(counts[index].Value.ToString(CultureInfo.InvariantCulture));
        }
        return line.ToString();
    }

    internal static JObject NotAvailableWithoutWorld(GameMcpFrameContext state, string code, string reason)
    {
        var result = new JObject
        {
            ["status"] = "not_available",
            ["code"] = code,
            ["reason"] = reason,
            ["lifecycleState"] = state.LifecycleState.ToString(),
        };
        return result;
    }

    /// <summary>
    /// The code and sentence for a world read with no live world behind it. A lifecycle that is not
    /// playing is the answer whenever it applies: it names the state <c>game_probe</c> reports, so a
    /// caller comparing the two tools sees one fact rather than two beliefs.
    /// </summary>
    private static void WorldUnavailable(
        GameMcpFrameContext state,
        out string code,
        out string reason)
    {
        if (GameLifecycleUnavailability.TryDescribe(state.LifecycleState, out code, out reason))
            return;
        code = "world_not_published";
        reason = state.RuntimeNotAvailableReason.Length == 0
            ? "the world collector has not published a captured world yet"
            : state.RuntimeNotAvailableReason;
    }

    /// <summary>
    /// Whether the pinned publication is a reading of a live run. Generation 1 with no collection
    /// timestamp is the flushed state a lifecycle boundary leaves behind, and it is the same shape
    /// the publisher is constructed in, so "before the first run" and "after the run ended" answer
    /// identically.
    /// </summary>
    internal static bool IsWorldPublished(GameMcpFrameContext state) =>
        state.World is not null &&
        state.World.Generation.Value > 1 &&
        state.World.Snapshot.CollectedAtUtcTicks > 0;

    internal static JObject WithEnvelope(GameMcpFrameContext state, JObject payload)
    {
        if (IsWorldPublished(state))
        {
            var envelope = Envelope(state.World!);
            envelope.CopyFrom(payload);
            return envelope;
        }

        var result = new JObject();
        result.CopyFrom(payload);
        return result;
    }

    internal static GameMcpValue WithEnvelope(GameMcpFrameContext state, GameMcpValue payload)
    {
        if (payload is not GameMcpObject objectPayload)
            throw new ArgumentException("An MCP response payload must be an object.", nameof(payload));
        var result = new JObject();
        result.CopyFrom(objectPayload);
        return result.Freeze();
    }

    private static bool TryWorld(
        GameMcpFrameContext state,
        out WorldPublication<GameWorldState> publication,
        out JObject unavailable)
    {
        if (IsWorldPublished(state))
        {
            publication = state.World!;
            unavailable = null!;
            return true;
        }

        publication = null!;
        WorldUnavailable(state, out var code, out var reason);
        unavailable = NotAvailableWithoutWorld(state, code, reason);
        return false;
    }

    private static JObject Envelope(WorldPublication<GameWorldState> publication)
    {
        return new JObject();
    }

    private static JObject NotAvailable(
        WorldPublication<GameWorldState> publication,
        string code,
        string reason)
    {
        var result = Envelope(publication);
        result["status"] = "not_available";
        result["code"] = code;
        result["reason"] = reason;
        return result;
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
                ["category"] = Normalize(category.Category),
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
        if (TryLocalizedRequirementFailures(world, out var implicated, out _) &&
            implicated.Length > 0)
        {
            // The overview is read every few calls and this evidence is the same bytes every time:
            // which condition classes the collector cannot localize is a fact of the build. The
            // summary says how many and of what, names the entities that carry them, and points at
            // the read that holds every leaf — world_get on an implicated owner answers
            // entity_data_incomplete with the full implicatedSkippedRows.
            var owners = new List<string>();
            var seenOwners = new HashSet<Guid>();
            var nativeTypes = new List<string>();
            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < implicated.Length; index++)
            {
                var leaf = implicated[index];
                if (seenOwners.Add(leaf.OwnerId))
                {
                    owners.Add(
                        GameMcpEntityHandle.Name(leaf.OwnerId, world.EntityIdentities) + " " +
                        GameMcpEntityHandle.Format(leaf.OwnerId));
                }
                if (seenTypes.Add(leaf.ConditionTypeName)) nativeTypes.Add(leaf.ConditionTypeName);
            }
            result["gap"] =
                implicated.Length.ToString(CultureInfo.InvariantCulture) +
                " requirement leaves of type " + string.Join(", ", nativeTypes) +
                " could not be localized; world_get on " + string.Join(", ", owners) +
                " returns every leaf";
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
                TryPurchaseAffordability(world, structure.EntityId, out var affordable) &&
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
            if (upgrade.Reading.Available && !upgrade.IsExhausted &&
                TryPurchaseAffordability(world, upgrade.EntityId, out var affordable) &&
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

    private static int CountDiscoveredAlchemy(GameWorldState world)
    {
        var count = 0;
        for (var index = 0; index < world.AlchemyRecipes.Count; index++)
            if (world.AlchemyRecipes[index].Discovered) count++;
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

    private static JObject DescribeCategory(GameWorldState world, GameMcpWorldCategory category)
    {
        var availability = Availability(world, category);
        var result = new JObject
        {
            ["category"] = category.Name,
            ["count"] = CategoryRowCount(world, category),
            ["available"] = availability.Available,
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
    private static int CategoryRowCount(GameWorldState world, GameMcpWorldCategory category)
    {
        if (!string.Equals(category.Name, "mastery-experience", StringComparison.Ordinal))
            return category.Count(world);
        var keys = new List<(MasteryExperienceDomain Domain, Guid SourceId, int SourceMastery)>();
        var counts = new List<int>();
        MasteryExperienceSources(world, keys, counts);
        return keys.Count;
    }

    private static GameMcpCategoryAvailability Availability(
        GameWorldState world,
        GameMcpWorldCategory category)
    {
        for (var requiredIndex = 0;
             requiredIndex < category.ReportCategories.Length;
             requiredIndex++)
        {
            var reportName = category.ReportCategories[requiredIndex];
            var found = false;
            for (var index = 0; index < world.CollectionCategories.Count; index++)
            {
                var report = world.CollectionCategories[index];
                if (!string.Equals(
                        Normalize(report.Category),
                        reportName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                found = true;
                if (report.Outcome == WorldCategoryOutcome.Unavailable)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName + " unavailable"
                            : report.FirstFailure);
                }
                if (report.Skipped > 0)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        "collection is partial: " +
                        report.Skipped.ToString(CultureInfo.InvariantCulture) +
                        " native rows were skipped from " + reportName +
                        "; first failure: " +
                        (report.FirstFailure.Length == 0
                            ? "the collector did not publish a failure reason"
                            : report.FirstFailure));
                }
                break;
            }
            if (!found)
            {
                return new GameMcpCategoryAvailability(
                    false,
                    "the publication carries no collection report for " + reportName);
            }
        }

        for (var blockerIndex = 0;
             blockerIndex < category.FailureOnlyReportCategories.Length;
             blockerIndex++)
        {
            var reportName = category.FailureOnlyReportCategories[blockerIndex];
            for (var index = 0; index < world.CollectionCategories.Count; index++)
            {
                var report = world.CollectionCategories[index];
                if (!string.Equals(
                        Normalize(report.Category),
                        reportName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (report.Outcome == WorldCategoryOutcome.Unavailable ||
                    report.Skipped > 0)
                {
                    return new GameMcpCategoryAvailability(
                        false,
                        report.FirstFailure.Length == 0
                            ? "the collector reported " + reportName +
                              " degraded without a failure reason"
                            : report.FirstFailure);
                }
                break;
            }
        }

        return new GameMcpCategoryAvailability(true, string.Empty);
    }

    private static GameMcpValue ProjectRow(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row) =>
        WithOwnIdentity(category, ProjectRowFields(world, category, row));

    private static GameMcpValue ProjectRowFields(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row) =>
        row is WorldResource resource
            ? ProjectResource(world, in resource)
            : row is WorldStructure structure
            ? ProjectStructure(in structure)
            : row is WorldUpgrade upgrade
            ? ProjectUpgrade(world, in upgrade)
            : row is WorldPurchaseCost purchaseCost
            ? ProjectPurchaseCost(world, in purchaseCost).Freeze()
            : row is WorldCraftingRecipe craftingRecipe
            ? ProjectCraftingRecipe(world, in craftingRecipe)
            : row is WorldDiscoveryTree tree
            ? ProjectDiscoveryTree(world, in tree)
            : row is WorldSpellRecipe spellRecipe
            ? ProjectSpellRecipe(world, in spellRecipe)
            : row is WorldAlchemyRecipe alchemyRecipe
            ? ProjectAlchemyRecipe(world, in alchemyRecipe)
            : row is WorldEquipment equipment
            ? ProjectEquipment(world, in equipment)
            : row is WorldAlchemyInstance alchemyInstance
            ? ProjectAlchemyInstance(world, in alchemyInstance)
            : row is WorldEquipmentType equipmentType
            ? ProjectEquipmentType(world, in equipmentType)
            : row is WorldResourceType resourceType
            ? ProjectResourceType(world, in resourceType)
            : row is WorldChallenge challenge
            ? ProjectChallenge(world, in challenge)
            : row is WorldGlyph glyph
            ? ProjectGlyph(world, in glyph)
            : row is WorldRitual ritual
            ? ProjectRitual(world, in ritual)
            : row is WorldTimeRune timeRune
            ? ProjectTimeRune(world, in timeRune)
            : row is WorldSpellSlot spellSlot
            ? ProjectSpellSlot(world, in spellSlot)
            : row is WorldTargetingRequest targeting
            ? ProjectTargeting(world, in targeting)
            : row is WorldConsumable consumable
            ? ProjectConsumable(world, in consumable)
            : row is WorldResearch research
            ? ProjectResearch(world, in research)
            : row is WorldConceptRecipe conceptRecipe
            ? ProjectConceptRecipe(world, in conceptRecipe)
            : row is WorldCraftingStation station
            ? ProjectCraftingStation(world, in station)
            : row is WorldPlayerLoadout playerLoadout
            ? ProjectPlayerLoadout(world, in playerLoadout)
            : row is WorldSnapshotLoadout snapshotLoadout
            ? ProjectSnapshotLoadout(world, in snapshotLoadout)
            : row is WorldHarvestElement harvestElement
            ? ProjectHarvestElement(world, in harvestElement)
            : row is WorldPlotAction plotAction
            ? ProjectPlotAction(world, in plotAction)
            : row is WorldActionQueueSlot processingSlot
            ? ProjectAgromancyProcessing(world, in processingSlot)
            : new GameMcpProjectedDomainValue(
                row,
                category.ScanFields,
                category.Name,
                category.ExpectedNativeType,
                tableRow: false);

    /// <summary>
    /// An upgrade with no ceiling has no ceiling to report: the game marks that with a negative
    /// <c>maxLevel</c>, which becomes the word for having none rather than a plausible number.
    /// <c>world_get</c> shares the list's vocabulary, so it says the lifecycle in the same word the
    /// page does rather than in a second grammar of its own.
    /// </summary>
    private static GameMcpValue ProjectUpgrade(GameWorldState world, in WorldUpgrade upgrade)
    {
        var result = new JObject
        {
            ["entityId"] = upgrade.EntityId.ToString("D"),
            ["category"] = "upgrades",
            ["screen"] = UpgradeScreen(world, upgrade.EntityId),
            ["state"] = UpgradeState(in upgrade),
            ["level"] = upgrade.Reading.Level,

            // Nothing developing is a fact about the upgrade, not a missing reading, so zero ships.
            ["queuedLevels"] = upgrade.Reading.QueuedLevels,
            ["maximum"] = UpgradeCeiling(in upgrade),
        };
        if (upgrade.IsDeveloping)
            result["developmentProgress"] = upgrade.DevelopmentProgress;
        return result.Freeze();
    }

    private static GameMcpValue ProjectStructure(in WorldStructure structure)
    {
        var enabled = !structure.Reading.Disabled;
        var toggle = new JObject
        {
            ["available"] = structure.Reading.Unlocked,
        };
        if (structure.Reading.Unlocked)
            toggle["next"] = enabled ? "disable" : "enable";
        else
            toggle["reasonCode"] = "not_available";
        var result = new JObject
        {
            ["entityId"] = structure.EntityId.ToString("D"),
            ["category"] = "structures",

            // The badge UIStructureItem renders is Utils.BeautifyInt(StructureSO.GetBaseLevel()),
            // which is what Reading.Level captures through GetPurchaseLevel; while levels are
            // developing the same badge switches to "+N" from GetQueuedQuantity(). Both are counts,
            // not magnitudes: routing them through the game's large-number renderer rounded a
            // 2,136-level attribute to 2.14e3 and made the wire disagree with the screen by up to
            // five levels. Publishing level plus queued under one name would put a number on the
            // wire that no screen shows.
            ["level"] = structure.Reading.Level.ToInt(),
            ["queuedLevels"] = structure.Reading.QueuedLevels.ToInt(),

            // Two words, and never a third: a structure has no ceiling to reach.
            ["state"] = StructureState(in structure),
            ["enabled"] = enabled,
            ["toggle"] = toggle,
        };
        return result.Freeze();
    }

    private static GameMcpValue ProjectHarvestElement(
        GameWorldState world,
        in WorldHarvestElement element)
    {
        var result = new JObject
        {
            ["entityId"] = element.EntityId.ToString("D"),
            ["category"] = "agromancy-elements",
            ["masteryLevel"] = element.MasteryLevel,
            ["masteryXp"] = new GameMcpDomainValue(element.MasteryXp),
        };
        for (var index = 0; index < world.HarvestResources.Count; index++)
        {
            var harvestResource = world.HarvestResources[index];
            if (harvestResource.ElementId != element.EntityId) continue;
            var resource = harvestResource.Resource;
            var output = new JObject
            {
                ["amount"] = new GameMcpDomainValue(
                    WorldResourceCoordinate.DisplayAmount(in resource)),
                ["netRatePerSecond"] = new GameMcpDomainValue(resource.TrueRate),
            };
            if (resource.IsCapped)
            {
                output["capacity"] = new GameMcpDomainValue(resource.Reading.Capacity);
                output["atCapacity"] = resource.IsAtCapacity;
            }
            result["output"] = output;
            break;
        }
        if (!TryFindHarvestElementControl(
                world.HarvestElementControls, element.EntityId, out var control))
            return result.Freeze();
        result["active"] = control.Active;
        result["addElement"] = ProjectHarvestElementDecision(world, in control);
        result["removeElement"] = RemoveElementDecision(in control);

        var actions = new JArray();
        for (var index = 0; index < world.HarvestActionControls.Count; index++)
        {
            var action = world.HarvestActionControls[index];
            if (action.ElementId != element.EntityId || !action.Visible) continue;
            var row = new JObject
            {
                ["uuid"] = action.ActionId.ToString("D"),
                ["active"] = action.Active,
                ["maximumAmount"] = action.Maximum,
                ["add"] = HarvestActionAddDecision(world, in action),
                ["remove"] = HarvestActionRemoveDecision(in action),
            };
            actions.Add(row);
        }
        if (actions.Count > 0) result["actions"] = actions;
        return result.Freeze();
    }

    private static GameMcpValue ProjectPlotAction(
        GameWorldState world,
        in WorldPlotAction action) =>
        ProjectPlotAction(world, in action, asRow: false);

    /// <summary>
    /// One plot-and-action pair, as a detail block or as a row of the table.
    /// </summary>
    /// <remarks>
    /// The two decisions are the same decisions either way; what differs is how much room the
    /// answer has. A detail reader asked about this pair and gets the whole verdict — the sentence,
    /// the repair to call, the remaining-instance count. A row of twenty gets the word, because the
    /// alternative is the same paragraph twenty times in a column three characters wide.
    /// </remarks>
    private static GameMcpValue ProjectPlotAction(
        GameWorldState world,
        in WorldPlotAction action,
        bool asRow)
    {
        var active = PlotActionQuantity(
            world.ActionQueueSlots, action.PlotNodeId, action.PlotNodeActionId);
        var add = ProjectPlotActionDecision(world, in action, active);
        var remove = PlotActionRemoveDecision(active);
        return new JObject
        {
            ["plot"] = EntityReference(world, action.PlotNodeId),
            ["action"] = EntityReference(world, action.PlotNodeActionId),
            ["active"] = active,
            ["add"] = asRow ? DecisionWord(add) : (object)add,
            ["remove"] = asRow ? DecisionWord(remove) : (object)remove,
        }.Freeze();
    }

    /// <summary>
    /// A decision block as the one word a cell has room for: <c>yes</c>, or what stands in the way.
    /// </summary>
    private static object DecisionWord(JObject decision)
    {
        if (decision["available"] is true) return GameMcpListColumns.Yes;
        var code = (decision["reasonCode"] ?? decision["code"]) as string;
        return string.IsNullOrEmpty(code)
            ? GameMcpListColumns.No
            : GameMcpListColumns.Word(code!);
    }

    /// <summary>
    /// Nothing running is not a refusal. Left bare, the no-bare-no backstop stamped this
    /// <c>native_rejected</c> and told a caller the game had refused something it never asked for;
    /// every sibling names its own axis, and this is the same axis they name.
    /// </summary>
    private static JObject PlotActionRemoveDecision(int active)
    {
        var result = new JObject { ["available"] = active > 0 };
        if (active <= 0) result["reasonCode"] = "not_active";
        return result;
    }

    private static GameMcpValue ProjectAgromancyProcessing(
        GameWorldState world,
        in WorldActionQueueSlot slot)
    {
        // The occupant columns say `empty` where there is no occupant, which is what the separate
        // `empty` flag used to say — one bit on five columns. What fills the slot answers it.
        //
        // Whether the occupant is under way rather than merely present — the game's `IsEngaged()` —
        // is not a column. It is the same fact as a spell's `casting`: a flag the game turns over on
        // its own between two reads of one scan, so a page of it plans nothing. What the slot holds
        // and how much of it are the standing facts, and they are here.
        var queueFound = WorldLookup.TryFind(world.ActionQueues, slot.QueueId, out var queue);
        var result = new JObject
        {
            ["slot"] = GameMcpSlotNumbering.Wire(slot.Index),
            ["capacity"] = queueFound ? queue.SlotCount : (object)GameMcpListColumns.Unreadable,
            ["used"] = queueFound ? queue.UsedSlots : (object)GameMcpListColumns.Unreadable,
            ["plot"] = slot.Empty
                ? GameMcpListColumns.Empty
                : EntityReference(world, slot.PlotNodeId),
            ["action"] = slot.Empty
                ? GameMcpListColumns.Empty
                : EntityReference(world, slot.PlotNodeActionId),
            ["amount"] = slot.Empty ? (object)GameMcpListColumns.Empty : slot.Quantity,
        };
        return result.Freeze();
    }

    private static JObject ProjectPlotActionDecision(
        GameWorldState world,
        in WorldPlotAction action,
        int active)
    {
        var result = new JObject();
        var reason = string.Empty;
        var available = true;
        if (action.Reading.OfferedCount != 1)
        {
            available = false;
            reason = action.Reading.OfferedCount == 0
                ? "not_offered"
                : "ambiguous_offer";
        }
        else if (action.Reading.PrerequisiteEvidence !=
                 PlotActionPrerequisiteEvidence.NativeLatchedTrue)
        {
            // Not "unavailable": the game latches this prerequisite only when the action is
            // attempted, so the read itself is what is missing. Said with the suite's one word for
            // a fact it cannot supply, next to the one word every other decision uses for the fact
            // it can.
            result["status"] = "not_available";
            result["code"] = "prerequisite_unverified";
            result["reason"] =
                "The game only checks this action's prerequisite when the action is started, " +
                "so whether it can be queued cannot be read ahead of time.";
            result["checkWith"] = "game_agromancy add_plot_action";
            return result;
        }
        else if (!action.ElementCostKnown)
        {
            available = false;
            reason = "cost_unavailable";
        }
        else if (!action.HasEnoughForOneInstance || action.MaximumRemainingInstances <= 0)
        {
            available = false;
            reason = "plot_quantity_insufficient";
        }
        else if (active == 0 &&
                 (!WorldLookup.TryFind(world.ActionQueues,
                      KnownEntities.ActivePlotNodeActions.Uuid, out var queue) ||
                  !queue.Consistent || !queue.HasEmptySlot))
        {
            available = false;
            reason = "plot_action_list_full";
        }
        result["available"] = available;
        if (!available)
        {
            result["reasonCode"] = reason;
            return result;
        }
        // The game's own remaining-instance count, and nothing else. Folding the tool schema's
        // per-call ceiling in here produced a third number that was neither bound: with the list
        // nearly full it under-reported what the game admits, and the schema's ceiling is a
        // per-call limit rather than a running budget in the first place.
        result["maximumAmount"] = action.MaximumRemainingInstances;
        result["plotQuantityCost"] = action.ElementCost;
        return result;
    }

    private static JObject ProjectHarvestElementDecision(
        GameWorldState world,
        in WorldHarvestElementControl control)
    {
        var result = new JObject
        {
            ["available"] = control.AddAvailable,
        };
        if (!control.AddAvailable)
        {
            result["reasonCode"] = !control.Visible
                ? "element_unavailable"
                : control.MaximumAdditional <= 0
                    ? "capacity_exhausted"
                    : !control.ListSpaceAvailable
                        ? "harvest_list_full"
                        : "unaffordable";
            return result;
        }
        result["maximumAmount"] = control.MaximumAdditional;
        result["affordable"] = true;
        var costs = ProjectHarvestLifecycleCosts(
            world, control.ElementId, Guid.Empty,
            WorldHarvestLifecycleCostKind.ElementUsage);
        if (costs.Count > 0) result["costs"] = costs;
        return result;
    }

    /// <summary>
    /// A false availability is a decision, not a flag. `addAvailable: false` beside nothing left a
    /// caller with no axis to branch on and no way to tell a temporary no from a permanent one,
    /// which is the stall the sentence rule exists to prevent.
    /// </summary>
    private static JObject HarvestActionAddDecision(
        GameWorldState world,
        in WorldHarvestActionControl action)
    {
        var result = new JObject { ["available"] = action.AddAvailable };
        if (!action.AddAvailable)
        {
            result["reasonCode"] = action.Active >= action.Maximum
                ? "capacity_exhausted"
                : "harvest_list_full";
            return result;
        }
        var costs = ProjectHarvestLifecycleCosts(
            world, action.ElementId, action.ActionId,
            WorldHarvestLifecycleCostKind.NextActionDrain);
        if (costs.Count > 0) result["nextDrain"] = costs;
        return result;
    }

    private static JObject HarvestActionRemoveDecision(in WorldHarvestActionControl action)
    {
        var result = new JObject { ["available"] = action.RemoveAvailable };
        if (!action.RemoveAvailable) result["reasonCode"] = "not_active";
        return result;
    }

    private static JObject RemoveElementDecision(in WorldHarvestElementControl control)
    {
        var result = new JObject { ["available"] = control.RemoveAvailable };
        if (!control.RemoveAvailable)
            result["reasonCode"] = control.Active > 0 ? "not_available" : "not_active";
        return result;
    }

    private static JArray ProjectHarvestLifecycleCosts(
        GameWorldState world,
        Guid elementId,
        Guid actionId,
        WorldHarvestLifecycleCostKind kind)
    {
        var result = new JArray();
        for (var index = 0; index < world.HarvestLifecycleCosts.Count; index++)
        {
            var cost = world.HarvestLifecycleCosts[index];
            if (cost.ElementId != elementId || cost.ActionId != actionId || cost.Kind != kind)
                continue;
            var row = new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(
                    PlayerFacingCost(world, cost.ResourceId, cost.Amount)),
            };
            if (TryHarvestSpendableAmount(world, cost.ResourceId, out var amount))
            {
                row["amount"] = new GameMcpDomainValue(amount);
                row["affordable"] = CanAfford(
                    world, cost.ResourceId, cost.Amount, amount);
            }
            result.Add(row);
        }
        return result;
    }

    private static bool TryHarvestSpendableAmount(
        GameWorldState world,
        Guid resourceId,
        out BigDouble amount)
    {
        if (WorldLookup.TryFind(world.Resources, resourceId, out var resource))
        {
            amount = SpendableAmount(world, resourceId, resource.Reading.Quantity);
            return true;
        }
        if (WorldLookup.TryFind(world.HarvestResources, resourceId, out var harvestResource))
        {
            var value = harvestResource.Resource;
            amount = WorldResourceCoordinate.SpendableAmount(in value);
            return true;
        }
        amount = BigDouble.Zero;
        return false;
    }

    private static bool TryFindHarvestElementControl(
        PublicationTable<WorldHarvestElementControl> values,
        Guid elementId,
        out WorldHarvestElementControl result)
    {
        for (var index = 0; index < values.Count; index++)
            if (values[index].ElementId == elementId)
            {
                result = values[index];
                return true;
            }
        result = default;
        return false;
    }

    private static bool TryFindHarvestActionControl(
        PublicationTable<WorldHarvestActionControl> values,
        Guid elementId,
        Guid actionId,
        out WorldHarvestActionControl result)
    {
        for (var index = 0; index < values.Count; index++)
            if (values[index].ElementId == elementId && values[index].ActionId == actionId)
            {
                result = values[index];
                return true;
            }
        result = default;
        return false;
    }

    private static GameMcpValue ProjectDiscoveryTree(
        GameWorldState world,
        in WorldDiscoveryTree tree)
    {
        var result = new JObject
        {
            ["entityId"] = tree.EntityId.ToString("D"),
            ["category"] = "discovery-trees",
            ["mode"] = DiscoveryMode(tree.ActionMode),
            ["rerollsLeft"] = tree.RerollsLeft,
            ["discoveredCount"] = tree.TotalDiscoveredCount,
            // The count the game caches is a count of the tree's own discoverable list, and without
            // that list's size no caller could tell an early tree from a nearly finished one.
            ["discoverableCount"] = tree.TotalDiscoverableCount,
            ["hasRemainingDiscoveries"] = tree.HasRemainingDiscovery ||
                tree.HasImmediateRequiredDiscovery,
        };

        if (tree.ActionMode == 1)
            result["actionTime"] = new GameMcpDomainValue(tree.ActionTime);
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
                initiate["reasonCode"] = !tree.Visible
                    ? "tree_unavailable"
                    : !hasNext
                        ? "no_discoveries"
                        : "unaffordable";
            }
            if (hasNext)
            {
                if (tree.NextItemCosts.Count > 0)
                {
                    var costs = new JArray();
                    for (var index = 0; index < tree.NextItemCosts.Count; index++)
                    {
                        var cost = tree.NextItemCosts[index];
                        var amount = SpendableAmount(
                            world, cost.ResourceId, cost.AvailableAmount);
                        costs.Add(new JObject
                        {
                            ["resourceId"] = cost.ResourceId.ToString("D"),
                            ["cost"] = new GameMcpDomainValue(
                                PlayerFacingCost(world, cost.ResourceId, cost.Amount)),
                            ["amount"] = new GameMcpDomainValue(amount),
                            ["affordable"] = CanAfford(
                                world, cost.ResourceId, cost.Amount, cost.AvailableAmount),
                        });
                    }
                    initiate["costs"] = costs;
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
                    if (GameMcpEntityExplainer.TryDescribePublishedEntity(
                            world, id, out var category, out _, out _))
                    {
                        offer["category"] = category;
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
                !tree.HasImmediateRequiredDiscovery &&
                tree.RerollsLeft > 0 && tree.CurrentOfferIds.Count > 0 &&
                !tree.UsedRerollsLastDiscover;
            var reroll = new JObject { ["available"] = rerollAvailable };
            if (!rerollAvailable)
            {
                reroll["reasonCode"] = !tree.Visible
                    ? "tree_unavailable"
                    : tree.HasImmediateRequiredDiscovery
                        ? "immediate_required_discovery"
                        : tree.UsedRerollsLastDiscover
                            ? "reroll_already_used"
                            : tree.RerollsLeft <= 0
                                ? "no_rerolls"
                                : "no_current_offers";
            }
            result["reroll"] = reroll;
        }
        return result.Freeze();
    }

    private static GameMcpValue ProjectResearch(GameWorldState world, in WorldResearch research)
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
                    ["sourceNativeType"] = value.SourceNativeType,
                    ["modifierType"] = value.ModifierType,
                    ["amount"] = new GameMcpDomainValue(value.Amount),
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
            var investment = new JArray();
            for (var index = 0; index < decision.Investment.Count; index++)
            {
                var value = decision.Investment[index];

                // `invested` and `required` are the native fill bar, both raw. `GetRemaining()` is
                // what is still owed after the resource's own quality conversion — a remaining
                // price, not a pool — so it is named as one and published beside the actual pool.
                var row = new JObject
                {
                    ["resourceId"] = value.ResourceId.ToString("D"),
                    ["invested"] = new GameMcpDomainValue(value.Invested),
                    ["required"] = new GameMcpDomainValue(value.Required),
                    ["remainingCost"] = new GameMcpDomainValue(value.Remaining),
                };
                if (TryFindResource(world, value.ResourceId, out var pool))
                    row["spendableAmount"] = new GameMcpDomainValue(
                        WorldResourceCoordinate.SpendableAmount(in pool));
                investment.Add(row);
            }
            result["investment"] = investment;
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
        var developAvailable = decision.LevelsAvailable > 0;
        var develop = new JObject
        {
            ["available"] = developAvailable,
            ["route"] = decision.QueueMode ? "queue" : "immediate",
        };
        if (decision.QueueMode)
            develop["maximumBatch"] = Number(Math.Min(decision.MultiBuy, queueRoom));
        develop["levels"] = Number(decision.LevelsAvailable);
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
            var reasonCode = research.Complete
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

            // The cost verdict is published exactly when the cost is what decides. A row refused
            // for being maxed, queued out, or short of requirements has no next price to afford.
            if (blockedOnPrice)
            {
                develop["affordable"] = decision.DevelopmentCostAffordable;
                develop["reason"] = ShortfallReason(world, decision.DevelopmentCosts);
            }
        }

        // A row refused for its price still owes the price. Only the open develop gates published
        // it, so an unaffordable row named its shortfall in prose while its own `costs` were
        // withheld and `investment` — the native fill bar — named no resource that was short.
        var developmentCostsInformNextDecision =
            blockedOnPrice ||
            !research.Complete &&
            research.Available &&
            research.MeetsLevelRequirements &&
            research.StillHasLeeway &&
            research.BelowArtificialMaxLevel &&
            research.BelowMaxInvestmentLevel &&
            research.WithinDevelopRange &&
            (!research.IsDeveloping || decision.QueueMode) &&
            (!decision.QueueMode || decision.MultiBuy > 0 && queueRoom > 0);
        if (developmentCostsInformNextDecision && decision.DevelopmentCosts.Count > 0)
        {
            var costs = new JArray();
            for (var index = 0; index < decision.DevelopmentCosts.Count; index++)
            {
                var value = decision.DevelopmentCosts[index];
                var playerCost = PlayerFacingCost(world, value.ResourceId, value.Cost);
                var spendable = SpendableAmount(world, value.ResourceId, value.Amount);
                costs.Add(new JObject
                {
                    ["resourceId"] = value.ResourceId.ToString("D"),
                    ["cost"] = new GameMcpDomainValue(playerCost),
                    ["amount"] = new GameMcpDomainValue(spendable),
                    ["affordable"] = CanAfford(
                        world, value.ResourceId, value.Cost, value.Amount),
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
        else if (decision.CanApplyBonusLevel && decision.FreeBonusLevels > 0)
            result["bonus"] = new JObject
            {
                ["available"] = true,
                ["remainingLevels"] = Number(decision.FreeBonusLevels),
            };
        return result.Freeze();
    }

    // Levels, slots, counts, and enum discriminants are bounded cardinals, not game-domain
    // magnitudes. Keep them as JSON numbers; only BigDouble values use scientific strings.
    private static int Number(int value) => value;

    private static GameMcpValue ProjectConsumable(
        GameWorldState world,
        in WorldConsumable consumable)
    {
        var result = new JObject
        {
            ["entityId"] = consumable.EntityId.ToString("D"),
            ["category"] = "consumables",
            ["visible"] = consumable.Visible,
            ["amount"] = consumable.Quantity,
            ["queued"] = consumable.QueuedQuantity,
            ["maximumCarry"] = consumable.MaximumCarryLoad,
        };
        if (consumable.CurrentPrepTime > BigDouble.Zero)
            result["preparationRemaining"] = new GameMcpDomainValue(consumable.CurrentPrepTime);
        if (consumable.CurrentCooldownTime > BigDouble.Zero)
            result["cooldownRemaining"] = new GameMcpDomainValue(
                BigDouble.Max(consumable.CurrentCooldownTime, BigDouble.Zero));

        var types = new JArray();
        if (WorldConsumableTypeLookup.TryFindRange(
                world.ConsumableTypes,
                consumable.EntityId,
                out var typeStart,
                out var typeCount))
        {
            for (var index = typeStart; index < typeStart + typeCount; index++)
                types.Add(new JObject
                {
                    ["typeId"] = world.ConsumableTypes[index].TypeId.ToString("D"),
                });
        }
        if (types.Count > 0) result["types"] = types;

        var levels = new JArray();
        if (WorldConsumableCountLookup.TryFindRange(
                world.ConsumableCounts,
                consumable.EntityId,
                out var countStart,
                out var countCount))
        {
            for (var index = countStart; index < countStart + countCount; index++)
            {
                var value = world.ConsumableCounts[index];
                levels.Add(new JObject
                {
                    ["level"] = value.Level,
                    ["amount"] = value.Quantity,
                    ["freeAmount"] = value.FreeQuantity,
                });
            }
        }
        if (levels.Count > 0) result["levels"] = levels;

        if (consumable.Visible && consumable.Quantity > 0)
        {
            var immediate = ProjectConsumableCosts(
                world,
                consumable.EntityId,
                WorldConsumableCostKind.Consume);
            var held = ProjectConsumableCosts(
                world,
                consumable.EntityId,
                WorldConsumableCostKind.Usage);
            if (immediate.Count > 0) result["useCosts"] = immediate;
            if (held.Count > 0) result["heldCostsPerSecond"] = held;
        }

        var useAvailable = consumable.Visible && consumable.Quantity > 0 && consumable.CanFire &&
            world.ConsumableInventory.CanUse;
        var use = new JObject { ["available"] = useAvailable };
        if (!useAvailable)
        {
            use["reasonCode"] = !consumable.Visible
                ? "not_visible"
                : consumable.Quantity <= 0
                    ? "none_owned"
                    : !world.ConsumableInventory.CanUse
                        ? "inventory_busy"
                        : !consumable.ImmediateCostsAffordable
                            ? "unaffordable"
                            : consumable.CurrentCooldownTime > BigDouble.Zero
                                ? "cooldown_active"
                                : "native_use_refused";
        }
        result["use"] = use;

        var usages = ProjectConsumableUsages(world, consumable.EntityId);
        if (usages.Count > 0) result["usages"] = usages;
        result["cancel"] = consumable.QueuedQuantity > 0 && usages.Count > 0
            ? new JObject { ["available"] = true }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "no_cancellable_usage",
            };
        result["discard"] = consumable.Quantity > 0
            ? new JObject
            {
                ["available"] = true,
                ["maximumAmount"] = consumable.Quantity,
            }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "none_owned",
            };
        if (consumable.CanBeRandomized)
        {
            result["randomization"] = new JObject
            {
                ["available"] = true,
                ["enabled"] = consumable.Randomized,
            };
        }

        var placements = ProjectConsumablePlacements(world, consumable.EntityId);
        if (placements.Count > 0) result["placements"] = placements;
        return result.Freeze();
    }

    private static GameMcpValue ProjectConceptRecipe(
        GameWorldState world,
        in WorldConceptRecipe recipe)
    {
        var amount = WorldAlchemyInstanceLookup.TryFind(
            world.AlchemyInstances, recipe.RecipeId, out var instance)
            ? instance.Quantity
            : 0;
        return new JObject
        {
            ["entityId"] = recipe.RecipeId.ToString("D"),
            ["category"] = "concept-recipes",
            ["activeCount"] = amount,
            ["usedSlots"] = world.AlchemyInstances.Count,
            ["maximumSlots"] = recipe.SlotCount,
            ["canAdd"] = ConceptAddDecision(world, in recipe),
        }.Freeze();
    }

    /// <summary>
    /// Whether the game's Active Concepts list takes this recipe now, and when it does not, why.
    /// </summary>
    /// <remarks>
    /// The published assignments are the filled slots only, so a caller counting them cannot tell a
    /// full list from a half-empty one that refuses this particular recipe. The decision carries the
    /// pair, which is also the difference between "swap something out" and "this one, not now".
    /// </remarks>
    internal static JObject ConceptAddDecision(
        GameWorldState world,
        in WorldConceptRecipe recipe)
    {
        var result = new JObject { ["available"] = recipe.CanAddNow };
        if (recipe.CanAddNow) return result;
        var used = world.AlchemyInstances.Count;
        result["reasonCode"] = "slot_unavailable";
        // The pair rides beside this decision on both surfaces that publish it, so the sentence
        // says which of the two situations this is and leaves the counting to those two fields.
        result["reason"] = recipe.SlotCount > 0 && used >= recipe.SlotCount
            ? "Every Concept slot is in use."
            : "Slots are free, and the game still will not take this Concept into one.";
        return result;
    }

    private static GameMcpValue ProjectAlchemyInstance(
        GameWorldState world,
        in WorldAlchemyInstance instance)
    {
        // `drainReadable` was a column whose only job was to explain the absence of the column
        // beside it. The ratio now carries both answers, so the page says it once.
        //
        // `settled` is gone for two reasons that agree: it is `activeCount == queuedCount`, which
        // the two columns beside it already show, and the only thing it ever reports is that a
        // change is still in flight — which the next read resolves without anyone doing anything.
        var result = new JObject
        {
            ["recipe"] = EntityReference(world, instance.RecipeId),
            ["activeCount"] = instance.Quantity,
            ["queuedCount"] = instance.QueuedQuantity,
            ["drainRatio"] = instance.DrainReadable
                ? new GameMcpDomainValue(instance.DrainRatio)
                : (object)GameMcpListColumns.Unreadable,
        };
        return result.Freeze();
    }

    private static JArray ProjectConsumableCosts(
        GameWorldState world,
        Guid consumableId,
        WorldConsumableCostKind kind)
    {
        var result = new JArray();
        if (!WorldConsumableCostLookup.TryFindRange(
                world.ConsumableCosts,
                consumableId,
                kind,
                out var start,
                out var count))
            return result;
        for (var index = start; index < start + count; index++)
        {
            var value = world.ConsumableCosts[index];
            var playerCost = PlayerFacingCost(world, value.ResourceId, value.Amount);
            var cost = new JObject
            {
                ["resourceId"] = value.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(playerCost),
            };
            if (WorldLookup.TryFind(world.Resources, value.ResourceId, out var resource))
            {
                var amount = SpendableAmount(world, value.ResourceId, resource.Reading.Quantity);
                cost["amount"] = new GameMcpDomainValue(amount);
                cost["affordable"] = CanAfford(
                    world, value.ResourceId, value.Amount, resource.Reading.Quantity);
            }
            else cost["affordable"] = false;
            result.Add(cost);
        }
        return result;
    }

    private static JArray ProjectConsumableUsages(GameWorldState world, Guid consumableId)
    {
        var result = new JArray();
        if (!WorldConsumableUsageLookup.TryFindRange(
                world.ConsumableUsages,
                consumableId,
                out var start,
                out var count))
            return result;
        for (var index = start; index < start + count; index++)
        {
            var usage = world.ConsumableUsages[index];
            var value = new JObject
            {
                ["usageId"] = usage.UsageId.ToString("D"),
                ["level"] = usage.Level,
                ["state"] = usage.Engaged ? "active" : "pending",
            };
            if (usage.Engaged)
            {
                value["remainingDuration"] = new GameMcpDomainValue(usage.RemainingDuration);
                value["maximumDuration"] = new GameMcpDomainValue(usage.MaximumDuration);
            }
            result.Add(value);
        }
        return result;
    }

    private static JArray ProjectConsumablePlacements(GameWorldState world, Guid consumableId)
    {
        var result = new JArray();
        var slots = world.ConsumableInventory.Slots;
        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (slot.ConsumableId != consumableId) continue;
            var placement = new JObject
            {
                ["list"] = ConsumableListName(slot.List),
                ["position"] = GameMcpSlotNumbering.Wire(slot.Position),
            };
            result.Add(placement);
        }
        return result;
    }

    private static string ConsumableListName(WorldConsumableListKind kind) => kind switch
    {
        WorldConsumableListKind.Inventory => "inventory",
        WorldConsumableListKind.Hotbar => "hotbar",
        _ => "unknown",
    };

    private static GameMcpValue ProjectSpellRecipe(
        GameWorldState world,
        in WorldSpellRecipe recipe)
    {
        var result = new JObject
        {
            ["entityId"] = recipe.EntityId.ToString("D"),
            ["category"] = "spell-recipes",
            ["discovered"] = recipe.Discovered,
            ["masteryLevel"] = recipe.MasteryLevel,
        };
        if (recipe.CoreGlyphs.Count > 0)
        {
            var glyphs = new JArray();
            for (var index = 0; index < recipe.CoreGlyphs.Count; index++)
            {
                var glyph = recipe.CoreGlyphs[index];
                var projected = new JObject
                {
                    ["glyphId"] = glyph.GlyphId.ToString("D"),
                };
                if (WorldLookup.TryFind(world.Glyphs, glyph.GlyphId, out var holding))
                {
                    projected["ownedLevel"] = holding.Level;
                    if (holding.FreeLevels != 0) projected["bonusLevel"] = holding.FreeLevels;
                    projected["discovered"] = holding.Learned;
                }
                glyphs.Add(projected);
            }
            result["coreGlyphs"] = glyphs;
        }

        var holdings = new JArray();
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied || slot.SpellRecipeId != recipe.EntityId) continue;
            holdings.Add(ProjectEquippedSpell(world, in slot));
        }
        if (holdings.Count > 0) result["equipped"] = holdings;

        result["loadBudget"] = ProjectSpellLoadBudget(world);

        var next = new JObject();
        if (recipe.Discovered)
        {
            var coreUsable = SpellCoreUsable(world, in recipe, out var coreReasonCode);
            var available = world.SpellWorkbench.HasEmptySlot && coreUsable;
            next["available"] = available;
            next["requiresGlyphLayout"] = true;
            if (available)
            {
                var options = ProjectOwnedAugmentOptions(world);
                if (options.Count > 0) next["augmentOptions"] = options;
            }
            else if (!world.SpellWorkbench.HasEmptySlot)
                next["reasonCode"] = "loadout_full";
            else if (!coreUsable)
                next["reasonCode"] = coreReasonCode;
        }
        else
        {
            var structurallyAvailable = recipe.CoreGlyphs.Count > 0 &&
                recipe.Discovery.Visible && recipe.Discovery.CanDiscover;
            if (structurallyAvailable)
            {
                var costs = ProjectSpellCosts(world, recipe.DiscoveryCosts);
                if (costs.Count > 0) next["costs"] = costs;
                next["affordable"] = recipe.DiscoveryAffordable;
            }
            next["available"] = structurallyAvailable && recipe.DiscoveryAffordable;
            if (recipe.CoreGlyphs.Count == 0)
                next["reasonCode"] = "components_unavailable";
            else if (!recipe.Discovery.Visible)
                next["reasonCode"] = "not_visible";
            else if (!recipe.Discovery.CanDiscover)
                next["reasonCode"] = "discovery_unavailable";
            else if (!recipe.DiscoveryAffordable)
                next["reasonCode"] = "unaffordable";
            next["surface"] = "spellcraft";
            next["components"] = ProjectComponentReferences(recipe.CoreGlyphs);
        }
        result[recipe.Discovered ? "loadoutAdd" : "discover"] = next;
        AddAuthoredSpellGraph(world, recipe.EntityId, result);
        return result.Freeze();
    }

    /// <summary>
    /// The authored half of a spell — how it casts, what it is priced at before any modifier, and
    /// which type, glyph, and recipe book it belongs to.
    /// </summary>
    /// <remarks>
    /// The world has captured this since the spell graph reader landed and nothing read it, which
    /// is a defect in the reader rather than in the publication: it is structural, so it costs one
    /// pass per run of the game, and a caller asking "what is this spell" wants it. It rides on the
    /// detail row rather than becoming three categories of its own, because it is three facets of
    /// one entity the surface already names.
    /// </remarks>
    private static void AddAuthoredSpellGraph(
        GameWorldState world,
        Guid recipeId,
        JObject result)
    {
        if (WorldSpellGraphLookup.TryFindAuthoring(
                world.SpellRecipeAuthoring, recipeId, out var authoring))
        {
            // Words, not the game's ordinals. The block already names this spell's type in player
            // words a few lines up, so a bare `castType: 2` beside them was the one cell a reader
            // could not use.
            var casting = new JObject
            {
                ["castType"] = GameMcpNativeVocabulary.CastType(authoring.CastType),
                ["rechargeSeconds"] = authoring.RechargeDuration,
                // What one counted unit is worth against the recharge. `1` means one cast (or one
                // attribute developed) advances it by one, and the game's own
                // `Duration.Entry.GetMultiplier()` forces exactly 1 whenever the recharge counts in
                // time, so the key beside `rechargeCountsIn: time` is always the identity. It
                // shipped as `rechargeMultiplier: 1` with nothing saying what it multiplied.
                ["rechargeUnitMultiplier"] = authoring.RechargeMultiplier,
                // What the recharge counts down in, rather than which processor class the game
                // builds for it: `Duration.ProcessorType`'s own labels are "Time", "Number of
                // Casts" and "Attributes Developed", and that is the question a reader has.
                ["rechargeCountsIn"] = GameMcpNativeVocabulary.RechargeProcessorType(
                    authoring.RechargeProcessorType),
            };
            if (authoring.MaximumChannelBase != 0d)
                casting["maximumChannelSeconds"] = authoring.MaximumChannelBase;
            // The name carries the unit, and the game's own name for it is a trap: the field is
            // called a "rate" and `Spell.InitializePersistence` hands it straight to
            // `TickTimer(tickTime, …)` as the seconds BETWEEN re-applications, floored at 0.01. It
            // shipped as a bare `repeatEffectRate: 1`, where the one reading a player would guess —
            // once per second, which happens to be right at 1 and wrong everywhere else — is the
            // reciprocal of what it means.
            if (authoring.RepeatInstantEffectRateBase != 0d)
                casting["repeatEffectSeconds"] = authoring.RepeatInstantEffectRateBase;
            result["casting"] = casting;
        }

        if (WorldSpellGraphLookup.TryFindCosts(
                world.SpellAuthoredCosts, recipeId, out var costStart, out var costCount))
        {
            var costs = new JObject();
            for (var index = costStart; index < costStart + costCount; index++)
            {
                var cost = world.SpellAuthoredCosts[index];
                var key = cost.Kind switch
                {
                    WorldSpellAuthoredCostKind.Immediate => "cast",
                    WorldSpellAuthoredCostKind.Usage => "upkeep",
                    _ => "hold",
                };
                if (costs[key] is not JArray rows) costs[key] = rows = new JArray();
                var row = new JObject
                {
                    ["resourceId"] = cost.ResourceId.ToString("D"),
                    ["name"] = EntityIdentityFormatter.PlayerName(
                        cost.ResourceId, world.EntityIdentities),
                    ["cost"] = new GameMcpDomainValue(cost.Amount),
                };
                rows.Add(row);
            }
            if (costs.Count > 0) result["authoredCosts"] = costs;
        }

        if (WorldSpellGraphLookup.TryFindRelations(
                world.SpellRelations, recipeId, out var relationStart, out var relationCount))
        {
            var belongsTo = new JObject();
            for (var index = relationStart; index < relationStart + relationCount; index++)
            {
                var relation = world.SpellRelations[index];
                var key = relation.Kind switch
                {
                    WorldSpellRelationKind.SpellType => "spellTypes",
                    WorldSpellRelationKind.CoreGlyph => "coreGlyphs",
                    _ => "recipeBooks",
                };
                if (belongsTo[key] is not JArray rows) belongsTo[key] = rows = new JArray();
                rows.Add(new JObject
                {
                    ["uuid"] = relation.TargetId.ToString("D"),
                    ["name"] = EntityIdentityFormatter.PlayerName(
                        relation.TargetId, world.EntityIdentities),
                });
            }
            if (belongsTo.Count > 0) result["belongsTo"] = belongsTo;
        }
    }

    internal static GameMcpValue ProjectDiscoveryPreview(
        GameMcpFrameContext state,
        string surface,
        GameMcpUuidCount[] components)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        if (surface.Length == 0)
            return ProjectSurfaceLessDiscoveryPreview(state, components);

        Guid outputId;
        string category;
        string reasonCode;
        string reason;
        if (string.Equals(surface, "spellcraft", StringComparison.Ordinal))
        {
            category = "spell-recipes";
            reasonCode = "discovery_recipe_unresolved";
            if (!TryResolveSpellDiscovery(
                    world, surface, components, out outputId, out reason))
                return new JObject
                {
                    ["status"] = "unavailable",
                    ["reasonCode"] = reasonCode,
                    ["reason"] = reason,
                }.Freeze();
        }
        else if (!TryResolveGenericDiscovery(
                     world,
                     surface,
                     components,
                     out outputId,
                     out _,
                     out category,
                     out reasonCode,
                     out reason))
            return new JObject
            {
                ["status"] = "unavailable",
                ["reasonCode"] = reasonCode,
                ["reason"] = reason,
            }.Freeze();
        return new JObject
        {
            ["status"] = "available",
            ["surface"] = surface,
            ["output"] = ProjectPostState(state, category, outputId),
        }.Freeze();
    }

    private static GameMcpValue ProjectSurfaceLessDiscoveryPreview(
        GameMcpFrameContext state,
        GameMcpUuidCount[] components)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var surfaces = new[]
        {
            "spellcraft", "glyphcraft", "devote", "runecraft",
            "alchemy", "artifacts", "concepts",
        };
        var matchingSurfaces = new JArray();
        var matchCount = 0;
        var matchedId = Guid.Empty;
        var matchedCategory = string.Empty;
        var matchedSurface = string.Empty;
        for (var index = 0; index < surfaces.Length; index++)
        {
            var candidateSurface = surfaces[index];
            Guid candidateId;
            string candidateCategory;
            bool resolved;
            if (candidateSurface == "spellcraft")
            {
                candidateCategory = "spell-recipes";
                resolved = TryResolveSpellDiscovery(
                    world, candidateSurface, components, out candidateId, out _);
            }
            else
            {
                resolved = TryResolveGenericDiscovery(
                    world,
                    candidateSurface,
                    components,
                    out candidateId,
                    out _,
                    out candidateCategory,
                    out _,
                    out _);
            }
            if (!resolved) continue;
            matchCount++;
            matchedId = candidateId;
            matchedCategory = candidateCategory;
            matchedSurface = candidateSurface;
            matchingSurfaces.Add(candidateSurface);
        }
        if (matchCount == 1)
        {
            return new JObject
            {
                ["status"] = "available",
                ["surface"] = matchedSurface,
                ["output"] = ProjectPostState(state, matchedCategory, matchedId),
            }.Freeze();
        }
        return new JObject
        {
            ["status"] = "unavailable",
            ["reasonCode"] = matchCount == 0
                ? "discovery_recipe_unresolved"
                : "discovery_surface_ambiguous",
            ["reason"] = matchCount == 0
                ? "The composition resolves on none of the seven discovery screens."
                : "The composition resolves on more than one discovery screen; specify surface.",
            ["matchingSurfaces"] = matchingSurfaces,
        }.Freeze();
    }

    internal static bool TryResolveGenericDiscovery(
        GameWorldState world,
        string surface,
        GameMcpUuidCount[] components,
        out Guid outputId,
        out string nativeType,
        out string category,
        out string reasonCode,
        out string reason)
    {
        outputId = Guid.Empty;
        if (!GenericDiscoverySurfaces.TryResolve(surface, out nativeType, out category))
        {
            reasonCode = "unknown_discovery_surface";
            reason = "No generic compose resolver owns discovery surface " + surface + ".";
            return false;
        }

        var glyphs = new List<Guid>();
        var resources = new List<Guid>();
        var glyphCount = 0;
        var resourceCount = 0;
        try
        {
            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];
                var isGlyph = WorldLookup.TryFind(world.Glyphs, component.Uuid, out var glyph);
                var isResource = WorldLookup.TryFind(world.Resources, component.Uuid, out _);
                if (isGlyph == isResource)
                {
                    reasonCode = "component_unavailable";
                    reason = "Component " +
                        EntityIdentityFormatter.PlayerName(component.Uuid, world.EntityIdentities) +
                        (isGlyph
                            ? " is ambiguous between glyph and resource categories."
                            : " is not a published glyph or resource in this world.");
                    return false;
                }
                if (isGlyph)
                {
                    // Not holding the glyph and asking too much of one you hold are different
                    // answers with different next moves. Folded together, a glyph the player has
                    // never seen refused by quoting a usage ceiling of nought, which reads as a
                    // clamp on something they own.
                    if (!glyph.Learned)
                    {
                        var glyphName = EntityIdentityFormatter.PlayerName(
                            component.Uuid, world.EntityIdentities);
                        if (glyph.Discoverable)
                        {
                            reasonCode = "undiscovered";
                            reason = "Glyph " + glyphName + " has not been discovered yet.";
                        }
                        else if (TryNameGlyphBlocker(world, component.Uuid, out var blocker, out _))
                        {
                            reasonCode = "prerequisites_unmet";
                            reason = "Glyph " + glyphName + " is unlocked by " + blocker +
                                ", which is not reached yet.";
                        }
                        else
                        {
                            reasonCode = "native_unavailable";
                            reason = "Glyph " + glyphName +
                                " is locked, and the game says nothing about what would unlock it.";
                        }
                        return false;
                    }
                    if (component.Count > glyph.MaximumUsages)
                    {
                        reasonCode = "component_unavailable";
                        reason = "Glyph " +
                            EntityIdentityFormatter.PlayerName(component.Uuid, world.EntityIdentities) +
                            " permits " + glyph.MaximumUsages + " usable selections, not " +
                            component.Count + ".";
                        return false;
                    }
                    glyphs.Add(component.Uuid);
                    glyphCount = checked(glyphCount + component.Count);
                }
                else
                {
                    resources.Add(component.Uuid);
                    resourceCount = checked(resourceCount + component.Count);
                }
            }
        }
        catch (OverflowException)
        {
            reasonCode = "component_count_too_large";
            reason = "The submitted discovery component counts exceed the resolver boundary.";
            return false;
        }

        var matches = 0;
        var candidates = GenericDiscoveryCandidateCount(world, surface);
        for (var index = 0; index < candidates; index++)
        {
            if (!TryGenericDiscoveryCandidate(
                    world, surface, index, out var candidateId, out var discovery))
                continue;
            if (!GenericDiscoveryRecipeMatches(
                    discovery.GlyphRecipe,
                    glyphs,
                    glyphCount,
                    discovery.ResourceRecipe,
                    resources,
                    resourceCount))
                continue;
            outputId = candidateId;
            matches++;
        }
        reasonCode = matches == 0
            ? "discovery_recipe_unresolved"
            : matches == 1
                ? string.Empty
                : "discovery_recipe_ambiguous";
        reason = matches switch
        {
            0 => "This composition does not resolve on the " + surface + " screen.",
            1 => string.Empty,
            _ => "The component composition resolves to " + matches + " published " +
                 category + " outputs; the action refuses to guess.",
        };
        if (matches != 1) outputId = Guid.Empty;
        return matches == 1;
    }

    private static int GenericDiscoveryCandidateCount(
        GameWorldState world,
        string surface) => surface switch
    {
        "glyphcraft" => world.Glyphs.Count,
        "devote" => world.Rituals.Count,
        "runecraft" => world.TimeRunes.Count,
        "alchemy" => world.AlchemyRecipes.Count,
        "concepts" => world.ConceptRecipes.Count,
        "artifacts" => world.Equipment.Count,
        _ => 0,
    };

    private static bool TryGenericDiscoveryCandidate(
        GameWorldState world,
        string surface,
        int index,
        out Guid entityId,
        out WorldDiscoverableDecision discovery)
    {
        switch (surface)
        {
            case "glyphcraft":
                var glyph = world.Glyphs[index];
                entityId = glyph.EntityId;
                discovery = glyph.Discovery;
                return true;
            case "devote":
                var ritual = world.Rituals[index];
                entityId = ritual.EntityId;
                discovery = ritual.Discovery;
                return true;
            case "runecraft":
                var rune = world.TimeRunes[index];
                entityId = rune.EntityId;
                discovery = rune.Discovery;
                return true;
            case "alchemy":
                var recipe = world.AlchemyRecipes[index];
                if (WorldConceptRecipeLookup.TryFind(
                        world.ConceptRecipes, recipe.EntityId, out _))
                {
                    entityId = Guid.Empty;
                    discovery = default;
                    return false;
                }
                entityId = recipe.EntityId;
                discovery = recipe.Discovery;
                return true;
            case "concepts":
                var concept = world.ConceptRecipes[index];
                if (!WorldLookup.TryFind(world.AlchemyRecipes, concept.RecipeId, out var conceptRecipe))
                {
                    entityId = Guid.Empty;
                    discovery = default;
                    return false;
                }
                entityId = conceptRecipe.EntityId;
                discovery = conceptRecipe.Discovery;
                return true;
            case "artifacts":
                var equipment = world.Equipment[index];
                entityId = equipment.EntityId;
                discovery = equipment.Discovery;
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(surface));
        }
    }

    private static bool GenericDiscoveryRecipeMatches(
        PublicationTable<Guid> nativeGlyphs,
        List<Guid> submittedGlyphs,
        int submittedGlyphCount,
        PublicationTable<Guid> nativeResources,
        List<Guid> submittedResources,
        int submittedResourceCount)
    {
        if (nativeGlyphs.Count != submittedGlyphCount ||
            nativeResources.Count != submittedResourceCount)
            return false;
        for (var index = 0; index < submittedGlyphs.Count; index++)
            if (!Contains(nativeGlyphs, submittedGlyphs[index])) return false;
        for (var index = 0; index < submittedResources.Count; index++)
            if (!Contains(nativeResources, submittedResources[index])) return false;
        return true;
    }

    private static bool Contains(PublicationTable<Guid> values, Guid target)
    {
        for (var index = 0; index < values.Count; index++)
            if (values[index] == target) return true;
        return false;
    }

    internal static bool TryResolveSpellDiscovery(
        GameWorldState world,
        string surface,
        GameMcpUuidCount[] components,
        out Guid recipeId,
        out string reason)
    {
        recipeId = Guid.Empty;
        if (!string.Equals(surface, "spellcraft", StringComparison.Ordinal))
        {
            reason = "The " + surface +
                " UI uses UIDiscoverablePage recipe resolution, whose installed lifecycle binding is not available in this fence.";
            return false;
        }
        var expanded = new List<Guid>();
        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];
            // `discoverable` is the population split — the augments are the 22 discoverable ones and
            // the core glyphs the 25 that are not. `augmentsSpells` reads false for three members of
            // the augment list, so gating on it admitted Distinct, Weak and Wrath here as though
            // they were core glyphs.
            if (!WorldLookup.TryFind(world.Glyphs, component.Uuid, out var glyph) ||
                glyph.Discoverable || !glyph.Learned)
            {
                reason = "Component " +
                    EntityIdentityFormatter.PlayerName(component.Uuid, world.EntityIdentities) +
                    " is not an available core glyph in this world.";
                return false;
            }
            if (component.Count > glyph.MaximumUsages)
            {
                reason = "Component " +
                    EntityIdentityFormatter.PlayerName(component.Uuid, world.EntityIdentities) +
                    " requests " + component.Count + " uses, but only " +
                    glyph.MaximumUsages + " are usable.";
                return false;
            }
            for (var count = 0; count < component.Count; count++)
                expanded.Add(component.Uuid);
        }
        var matches = 0;
        for (var index = 0; index < world.SpellRecipes.Count; index++)
        {
            var recipe = world.SpellRecipes[index];
            if (recipe.CoreGlyphs.Count != expanded.Count) continue;
            var match = true;
            for (var glyph = 0; glyph < expanded.Count; glyph++)
                if (recipe.CoreGlyphs[glyph].GlyphId != expanded[glyph])
                {
                    match = false;
                    break;
                }
            if (!match) continue;
            recipeId = recipe.EntityId;
            matches++;
        }
        reason = matches switch
        {
            0 => "The ordered core-glyph composition resolves to no published spell recipe.",
            1 => string.Empty,
            _ => "The ordered core-glyph composition is ambiguous across " + matches +
                 " published spell recipes.",
        };
        if (matches != 1) recipeId = Guid.Empty;
        return matches == 1;
    }

    private static JArray ProjectComponentReferences(GameMcpUuidCount[] components)
    {
        var result = new JArray();
        for (var index = 0; index < components.Length; index++)
            result.Add(new JObject
            {
                ["componentId"] = components[index].Uuid.ToString("D"),
                ["count"] = components[index].Count,
            });
        return result;
    }

    private static JArray ProjectComponentReferences(
        PublicationTable<WorldSpellRecipeGlyph> components)
    {
        var result = new JArray();
        for (var index = 0; index < components.Count; index++)
            result.Add(new JObject
            {
                ["componentId"] = components[index].GlyphId.ToString("D"),
                ["count"] = 1,
            });
        return result;
    }

    private static JObject ProjectSpellLoadBudget(GameWorldState world) => new()
    {
        ["used"] = world.SpellWorkbench.EquippedCount,
        ["maximum"] = world.SpellWorkbench.MaximumEquipped,
        ["fitsAnotherSpell"] = world.SpellWorkbench.HasEmptySlot,
    };

    internal static GameMcpValue ProjectTargetingPostState(GameMcpFrameContext state, Guid submittedTarget)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var world = state.World.Snapshot;
        var result = new JObject();
        if (submittedTarget != Guid.Empty)
            result["submittedTarget"] = ProjectTargetCandidate(world, submittedTarget, -1);

        // A block that would only say nothing is pending does not appear. The game asks for a target
        // by putting a request up, so the block is the request, and no block is the answer that no
        // request is waiting — the same silence a spell with nothing to toggle already answers with.
        if (world.Targeting.Count > 0)
        {
            var request = world.Targeting[0];
            result["targeting"] = ProjectTargeting(world, in request);
        }
        return result.Freeze();
    }

    /// <summary>
    /// The request the game is waiting on: who asked, and every structure it will accept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request that is not pending has no row, so a <c>pending</c> column could only ever say
    /// <c>yes</c> — the row's existence already is that fact, and a column with one value in every
    /// world where it can appear costs a reader a column and tells them nothing.
    /// </para>
    /// <para>
    /// <c>ownerNativeType</c> and <c>selectionType</c> were the requesting object's class and the
    /// selection strategy's class, spelled the way the game's own code spells them. The owner is
    /// named beside them in the words the player sees it in, and this verb offers two decisions —
    /// submit one candidate, or let the request pick — neither of which turns on either class. So
    /// they carried no decision fact to rename into player words, and a native type name is not
    /// something this surface says.
    /// </para>
    /// <para>
    /// Whether a random pick would land is whether there is anything to pick: the old
    /// <c>randomize</c> column was <c>candidates</c> being non-empty, restated one column over.
    /// The candidates are the answer, so they are the only place it is said.
    /// </para>
    /// </remarks>
    private static GameMcpValue ProjectTargeting(
        GameWorldState world, in WorldTargetingRequest request)
    {
        var candidates = new JArray();
        for (var index = 0; index < request.Candidates.Count; index++)
        {
            var candidate = request.Candidates[index];
            candidates.Add(ProjectTargetCandidate(world, candidate.StructureId, candidate.Position));
        }
        return new JObject
        {
            ["owner"] = request.OwnerName,
            ["candidates"] = candidates,
        }.Freeze();
    }

    private static GameMcpValue ProjectTargetCandidate(GameWorldState world, Guid id, int position)
    {
        var identity = EntityIdentityFormatter.Describe(id, world.EntityIdentities);
        var result = new JObject { ["uuid"] = id.ToString("D") };
        if (identity.HasName) result["name"] = identity.Name;
        if (position >= 0) result["position"] = GameMcpSlotNumbering.Wire(position);
        for (var index = 0; index < world.Structures.Count; index++)
        {
            var structure = world.Structures[index];
            if (structure.EntityId != id) continue;
            // The two counts the attribute's own badge owns, under the names every other surface
            // uses for them. Their sum has no badge, and a separate work-in-flight flag only
            // restates a queue the caller can already read.
            result["level"] = structure.Reading.Level.ToInt();
            result["queuedLevels"] = structure.Reading.QueuedLevels.ToInt();
            result["effectiveLevel"] = structure.EffectiveLevel;
            result["available"] = structure.Reading.Unlocked;
            break;
        }
        return result.Freeze();
    }

    private static GameMcpValue ProjectSpellSlot(
        GameWorldState world,
        in WorldSpellSlot slot)
    {
        if (!slot.Occupied)
        {
            return new JObject
            {
                ["category"] = "spell-slots",
                ["slot"] = GameMcpSlotNumbering.Wire(slot.SlotIndex),
                ["occupied"] = false,
            }.Freeze();
        }
        var result = ProjectEquippedSpell(world, in slot);
        result["category"] = "spell-slots";
        result["occupied"] = true;
        return result.Freeze();
    }

    private static JObject ProjectEquippedSpell(
        GameWorldState world,
        in WorldSpellSlot slot)
    {
        // The equipped spell is a runtime instance, so its id is in no catalog and resolves for
        // nobody. Its recipe does, and carries the same name the instance was printed under, so the
        // row names the spell once — by the identity a caller can look up. The slot is the address
        // every spell verb takes, and it is already here.
        var result = new JObject
        {
            ["spellRecipeId"] = slot.SpellRecipeId.ToString("D"),
            ["slot"] = GameMcpSlotNumbering.Wire(slot.SlotIndex),
            ["effectiveLevel"] = slot.EffectiveLevel,
            ["requiredMasteryLevel"] = slot.RequiredMasteryLevel,
            ["recipeMasteryLevel"] = slot.RecipeMasteryLevel,
            ["duration"] = slot.DurationSpell,
            ["toggleable"] = slot.Toggled,
            ["usageRequirementsMet"] = slot.UsageRequirementsMet,
            // The game's own manual-cast counter. It is what a firing loop compares to learn whether
            // anything fired, so it is present whether or not it has ever moved.
            ["casts"] = slot.CastCount,
        };
        if (slot.Casting) result["casting"] = true;
        if (slot.ReadyingCast) result["readyingCast"] = true;
        if (slot.Attuning) result["attuning"] = true;
        if (slot.Toggled && slot.Casting)
        {
            result["toggleOff"] = slot.CancellationEnabled
                ? new JObject { ["available"] = true }
                : new JObject
                {
                    ["available"] = false,
                    ["reasonCode"] = "cancellable_spells_disabled",
                };
        }
        result["remove"] = slot.CanRemove
            ? new JObject { ["available"] = true }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "native_remove_refused",
            };
        // Where a spell can move is the slot list, and the slot list is one read for the whole bar.
        // Inlining it per spell meant explaining eight spells delivered the same eight-slot roster
        // eight times, on the verb that is called most.
        result["move"] = world.SpellSlots.Count > 1
            ? new JObject { ["available"] = true }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = "no_other_slot",
            };
        var applied = new JArray();
        for (var index = 0; index < slot.AugmentGlyphs.Count; index++)
        {
            var value = slot.AugmentGlyphs[index];
            applied.Add(new JObject
            {
                ["glyphId"] = value.GlyphId.ToString("D"),
                ["count"] = value.Quantity,
            });
        }
        result["glyphs"] = applied;
        var immediate = ProjectEquippedSpellCosts(world, slot.SlotIndex, WorldSpellCostKind.Immediate);
        var drain = ProjectEquippedSpellCosts(world, slot.SlotIndex, WorldSpellCostKind.Drain);
        if (immediate.Count > 0) result["castCosts"] = immediate;
        if (drain.Count > 0) result["drainCostsPerSecond"] = drain;
        return result;
    }

    private static JArray ProjectOwnedAugmentOptions(GameWorldState world)
    {
        var options = new JArray();
        for (var index = 0; index < world.Glyphs.Count; index++)
        {
            var glyph = world.Glyphs[index];

            // The augments are the discoverable population, all 22 of them. Gating on
            // `augmentsSpells` dropped Distinct, Weak and Wrath from the options a player holds.
            if (!glyph.Discoverable || !glyph.Learned || glyph.Level <= 0) continue;
            var option = new JObject
            {
                ["glyphId"] = glyph.GlyphId.ToString("D"),
                ["ownedLevel"] = glyph.Level,
                ["usableCount"] = glyph.MaximumUsages,
                ["masteryRequirement"] = glyph.MasteryReqCount,
            };
            if (glyph.FreeLevels != 0) option["bonusLevel"] = glyph.FreeLevels;
            if (glyph.RequiresDuration) option["requiresDuration"] = true;
            if (glyph.RequiresToggleable) option["requiresToggleable"] = true;
            options.Add(option);
        }
        return options;
    }

    /// <summary>
    /// Whether this recipe's core glyphs can carry a spell, and when they cannot, which fact stops
    /// them. One code for four different facts told a player holding the glyph to go acquire it.
    /// </summary>
    private static bool SpellCoreUsable(
        GameWorldState world,
        in WorldSpellRecipe recipe,
        out string reasonCode)
    {
        reasonCode = string.Empty;
        if (recipe.CoreGlyphs.Count == 0)
        {
            reasonCode = "recipe_has_no_core_glyph";
            return false;
        }
        for (var index = 0; index < recipe.CoreGlyphs.Count; index++)
        {
            if (!WorldLookup.TryFind(
                    world.Glyphs, recipe.CoreGlyphs[index].GlyphId, out var glyph))
            {
                reasonCode = "core_glyph_not_published";
                return false;
            }
            if (!glyph.Learned)
            {
                reasonCode = "core_glyph_not_owned";
                return false;
            }
            if (glyph.Level <= 0)
            {
                reasonCode = "core_glyph_not_leveled";
                return false;
            }
            // Same population split as the compose resolver: a recipe's core slot holds one of the
            // 25 non-discoverable unlockers, and three augments read `augmentsSpells` false.
            if (glyph.Discoverable)
            {
                reasonCode = "core_glyph_augments_only";
                return false;
            }
        }
        return true;
    }

    internal static JArray ProjectEquippedSpellCosts(
        GameWorldState world,
        int slotIndex,
        WorldSpellCostKind kind)
    {
        var result = new JArray();
        if (!WorldSpellCostLookup.TryFindRange(
                world.SpellCosts,
                slotIndex,
                kind,
                out var start,
                out var count))
            return result;
        for (var index = start; index < start + count; index++)
        {
            var value = world.SpellCosts[index];
            var playerCost = PlayerFacingCost(world, value.ResourceId, value.Amount);
            var row = new JObject
            {
                ["resourceId"] = value.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(playerCost),
            };
            if (WorldLookup.TryFind(world.Resources, value.ResourceId, out var resource))
            {
                var amount = SpendableAmount(
                    world, value.ResourceId, resource.Reading.Quantity);
                row["amount"] = new GameMcpDomainValue(amount);
                row["affordable"] = CanAfford(
                    world, value.ResourceId, value.Amount, resource.Reading.Quantity);
            }
            else row["affordable"] = false;
            result.Add(row);
        }
        return result;
    }

    private static JArray ProjectSpellCosts(
        GameWorldState world,
        PublicationTable<WorldDiscoverableCost> values)
    {
        var result = new JArray();
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            var playerCost = PlayerFacingCost(world, value.ResourceId, value.Cost);
            var spendable = SpendableAmount(world, value.ResourceId, value.AvailableAmount);
            result.Add(new JObject
            {
                ["resourceId"] = value.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(playerCost),
                ["amount"] = new GameMcpDomainValue(spendable),
                ["affordable"] = CanAfford(
                    world, value.ResourceId, value.Cost, value.AvailableAmount),
            });
        }
        return result;
    }

    internal static GameMcpValue ProjectResource(GameWorldState world, in WorldResource resource)
    {
        var amount = WorldResourceCoordinate.DisplayAmount(in resource);
        var result = new JObject
        {
            ["entityId"] = resource.EntityId.ToString("D"),
            ["category"] = "resources",
            ["amount"] = new GameMcpDomainValue(amount),

            // A resource with no storage ceiling says so under both keys. Publishing the native
            // capacity would state a limit the game does not apply, and a bare `atCapacity: no`
            // would answer "is it full" about a thing that cannot fill.
            ["capacity"] = resource.IsCapped
                ? new GameMcpDomainValue(resource.Reading.Capacity)
                : (object)GameMcpListColumns.Uncapped,
            ["netRatePerSecond"] = new GameMcpDomainValue(resource.TrueRate),
            ["atCapacity"] = resource.IsCapped
                ? resource.IsAtCapacity
                : (object)GameMcpListColumns.Uncapped,
        };
        return result.Freeze();
    }

    internal static BigDouble SpendableAmount(
        GameWorldState world,
        Guid resourceId,
        BigDouble fallback) =>
        TryFindResource(world, resourceId, out var resource)
            ? WorldResourceCoordinate.SpendableAmount(in resource)
            : fallback;

    /// <summary>
    /// Converts the game's nominal cost unit to the amount removed from the player's visible pool.
    /// This is the owned equivalent of ResourceSO.GetTrueSpend(amount).
    /// </summary>
    internal static BigDouble PlayerFacingCost(
        GameWorldState world,
        Guid resourceId,
        BigDouble nominalCost)
    {
        if (!TryFindResource(world, resourceId, out var resource))
            return nominalCost;
        return WorldResourceCoordinate.PlayerFacingCost(in resource, nominalCost);
    }

    internal static string ShortfallReason(
        GameWorldState world,
        PublicationTable<WorldResearchCost> costs)
    {
        var rows = new List<(string, BigDouble, BigDouble)>();
        for (var index = 0; index < costs.Count; index++)
        {
            var value = costs[index];
            if (CanAfford(world, value.ResourceId, value.Cost, value.Amount)) continue;
            var identity = EntityIdentityFormatter.Describe(
                value.ResourceId, world.EntityIdentities);
            rows.Add((
                identity.HasName ? identity.Name : value.ResourceId.ToString("D"),
                PlayerFacingCost(world, value.ResourceId, value.Cost),
                SpendableAmount(world, value.ResourceId, value.Amount)));
        }
        var sentence = GameMcpDecisionReason.Shortfall(rows);
        return sentence.Length == 0
            ? GameMcpDecisionReason.For("unaffordable")
            : sentence;
    }

    internal static bool CanAfford(
        GameWorldState world,
        Guid resourceId,
        BigDouble nominalCost,
        BigDouble fallbackAvailable) =>
        TryFindResource(world, resourceId, out var resource)
            ? WorldResourceCoordinate.HasAmount(in resource, nominalCost)
            : fallbackAvailable.CompareTo(nominalCost) >= 0;

    private static bool TryFindResource(
        GameWorldState world,
        Guid resourceId,
        out WorldResource resource)
    {
        if (WorldLookup.TryFind(world.Resources, resourceId, out resource)) return true;
        if (WorldLookup.TryFind(world.HarvestResources, resourceId, out var harvest))
        {
            resource = harvest.Resource;
            return true;
        }
        resource = default;
        return false;
    }

    private static GameMcpValue ProjectAlchemyRecipe(
        GameWorldState world,
        in WorldAlchemyRecipe recipe)
    {
        var result = new JObject
        {
            ["entityId"] = recipe.EntityId.ToString("D"),
            ["category"] = "alchemy-recipes",

            // `discovered` was this row's lifecycle under another name — it is what
            // AlchemyRecipeSO.IsAvailable() reads for every recipe the pinned build authors — so it
            // says it once, in the word the rest of the surface says it in. The raw field stays on
            // the category's fact scan.
            ["state"] = AlchemyRecipeState(in recipe),
            ["masteryLevel"] = recipe.MasteryLevel,
        };
        if (recipe.MaxLevel >= 0) result["maximumLevel"] = recipe.MaxLevel;
        if (WorldAlchemyInstanceLookup.TryFind(
                world.AlchemyInstances, recipe.RecipeId, out var instance))
        {
            result["activeCount"] = instance.Quantity;
            if (!instance.IsSettled) result["queuedCount"] = instance.QueuedQuantity;
        }
        AddAlchemyLoadoutDecision(world, result, recipe.RecipeId);
        AddDiscoveryDecision(world, result, recipe.Discovery);
        return result.Freeze();
    }

    private static void AddAlchemyLoadoutDecision(
        GameWorldState world,
        JObject result,
        Guid recipeId)
    {
        if (!WorldAlchemyLoadoutLookup.TryFind(world.AlchemyLoadout, recipeId, out var decision))
            return;
        var loadout = new JObject
        {
            ["activeCount"] = decision.Amount,
            ["targetAmount"] = decision.TargetAmount,
        };
        if (decision.IsActive)
            loadout["slot"] = GameMcpSlotNumbering.Wire(decision.Position);
        var addAvailable = decision.Discovered && decision.CanAdd && decision.MaximumAdd > 0;
        var add = new JObject { ["available"] = addAvailable };
        if (addAvailable)
        {
            add["maximumAmount"] = decision.MaximumAdd;
            add["freeUsesRemaining"] = decision.FreeUsesRemaining;
            var costs = ProjectAlchemyUsageCosts(world, recipeId);
            if (costs.Count > 0) add["usageCosts"] = costs;
        }
        else
        {
            add["reasonCode"] = !decision.Discovered
                ? "not_discovered"
                : !decision.CanAdd
                    ? "loadout_full"
                    : "usage_unavailable";
        }
        loadout["add"] = add;
        loadout["remove"] = decision.TargetAmount > 0
            ? new JObject { ["available"] = true, ["maximumAmount"] = decision.TargetAmount }
            : new JObject { ["available"] = false, ["reasonCode"] = "not_active" };
        result["alchemyLoadout"] = loadout;
    }

    private static JArray ProjectAlchemyUsageCosts(GameWorldState world, Guid recipeId)
    {
        var result = new JArray();
        if (!WorldAlchemyLoadoutLookup.TryFindCostRange(
                world.AlchemyUsageCosts, recipeId, out var start, out var count))
            return result;
        for (var index = start; index < start + count; index++)
        {
            var cost = world.AlchemyUsageCosts[index];
            var row = new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["amount"] = new GameMcpDomainValue(
                    PlayerFacingCost(world, cost.ResourceId, cost.Amount)),
            };
            if (WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
                row["spendableAmount"] = new GameMcpDomainValue(
                    SpendableAmount(world, cost.ResourceId, resource.Reading.Quantity));
            result.Add(row);
        }
        return result;
    }

    private static GameMcpValue ProjectEquipment(GameWorldState world, in WorldEquipment equipment)
    {
        var result = new JObject
        {
            ["entityId"] = equipment.EntityId.ToString("D"),
            ["category"] = "equipment",
            ["created"] = equipment.IsCreated,
            ["masteryLevel"] = equipment.MasteryLevel,
            ["attuningLevel"] = equipment.AttuningLevel,
        };
        if (equipment.AttunementTimeLeft > 0d)
            result["attunementTimeLeft"] = equipment.AttunementTimeLeft;
        var decision = equipment.Loadout;
        if (decision.Available)
        {
            result["equipmentTypeId"] = decision.EquipmentTypeId.ToString("D");
            result["equippedStacks"] = decision.EquippedStacks;
            result["maximumStacks"] = decision.MaximumStacks;
            result["loadout"] = new JObject
            {
                ["usedSlots"] = decision.UsedSlots,
                ["maximumSlots"] = decision.MaximumSlots,
                ["typeUsedSlots"] = decision.TypeUsedSlots,
                ["typeMaximumSlots"] = decision.TypeMaximumSlots,
            };
            var equip = new JObject { ["available"] = equipment.IsCreated && decision.MaximumEquipAmount > 0 };
            if (equipment.IsCreated && decision.MaximumEquipAmount > 0)
                equip["maximumAmount"] = decision.MaximumEquipAmount;
            else
                equip["reasonCode"] = !equipment.IsCreated
                    ? "not_created"
                    : decision.EquippedStacks >= decision.MaximumStacks
                        ? "maximum_stacks"
                        : decision.EquippedStacks == 0 && decision.UsedSlots >= decision.MaximumSlots
                            ? "loadout_full"
                            : decision.EquippedStacks == 0 && decision.TypeUsedSlots >= decision.TypeMaximumSlots
                                ? "equipment_type_full"
                                : !decision.UsageAffordable
                                    ? "usage_unaffordable"
                                    : "usage_unavailable";
            if (decision.Costs.Count > 0)
            {
                var costs = new JArray();
                for (var index = 0; index < decision.Costs.Count; index++)
                {
                    var cost = decision.Costs[index];
                    var playerCost = PlayerFacingCost(world, cost.ResourceId, cost.Cost);
                    var costRow = new JObject
                    {
                        ["resourceId"] = cost.ResourceId.ToString("D"),
                        ["cost"] = new GameMcpDomainValue(playerCost),
                    };
                    if (WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
                    {
                        var amount = SpendableAmount(world, cost.ResourceId, resource.Reading.Quantity);
                        costRow["amount"] = new GameMcpDomainValue(amount);
                        var affordable = CanAfford(
                            world, cost.ResourceId, cost.Cost, resource.Reading.Quantity);
                        costRow["affordable"] = affordable;
                        if (resource.Reading.Traits.BandwidthResource)
                        {
                            // The ceiling follows the value it caps: this one bounds a carried
                            // weight, so it is the magnitude-shaped `maximumCarry`, never the
                            // `maximum` every argument bound on the surface ships as a number.
                            result["weightBudget"] = new JObject
                            {
                                ["used"] = new GameMcpDomainValue(resource.Reading.Quantity),
                                ["maximumCarry"] = new GameMcpDomainValue(resource.Reading.Capacity),
                                ["itemWeight"] = new GameMcpDomainValue(playerCost),
                                ["fits"] = affordable,
                            };
                        }
                    }
                    else costRow["affordable"] = false;
                    costs.Add(costRow);
                }
                equip["usageCosts"] = costs;
            }
            result["equip"] = equip;
            result["unequip"] = decision.MaximumUnequipAmount > 0
                ? new JObject
                {
                    ["available"] = true,
                    ["maximumAmount"] = decision.MaximumUnequipAmount,
                }
                : new JObject { ["available"] = false, ["reasonCode"] = "not_equipped" };
        }
        else
        {
            result["loadoutUnavailable"] = new JObject
            {
                ["reasonCode"] = decision.UnavailableReason.Length == 0
                    ? "loadout_unavailable"
                    : decision.UnavailableReason,
            };
        }
        AddDiscoveryDecision(world, result, equipment.Discovery);
        return result.Freeze();
    }

    private static GameMcpValue ProjectChallenge(GameWorldState world, in WorldChallenge challenge)
    {
        var context = world.ChallengeContext;
        var selected = Contains(context.Selected, challenge.EntityId, out _);
        var inTime = Contains(context.TimeOffers, challenge.EntityId, out var timeRestricted);
        var inPrestige = Contains(context.PrestigeOffers, challenge.EntityId, out var prestigeRestricted);
        var restricted = timeRestricted || prestigeRestricted;
        var selectionRoom = context.Selected.Count < context.SelectionMaximum;
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
        var selectable = context.Available && (selected || inTime || inPrestige) &&
            (selected || (selectionRoom && !restricted));
        var select = new JObject { ["available"] = selectable, ["selected"] = selected };
        if (!selectable)
            select["reasonCode"] = !context.Available
                ? "challenge_state_unavailable"
                : !selected && !inTime && !inPrestige
                    ? "not_offered"
                    : !selectionRoom
                        ? "selection_full"
                        : "selection_restricted";
        result["select"] = select;

        // The button reads "activate" on the screen but it queues: the challenge starts at the next
        // reset, not now. Naming the verb after the press taught callers to expect a running
        // challenge and to read the queued state as a failure.
        var queueAvailable = context.Available && (inTime || inPrestige) && challenge.State is 0 or 1;
        var queue = new JObject
        {
            ["available"] = queueAvailable,
            ["queued"] = challenge.State == 1,
        };
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
    private static string ChallengeRun(int state) => state switch
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
    /// This block used to ride every challenge read: a request for one row at offset fifty came back
    /// nine tenths ambient state, repeated verbatim on every page of a ninety-eight-row category. It
    /// also said the same list three times — the two offer arrays and the surviving-selection filter
    /// were byte-identical on every live sample — under two reroll decisions that were one budget,
    /// one list, and one button. It is one answer now, and a caller asks for it.
    /// </remarks>
    internal static JObject ChallengeStateRead(GameMcpFrameContext state)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        // The reset state is published once, at the top level, on both verbs that carry it. Nesting
        // a second byte-identical copy inside the challenge block gave a caller two blocks with
        // nothing to tell them apart, on the two responses that were already the largest.
        var result = Envelope(publication);
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
            ["offers"] = ChallengeReferences(context.TimeOffers),
        };

        // The Reset modal draws from the same list asset the Time screen does, so it is said once.
        // A build where the two ever part company says the second one out loud rather than quietly
        // planning off the first.
        if (!SameChallengeOffers(context.TimeOffers, context.PrestigeOffers))
            result["resetOffers"] = ChallengeReferences(context.PrestigeOffers);
        result["reroll"] = RerollDecision(fetchAvailable, context);
        return result.Freeze();
    }

    /// <summary>
    /// The reset decision, in the numbers the Reset screen itself shows.
    /// </summary>
    /// <remarks>
    /// The screen says "Starting Time Advancements: 94" and "20 more than last time", and both are
    /// the same subtraction: what a reset would start with, less what the previous one started with.
    /// This block used to publish the game's third figure under the screen's label and subtract the
    /// wrong pair from it, so the one number that decides whether to reset arrived negative when the
    /// screen showed a gain — a stall an unattended caller had nothing to catch it with.
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
                ["atStart"] = context.PersistenceCurrent,
                ["previousStart"] = context.PersistencePrevious,
                ["change"] = context.PersistenceCurrent - context.PersistencePrevious,
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

    private static JArray PrestigeChallenges(GameWorldState world, bool queuedRewards)
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

    /// <summary>
    /// Whether the challenge-offer button can be pressed, and what it costs. The game relabels one
    /// button: the first press is free and sets the fetched flag, every later press spends a reroll,
    /// so the decision block says which of the two this press would be.
    /// </summary>
    private static JObject RerollDecision(bool available, in WorldChallengeContext context)
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

    private static JArray ChallengeReferences(PublicationTable<WorldChallengeReference> references)
    {
        var result = new JArray();
        for (var index = 0; index < references.Count; index++)
            result.Add(references[index].ChallengeId.ToString("D"));
        return result;
    }

    private static bool Contains(PublicationTable<WorldChallengeReference> references,
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

    private static GameMcpValue ProjectGlyph(GameWorldState world, in WorldGlyph glyph)
    {
        var result = new JObject
        {
            ["entityId"] = glyph.EntityId.ToString("D"),
            ["category"] = "glyphs",
            ["population"] = GlyphPopulation(in glyph),
            ["screen"] = GlyphScreen(world, glyph.EntityId),
            ["state"] = GlyphState(in glyph),
            ["discovered"] = glyph.Discovered,
            ["usableCount"] = glyph.MaximumUsages,
        };

        // The glyph the picker will not offer says which of the two reasons it is, and the two are
        // the two populations: GlyphSO.IsAvailable() returns `discovered` for a discoverable glyph
        // and `prerequisites.Check()` for every other. So an augment is waiting on a discovery, and
        // an unlocker is waiting on the one authored condition it carries — which the world now
        // publishes, and which this names. The honest-unknown sentence is what is left when neither
        // holds: a glyph the game refuses with no readable condition behind it.
        if (!glyph.Learned)
        {
            if (glyph.Discoverable)
            {
                result["reasonCode"] = "undiscovered";
            }
            else if (TryNameGlyphBlocker(world, glyph.EntityId, out var blocker, out var blockerId))
            {
                result["reasonCode"] = "prerequisites_unmet";
                result["reason"] = blocker + " unlocks this glyph, and it is not reached yet.";
                result["blockedBy"] = EntityReference(world, blockerId);
            }
            else
            {
                result["reasonCode"] = "native_unavailable";
            }
        }
        AddLevelDecision(world, result, glyph.LevelDecision,
            glyph.Learned,
            "not_available");
        AddDiscoveryDecision(
            world,
            result,
            glyph.Discovery,
            glyph.Discoverable,
            glyph.Discoverable && !glyph.Discovered && IsCurrentDiscoveryOffer(world, glyph.EntityId));
        return result.Freeze();
    }

    private static GameMcpValue ProjectEquipmentType(
        GameWorldState world,
        in WorldEquipmentType equipmentType)
    {
        var result = new JObject
        {
            ["entityId"] = equipmentType.EntityId.ToString("D"),
            ["category"] = "equipment-types",
            ["baseUsage"] = equipmentType.BaseUsage,
            ["masteryLevel"] = new GameMcpDomainValue(equipmentType.MasteryLevel),

            // The game's own answer for this quantity is an integer: GetMaxTypeSlots() is
            // maxTypeSlots.AsInt(), which is GetValue().ToInt() over the very record this row
            // holds. Publishing the raw record put one key on the wire in two JSON types, because
            // the loadout block's typeMaximumSlots reads that method.
            ["maximumSlots"] = Number(equipmentType.MaxTypeSlots.ToInt()),
        };
        AddLevelDecision(world, result, equipmentType.LevelDecision);
        return result.Freeze();
    }

    private static GameMcpValue ProjectResourceType(
        GameWorldState world,
        in WorldResourceType resourceType)
    {
        var result = new JObject
        {
            ["entityId"] = resourceType.EntityId.ToString("D"),
            ["category"] = "resource-types",
            ["hidden"] = resourceType.SpecialHidden,
        };
        AddLevelDecision(world, result, resourceType.LevelDecision,
            !resourceType.SpecialHidden, "hidden");
        return result.Freeze();
    }

    private static GameMcpValue ProjectRitual(GameWorldState world, in WorldRitual ritual)
    {
        var result = new JObject
        {
            ["entityId"] = ritual.EntityId.ToString("D"),
            ["category"] = "rituals",
            ["state"] = RitualState(in ritual),
            ["inBattle"] = ritual.InBattle,
            ["activeInstances"] = ritual.ActiveInstances,
            ["reachedLevel"] = ritual.ReachedLevel,
            ["selectedLevel"] = ritual.SelectedLevel,
            ["waveTotal"] = ritual.RequiredWaves,
        };
        AddRitualRun(result, in ritual);
        AddRitualDecision(world, result, in ritual);
        AddDiscoveryDecision(world, result, ritual.Discovery);
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
    private static void AddRitualRun(JObject result, in WorldRitual ritual)
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

    private static GameMcpValue ProjectCraftingStation(
        GameWorldState world,
        in WorldCraftingStation station)
    {
        var stationIdentity = EntityIdentityFormatter.Describe(
            station.StructureTypeId, world.EntityIdentities);
        var result = new JObject
        {
            ["entityId"] = station.StationId.ToString("D"),
            ["name"] = stationIdentity.HasName
                ? stationIdentity.Name
                : station.StationId.ToString("D"),
            ["category"] = "crafting-stations",
        };
        AddCraftingStationDecision(world, result, in station);
        return result.Freeze();
    }

    private static void AddCraftingStationDecision(
        GameWorldState world,
        JObject result,
        in WorldCraftingStation station)
    {
        result["loaded"] = station.Loaded;
        result["active"] = station.Active;
        result["level"] = station.Level;
        if (station.FirstIngredientId != Guid.Empty)
            result["firstIngredientId"] = station.FirstIngredientId.ToString("D");
        if (station.SecondIngredientId != Guid.Empty)
            result["secondIngredientId"] = station.SecondIngredientId.ToString("D");
        if (station.OutputId != Guid.Empty) result["outputId"] = station.OutputId.ToString("D");

        result["setLevel"] = new JObject
        {
            ["available"] = station.MinimumLevel < station.MaximumLevel,
            ["minimum"] = station.MinimumLevel,
            ["maximum"] = station.MaximumLevel,
        };
        result["start"] = station.Loaded && !station.Active
            ? new JObject { ["available"] = true }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = station.Active ? "already_active" : "recipe_incomplete",
            };
        result["stop"] = station.Active
            ? new JObject { ["available"] = true }
            : new JObject { ["available"] = false, ["reasonCode"] = "already_stopped" };

        var first = new JArray();
        var second = new JArray();
        var outputs = new JArray();
        if (WorldCraftingStationLookup.TryFindOptions(
                world.CraftingStationOptions, station.StationId, out var start, out var count))
        {
            for (var index = start; index < start + count; index++)
            {
                var option = world.CraftingStationOptions[index];
                if (!option.Available) continue;
                var row = new JObject { ["uuid"] = option.OptionId.ToString("D") };
                if (option.Kind == WorldCraftingStationOptionKind.FirstIngredient) first.Add(row);
                else if (option.Kind == WorldCraftingStationOptionKind.SecondIngredient) second.Add(row);
                else outputs.Add(row);
            }
        }
        result["ingredientOptions"] = new JArray(first, second);
        result["outputOptions"] = outputs;

        if (WorldCraftingStationLookup.TryFindDrains(
                world.CraftingStationDrains, station.StationId, out var drainStart, out var drainCount))
        {
            var drains = new JArray();
            for (var index = drainStart; index < drainStart + drainCount; index++)
            {
                var drain = world.CraftingStationDrains[index];
                var row = new JObject
                {
                    ["resourceId"] = drain.ResourceId.ToString("D"),
                    ["amount"] = new GameMcpDomainValue(
                        PlayerFacingCost(world, drain.ResourceId, drain.Amount)),
                };
                if (WorldLookup.TryFind(world.Resources, drain.ResourceId, out var resource))
                    row["spendableAmount"] = new GameMcpDomainValue(
                        SpendableAmount(world, drain.ResourceId, resource.Reading.Quantity));
                drains.Add(row);
            }
            result["drain"] = drains;
        }
    }

    private static void AddRitualDecision(
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
            ["current"] = ritual.SelectedLevel,
        };
        if (ritual.ForceLevel)
            level["reasonCode"] = "level_locked";
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
    private static JArray ProjectRitualSpoils(PublicationTable<WorldRitualSpoil> spoils)
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

    private static JArray ProjectRitualCosts(
        GameWorldState world,
        PublicationTable<WorldRitualCost> costs)
    {
        var result = new JArray();
        for (var index = 0; index < costs.Count; index++)
        {
            var cost = costs[index];
            var row = new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(
                    PlayerFacingCost(world, cost.ResourceId, cost.Cost)),
            };
            var held = BigDouble.Zero;
            if (WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
            {
                held = resource.Reading.Quantity;
                row["spendableAmount"] = new GameMcpDomainValue(
                    SpendableAmount(world, cost.ResourceId, held));
            }

            // Every other cost row in the surface says which line the player is short on; a ritual
            // row left the caller to compare two formatted magnitudes itself.
            row["affordable"] = CanAfford(world, cost.ResourceId, cost.Cost, held);
            result.Add(row);
        }
        return result;
    }

    private static GameMcpValue ProjectTimeRune(GameWorldState world, in WorldTimeRune rune)
    {
        var result = new JObject
        {
            ["entityId"] = rune.EntityId.ToString("D"),
            ["category"] = "time-runes",
            ["discovered"] = rune.Discovered,
            ["masteryLevel"] = rune.MasteryLevel,
            ["seen"] = rune.Seen,
        };
        AddLevelDecision(world, result, rune.LevelDecision,
            rune.Discovered, "undiscovered");
        AddDiscoveryDecision(world, result, rune.Discovery);
        return result.Freeze();
    }

    private static void AddLevelDecision(
        GameWorldState world,
        JObject result,
        WorldLevelableDecision decision,
        bool targetAvailable = true,
        string targetReasonCode = "not_available")
    {
        result["paidLevel"] = decision.TotalLevel - decision.BonusLevels;
        if (decision.SupportsBonus) result["bonusLevel"] = decision.BonusLevels;
        result["totalLevel"] = decision.TotalLevel;

        var purchase = new JObject
        {
            ["available"] = targetAvailable && decision.CanPurchase && decision.PurchaseAffordable,
        };
        if (!targetAvailable) purchase["reasonCode"] = targetReasonCode;
        else if (!decision.CanPurchase) purchase["reasonCode"] = "native_level_refused";
        else
        {
            purchase["affordable"] = decision.PurchaseAffordable;
            if (!decision.PurchaseAffordable) purchase["reasonCode"] = "unaffordable";
            var costs = ProjectLevelCosts(world, decision.PaidCosts);
            purchase["costs"] = costs;
            if (costs.Count == 0) purchase["free"] = true;
        }
        result["purchase"] = purchase;

        if (!decision.SupportsBonus) return;
        var bonus = new JObject
        {
            ["available"] = targetAvailable &&
                decision.BonusResourcesVisible && decision.BonusAffordable,
        };
        if (!targetAvailable) bonus["reasonCode"] = targetReasonCode;
        else if (!decision.BonusResourcesVisible) bonus["reasonCode"] = "resources_hidden";
        else
        {
            bonus["affordable"] = decision.BonusAffordable;
            if (!decision.BonusAffordable) bonus["reasonCode"] = "unaffordable";
            var costs = ProjectLevelCosts(world, decision.BonusCosts);
            bonus["costs"] = costs;
            if (costs.Count == 0) bonus["free"] = true;
        }
        result["bonus"] = bonus;
    }

    private static JArray ProjectLevelCosts(
        GameWorldState world,
        PublicationTable<WorldLevelableCost> costs)
    {
        var result = new JArray();
        for (var index = 0; index < costs.Count; index++)
        {
            var cost = costs[index];
            var row = new JObject
            {
                ["resourceId"] = cost.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(
                    PlayerFacingCost(world, cost.ResourceId, cost.Amount)),
            };
            if (WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource))
                row["spendableAmount"] = new GameMcpDomainValue(
                    SpendableAmount(world, cost.ResourceId, resource.Reading.Quantity));
            result.Add(row);
        }
        return result;
    }

    /// <summary>
    /// The discovery verdict, present wherever the concept applies to the row's kind. An entity the
    /// game never routes through discovery still answers the verb — silence there read as
    /// "not discovered yet", which is the opposite of the truth for a glyph learned by prerequisite.
    /// </summary>
    private static void AddDiscoveryDecision(
        GameWorldState world,
        JObject result,
        WorldDiscoverableDecision decision,
        bool nativeDiscoverable = true,
        bool offered = false)
    {
        var available = nativeDiscoverable && decision.Visible && decision.CanDiscover &&
            !decision.Discovered && decision.Affordable;
        var discover = new JObject { ["available"] = available };
        if (!available)
        {
            discover["reasonCode"] = decision.Discovered
                ? "already_discovered"
                : !nativeDiscoverable
                    ? "native_not_discoverable"
                    : !decision.Visible
                        ? "not_visible"
                        : !decision.CanDiscover
                            ? "native_discovery_refused"
                            : "unaffordable";
        }
        if (nativeDiscoverable && decision.Visible && !decision.Discovered &&
            decision.CanDiscover && decision.Costs.Count > 0)
        {
            var costs = new JArray();
            for (var index = 0; index < decision.Costs.Count; index++)
            {
                var cost = decision.Costs[index];
                costs.Add(new JObject
                {
                    ["resourceId"] = cost.ResourceId.ToString("D"),
                    ["cost"] = new GameMcpDomainValue(
                        PlayerFacingCost(world, cost.ResourceId, cost.Cost)),
                    ["amount"] = new GameMcpDomainValue(
                        SpendableAmount(world, cost.ResourceId, cost.Amount)),
                    ["affordable"] = CanAfford(
                        world, cost.ResourceId, cost.Cost, cost.Amount),
                });
            }
            discover["costs"] = costs;
        }
        if (decision.Required) discover["required"] = true;
        if (offered) discover["offered"] = true;
        result["discover"] = discover;
    }

    /// <summary>
    /// Whether one of the discovery trees is offering this entity a slot right now — the fact that
    /// separates "you could discover this next" from "this is in the pool somewhere".
    /// </summary>
    private static bool IsCurrentDiscoveryOffer(GameWorldState world, Guid id)
    {
        for (var treeIndex = 0; treeIndex < world.DiscoveryTrees.Count; treeIndex++)
        {
            var offers = world.DiscoveryTrees[treeIndex].CurrentOfferIds;
            for (var offerIndex = 0; offerIndex < offers.Count; offerIndex++)
                if (offers[offerIndex] == id) return true;
        }
        return false;
    }

    /// <summary>
    /// The price one cost row asks in the player's own units, straight from the capture the action
    /// is admitted against. The published cost row, the unaffordable sentence, and a committed
    /// purchase's <c>paid[]</c> all read this one expression, so they cannot disagree.
    /// </summary>
    internal static BigDouble AdmittedCost(GameWorldState world, in WorldPurchaseCost cost) =>
        PlayerFacingCost(
            world,
            cost.ResourceId,
            cost.AffordabilityEvaluated
                ? cost.CombinedEffectiveAmount
                : cost.EffectiveExactAmount);

    internal static JObject ProjectPurchaseCost(
        GameWorldState world,
        in WorldPurchaseCost cost) =>
        ProjectPurchaseCost(world, in cost, asRow: false);

    internal static JObject ProjectPurchaseCost(
        GameWorldState world,
        in WorldPurchaseCost cost,
        bool asRow)
    {
        var result = new JObject
        {
            ["resourceId"] = cost.ResourceId.ToString("D"),
            ["cost"] = new GameMcpDomainValue(AdmittedCost(world, in cost)),
        };
        // A price the publication could not compare against a same-generation holding says that,
        // rather than dropping the two columns that would have said it.
        if (!cost.AffordabilityEvaluated)
        {
            result["spendableAmount"] = GameMcpListColumns.Unevaluated;
            result["affordable"] = GameMcpListColumns.Unevaluated;
            return result;
        }

        result["spendableAmount"] = new GameMcpDomainValue(
            SpendableAmount(world, cost.ResourceId, cost.AvailableAmount));

        // This row answers for its own resource. The whole-price verdict is what the rows fold
        // to, and a row that reported it claimed to be short of a resource it holds plenty of.
        result["affordable"] = cost.ResourceAffordable;

        // `affordable: no` beside the cost and the holding already says "short of this". The
        // plain shortfall code and the generic sentence behind it wrote that same fact a second
        // and a third time on every row of a 744-row category, so only a shortfall that says
        // something else — a bandwidth ceiling rather than a quantity — still names itself.
        //
        // On a row it names itself in the column that asked. `affordable` already answers with a
        // word wherever a plain no would mislead, so the shortfall the cost and holding columns
        // cannot show is one more word in that vocabulary rather than two columns of its own.
        if (!cost.ResourceAffordable &&
            !string.Equals(
                cost.ResourceAffordabilityReasonCode,
                "insufficient_quantity",
                StringComparison.Ordinal))
        {
            if (asRow)
                result["affordable"] =
                    GameMcpListColumns.Word(cost.ResourceAffordabilityReasonCode);
            else result["reasonCode"] = cost.ResourceAffordabilityReasonCode;
        }
        return result;
    }

    /// <summary>
    /// What occupies a queue right now, so a row that reports a queue is full also reports what is
    /// filling it. The collection is always present; a queue that was read and is empty is empty.
    /// </summary>
    private static GameMcpValue QueueSlots(GameWorldState world, Guid queueId)
    {
        var slots = new JArray();
        if (WorldCraftingDecisionLookup.TryFindQueueRange(
                world.CraftingQueueEntries, queueId, out var start, out var count))
            for (var index = 0; index < count; index++)
                slots.Add(ProjectCraftingQueueEntry(world.CraftingQueueEntries[start + index]));
        return slots.Freeze();
    }

    /// <summary>
    /// What the automation strip's badge shows for one recipe. <c>UICraftingInstance</c> draws the
    /// queued instance's own <c>quantity</c>, while the entry's repetition count is the exponent
    /// behind it — <c>CraftingInstance.SetAutomationQuantity(n)</c> stores
    /// <c>quantity = 2^(n-1)</c>. The two coincide at 1 and 2 and diverge exponentially after that,
    /// so only the badge's number is published as <c>amount</c>.
    /// </summary>
    /// <remarks>
    /// A miss is only "nothing is automated" while the repetition count agrees. A recipe the game
    /// says is repeating some number of times has an entry drawing that badge, so failing to find
    /// it means the queue-entry collection did not arrive — a different answer from zero, and the
    /// caller is told which one it got.
    /// </remarks>
    private static bool TryAutomationAmount(
        GameWorldState world,
        Guid automationQueueId,
        Guid recipeId,
        out BigDouble amount)
    {
        amount = BigDouble.Zero;
        if (!WorldCraftingDecisionLookup.TryFindQueueRange(
                world.CraftingQueueEntries, automationQueueId, out var start, out var count))
            return false;
        for (var index = 0; index < count; index++)
        {
            var entry = world.CraftingQueueEntries[start + index];
            if (entry.RecipeId == recipeId)
            {
                amount = entry.Amount;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The badge amount, or false when the entry that draws it is missing from a recipe the game
    /// says is repeating. Zero repetitions need no entry, and zero is that recipe's honest badge.
    /// </summary>
    private static bool TryAutomationBadge(
        GameWorldState world,
        Guid automationQueueId,
        Guid recipeId,
        int repetitions,
        out BigDouble amount) =>
        TryAutomationAmount(world, automationQueueId, recipeId, out amount) || repetitions <= 0;

    private static JObject AutomationAmountUnavailable() => new()
    {
        ["reasonCode"] = "automation_entry_not_published",
        ["reason"] = "the recipe repeats but no queue entry for it was collected, " +
            "so the badge amount behind those repetitions is unknown",
    };

    private static string AutomationRefusal(string reasonCode) => reasonCode switch
    {
        "hidden_or_undiscovered" => "This recipe is not discovered yet.",
        _ => "Every automation slot on this queue is in use.",
    };

    private static GameMcpValue ProjectCraftingRecipe(
        GameWorldState world,
        in WorldCraftingRecipe recipe)
    {
        var reading = recipe.Reading;
        var hasDecision = WorldCraftingDecisionLookup.TryFind(
            world.CraftingDecisions,
            recipe.EntityId,
            out var decision);
        var canStart = reading.Visible && reading.OutputWithinCapacity &&
            (hasDecision ? decision.CanStart : reading.CanBuyAtStartingQuantity);
        var result = new JObject
        {
            ["entityId"] = recipe.EntityId.ToString("D"),
            ["category"] = "crafting-recipes",
            ["visible"] = reading.Visible,
            ["startingAmount"] = new GameMcpDomainValue(reading.StartingQuantity),
            ["craftTimeSeconds"] = reading.TimeToComplete,
            ["canStart"] = canStart,
        };
        if (reading.UseQuantityAsLevel) result["amountActsAsLevel"] = true;
        var blockers = new JArray();
        if (!reading.Visible) blockers.Add("hidden_or_undiscovered");
        if (hasDecision)
        {
            result["execution"] = CraftingPipeline(decision.Pipeline);
            result["purchaseAmount"] = new GameMcpDomainValue(decision.PurchaseAmount);
            if (decision.Pipeline is WorldCraftingPipeline.QueueStack or
                WorldCraftingPipeline.QueueNew)
            {
                result["queuedAmount"] = decision.QueuedAmount.ToInt();
                result["queue"] = new JObject
                {
                    ["queueId"] = decision.QueueId.ToString("D"),
                    ["used"] = decision.QueueUsed,
                    ["maximum"] = decision.QueueMaximum,
                    ["slots"] = QueueSlots(world, decision.QueueId),
                };
                if (decision.CanCancelManual)
                    result["cancelManual"] = new JObject { ["available"] = true };
                var automation = new JObject
                {
                    ["queueId"] = decision.AutomationQueueId.ToString("D"),
                };
                if (TryAutomationBadge(world, decision.AutomationQueueId, recipe.EntityId,
                        decision.AutomationRepetitions, out var badge))
                    automation["amount"] = new GameMcpDomainValue(badge);
                else
                    automation["amountUnavailable"] = AutomationAmountUnavailable();
                automation["repetitions"] = decision.AutomationRepetitions;
                automation["available"] = decision.CanAutomate;
                automation["slots"] = QueueSlots(world, decision.AutomationQueueId);
                if (decision.CanCancelAutomation) automation["canCancel"] = true;
                if (decision.AutomationMaximum > 0)
                {
                    automation["used"] = decision.AutomationUsed;
                    automation["maximum"] = decision.AutomationMaximum;
                }
                if (!decision.CanAutomate)
                {
                    automation["reasonCode"] = decision.AutomationReasonCode;
                    automation["reason"] = AutomationRefusal(decision.AutomationReasonCode);
                }
                result["automation"] = automation;
            }
            if (!decision.CanStart && decision.ReasonCode.Length > 0 &&
                decision.ReasonCode != "hidden_or_undiscovered" &&
                decision.ReasonCode != "output_capacity_blocked")
                blockers.Add(decision.ReasonCode);
            if (WorldCraftingDecisionLookup.TryFindCostRange(
                    world.CraftingDecisionCosts,
                    recipe.EntityId,
                    out var costStart,
                    out var costCount))
            {
                var exactCosts = new JArray();
                for (var index = 0; index < costCount; index++)
                {
                    var cost = world.CraftingDecisionCosts[costStart + index];
                    var playerCost = PlayerFacingCost(world, cost.ResourceId, cost.Cost);
                    var spendable = SpendableAmount(world, cost.ResourceId, cost.Amount);
                    exactCosts.Add(new JObject
                    {
                        ["resourceId"] = cost.ResourceId.ToString("D"),
                        ["cost"] = new GameMcpDomainValue(playerCost),
                        ["amount"] = new GameMcpDomainValue(spendable),
                        ["affordable"] = CanAfford(
                            world, cost.ResourceId, cost.Cost, cost.Amount),
                    });
                }
                result["nextCosts"] = exactCosts;
            }
        }
        else if (!reading.CanBuyAtStartingQuantity) blockers.Add("native_purchase_refused");
        if (!reading.OutputWithinCapacity) blockers.Add("output_capacity_blocked");
        if (blockers.Count > 0) result["blockers"] = blockers;
        if (recipe.Types.Count > 0)
        {
            var types = new JArray();
            for (var index = 0; index < recipe.Types.Count; index++)
                types.Add(recipe.Types[index].TypeId.ToString("D"));
            result["types"] = types;
        }
        if (recipe.Resources.Count > 0)
        {
            var inputs = new JArray();
            var outputs = new JArray();
            for (var index = 0; index < recipe.Resources.Count; index++)
            {
                var resource = recipe.Resources[index];
                var projected = new JObject
                {
                    ["resourceId"] = resource.ResourceId.ToString("D"),
                };
                if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    projected["cost"] = new GameMcpDomainValue(
                        PlayerFacingCost(world, resource.ResourceId, resource.Amount));
                else
                    projected["yield"] = new GameMcpDomainValue(resource.Amount);
                if (resource.ResourceStateAvailable)
                {
                    var spendable = SpendableAmount(
                        world, resource.ResourceId, resource.TrueQuantity);
                    projected["amount"] = new GameMcpDomainValue(spendable);
                    if (resource.IsCapped)
                        projected["capacity"] = new GameMcpDomainValue(resource.Capacity);
                    if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    {
                        projected["affordable"] = CanAfford(
                            world, resource.ResourceId, resource.Amount, resource.TrueQuantity);
                    }
                }
                else if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    projected["affordable"] = false;
                if (resource.BandwidthResource) projected["bandwidth"] = true;
                if (!resource.Visible) projected["hidden"] = true;
                if (resource.Kind == WorldCraftingRecipeResourceKind.AuthoredInput)
                    inputs.Add(projected);
                else
                    outputs.Add(projected);
            }
            if (inputs.Count > 0) result["inputs"] = inputs;
            if (outputs.Count > 0) result["outputs"] = outputs;
        }
        if (recipe.ConsumableOutputs.Count > 0)
        {
            var consumables = new JArray();
            for (var index = 0; index < recipe.ConsumableOutputs.Count; index++)
                consumables.Add(recipe.ConsumableOutputs[index].ConsumableId.ToString("D"));
            result["consumableOutputs"] = consumables;
        }
        var drainBlockers = new JArray();
        for (var index = 0; index < recipe.DrainBlocks.Count; index++)
        {
            var drain = recipe.DrainBlocks[index];
            if (!drain.Blocked) continue;
            drainBlockers.Add(new JObject
            {
                ["reasonCode"] = "engagement_drain_limited",
                ["availableRatio"] = new GameMcpDomainValue(drain.NecessaryRatio),
            });
        }
        if (drainBlockers.Count > 0) result["drainBlockers"] = drainBlockers;
        return result.Freeze();
    }

    private static string CraftingPipeline(WorldCraftingPipeline pipeline) => pipeline switch
    {
        WorldCraftingPipeline.Direct => "direct",
        WorldCraftingPipeline.QueueStack => "queue_stack",
        WorldCraftingPipeline.QueueNew => "queue_new",
        _ => "unknown",
    };

    private static string DiscoveryMode(int mode) => mode switch
    {
        0 => "idle",
        1 => "crafting",
        2 => "choice",
        _ => "unknown_" + mode.ToString(CultureInfo.InvariantCulture),
    };

    private static JArray LocalizedRequirementImplications(
        GameWorldState world,
        HashSet<Guid> touchedIdentities)
    {
        var result = new JArray();
        if (touchedIdentities.Count == 0 ||
            !TryLocalizedRequirementFailures(world, out var failures, out var collectorReason))
        {
            return result;
        }

        for (var index = 0; index < failures.Length; index++)
        {
            var failure = failures[index];
            if (!touchedIdentities.Contains(failure.OwnerId)) continue;
            result.Add(new JObject
            {
                ["category"] = "entity-requirements",
                ["reasonCode"] = "unmodeled_requirement_leaf",
                ["ownerUuid"] = failure.OwnerId.ToString("D"),
                ["ownerKind"] = failure.OwnerKind.ToString(),
                ["containerIndex"] = failure.ContainerIndex,
                ["ordinal"] = failure.Ordinal,
                ["parentOrdinal"] = failure.ParentOrdinal,
                ["conditionTypeName"] = failure.ConditionTypeName,
                ["collectorReason"] = collectorReason,
            });
        }
        return result;
    }

    private static JArray LocalizedDiscoveryOfferImplications(
        GameWorldState world,
        HashSet<Guid> touchedIdentities)
    {
        var result = new JArray();
        if (touchedIdentities.Count == 0) return result;
        for (var treeIndex = 0; treeIndex < world.DiscoveryTrees.Count; treeIndex++)
        {
            var tree = world.DiscoveryTrees[treeIndex];
            if (!touchedIdentities.Contains(tree.EntityId)) continue;
            for (var offerIndex = 0; offerIndex < tree.CurrentOfferIds.Count; offerIndex++)
            {
                var offerId = tree.CurrentOfferIds[offerIndex];
                if (GameMcpEntityExplainer.TryDescribePublishedEntity(
                        world, offerId, out _, out _, out _))
                {
                    continue;
                }
                result.Add(new JObject
                {
                    ["treeUuid"] = tree.EntityId.ToString("D"),
                    ["ordinal"] = offerIndex,
                    ["offerUuid"] = offerId.ToString("D"),
                    ["reasonCode"] = "offer_not_in_explainable_world",
                });
            }
        }
        return result;
    }

    /// <summary>
    /// The entity-requirements reader deliberately publishes every unmodeled leaf with its owner.
    /// It is safe to localize the category's skipped count only when those published rows account
    /// for the entire count. A thrown read has no owner row and therefore remains category-global.
    /// </summary>
    private static bool TryLocalizedRequirementFailures(
        GameWorldState world,
        out WorldEntityRequirement[] failures,
        out string collectorReason)
    {
        collectorReason = string.Empty;
        var skipped = 0;
        var reportFound = false;
        for (var index = 0; index < world.CollectionCategories.Count; index++)
        {
            var report = world.CollectionCategories[index];
            if (!string.Equals(
                    Normalize(report.Category),
                    "entity-requirements",
                    StringComparison.Ordinal))
            {
                continue;
            }
            reportFound = true;
            if (report.Outcome != WorldCategoryOutcome.Collected || report.Skipped <= 0)
            {
                failures = Array.Empty<WorldEntityRequirement>();
                return false;
            }
            skipped = report.Skipped;
            collectorReason = report.FirstFailure.Length == 0
                ? "the collector did not publish a failure reason"
                : report.FirstFailure;
            break;
        }
        if (!reportFound)
        {
            failures = Array.Empty<WorldEntityRequirement>();
            return false;
        }

        var localized = new List<WorldEntityRequirement>();
        for (var index = 0; index < world.EntityRequirements.Count; index++)
        {
            var row = world.EntityRequirements[index];
            if (row.NodeKind == WorldRequirementNodeKind.Leaf &&
                row.Kind == WorldRequirementConditionKind.Unknown)
            {
                localized.Add(row);
            }
        }
        if (localized.Count != skipped)
        {
            failures = Array.Empty<WorldEntityRequirement>();
            return false;
        }
        failures = localized.ToArray();
        return true;
    }

    private static bool TryCategory(
        string name,
        out GameMcpWorldCategory category,
        out string reason)
    {
        var normalized = Normalize(name);
        if (ByName.TryGetValue(normalized, out category!))
        {
            reason = string.Empty;
            return true;
        }
        category = null!;
        reason = "unknown category '" + (name ?? string.Empty) +
            "'; call world_categories for the exact discoverable names";
        return false;
    }

    /// <summary>Shares the exact world-query completeness rule with composite diagnostic tools.</summary>
    internal static bool TryCategoryAvailability(
        GameWorldState world,
        string name,
        out string reason)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (!TryCategory(name, out var category, out reason)) return false;
        var availability = Availability(world, category);
        if (availability.Available)
        {
            reason = string.Empty;
            return true;
        }
        reason = availability.Reason.Length == 0
            ? "category collection was incomplete"
            : availability.Reason;
        return false;
    }

    private readonly struct GameMcpCategoryAvailability
    {
        internal GameMcpCategoryAvailability(bool available, string reason)
        {
            Available = available;
            Reason = reason ?? string.Empty;
        }

        internal bool Available { get; }
        internal string Reason { get; }
    }

    private static GameMcpWorldCategory[] CreateCategories()
    {
        var result = new GameMcpWorldCategory[]
        {
            Entity(nameof(GameWorldState.Resources), world => world.Resources),
            Entity(nameof(GameWorldState.Structures), world => world.Structures),
            Entity(nameof(GameWorldState.Upgrades), world => world.Upgrades),
            Entity(nameof(GameWorldState.Research), world => world.Research),
            Entity(nameof(GameWorldState.DoubleVariables), world => world.DoubleVariables),
            Entity(nameof(GameWorldState.IntVariables), world => world.IntVariables),
            Entity(nameof(GameWorldState.BoolVariables), world => world.BoolVariables),
            Entity(nameof(GameWorldState.ModifierVariables), world => world.ModifierVariables),
            Composite(nameof(GameWorldState.PurchaseCosts), world => world.PurchaseCosts),
            Entity(nameof(GameWorldState.AlchemyRecipes), world => world.AlchemyRecipes),
            Entity(nameof(GameWorldState.AlchemyTypes), world => world.AlchemyTypes),
            Entity(nameof(GameWorldState.SpellRecipes), world => world.SpellRecipes),
            Entity(nameof(GameWorldState.SpellTypes), world => world.SpellTypes),
            Entity(nameof(GameWorldState.Equipment), world => world.Equipment),
            Entity(nameof(GameWorldState.EquipmentTypes), world => world.EquipmentTypes),
            Entity(nameof(GameWorldState.ResourceTypes), world => world.ResourceTypes),
            Entity(nameof(GameWorldState.CraftingRecipeTypes), world => world.CraftingRecipeTypes),
            Entity(nameof(GameWorldState.CraftingRecipes), world => world.CraftingRecipes),
            Composite(
                nameof(GameWorldState.CraftingQueueEntries),
                world => world.CraftingQueueEntries),
            Entity(nameof(GameWorldState.PlayerLoadouts), world => world.PlayerLoadouts),
            Composite(nameof(GameWorldState.PlayerLoadoutEntries), world => world.PlayerLoadoutEntries),
            Entity(nameof(GameWorldState.SnapshotLoadouts), world => world.SnapshotLoadouts),
            Composite(nameof(GameWorldState.SnapshotSlots), world => world.SnapshotSlots),
            Composite(nameof(GameWorldState.SnapshotEntries), world => world.SnapshotEntries),
            Entity("agromancy-elements", nameof(GameWorldState.HarvestElements),
                world => world.HarvestElements),
            Entity(nameof(GameWorldState.TimeRunes), world => world.TimeRunes),
            Entity(nameof(GameWorldState.Glyphs), world => world.Glyphs),
            Entity(nameof(GameWorldState.Consumables), world => world.Consumables),
            Entity(nameof(GameWorldState.Rituals), world => world.Rituals),
            Entity(nameof(GameWorldState.Achievements), world => world.Achievements),
            Entity(nameof(GameWorldState.Advancements), world => world.Advancements),
            Entity(nameof(GameWorldState.Challenges), world => world.Challenges),
            Entity(nameof(GameWorldState.ThoughtStreams), world => world.ThoughtStreams),
            Entity(nameof(GameWorldState.Tutorials), world => world.Tutorials),
            Entity(nameof(GameWorldState.Views), world => world.Views),
            Entity(nameof(GameWorldState.PlotNodeActions), world => world.PlotNodeActions),
            Entity(nameof(GameWorldState.PassiveAbilities), world => world.PassiveAbilities),
            Entity(nameof(GameWorldState.Characters), world => world.Characters),
            Entity(nameof(GameWorldState.DiscoveryTrees), world => world.DiscoveryTrees),
            Entity(nameof(GameWorldState.RecipeBooks), world => world.RecipeBooks),
            Entity(nameof(GameWorldState.PlotNodes), world => world.PlotNodes),
            Composite("agromancy-plot-actions", nameof(GameWorldState.PlotActions),
                world => world.PlotActions),
            Composite("agromancy-processing", nameof(GameWorldState.ActionQueueSlots),
                world => world.ActionQueueSlots),
            Composite(nameof(GameWorldState.SpellSlots), world => world.SpellSlots),
            Composite(nameof(GameWorldState.SpellCosts), world => world.SpellCosts),
            Composite(nameof(GameWorldState.Targeting), world => world.Targeting),
            Composite(nameof(GameWorldState.MasteryExperience), world => world.MasteryExperience),
            Entity(nameof(GameWorldState.ConceptRecipes), world => world.ConceptRecipes),
            Composite(nameof(GameWorldState.AlchemyInstances), world => world.AlchemyInstances),
            Composite(nameof(GameWorldState.AlchemyCosts), world => world.AlchemyCosts),
            Composite(nameof(GameWorldState.AlchemyLoadout), world => world.AlchemyLoadout),
            Composite(nameof(GameWorldState.AlchemyUsageCosts), world => world.AlchemyUsageCosts),
            Composite(nameof(GameWorldState.PlotAuthoring), world => world.PlotAuthoring),
            Composite(nameof(GameWorldState.PlotPhaseDescriptors), world => world.PlotPhaseDescriptors),
            Composite(nameof(GameWorldState.EffectBlocks), world => world.EffectBlocks),
            Composite(nameof(GameWorldState.EntityRequirements), world => world.EntityRequirements),
            Entity(nameof(GameWorldState.TreasurePools), world => world.TreasurePools),
        };
        Array.Sort(result, static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        return result;
    }

    private static GameMcpWorldCategory Entity<TRow>(
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table)
        where TRow : struct, IWorldEntity =>
        Entity(Normalize(propertyName), propertyName, table);

    private static GameMcpWorldCategory Entity<TRow>(
        string publicName,
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table)
        where TRow : struct, IWorldEntity =>
        new GameMcpEntityCategory<TRow>(
            publicName,
            propertyName,
            table,
            ExpectedNativeType(Normalize(publicName)),
            RequiredReportCategories(Normalize(propertyName)),
            FailureOnlyReportCategories(Normalize(propertyName)),
            ScanFields(Normalize(propertyName)));

    private static GameMcpWorldCategory Composite<TRow>(
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table)
        where TRow : struct =>
        Composite(Normalize(propertyName), propertyName, table);

    private static GameMcpWorldCategory Composite<TRow>(
        string publicName,
        string propertyName,
        Func<GameWorldState, PublicationTable<TRow>> table)
        where TRow : struct =>
        new GameMcpCompositeCategory<TRow>(
            publicName,
            propertyName,
            table,
            ExpectedNativeType(Normalize(publicName)),
            RequiredReportCategories(Normalize(propertyName)),
            FailureOnlyReportCategories(Normalize(propertyName)),
            ScanFields(Normalize(propertyName)));

    private static Dictionary<string, GameMcpWorldCategory> IndexCategories()
    {
        var result = new Dictionary<string, GameMcpWorldCategory>(StringComparer.Ordinal);
        for (var index = 0; index < Categories.Length; index++)
            result.Add(Categories[index].Name, Categories[index]);
        return result;
    }

    private static string ExpectedNativeType(string category) =>
        GameMcpEntityCapabilityMap.ExpectedNativeType(category);

    private static string[] RequiredReportCategories(string category) => category switch
    {
        "purchase-costs" => new[]
        {
            "structures",
            "upgrades",
            "resources",
            "modifier-variables",
            "int-variables",
            "structure-costs",
            "upgrade-costs",
        },
        "plot-actions" => new[] { "plot-nodes", "plot-node-actions", "plot-actions" },
        "plot-action-instances" => new[] { "plot-actions" },
        "action-queue-slots" => new[] { "action-queues" },
        "spell-costs" => new[] { "spell-slots" },
        "targeting" => new[] { "targeting" },
        "concept-recipes" or "alchemy-instances" or "alchemy-costs" =>
            new[] { "concept-instances" },
        "alchemy-loadout" or "alchemy-usage-costs" =>
            new[] { "ordinary-alchemy-loadout" },
        "plot-phase-descriptors" => new[] { "plot-authoring" },
        "mastery-experience" =>
            new[] { "spell-recipes", "alchemy-recipes", "equipment" },
        "crafting-recipes" => new[]
        {
            "crafting-recipes",
            "crafting-recipe-state",
            "crafting-decisions",
            "resources",
        },
        "crafting-queue-entries" => new[] { "crafting-decisions" },
        "crafting-station-options" or "crafting-station-drains" =>
            new[] { "crafting-stations" },
        "player-loadouts" or "player-loadout-entries" or "snapshot-loadouts" or
            "snapshot-slots" or "snapshot-entries" => new[] { "loadouts" },
        "harvest-element-controls" or "harvest-action-controls" or
            "harvest-lifecycle-costs" => new[] { "harvest-lifecycle" },
        "consumables" => new[] { "consumables", "consumable-inventory" },
        _ => new[] { category },
    };

    private static string[] FailureOnlyReportCategories(string category) => category switch
    {
        // The collector emits modifier-folding only when a frame-global modifier could not be
        // reconstructed. Its absence is the clean case; its presence invalidates every row derived
        // with those globals.
        "resources" or "harvest-resources" or "purchase-costs" =>
            new[] { "modifier-folding" },
        _ => Array.Empty<string>(),
    };

    private static string[] ScanFields(string category) => category switch
    {
        "resources" => new[]
        {
            "entityId", "reading.visible", "reading.quantity", "reading.capacity",
            "reading.rate", "reading.usage", "reading.reservation", "trueQuantity",
            "trueRate", "fillFraction", "isCapped", "isAtCapacity", "headroom",
        },
        "structures" => new[]
        {
            "entityId", "reading.unlocked", "reading.level", "reading.queuedLevels",
            "effectiveLevel", "hasWorkInFlight",
            "developmentProgress", "reading.insufficientReqPenaltyActive",
        },
        "upgrades" => new[]
        {
            "entityId", "reading.available", "reading.level", "reading.maxLevel",
            "reading.queuedLevels",
            "remainingLevels", "isExhausted", "isDeveloping", "developmentProgress",
        },
        "research" => new[]
        {
            "entityId", "level", "queuedLevels", "maxLevel", "researchStage",
            "available", "visible", "complete", "canDevelop", "withinDevelopRange",
            "meetsLevelRequirements", "stillHasLeeway", "belowArtificialMaxLevel",
            "belowMaxInvestmentLevel", "isActive", "isDeveloping", "flagged",
            "purchasedLevels", "baseLevel", "bonusLevel", "totalLevel", "artificialMaxLevel",
            "baseRequirementLevel", "effectiveRequirementLevel",
            "requirementLevelAdjustment", "requirementAdjustments",
        },
        "double-variables" or "int-variables" =>
            new[] { "entityId", "value", "isPercent" },
        "bool-variables" =>
            new[] { "entityId", "value", "initialValue", "isSaved" },
        "modifier-variables" =>
            new[] { "entityId", "modifierType", "amount", "order" },
        "purchase-costs" => new[]
        {
            "entityId", "resourceId", "baseExactAmount", "effectiveExactAmount",
            "amount", "exactGroupedLevels", "exactGroupedAmount", "modifierSources",
            "affordabilityEvaluated", "availableAmount", "combinedEffectiveAmount",
            "resourceAffordable", "resourceAffordabilityReasonCode", "affordable",
            "affordabilityReasonCode",
        },
        "alchemy-recipes" => new[]
        {
            "entityId", "coreTypeId", "discovered", "masteryLevel", "masteryXp",
            "maxLevel", "advancementLevel",
        },
        "alchemy-types" => new[]
        {
            "entityId", "selectedLevelId", "level", "maxUsageByMastery",
        },
        "alchemy-loadout" => new[]
        {
            "recipeId", "position", "slotCount", "amount", "targetAmount", "multiBuy",
            "freeUsesRemaining", "maximumAdd", "discovered", "canAdd", "isActive",
            "nextAdd", "nextRemove",
        },
        "alchemy-usage-costs" => new[] { "recipeId", "resourceId", "amount" },
        "spell-recipes" => new[]
        {
            "entityId", "discovered", "masteryLevel", "masteryLevelReady",
            "masteryXp", "baseCharges", "castSpeed",
        },
        "spell-types" => new[]
        {
            "entityId", "typeLevel", "typeXp", "isVisible", "isElemental",
            "isLoadoutUnique",
        },
        "equipment" => new[]
        {
            "entityId", "isCreated", "masteryLevel", "masteryXp", "equippedLevel",
            "attuningLevel", "attunementTimeLeft",
        },
        "equipment-types" => new[]
        {
            "entityId", "level", "freeLevels", "baseUsage", "masteryLevel",
            "maxTypeSlots",
        },
        "resource-types" =>
            new[] { "entityId", "level", "freeLevels", "specialHidden" },
        "crafting-recipe-types" => new[]
        {
            "entityId", "startingLevel", "maxStartingLevel", "craftVerb",
            "initiated",
        },
        "crafting-recipes" => new[]
        {
            "entityId", "reading.visible", "reading.visibilityReasonCode",
            "reading.canBuyAtStartingQuantity", "reading.nativePurchaseReasonCode",
            "reading.startingQuantity", "reading.outputWithinCapacity",
            "reading.outputCapacityReasonCode", "reading.authoredInputCount",
            "reading.generatedOutputCount", "reading.consumableOutputCount",
            "reading.engagementEffectCount",
        },
        "crafting-queue-entries" => new[]
        {
            "queueId", "slot", "recipeId", "amount", "automatic", "repetitions",
        },
        "crafting-stations" => new[]
        {
            "stationId", "structureTypeId", "firstIngredientId",
            "secondIngredientId", "outputId", "loaded", "active", "level",
            "minimumLevel", "maximumLevel",
        },
        "crafting-station-options" =>
            new[] { "stationId", "kind", "optionId", "available" },
        "crafting-station-drains" =>
            new[] { "stationId", "resourceId", "amount" },
        "player-loadouts" => new[]
        {
            "entityId", "name", "selected", "savesEquipment", "savesAlchemy",
            "icon", "color", "canSwitchNow",
        },
        "player-loadout-entries" =>
            new[] { "ownerId", "kind", "entryId", "referenceId", "quantity" },
        "snapshot-loadouts" => new[] { "entityId", "kind", "slots" },
        "snapshot-slots" => new[] { "ownerId", "slot", "populated" },
        "snapshot-entries" => new[] { "ownerId", "slot", "entryId", "quantity" },
        "harvest-elements" => new[]
        {
            "entityId", "masteryLevel", "masteryXp", "instances", "harvestTime",
            "growthTime", "harvestRate",
        },
        "harvest-resources" => new[]
        {
            "entityId", "elementId", "resource.entityId", "resource.reading.visible",
            "resource.trueQuantity", "resource.trueRate",
        },
        "harvest-element-controls" => new[]
        {
            "elementId", "visible", "active", "maximumAdditional",
            "listSpaceAvailable", "usageAffordable", "addAvailable", "removeAvailable",
        },
        "harvest-action-controls" => new[]
        {
            "elementId", "actionId", "visible", "active", "maximum",
            "addAvailable", "removeAvailable",
        },
        "harvest-lifecycle-costs" => new[]
        {
            "elementId", "actionId", "kind", "resourceId", "amount",
        },
        "time-runes" => new[]
        {
            "entityId", "discovered", "level", "masteryLevel", "masteryXp", "seen",
        },
        "glyphs" => new[]
        {
            "entityId", "level", "freeLevels", "discovered", "discoverable",
            "maxUsages",
        },
        "consumables" => new[]
        {
            "entityId", "visible", "quantity", "queuedQuantity", "maximumCarryLoad",
            "currentPrepTime", "currentCooldownTime", "canFire",
        },
        "rituals" => new[]
        {
            "entityId", "discovered", "inBattle", "activeInstances", "reachedLevel",
            "selectedLevel", "wavesCompleted",
        },
        "achievements" =>
            new[] { "entityId", "level", "seen", "maxLevels" },
        "advancements" =>
            new[] { "entityId", "levels", "xp", "isPersistent" },
        "challenges" => new[]
        {
            "entityId", "level", "state", "seen", "rewardQueued", "maxLevel",
            "difficulty",
        },
        "thought-streams" => new[] { "entityId", "state" },
        "tutorials" => new[] { "entityId", "isCompleted" },
        "views" => new[] { "entityId", "active", "alwaysActive", "available" },
        "plot-node-actions" => new[]
        {
            "entityId", "hasBeenUsed", "isGrowingAction", "elementCost", "baseTime",
            "parallelAction", "prerequisiteCount", "resourceCostCount",
        },
        "passive-abilities" => new[]
        {
            "entityId", "muted", "touched", "hidden", "global", "tokenRate",
        },
        "characters" =>
            new[] { "entityId", "discovered", "numberSlain", "floats" },
        "discovery-trees" => new[]
        {
            "entityId", "actionMode", "actionTime", "rerollsLeft",
            "totalDiscoveredCount", "hasRemainingDiscovery",
            "hasCompletedAllDiscoveries", "selectedChoiceId",
        },
        "recipe-books" => new[] { "entityId", "available" },
        "plot-nodes" => new[]
        {
            "entityId", "reading.visible", "reading.masteryLevel",
            "reading.currentTime", "reading.idleQuantity", "reading.totalQuantity",
            "remainingQuantity", "remainingTotalQuantity",
        },
        "plot-actions" => new[]
        {
            "plotNodeId", "plotNodeActionId", "reading.offeredCount",
            "reading.instanceCount", "reading.prerequisitesConfirmed", "elementCost",
            "elementCostKnown", "hasEnoughForOneInstance",
            "maximumRemainingInstances",
        },
        "plot-action-instances" => new[]
        {
            "plotNodeId", "plotNodeActionId", "ordinal", "quantity", "engaged",
            "empty", "referenceResolved",
        },
        "action-queues" => new[]
        {
            "entityId", "queueId", "slotCount", "usedSlots", "emptySlots",
            "hasEmptySlot", "consistent",
        },
        "action-queue-slots" => new[]
        {
            "queueId", "index", "empty", "plotNodeId", "plotNodeActionId",
            "quantity", "engaged",
        },
        "spell-slots" => new[]
        {
            "slotIndex", "spellRecipeId", "occupied",
            "casting", "readyingCast", "attuning", "toggled", "castReady",
            "chargeAvailable", "resourcesCovered", "currentCharges",
            "maximumCharges", "cooldownRemaining",
        },
        "spell-costs" => new[] { "slotIndex", "kind", "resourceId", "amount" },
        "targeting" => new[]
        {
            "ownerName", "ownerNativeType", "selectionNativeType", "cancelAvailable",
            "candidates.position", "candidates.structureId",
        },
        "mastery-experience" => new[]
        {
            "sequence", "domain", "sourceId", "sourceMastery", "sourceEligible",
            "amount",
        },
        "concept-recipes" =>
            new[] { "recipeId", "coreTypeId", "canAddNow" },
        "alchemy-instances" => new[]
        {
            "recipeId", "quantity", "queuedQuantity", "drainReadable", "drainRatio",
        },
        "alchemy-costs" => new[] { "recipeId", "kind", "resourceId", "amount" },
        "plot-authoring" => new[] { "plotNodeId", "autoActionId", "phaseCount" },
        "plot-phase-descriptors" => new[]
        {
            "plotNodeId", "ordinal", "phase", "phaseTimeSeconds", "processType",
            "exitPhase",
        },
        "effect-blocks" => new[]
        {
            "ownerId", "ordinal", "blockTypeName", "effectTypeName", "effectValue",
            "treasurePoolId", "prerequisiteCount", "modCount", "scriptCount",
        },
        "entity-requirements" => new[]
        {
            "ownerId", "ownerKind", "ordinal", "kind", "conditionTypeName",
            "targetId", "reqType", "baseValue",
        },
        "treasure-pools" => new[]
        {
            "entityId", "poolId", "treasuresFound", "partialReward",
            "treasureLevel", "calculatedTreasureLevel",
        },
        _ => throw new InvalidOperationException(
            "world category '" + category + "' has no deliberate MCP scan projection"),
    };

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new System.Text.StringBuilder(value.Length + 8);
        var previousWasSeparator = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '-' || character == '_' || char.IsWhiteSpace(character))
            {
                if (!previousWasSeparator) result.Append('-');
                previousWasSeparator = true;
                continue;
            }
            if (char.IsUpper(character) && !previousWasSeparator && result.Length > 0)
                result.Append('-');
            result.Append(char.ToLowerInvariant(character));
            previousWasSeparator = false;
        }
        if (result.Length > 0 && result[result.Length - 1] == '-')
            result.Length--;
        return result.ToString();
    }

    internal static int DefaultLimit => DefaultPageSize;

    internal static string[] RegisteredCategoryNames()
    {
        var result = new string[Categories.Length];
        for (var index = 0; index < Categories.Length; index++)
            result[index] = Categories[index].Name;
        return result;
    }

    private abstract class GameMcpWorldCategory
    {
        protected GameMcpWorldCategory(
            string publicName,
            string propertyName,
            string rowTypeName,
            string expectedNativeType,
            string[] reportCategories,
            string[] failureOnlyReportCategories,
            string[] scanFields,
            string identityMode)
        {
            WorldPropertyName = propertyName;
            Name = Normalize(publicName);
            RowTypeName = rowTypeName;
            ExpectedNativeType = expectedNativeType;
            ReportCategories = reportCategories ??
                throw new ArgumentNullException(nameof(reportCategories));
            if (ReportCategories.Length == 0)
                throw new ArgumentException(
                    "A world category requires at least one collection report.",
                    nameof(reportCategories));
            FailureOnlyReportCategories = failureOnlyReportCategories ??
                throw new ArgumentNullException(nameof(failureOnlyReportCategories));
            ScanFields = scanFields ??
                throw new ArgumentNullException(nameof(scanFields));
            if (ScanFields.Length == 0)
                throw new ArgumentException(
                    "A world category requires a deliberate MCP scan projection.",
                    nameof(scanFields));
            IdentityMode = identityMode;
        }

        internal string Name { get; }
        internal string WorldPropertyName { get; }
        internal string RowTypeName { get; }
        internal string ExpectedNativeType { get; }
        internal string[] ReportCategories { get; }
        internal string[] FailureOnlyReportCategories { get; }
        internal string[] ScanFields { get; }
        internal string IdentityMode { get; }
        internal abstract int Count(GameWorldState world);
        internal abstract object Row(GameWorldState world, int index);
        internal abstract bool TryIdentity(object row, out Guid identity);
    }

    private sealed class GameMcpEntityCategory<TRow> : GameMcpWorldCategory
        where TRow : struct, IWorldEntity
    {
        private readonly Func<GameWorldState, PublicationTable<TRow>> _table;

        internal GameMcpEntityCategory(
            string publicName,
            string propertyName,
            Func<GameWorldState, PublicationTable<TRow>> table,
            string expectedNativeType,
            string[] reportCategories,
            string[] failureOnlyReportCategories,
            string[] scanFields)
            : base(
                publicName,
                propertyName,
                typeof(TRow).Name,
                expectedNativeType,
                reportCategories,
                failureOnlyReportCategories,
                scanFields,
                "stable_entity_uuid")
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
        }

        internal override int Count(GameWorldState world) => _table(world).Count;
        internal override object Row(GameWorldState world, int index) => _table(world)[index];
        internal override bool TryIdentity(object row, out Guid identity)
        {
            if (row is TRow typed)
            {
                identity = typed.EntityId;
                return identity != Guid.Empty;
            }
            identity = Guid.Empty;
            return false;
        }
    }

    private sealed class GameMcpCompositeCategory<TRow> : GameMcpWorldCategory
        where TRow : struct
    {
        private readonly Func<GameWorldState, PublicationTable<TRow>> _table;

        internal GameMcpCompositeCategory(
            string publicName,
            string propertyName,
            Func<GameWorldState, PublicationTable<TRow>> table,
            string expectedNativeType,
            string[] reportCategories,
            string[] failureOnlyReportCategories,
            string[] scanFields)
            : base(
                publicName,
                propertyName,
                typeof(TRow).Name,
                expectedNativeType,
                reportCategories,
                failureOnlyReportCategories,
                scanFields,
                "composite_guid_fields")
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
        }

        internal override int Count(GameWorldState world) => _table(world).Count;
        internal override object Row(GameWorldState world, int index) => _table(world)[index];
        internal override bool TryIdentity(object row, out Guid identity)
        {
            identity = Guid.Empty;
            return false;
        }
    }
}
#endif
