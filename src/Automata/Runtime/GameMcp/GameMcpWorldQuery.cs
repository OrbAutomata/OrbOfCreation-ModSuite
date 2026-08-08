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
            ["equippedSpellSlots"] = world.SpellSlots.Count,
            ["activeConceptAssignments"] = world.AlchemyInstances.Count,
        };
        if (world.SpellWorkbench.MaximumOutputLevel > 0)
            result["casting"] = new JObject
            {
                ["output"] = new JObject
                {
                    ["current"] = world.SpellWorkbench.OutputLevel,
                    ["minimum"] = WorldSpellWorkbench.MinimumDialLevel,
                    ["maximum"] = world.SpellWorkbench.MaximumOutputLevel,
                },
                ["reserve"] = new JObject
                {
                    ["current"] = world.SpellWorkbench.ReserveLevel,
                    ["minimum"] = WorldSpellWorkbench.MinimumDialLevel,
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
        int limit)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        if (!TryCategory(categoryName, out var category, out var reason))
            return NotAvailable(publication, "unknown_category", reason);
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
        var count = category.Count(world);
        var rows = new JArray();
        var requestedEnd = Math.Min(count, checked(offset + limit));
        var end = offset;
        var estimatedBytes = 128;
        for (var index = offset; index < requestedEnd; index++)
        {
            var row = category.Row(world, index);
            var rowBytes = EstimateListRowBytes(world, category, row);
            if (rows.Count > 0 && estimatedBytes + rowBytes > MaximumListResponseBytes)
                break;
            var projected = ProjectListRow(world, category, row);
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
            end = index + 1;
        }

        // One pagination rule: nextOffset present means more rows remain and names where to
        // resume. A page shorter than the limit with a nextOffset is the byte budget; a page
        // shorter than the limit without one is the end of the category.
        var result = Envelope(publication);
        result["rows"] = rows;
        result["total"] = count;
        if (end < count) result["nextOffset"] = end;
        if (string.Equals(category.Name, "challenges", StringComparison.Ordinal))
            result["challengeState"] = ProjectChallengeState(world);
        return result;
    }

    private static GameMcpValue ProjectListRow(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row) =>
        WithOwnIdentity(category, ProjectListRowFields(world, category, row));

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
                ["enabled"] = !structure.Reading.Disabled,
            };
            if (TryPurchaseAffordability(world, structure.EntityId, out var affordable))
                projected["affordable"] = affordable;
            return projected.Freeze();
        }
        if (row is WorldUpgrade upgrade)
        {
            var projected = new JObject
            {
                ["entityId"] = upgrade.EntityId.ToString("D"),
                ["level"] = upgrade.Reading.Level,
                ["queuedLevels"] = upgrade.Reading.QueuedLevels,
            };

            // An exhausted upgrade has no next level, so it has no price to be short of.
            if (!upgrade.IsExhausted &&
                TryPurchaseAffordability(world, upgrade.EntityId, out var upgradeAffordable))
            {
                projected["affordable"] = upgradeAffordable;
            }

            // The ceiling and the distance to it travel together on every surface, so a row
            // missing them means the upgrade is uncapped and never means this surface is lean.
            if (upgrade.IsBounded)
            {
                projected["maxLevel"] = upgrade.Reading.MaxLevel;
                projected["remainingLevels"] = upgrade.RemainingLevels;
            }
            projected["available"] = upgrade.Reading.Available && !upgrade.IsExhausted;
            if (upgrade.IsExhausted) projected["reasonCode"] = "already_maxed";
            return projected.Freeze();
        }
        if (row is WorldEquipment equipment)
            return new JObject
            {
                ["entityId"] = equipment.EntityId.ToString("D"),
                ["equippedCount"] = equipment.EquippedLevel,
            }.Freeze();
        if (row is WorldGlyph glyph)
            return new JObject
            {
                ["entityId"] = glyph.EntityId.ToString("D"),
                ["discovered"] = glyph.Discovered,
                ["available"] = glyph.Learned,
                ["paidLevel"] = glyph.LevelDecision.TotalLevel - glyph.LevelDecision.BonusLevels,
                ["bonusLevel"] = glyph.LevelDecision.BonusLevels,
                ["totalLevel"] = glyph.LevelDecision.TotalLevel,
            }.Freeze();
        if (row is WorldPlotNode plot)
            return new JObject
            {
                ["entityId"] = plot.EntityId.ToString("D"),
                ["visible"] = plot.Reading.Visible,
                ["masteryLevel"] = plot.Reading.MasteryLevel,
                ["quantity"] = plot.Reading.TotalQuantity,
                ["idleQuantity"] = plot.Reading.IdleQuantity,
                ["availableQuantity"] = plot.RemainingTotalQuantity,
            }.Freeze();
        if (row is WorldPurchaseCost purchaseCost)
        {
            var projected = ProjectPurchaseCost(world, in purchaseCost);
            projected["targetId"] = purchaseCost.EntityId.ToString("D");
            return projected.Freeze();
        }
        if (row is WorldTargetingRequest targeting)
            return ProjectTargeting(world, in targeting);
        if (row is WorldChallenge challenge)
            return new JObject
            {
                ["entityId"] = challenge.EntityId.ToString("D"),
                ["state"] = ChallengeState(challenge.State),
                ["level"] = new GameMcpDomainValue(new BigDouble(challenge.Level)),
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
        if (row is WorldPlayerLoadout playerLoadout)
            return new JObject
            {
                ["uuid"] = playerLoadout.EntityId.ToString("D"),
                ["name"] = playerLoadout.Name,
                ["selected"] = playerLoadout.Selected,
            }.Freeze();
        if (row is WorldSnapshotLoadout snapshotLoadout)
        {
            var identity = EntityIdentityFormatter.Describe(
                snapshotLoadout.EntityId, world.EntityIdentities);
            return new JObject
            {
                ["uuid"] = snapshotLoadout.EntityId.ToString("D"),
                ["name"] = identity.HasName
                    ? identity.Name
                    : SnapshotKind(snapshotLoadout.Kind) + " snapshots",
                ["kind"] = SnapshotKind(snapshotLoadout.Kind),
                ["slots"] = snapshotLoadout.Slots,
            }.Freeze();
        }
        if (row is WorldResource resource)
            return ProjectResource(world, in resource);
        if (row is WorldAlchemyInstance alchemyInstance)
            return ProjectAlchemyInstance(world, in alchemyInstance);
        if (row is WorldActionQueueSlot processingSlot)
            return ProjectAgromancyProcessing(world, in processingSlot);
        if (row is WorldPlotAction plotAction)
            return ProjectPlotAction(world, in plotAction);
        return new GameMcpProjectedDomainValue(
            row,
            ListFields(category),
            category.Name,
            category.ExpectedNativeType);
    }

    private static GameMcpValue ProjectCraftingQueueEntry(
        in WorldCraftingQueueEntry entry)
    {
        var result = new JObject
        {
            ["queueId"] = entry.QueueId,
            ["slot"] = entry.Slot,
            ["recipeId"] = entry.RecipeId,
            ["amount"] = new GameMcpDomainValue(entry.Amount),
            ["automatic"] = entry.Automatic,
        };
        if (entry.Automatic) result["repetitions"] = entry.Repetitions;
        return result.Freeze();
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

    private static string[] ListFields(GameMcpWorldCategory category) => category.Name switch
    {
        "resources" => new[] { "entityId", "trueQuantity" },
        "structures" => new[] { "entityId", "level", "reading.disabled" },
        "upgrades" => new[] { "entityId", "level" },
        "research" => new[] { "entityId", "totalLevel", "complete" },
        "spell-recipes" => new[] { "entityId", "masteryLevel", "discovered" },
        "alchemy-recipes" => new[] { "entityId", "masteryLevel", "discovered" },
        "equipment" => new[] { "entityId", "equippedLevel" },
        "glyphs" => new[] { "entityId", "level" },
        "consumables" => new[] { "entityId", "quantity" },
        "crafting-recipes" => new[] { "entityId", "reading.startingQuantity" },
        "crafting-queue-entries" => new[]
        {
            "queueId", "slot", "recipeId", "amount", "automatic", "repetitions",
        },
        "plot-nodes" => new[] { "entityId", "reading.masteryLevel" },
        "discovery-trees" => new[] { "entityId", "actionMode" },
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

    private static int EstimateListRowBytes(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row)
    {
        var bytes = 512 + 64 * ListFields(category).Length;
        if (!category.TryIdentity(row, out var identity)) return bytes;
        var name = EntityIdentityFormatter.Describe(identity, world.EntityIdentities).Name;
        return checked(bytes + Encoding.UTF8.GetByteCount(name));
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
            return NotAvailable(publication, "invalid_uuid", "uuid must be a canonical D-format GUID");
        if (!string.Equals(
                category.IdentityMode,
                "stable_entity_uuid",
                StringComparison.Ordinal))
        {
            return NotAvailable(
                publication,
                "composite_identity_required",
                "category " + category.Name + " has composite identity fields and cannot " +
                "be uniquely addressed by one UUID; use world_list to read its exact rows");
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
                        ? "this entity has incomplete published requirement evidence"
                        : "this discovery tree has an offer UUID absent from all published " +
                          "explainable entity categories";
                result["partialRow"] = ProjectRow(publication.Snapshot, category, row);
                if (implicated.Count > 0) result["implicatedSkippedRows"] = implicated;
                if (implicatedOffers.Count > 0) result["implicatedOffers"] = implicatedOffers;
            }
            if (string.Equals(category.Name, "challenges", StringComparison.Ordinal))
                result["challengeState"] = ProjectChallengeState(publication.Snapshot);
            return result;
        }

        return NotAvailable(
            publication,
            "unknown_uuid",
            "category " + category.Name + " has no row with stable identity " +
            uuid.ToString("D"));
    }

    /// <summary>
    /// Resolve many stable identities from the one immutable publication already pinned by the
    /// router. Results preserve input order; only a failing row repeats its UUID because that
    /// identity is needed to act on the failure. No publication token or state survives this call.
    /// </summary>
    internal static JObject GetRows(
        GameMcpFrameContext state,
        string categoryName,
        IReadOnlyList<string> uuidTexts)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return BatchUnavailable(unavailable);
        if (!TryCategory(categoryName, out var category, out var reason))
            return BatchUnavailable(NotAvailable(publication, "unknown_category", reason));
        if (uuidTexts is null || uuidTexts.Count == 0 || uuidTexts.Count > MaximumBatchSize)
        {
            return BatchUnavailable(NotAvailable(
                publication,
                "invalid_batch_size",
                "uuids must contain between 1 and " +
                MaximumBatchSize.ToString(CultureInfo.InvariantCulture) + " entries"));
        }
        if (!string.Equals(
                category.IdentityMode,
                "stable_entity_uuid",
                StringComparison.Ordinal))
        {
            return BatchUnavailable(NotAvailable(
                publication,
                "composite_identity_required",
                "category " + category.Name + " has composite identity fields and cannot " +
                "be addressed by UUID; use world_list to read its exact rows"));
        }

        var availability = Availability(publication.Snapshot, category);
        if (!availability.Available)
        {
            return BatchUnavailable(NotAvailable(
                publication,
                "category_not_collected",
                availability.Reason.Length == 0
                    ? "the category was not collected"
                    : availability.Reason));
        }

        var results = new JArray();
        for (var inputIndex = 0; inputIndex < uuidTexts.Count; inputIndex++)
        {
            var uuidText = uuidTexts[inputIndex] ?? string.Empty;
            var item = new JObject();
            if (!Guid.TryParseExact(uuidText, "D", out var uuid) || uuid == Guid.Empty)
            {
                item["status"] = "not_available";
                item["code"] = "invalid_uuid";
                item["reason"] = "uuid must be a non-empty canonical D-format GUID";
                item["uuid"] = uuidText;
                results.Add(item);
                continue;
            }

            object? matched = null;
            var count = category.Count(publication.Snapshot);
            for (var rowIndex = 0; rowIndex < count; rowIndex++)
            {
                var row = category.Row(publication.Snapshot, rowIndex);
                if (!category.TryIdentity(row, out var rowIdentity) || rowIdentity != uuid)
                    continue;
                matched = row;
                break;
            }
            if (matched is null)
            {
                item["status"] = "not_available";
                item["code"] = "unknown_uuid";
                item["reason"] = "category " + category.Name +
                    " has no row with stable identity " + uuid.ToString("D");
                item["uuid"] = uuid.ToString("D");
            }
            else
            {
                var implicated = LocalizedRequirementImplications(
                    publication.Snapshot,
                    new HashSet<Guid> { uuid });
                var implicatedOffers = LocalizedDiscoveryOfferImplications(
                    publication.Snapshot,
                    new HashSet<Guid> { uuid });
                if (implicated.Count == 0 && implicatedOffers.Count == 0)
                {
                    item["row"] = ProjectRow(publication.Snapshot, category, matched);
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
                    item["partialRow"] = ProjectRow(publication.Snapshot, category, matched);
                    if (implicated.Count > 0) item["implicatedSkippedRows"] = implicated;
                    if (implicatedOffers.Count > 0) item["implicatedOffers"] = implicatedOffers;
                }
            }
            results.Add(item);
        }

        var result = Envelope(publication);
        result["results"] = results;
        if (string.Equals(category.Name, "challenges", StringComparison.Ordinal))
            result["challengeState"] = ProjectChallengeState(publication.Snapshot);
        return result;
    }

    private static JObject BatchUnavailable(JObject result)
    {
        result["results"] = new JArray();
        return result;
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
    internal static GameMcpValue ProjectGameplayPostState(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpCommandResult committed) =>
        WithPaid(state, command, ProjectChangedFact(state, command, committed));

    /// <summary>
    /// What a committed purchase was priced at and what the settled world leaves.
    /// <c>costPerLevel</c> is the admission capture's next-level price — the same number the
    /// caller's cost row and the unaffordable sentence read — and it is per level on purpose: a
    /// call may commit up to its whole requested amount, and the price rises with every level it
    /// takes. The capture prices the levels the game's own multi-buy variable was set to, never the
    /// count this call ended up committing, so the sum for that count is not a number the suite
    /// holds; <c>level {before, after}</c> is what says how many levels were bought.
    /// <c>remaining</c> is read from the settled world. Neither is derived by subtracting one world
    /// from another: an income stream or a second spender in the same window would land in that
    /// difference.
    /// </summary>
    private static GameMcpValue WithPaid(
        GameMcpFrameContext state,
        GameMcpCommand command,
        GameMcpValue projected)
    {
        // Only a purchase is admitted against a price it then charges. Every other kind reaching a
        // priced target — the free game_structure toggle above all — pays nothing for it.
        if (state.World is null || command.Kind != GameMcpCommandKind.Purchase) return projected;
        var before = Before(command);
        if (before is null) return projected;
        var after = state.World.Snapshot;
        if (!WorldPurchaseCostLookup.TryFindRange(
                before.PurchaseCosts, command.TargetId, out var start, out var count))
            return projected;
        var paid = new JArray();
        for (var index = start; index < start + count; index++)
        {
            var cost = before.PurchaseCosts[index];
            var row = new JObject
            {
                ["resource"] = cost.ResourceId.ToString("D"),
                ["costPerLevel"] = new GameMcpDomainValue(AdmittedCost(before, in cost)),
            };

            // Zero is a balance. A settled world that carries no row for this resource has not told
            // us the player is broke, so the absence is named instead of spent as a number.
            if (TryFindResource(after, cost.ResourceId, out var settled))
                row["remaining"] = new GameMcpDomainValue(
                    WorldResourceCoordinate.SpendableAmount(in settled));
            else
                row["remainingUnavailable"] = new JObject
                {
                    ["reasonCode"] = "resource_not_published",
                    ["reason"] = "the settled world carries no row for this resource",
                };
            paid.Add(row);
        }
        if (paid.Count == 0) return projected;
        var result = new JObject();
        if (projected is GameMcpObject existing) result.CopyFrom(existing);
        else result["result"] = projected;
        result["paid"] = paid;
        return result.Freeze();
    }

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
            GameMcpCommandKind.Purchase => ProjectPurchaseDelta(state, command),
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
            _ => ProjectPostState(state, PostStateCategory(command), command.TargetId),
        };

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
            ["slot"] = slotIndex,
        };
        if (string.Equals(command.Mode, "fire", StringComparison.Ordinal))
        {
            var costs = ProjectEquippedSpellCosts(
                state.World.Snapshot, slotIndex, WorldSpellCostKind.Immediate);
            if (costs.Count > 0) result["costs"] = costs;
        }
        // A toggle spell always says whether it is running: publishing the pair only when it moved
        // meant a second fire on an already-running spell said nothing, and silence there is
        // indistinguishable from a response that does not carry the fact at all. A non-toggle spell
        // has no running state to report while it sits idle, so it reports one exactly when it
        // moved — the fact still ships wherever it exists.
        if (after.Toggled || (hasBefore && prior.Casting != after.Casting))
        {
            result["active"] = new JObject
            {
                ["before"] = hasBefore ? prior.Casting : (bool?)null,
                ["after"] = after.Casting,
            };
        }
        if (hasBefore && prior.CurrentCharges != after.CurrentCharges)
        {
            result["charges"] = new JObject
            {
                ["before"] = prior.CurrentCharges,
                ["after"] = after.CurrentCharges,
            };
        }
        result["ready"] = after.CastReady;
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
            ["minimum"] = WorldSpellWorkbench.MinimumDialLevel,
            ["maximum"] = output ? after.MaximumOutputLevel : after.MaximumReserveLevel,
        }.Freeze();
    }

    private static GameMcpValue ProjectPurchaseDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var after = state.World.Snapshot;
        var before = Before(command);
        if (command.Mode == "structure" &&
            WorldLookup.TryFind(after.Structures, command.TargetId, out var afterStructure))
        {
            WorldStructure oldStructure = default;
            var hadStructure = before is not null &&
                WorldLookup.TryFind(before.Structures, command.TargetId, out oldStructure);
            return PurchaseChange(
                command.TargetId,
                hadStructure ? oldStructure.Reading.Quantity : null,
                afterStructure.Reading.Quantity,
                hadStructure ? oldStructure.Reading.QueuedLevels.ToInt() : null,
                afterStructure.Reading.QueuedLevels.ToInt());
        }
        if (WorldLookup.TryFind(after.Upgrades, command.TargetId, out var afterUpgrade))
        {
            WorldUpgrade oldUpgrade = default;
            var hadUpgrade = before is not null &&
                WorldLookup.TryFind(before.Upgrades, command.TargetId, out oldUpgrade);
            return PurchaseChange(
                command.TargetId,
                hadUpgrade ? oldUpgrade.Reading.Level : null,
                afterUpgrade.Reading.Level,
                hadUpgrade ? oldUpgrade.Reading.QueuedLevels : null,
                afterUpgrade.Reading.QueuedLevels);
        }
        return PostStateUnavailable("post_state_not_published",
            "the settled world has no purchased target row");
    }

    /// <summary>
    /// What a purchase settled, in the two counts the screen owns. A level that lands immediately
    /// moves <c>level</c>; a level that has to be built moves <c>queuedLevels</c> and leaves the
    /// badge where it was, so publishing only one of them is how a caller ends up reading a pair
    /// that never moved. <c>level</c> keeps the single meaning it carries on every read — the
    /// badge's own count, never the sum of built and building behind it.
    /// </summary>
    private static GameMcpValue PurchaseChange(
        Guid uuid,
        int? levelBefore,
        int levelAfter,
        int? queuedBefore,
        int queuedAfter) => new JObject
    {
        ["uuid"] = uuid.ToString("D"),
        ["level"] = new JObject { ["before"] = levelBefore, ["after"] = levelAfter },
        ["queuedLevels"] = new JObject
        {
            ["before"] = queuedBefore,
            ["after"] = queuedAfter,
        },
    }.Freeze();

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
                    ["before"] = hadBefore ? previous.Position : (int?)null,
                    ["after"] = current.Position,
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

            // Every other ritual mode answers with what the caller can do next. End is the mode that
            // most needs it, because it is the one that reopens selecting, levelling and activating.
            var afterBattle = new JObject();
            AddRitualDecision(world, afterBattle, in current);
            postState["next"] = afterBattle;
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
        var next = new JObject();
        AddRitualDecision(world, next, in current);
        result["next"] = next;
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
                ["next"] = ProjectHarvestElementDecision(world, in current),
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
            ["next"] = ProjectHarvestActionDecision(world, in action),
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
        }
        result["totalLevel"] = new JObject
        {
            ["before"] = hadBefore ? previous.TotalLevel : (int?)null,
            ["after"] = current.TotalLevel,
        };

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
                    ["slot"] = command.Amount - 1,
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
        var next = new JObject();
        AddCraftingStationDecision(state.World.Snapshot, next, in station);
        result["next"] = next;
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
            var result = new JObject
            {
                ["uuid"] = current.EntityId.ToString("D"),
                ["name"] = current.Name,
            };
            switch (command.Mode)
            {
                case "select":
                    result["selected"] = new JObject
                    {
                        ["before"] = hadBefore && previous.Selected,
                        ["after"] = current.Selected,
                    };
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
            ["uuid"] = owner.EntityId.ToString("D"),
            ["name"] = SnapshotOwnerName(world, in owner),
            ["kind"] = SnapshotKind(owner.Kind),
            ["snapshot"] = snapshot,
        };
        var internalName = SnapshotOwnerInternalName(world, in owner);
        if (internalName.Length > 0) response["internalName"] = internalName;
        if (command.Mode == "snapshot_load")
            response["active"] = ProjectActiveLoadoutSection(world, owner.Kind);
        return response.Freeze();
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
                var row = new JObject
                {
                    ["instanceUuid"] = entry.EntryId.ToString("D"),
                    ["spell"] = EntityReference(world, entry.ReferenceId),
                };
                spells.Add(row);
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
            ["uuid"] = loadout.EntityId.ToString("D"),
            ["name"] = loadout.Name,
            ["category"] = "player-loadouts",
            ["nativeType"] = "PlayerLoadout",
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
            ["uuid"] = owner.EntityId.ToString("D"),
            ["name"] = SnapshotOwnerName(world, in owner),
            ["category"] = "snapshot-loadouts",
            ["nativeType"] = owner.Kind == WorldSnapshotLoadoutKind.Alchemy
                ? "AlchemySnapshotListVariable"
                : "EquipmentSnapshotListVariable",
            ["kind"] = SnapshotKind(owner.Kind),
            ["slots"] = slots,
        };
        var internalName = SnapshotOwnerInternalName(world, in owner);
        if (internalName.Length > 0) result["internalName"] = internalName;
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
            ["slot"] = slot,
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
        var result = new JObject
        {
            ["uuid"] = id.ToString("D"),
            ["name"] = identity.HasName ? identity.Name : id.ToString("D"),
        };
        if (identity.AssetName.Length > 0 &&
            !string.Equals(identity.AssetName, identity.Name, StringComparison.Ordinal))
            result["internalName"] = identity.AssetName;
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

    private static string SnapshotOwnerInternalName(
        GameWorldState world,
        in WorldSnapshotLoadout owner)
    {
        var identity = EntityIdentityFormatter.Describe(owner.EntityId, world.EntityIdentities);
        return identity.Source == EntityIdentityNameSource.LiveAssetName
            ? identity.Name
            : identity.AssetName;
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
                ["next"] = ProjectPlotActionDecision(world, in current, after),
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
            ["next"] = ProjectPlotActionDecision(world, in current, after),
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
        // What a develop, cancel, pause, or resume settles is the queue and the entry's state. A
        // level takes research time to finish, so the level count is not what these verbs move —
        // publishing it as an unconditional pair meant every commit reported the same number twice
        // while the count that did move was nowhere on the wire. It is carried only when it moved,
        // which is what an instantly-finishing level looks like.
        var result = new JObject
        {
            ["uuid"] = command.TargetId.ToString("D"),
            ["state"] = new JObject
            {
                ["before"] = hasPrevious ? ResearchState(previous) : null,
                ["after"] = ResearchState(current),
            },
            ["queuedLevels"] = new JObject
            {
                ["before"] = hasPrevious ? ResearchQueuedLevels(previous) : (int?)null,
                ["after"] = ResearchQueuedLevels(current),
            },
        };
        if (hasPrevious && previous.TotalLevel != current.TotalLevel)
        {
            result["totalLevel"] = new JObject
            {
                ["before"] = previous.TotalLevel,
                ["after"] = current.TotalLevel,
            };
        }
        return result.Freeze();
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

    private static string ResearchState(in WorldResearch research) =>
        research.Complete ? "complete" :
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
            "use" or "cancel" => Change(
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
        if (!hasBefore) return Change(command.TargetId, null, settled, "queued");
        var started = previous.QueuedAmount.ToInt();
        if (settled > started) return Change(command.TargetId, started, settled, "queued");
        // An unmoved queue count is the same number before and after the craft, which proves
        // nothing about it. Say what the settled world shows instead of publishing the pre-state.
        return PostStateUnavailable(
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
                    ["slot"] = new JObject { ["before"] = null, ["after"] = slot.SlotIndex },
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
                    ["slot"] = new JObject { ["before"] = oldSlot.SlotIndex, ["after"] = null },
                }.Freeze();
            if (command.Mode == "move" && hadBefore && hasAfter)
                return new JObject
                {
                    ["uuid"] = newSlot.SpellRecipeId.ToString("D"),
                    ["slot"] = new JObject
                    {
                        ["before"] = oldSlot.SlotIndex,
                        ["after"] = newSlot.SlotIndex,
                    },
                }.Freeze();
        }
        return PostStateUnavailable("requested_state_not_reached",
            "the settled loadout does not show the requested add, remove, or move");
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
            return new JObject { ["challengeState"] = ProjectChallengeState(world) }.Freeze();
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
            ? ChallengeState(previous.State)
            : null;
        return Change(
            command.TargetId,
            beforeState,
            ChallengeState(current.State),
            "state");
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
            ["scene"] = state.SceneName,
            ["prestigeState"] = ProjectPrestigeState(world),
            ["challengeState"] = ProjectChallengeState(world),
        };
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

    internal static JObject Search(
        GameMcpFrameContext state,
        string query,
        int offset,
        int limit)
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

        // Search is deliberately an entity-catalog surface. Composite diagnostic categories are
        // readable through world_list, where their full identity and localized partiality survive.
        // One entity is one match however many categories publish it: identity is deduplicated
        // before paging, so a repeat can never eat a slot the caller paid for.
        var rows = new JArray();
        var totalMatches = 0;
        var scanned = 0;
        var seen = new HashSet<Guid>();
        var estimatedBytes = 128;
        var byteBudgetReached = false;
        var unavailableCategories = new JArray();
        for (var categoryIndex = 0; categoryIndex < Categories.Length; categoryIndex++)
        {
            var category = Categories[categoryIndex];
            if (!string.Equals(
                    category.IdentityMode,
                    "stable_entity_uuid",
                    StringComparison.Ordinal))
            {
                continue;
            }
            var availability = Availability(publication.Snapshot, category);
            if (!availability.Available)
            {
                unavailableCategories.Add(new JObject
                {
                    ["category"] = category.Name,
                    ["reason"] = availability.Reason,
                });
                continue;
            }
            var count = category.Count(publication.Snapshot);
            for (var rowIndex = 0; rowIndex < count; rowIndex++)
            {
                var row = category.Row(publication.Snapshot, rowIndex);
                if (!Matches(publication.Snapshot, category, row, normalized)) continue;
                if (!category.TryIdentity(row, out var identity)) continue;
                if (!seen.Add(identity)) continue;
                totalMatches++;
                scanned++;
                if (scanned <= offset || rows.Count >= limit || byteBudgetReached) continue;
                var match = new JObject
                {
                    ["uuid"] = identity.ToString("D"),
                    ["category"] = category.Name,
                    ["nativeType"] = category.ExpectedNativeType,
                };
                var local = LocalizedRequirementImplications(
                    publication.Snapshot, new HashSet<Guid> { identity });
                var localOffers = LocalizedDiscoveryOfferImplications(
                    publication.Snapshot, new HashSet<Guid> { identity });
                if (local.Count > 0 || localOffers.Count > 0)
                {
                    match["status"] = "not_available";
                    match["code"] = local.Count > 0
                        ? "entity_data_incomplete"
                        : "discovery_offer_read_incomplete";
                    match["reason"] = local.Count > 0
                        ? "this match has incomplete published requirement evidence"
                        : "this discovery tree has an offer absent from the published entity rows";
                    if (local.Count > 0) match["implicatedSkippedRows"] = local;
                    if (localOffers.Count > 0) match["implicatedOffers"] = localOffers;
                }
                var matchBytes = 192 + local.Count * 128 + localOffers.Count * 128;
                if (rows.Count > 0 && estimatedBytes + matchBytes > MaximumListResponseBytes)
                {
                    byteBudgetReached = true;
                    continue;
                }
                estimatedBytes += matchBytes;
                rows.Add(match);
            }
        }
        var result = Envelope(publication);
        result["total"] = totalMatches;
        if (unavailableCategories.Count > 0)
            result["unavailableCategories"] = unavailableCategories;
        result["rows"] = rows;
        if (offset + rows.Count < totalMatches) result["nextOffset"] = offset + rows.Count;
        return result;
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
        switch (state.LifecycleState)
        {
            case GameLifecycleState.NoGame:
                code = "lifecycle_no_game";
                reason = "no save is loaded, so there is no world to read.";
                return;
            case GameLifecycleState.Initializing:
                code = "lifecycle_initializing";
                reason = "the save is still loading, so no world has been collected yet.";
                return;
            case GameLifecycleState.Resetting:
                code = "lifecycle_resetting";
                reason =
                    "a save load, reset, or new game plus is replacing the run, so the previous " +
                    "world was dropped and the next one has not been collected yet.";
                return;
            case GameLifecycleState.SceneExit:
                code = "lifecycle_scene_exit";
                reason = "the play scene is unloading, so the world it was read from is gone.";
                return;
            default:
                code = "world_not_published";
                reason = state.RuntimeNotAvailableReason.Length == 0
                    ? "the world collector has not published a captured world yet"
                    : state.RuntimeNotAvailableReason;
                return;
        }
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
        if (TryLocalizedRequirementFailures(world, out var implicated, out _))
        {
            var skipped = new JArray();
            for (var index = 0; index < implicated.Length; index++)
            {
                var leaf = implicated[index];
                skipped.Add(new JObject
                {
                    ["ownerId"] = leaf.OwnerId.ToString("D"),
                    ["ordinal"] = leaf.Ordinal,
                    ["nativeType"] = leaf.ConditionTypeName,
                });
            }
            if (skipped.Count > 0) result["skippedEntities"] = skipped;
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
            ["nativeType"] = category.ExpectedNativeType,
            ["count"] = category.Count(world),
            ["available"] = availability.Available,
        };
        if (availability.Reason.Length > 0) result["reason"] = availability.Reason;
        return result;
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
            ? ProjectUpgrade(in upgrade)
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
                category.ExpectedNativeType);

    /// <summary>
    /// An upgrade with no ceiling has no ceiling to report: the game marks that with a negative
    /// <c>maxLevel</c>, and a sentinel becomes absence rather than a plausible number. Both
    /// <c>maxLevel</c> and <c>remainingLevels</c> are therefore published together or not at all.
    /// </summary>
    private static GameMcpValue ProjectUpgrade(in WorldUpgrade upgrade)
    {
        var result = new JObject
        {
            ["entityId"] = upgrade.EntityId.ToString("D"),
            ["category"] = "upgrades",
            ["nativeType"] = "UpgradeSO",
            ["available"] = upgrade.Reading.Available && !upgrade.IsExhausted,
            ["level"] = upgrade.Reading.Level,
        };
        if (upgrade.IsExhausted) result["reasonCode"] = "already_maxed";

        // Nothing developing is a fact about the upgrade, not a missing reading, so zero ships.
        result["queuedLevels"] = upgrade.Reading.QueuedLevels;
        if (upgrade.IsBounded)
        {
            result["maxLevel"] = upgrade.Reading.MaxLevel;
            result["remainingLevels"] = upgrade.RemainingLevels;
        }
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
            ["nativeType"] = "StructureSO",

            // The badge UIStructureItem renders is Utils.BeautifyInt(StructureSO.GetBaseLevel()),
            // which is what Reading.Level captures through GetPurchaseLevel; while levels are
            // developing the same badge switches to "+N" from GetQueuedQuantity(). Both are counts,
            // not magnitudes: routing them through the game's large-number renderer rounded a
            // 2,136-level attribute to 2.14e3 and made the wire disagree with the screen by up to
            // five levels. Publishing level plus queued under one name would put a number on the
            // wire that no screen shows.
            ["level"] = structure.Reading.Level.ToInt(),
            ["queuedLevels"] = structure.Reading.QueuedLevels.ToInt(),
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
            ["nativeType"] = "HarvestElementSO",
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
        result["removeElement"] = new JObject
        {
            ["available"] = control.RemoveAvailable,
        };

        var actions = new JArray();
        for (var index = 0; index < world.HarvestActionControls.Count; index++)
        {
            var action = world.HarvestActionControls[index];
            if (action.ElementId != element.EntityId || !action.Visible) continue;
            var row = new JObject
            {
                ["uuid"] = action.ActionId.ToString("D"),
                ["active"] = action.Active,
                ["maximum"] = action.Maximum,
                ["addAvailable"] = action.AddAvailable,
                ["removeAvailable"] = action.RemoveAvailable,
            };
            if (action.AddAvailable)
            {
                var costs = ProjectHarvestLifecycleCosts(
                    world, action.ElementId, action.ActionId,
                    WorldHarvestLifecycleCostKind.NextActionDrain);
                if (costs.Count > 0) row["nextDrain"] = costs;
            }
            else
            {
                row["addReasonCode"] = action.Active >= action.Maximum
                    ? "mastery_cap_reached"
                    : "harvest_action_list_full";
            }
            actions.Add(row);
        }
        if (actions.Count > 0) result["actions"] = actions;
        return result.Freeze();
    }

    private static GameMcpValue ProjectPlotAction(
        GameWorldState world,
        in WorldPlotAction action)
    {
        var active = PlotActionQuantity(
            world.ActionQueueSlots, action.PlotNodeId, action.PlotNodeActionId);
        return new JObject
        {
            ["plot"] = EntityReference(world, action.PlotNodeId),
            ["action"] = EntityReference(world, action.PlotNodeActionId),
            ["active"] = active,
            ["add"] = ProjectPlotActionDecision(world, in action, active),
            ["remove"] = new JObject { ["available"] = active > 0 },
        }.Freeze();
    }

    private static GameMcpValue ProjectAgromancyProcessing(
        GameWorldState world,
        in WorldActionQueueSlot slot)
    {
        var result = new JObject
        {
            ["slot"] = slot.Index,
            ["empty"] = slot.Empty,
        };
        if (WorldLookup.TryFind(world.ActionQueues, slot.QueueId, out var queue))
        {
            result["capacity"] = queue.SlotCount;
            result["used"] = queue.UsedSlots;
        }
        if (!slot.Empty)
        {
            result["plot"] = EntityReference(world, slot.PlotNodeId);
            result["action"] = EntityReference(world, slot.PlotNodeActionId);
            result["amount"] = slot.Quantity;
            result["processing"] = slot.Engaged;
        }
        return result.Freeze();
    }

    private static GameMcpValue ProjectPlotActionDecision(
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
            result["availability"] = "unknown";
            result["checkWith"] = "game_agromancy add_plot_action";
            return result.Freeze();
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
        result["availability"] = available ? "available" : "unavailable";
        if (!available)
        {
            result["reasonCode"] = reason;
            return result.Freeze();
        }
        // The game's own remaining-instance count, and nothing else. Folding the tool schema's
        // per-call ceiling in here produced a third number that was neither bound: with the list
        // nearly full it under-reported what the game admits, and the schema's ceiling is a
        // per-call limit rather than a running budget in the first place.
        result["maximumAdditional"] = action.MaximumRemainingInstances;
        result["plotQuantityCost"] = action.ElementCost;
        return result.Freeze();
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
        result["maximumAdditional"] = control.MaximumAdditional;
        result["affordable"] = true;
        var costs = ProjectHarvestLifecycleCosts(
            world, control.ElementId, Guid.Empty,
            WorldHarvestLifecycleCostKind.ElementUsage);
        if (costs.Count > 0) result["costs"] = costs;
        return result;
    }

    private static JObject ProjectHarvestActionDecision(
        GameWorldState world,
        in WorldHarvestActionControl action)
    {
        var result = new JObject
        {
            ["addAvailable"] = action.AddAvailable,
            ["removeAvailable"] = action.RemoveAvailable,
            ["maximum"] = action.Maximum,
        };
        if (!action.AddAvailable)
        {
            result["addReasonCode"] = action.Active >= action.Maximum
                ? "mastery_cap_reached"
                : "harvest_action_list_full";
            return result;
        }
        var costs = ProjectHarvestLifecycleCosts(
            world, action.ElementId, action.ActionId,
            WorldHarvestLifecycleCostKind.NextActionDrain);
        if (costs.Count > 0) result["nextDrain"] = costs;
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
            ["nativeType"] = "DiscoveryTreeSO",
            ["mode"] = DiscoveryMode(tree.ActionMode),
            ["rerollsLeft"] = tree.RerollsLeft,
            ["discoveredCount"] = tree.TotalDiscoveredCount,
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
                            world, id, out var category, out var nativeType, out _))
                    {
                        offer["category"] = category;
                        offer["nativeType"] = nativeType;
                    }
                    offers.Add(offer);
                }
                result["offers"] = offers;
            }

        }
        if (tree.ActionMode == 2)
            result["rerollAvailable"] = tree.Visible &&
                !tree.HasImmediateRequiredDiscovery &&
                tree.RerollsLeft > 0 && tree.CurrentOfferIds.Count > 0 &&
                !tree.UsedRerollsLastDiscover;
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
            ["nativeType"] = "ResearchSO",
            ["available"] = research.Available,
            ["visible"] = research.Visible,
            ["state"] = ResearchState(research),
            ["purchasedLevel"] = Number(research.PurchasedLevels),
            ["baseLevel"] = Number(research.BaseLevel),
            ["bonusLevel"] = Number(research.BonusLevel),
            ["totalLevel"] = Number(research.TotalLevel),
            ["queuedLevels"] = Number(queued),
            ["complete"] = research.Complete,
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

            // The cost verdict is published exactly when the cost is what decides. A row refused
            // for being maxed, queued out, or short of requirements has no next price to afford.
            if (reasonCode == "unaffordable")
            {
                develop["affordable"] = decision.DevelopmentCostAffordable;
                develop["reason"] = ShortfallReason(world, decision.DevelopmentCosts);
            }
            else
            {
                develop["reason"] = reasonCode == "already_maxed"
                    ? "This research is already maxed."
                    : "This research cannot be developed right now: " +
                      reasonCode.Replace('_', ' ') + ".";
            }
        }
        var developmentCostsInformNextDecision =
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
            ["nativeType"] = "ConsumableSO",
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
            ["nativeType"] = "AlchemyRecipeSO",
            ["activeCount"] = amount,
            ["canAdd"] = recipe.CanAddNow,
        }.Freeze();
    }

    private static GameMcpValue ProjectAlchemyInstance(
        GameWorldState world,
        in WorldAlchemyInstance instance)
    {
        var result = new JObject
        {
            ["recipe"] = EntityReference(world, instance.RecipeId),
            ["activeCount"] = instance.Quantity,
            ["queuedCount"] = instance.QueuedQuantity,
            ["settled"] = instance.IsSettled,
            ["drainReadable"] = instance.DrainReadable,
        };
        if (instance.DrainReadable)
            result["drainRatio"] = new GameMcpDomainValue(instance.DrainRatio);
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
                ["position"] = slot.Position,
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
            ["nativeType"] = "SpellRecipeSO",
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
        return result.Freeze();
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
                        EntityIdentityFormatter.Format(component.Uuid, world.EntityIdentities) +
                        (isGlyph
                            ? " is ambiguous between glyph and resource categories."
                            : " is not a published glyph or resource in this world.");
                    return false;
                }
                if (isGlyph)
                {
                    if (!glyph.Learned || component.Count > glyph.MaximumUsages)
                    {
                        reasonCode = "component_unavailable";
                        reason = "Glyph " +
                            EntityIdentityFormatter.Format(component.Uuid, world.EntityIdentities) +
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
            if (!WorldLookup.TryFind(world.Glyphs, component.Uuid, out var glyph) ||
                glyph.AugmentsSpells || !glyph.Learned)
            {
                reason = "Component " +
                    EntityIdentityFormatter.Format(component.Uuid, world.EntityIdentities) +
                    " is not an available core glyph in this world.";
                return false;
            }
            if (component.Count > glyph.MaximumUsages)
            {
                reason = "Component " +
                    EntityIdentityFormatter.Format(component.Uuid, world.EntityIdentities) +
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
        if (world.Targeting.Count == 0)
            result["targeting"] = new JObject { ["pending"] = false };
        else
        {
            var request = world.Targeting[0];
            result["targeting"] = ProjectTargeting(world, in request);
        }
        return result.Freeze();
    }

    private static GameMcpValue ProjectTargeting(
        GameWorldState world, in WorldTargetingRequest request)
    {
        var candidates = new JArray();
        for (var index = 0; index < request.Candidates.Count; index++)
        {
            var candidate = request.Candidates[index];
            candidates.Add(ProjectTargetCandidate(world, candidate.StructureId, candidate.Position));
        }
        var result = new JObject
        {
            ["pending"] = true,
            ["owner"] = request.OwnerName,
            ["ownerNativeType"] = request.OwnerNativeType,
            ["selectionType"] = request.SelectionNativeType,
            ["candidates"] = candidates,
            ["randomize"] = new JObject { ["available"] = candidates.Count > 0 },
        };
        return result.Freeze();
    }

    private static GameMcpValue ProjectTargetCandidate(GameWorldState world, Guid id, int position)
    {
        var identity = EntityIdentityFormatter.Describe(id, world.EntityIdentities);
        var result = new JObject
        {
            ["uuid"] = id.ToString("D"),
            ["name"] = identity.HasName ? identity.Name : id.ToString("D"),
        };
        if (position >= 0) result["position"] = position;
        if (identity.AssetName.Length > 0 && !string.Equals(identity.AssetName, identity.Name, StringComparison.Ordinal))
            result["internalName"] = identity.AssetName;
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
                ["nativeType"] = "Spell",
                ["slot"] = slot.SlotIndex,
                ["occupied"] = false,
            }.Freeze();
        }
        var result = ProjectEquippedSpell(world, in slot);
        result["category"] = "spell-slots";
        result["nativeType"] = "Spell";
        result["occupied"] = true;
        return result.Freeze();
    }

    private static JObject ProjectEquippedSpell(
        GameWorldState world,
        in WorldSpellSlot slot)
    {
        var recipe = EntityIdentityFormatter.Describe(
            slot.SpellRecipeId,
            world.EntityIdentities);
        var instance = new JObject
        {
            ["uuid"] = slot.SpellInstanceId.ToString("D"),
            ["name"] = recipe.HasName ? recipe.Name : "Equipped spell",
        };
        var result = new JObject
        {
            ["spellInstance"] = instance,
            ["spellRecipeId"] = slot.SpellRecipeId.ToString("D"),
            ["slot"] = slot.SlotIndex,
            ["effectiveLevel"] = slot.EffectiveLevel,
            ["requiredMasteryLevel"] = slot.RequiredMasteryLevel,
            ["recipeMasteryLevel"] = slot.RecipeMasteryLevel,
            ["duration"] = slot.DurationSpell,
            ["toggleable"] = slot.Toggled,
            ["usageRequirementsMet"] = slot.UsageRequirementsMet,
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
        result["move"] = world.SpellSlots.Count > 1
            ? new JObject
            {
                ["available"] = true,
                ["destinations"] = ProjectSpellMoveDestinations(world, slot.SlotIndex),
            }
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
            if (!glyph.AugmentsSpells || !glyph.Learned || glyph.Level <= 0) continue;
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
            if (glyph.AugmentsSpells)
            {
                reasonCode = "core_glyph_augments_only";
                return false;
            }
        }
        return true;
    }

    private static JArray ProjectSpellMoveDestinations(GameWorldState world, int currentSlot)
    {
        var destinations = new JArray();
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (slot.SlotIndex == currentSlot) continue;
            var option = new JObject { ["slot"] = slot.SlotIndex };
            if (slot.Occupied)
                option["occupantId"] = slot.SpellRecipeId.ToString("D");
            else option["empty"] = true;
            destinations.Add(option);
        }
        return destinations;
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
            ["nativeType"] = "ResourceSO",
            ["amount"] = new GameMcpDomainValue(amount),
        };
        if (resource.IsCapped)
            result["capacity"] = new GameMcpDomainValue(resource.Reading.Capacity);
        result["netRatePerSecond"] = new GameMcpDomainValue(resource.TrueRate);
        if (resource.IsCapped)
            result["atCapacity"] = resource.IsAtCapacity;
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

    /// <summary>
    /// The one have/need sentence on every surface. Each entry names a resource that is genuinely
    /// short, the price it asks, and what the player holds, both in the player's own units. A
    /// caller that fixes the first named resource is not blocked by a second one nobody mentioned.
    /// </summary>
    internal static string ShortfallSentence(
        IEnumerable<(string Resource, BigDouble Needed, BigDouble Held)> rows)
    {
        var text = new StringBuilder("Needs ");
        var written = 0;
        foreach (var row in rows)
        {
            if (written > 0) text.Append("; ");
            written++;
            text.Append(GameMcpNumberFormatter.Format(row.Needed))
                .Append(' ')
                .Append(row.Resource)
                .Append(" (have ")
                .Append(GameMcpNumberFormatter.Format(row.Held))
                .Append(')');
        }
        return written == 0 ? string.Empty : text.Append('.').ToString();
    }

    private static string ShortfallReason(
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
        var sentence = ShortfallSentence(rows);
        return sentence.Length == 0
            ? "This research cannot be developed right now: unaffordable."
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
            ["nativeType"] = "AlchemyRecipeSO",
            ["discovered"] = recipe.Discovered,
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
        if (decision.IsActive) loadout["slot"] = decision.Position;
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
        loadout["move"] = decision.IsActive && decision.SlotCount > 1
            ? new JObject
            {
                ["available"] = true,
                ["maximumDestination"] = decision.SlotCount - 1,
            }
            : new JObject
            {
                ["available"] = false,
                ["reasonCode"] = decision.IsActive ? "single_slot" : "not_active",
            };
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
            ["nativeType"] = "EquipmentSO",
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
                            result["weightBudget"] = new JObject
                            {
                                ["used"] = new GameMcpDomainValue(resource.Reading.Quantity),
                                ["maximum"] = new GameMcpDomainValue(resource.Reading.Capacity),
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
            ["nativeType"] = "ChallengeSO",
            ["state"] = ChallengeState(challenge.State),
            ["level"] = new GameMcpDomainValue(new BigDouble(challenge.Level)),
            ["seen"] = challenge.Seen,
            ["rewardQueued"] = challenge.RewardQueued,
            ["completedOnce"] = challenge.CompletedOnce,
            ["maximumLevelReached"] = challenge.MaximumLevelReached,
            ["availableToRun"] = challenge.AvailableToRun,
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
        var activateAvailable = context.Available && (inTime || inPrestige) && challenge.State is 0 or 1;
        var activate = new JObject
        {
            ["available"] = activateAvailable,
            ["selectedForActivation"] = challenge.State == 1,
        };
        if (!activateAvailable)
            activate["reasonCode"] = !inTime && !inPrestige ? "not_offered" : "invalid_state";
        result["activate"] = activate;
        if (challenge.State == 2)
            result["abandon"] = new JObject { ["available"] = true };
        return result.Freeze();
    }

    private static string ChallengeState(int state) => state switch
    {
        0 => "idle",
        1 => "queued",
        2 => "active",
        3 => "passed",
        4 => "failed",
        _ => "unknown",
    };

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
            ["rerollsLeft"] = new GameMcpDomainValue(new BigDouble(context.RerollsLeft)),
            ["rerollsMaximum"] = new GameMcpDomainValue(new BigDouble(context.RerollsMaximum)),
            ["selectionMaximum"] = new GameMcpDomainValue(new BigDouble(context.SelectionMaximum)),
            ["selected"] = ChallengeReferences(context.Selected),
            ["timeOffers"] = ChallengeReferences(context.TimeOffers),
            ["prestigeOffers"] = ChallengeReferences(context.PrestigeOffers),
            ["fetchTimeChallenges"] = FetchDecision(fetchAvailable, context),
            ["fetchPrestigeChallenges"] = FetchDecision(fetchAvailable, context),
            ["prestige"] = ProjectPrestigeState(world),
        };
        return result.Freeze();
    }

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
            ["currentTimeAdvancements"] = context.PersistenceCurrent,
            ["startingTimeAdvancements"] = context.PersistenceProjected,
            ["previousStartingTimeAdvancements"] = context.PersistencePrevious,
            ["changeFromPrevious"] = context.PersistenceProjected - context.PersistencePrevious,
            ["resetCount"] = context.ResetCount,
            ["survivingChallengeSelections"] = PrestigeChallenges(world, queuedRewards: false),
            ["survivingChallengeRewards"] = PrestigeChallenges(world, queuedRewards: true),
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

    private static JObject FetchDecision(bool available, in WorldChallengeContext context)
    {
        var result = new JObject { ["available"] = available };
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
            ["nativeType"] = "GlyphSO",
            ["discovered"] = glyph.Discovered,
            ["available"] = glyph.Learned,
            ["usableCount"] = glyph.MaximumUsages,
        };
        AddLevelDecision(world, result, glyph.LevelDecision,
            glyph.Learned,
            "not_available");
        AddDiscoveryDecision(world, result, glyph.Discovery, glyph.Discoverable);
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
            ["nativeType"] = "EquipmentTypeSO",
            ["baseUsage"] = equipmentType.BaseUsage,
            ["masteryLevel"] = new GameMcpDomainValue(equipmentType.MasteryLevel),
            ["maximumSlots"] = new GameMcpDomainValue(equipmentType.MaxTypeSlots),
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
            ["nativeType"] = "ResourceTypeSO",
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
            ["nativeType"] = "RitualSO",
            ["discovered"] = ritual.Discovered,
            ["inBattle"] = ritual.InBattle,
            ["activeInstances"] = ritual.ActiveInstances,
            ["reachedLevel"] = ritual.ReachedLevel,
            ["selectedLevel"] = ritual.SelectedLevel,
        };
        AddRitualDecision(world, result, in ritual);
        AddDiscoveryDecision(world, result, ritual.Discovery);
        return result.Freeze();
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
            ["nativeType"] = "CraftingStructure",
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
        var level = new JObject
        {
            ["available"] = selected && !ritual.ForceLevel && !anyBattleActive,
            ["current"] = ritual.SelectedLevel,
        };
        if (selected && !ritual.ForceLevel)
        {
            // Both bounds, from the same native control: the jump-start selector is clamped to
            // 1..RitualSO.GetMaxSelectedLevel(), and a caller that only ever saw the ceiling had no
            // way to discover that 0 is not a starting level the game offers.
            level["minimum"] = WorldRitualDecision.NativeMinimumStartingLevel;
            level["maximum"] = ritual.Decision.MaximumStartingLevel;
            if (anyBattleActive) level["reasonCode"] = "ritual_battle_active";
        }
        else if (ritual.ForceLevel)
            level["reasonCode"] = "level_locked";
        else
            level["reasonCode"] = "not_selected";
        result["setLevel"] = level;

        var activateAvailable = ritual.Discovered && selected && !anyBattleActive &&
            ritual.Decision.UsageRequirementsMet && ritual.Decision.ActivationAffordable;
        var activate = new JObject { ["available"] = activateAvailable };
        if (selected)
        {
            activate["affordable"] = ritual.Decision.ActivationAffordable;
            var activationCosts = ProjectRitualCosts(world, ritual.Decision.ActivationCosts);
            if (activationCosts.Count > 0) activate["costs"] = activationCosts;
            var completionCosts = ProjectRitualCosts(world, ritual.Decision.CompletionCosts);
            if (completionCosts.Count > 0) activate["completionCosts"] = completionCosts;
        }
        if (!activateAvailable)
            activate["reasonCode"] = !ritual.Discovered
                ? "not_discovered"
                : !selected
                    ? "not_selected"
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
            ["nativeType"] = "TimeRuneSO",
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
        bool nativeDiscoverable = true)
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
        result["discover"] = discover;
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
        in WorldPurchaseCost cost)
    {
        var result = new JObject
        {
            ["resourceId"] = cost.ResourceId.ToString("D"),
            ["cost"] = new GameMcpDomainValue(AdmittedCost(world, in cost)),
        };
        if (cost.AffordabilityEvaluated)
        {
            result["spendableAmount"] = new GameMcpDomainValue(
                SpendableAmount(world, cost.ResourceId, cost.AvailableAmount));

            // This row answers for its own resource. The whole-price verdict is what the rows fold
            // to, and a row that reported it claimed to be short of a resource it holds plenty of.
            result["affordable"] = cost.ResourceAffordable;
            if (!cost.ResourceAffordable)
                result["reasonCode"] = cost.ResourceAffordabilityReasonCode;
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
            ["nativeType"] = "CraftingRecipeSO",
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

    private static bool Matches(
        GameWorldState world,
        GameMcpWorldCategory category,
        object row,
        string query)
    {
        if (category.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            category.RowTypeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            category.ExpectedNativeType.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        return category.TryIdentity(row, out var identity) &&
            GameMcpEntityCatalog.Matches(world.EntityIdentities, identity, query);
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
            Composite(
                nameof(GameWorldState.RequirementNativeVerdicts),
                world => world.RequirementNativeVerdicts),
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
        "requirement-native-verdicts" => new[] { "requirement-native-verdicts" },
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
            "slotIndex", "spellInstanceId", "spellRecipeId", "occupied",
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
        "requirement-native-verdicts" => new[]
        {
            "entityId", "ownerKind", "checkLevel", "met",
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
