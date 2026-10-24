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


using static OrbAutomata.GameMcp.GameMcpAlchemyLoadoutProjection;
using static OrbAutomata.GameMcp.GameMcpCastProjection;
using static OrbAutomata.GameMcp.GameMcpChallengeProjection;
using static OrbAutomata.GameMcp.GameMcpConsumableProjection;
using static OrbAutomata.GameMcp.GameMcpCraftingProjection;
using static OrbAutomata.GameMcp.GameMcpDecisionReason;
using static OrbAutomata.GameMcp.GameMcpDiscoveryTreeOfferProjection;
using static OrbAutomata.GameMcp.GameMcpEquipmentLoadoutProjection;
using static OrbAutomata.GameMcp.GameMcpGenericLevelProjection;
using static OrbAutomata.GameMcp.GameMcpGlyphProjection;
using static OrbAutomata.GameMcp.GameMcpHarvestLifecycleProjection;
using static OrbAutomata.GameMcp.GameMcpLoadoutProjection;
using static OrbAutomata.GameMcp.GameMcpPlotLifecycleProjection;
using static OrbAutomata.GameMcp.GameMcpRecipeBookProjection;
using static OrbAutomata.GameMcp.GameMcpResearchProjection;
using static OrbAutomata.GameMcp.GameMcpResourceProjection;
using static OrbAutomata.GameMcp.GameMcpRitualLifecycleProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;
using static OrbAutomata.GameMcp.GameMcpStructureLifecycleProjection;
using static OrbAutomata.GameMcp.GameMcpTargetingProjection;
using static OrbAutomata.GameMcp.GameMcpUpgradeProjection;
using static OrbAutomata.GameMcp.GameMcpWorldList;
using static OrbAutomata.GameMcp.GameMcpWorldRead;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWorldDetail
{

    /// <summary>
    /// One id's answer in the singular shape, which is the live <c>world_get</c> read with one id
    /// in it: the same category resolution, the same block, the same words.
    /// </summary>
    /// <remarks>
    /// This read had its own copy of all of that — its own category check, its own row walk, its
    /// own refusal sentences — and nothing in <c>src</c> called it; only tests did, so the surface
    /// they were holding to account was a second implementation of the one callers reach. A
    /// refusal that fails the whole call, such as no world or an unknown category, is that call's
    /// own answer and comes back whole. A block that answers omits <c>status</c>, because on the
    /// wire silence is the yes; the singular shape says the word, so it is filled in here rather
    /// than left to every caller to know.
    /// </remarks>
    internal static JObject GetRow(
        GameMcpFrameContext state,
        string categoryName,
        string uuidText)
    {
        if (!TryDetailRead(state, categoryName, 1, out var publication, out var requested,
                out var refusal))
        {
            return refusal;
        }

        var block = GetOne(
            publication.Snapshot,
            requested,
            uuidText ?? string.Empty,
            GameMcpKeywordIndex.Build(publication.Snapshot));
        if (block["status"] is null) block["status"] = "available";
        return block;
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
        var ids = uuidTexts ?? Array.Empty<string>();
        if (!TryDetailRead(state, categoryName, ids.Count,
                out var publication, out var requested, out var refusal))
        {
            return refusal;
        }

        // Built once for the whole call. It is an index over the world's whole keyword table, and a
        // 200-id batch that rebuilt it per block would pay for that table two hundred times.
        var keywordIndex = GameMcpKeywordIndex.Build(publication.Snapshot);
        var results = new JArray();
        for (var inputIndex = 0; inputIndex < ids.Count; inputIndex++)
            results.Add(GetOne(publication.Snapshot, requested, ids[inputIndex], keywordIndex));

        var result = new JObject();
        result["results"] = results;
        return result;
    }

    /// <summary>
    /// Everything a detail read settles before it looks at an id: that there is a world, that the
    /// batch is a batch, and which table the caller named, if any. One id and two hundred answer to
    /// the same checks in the same words, so they are asked in one place.
    /// </summary>
    internal static bool TryDetailRead(
        GameMcpFrameContext state,
        string categoryName,
        int requestedCount,
        out WorldPublication<GameWorldState> publication,
        out GameMcpEntityCapabilityDescriptor? requested,
        out JObject refusal)
    {
        requested = null;
        if (!TryWorld(state, out publication, out refusal)) return false;
        if (requestedCount <= 0 || requestedCount > MaximumBatchSize)
        {
            refusal = NotAvailable(
                publication,
                "invalid_batch_size",
                "uuids must contain between 1 and " +
                MaximumBatchSize.ToString(CultureInfo.InvariantCulture) + " entries");
            return false;
        }
        if (string.IsNullOrEmpty(categoryName)) return true;

        if (!GameMcpWorldCategories.TryCategory(categoryName, out var named, out var reason))
        {
            refusal = NoSuchCategory(publication, categoryName, reason);
            return false;
        }
        if (!string.Equals(named.IdentityMode, "stable_entity_uuid", StringComparison.Ordinal))
        {
            refusal = NotAvailable(
                publication,
                "composite_identity_required",
                "Rows in " + named.Name + " are not addressed by one id; " +
                "read them with world_list.");
            return false;
        }
        var namedAvailability = GameMcpWorldCategories.Availability(publication.Snapshot, named);
        if (!namedAvailability.Available)
        {
            refusal = NotAvailable(
                publication,
                "category_not_collected",
                namedAvailability.Reason.Length == 0
                    ? "the category was not collected"
                    : namedAvailability.Reason);
            return false;
        }

        requested = named;
        return true;
    }

    /// <summary>One id's block of the detail read, refusing on its own without failing the batch.</summary>
    internal static JObject GetOne(
        GameWorldState world,
        GameMcpEntityCapabilityDescriptor? requested,
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
            return GameMcpEntityIdentity.UnresolvedEntity(world, uuid);

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
            // The category came from the id's native type, which is right for every id except the
            // twenty-five retired unlockers: they are loaded `GlyphSO` and their row is deliberately
            // gone, so "no augment-glyphs entry with that id" is true and useless. The explainer has
            // the signpost that names the book instead.
            if (requested is null &&
                WorldRecipeBookGlyphLookup.TryFindBook(world.RecipeBookGlyphs, uuid, out _))
            {
                return GameMcpEntityIdentity.UnresolvedEntity(world, uuid);
            }
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
        //
        // Identity rides the detail read and only the detail read. What the asset is called and what
        // native type answers for it are catalog-browsing facts, so they are published where someone
        // browsing asks for them, and never stamped on a table's rows.
        //
        // One skeleton for every category: identity at the top, the published row under `row:`, and
        // the evaluated sections below it wherever this build has them. It used to be gated twice
        // over — on the category having evaluated sections, and on the live catalog holding a name
        // for the id — so three layouts lived at once and no caller could write one reader for
        // them. A plot node, which has no sections, answered as a bare field dump with no category
        // and no lifecycle word while its own list page said `locked`; an equipment type carried
        // its uuid, name and category INSIDE `row:` because nothing had claimed the top level; and
        // a variable, which the catalog names nothing, answered as a lone `row:` envelope that the
        // page then flattened into a field list. The id and the table are facts this call already
        // holds, so the top level is published from them and the catalog only adds what it knows.
        // A category with no predicates now simply has no predicates section.
        var item = new JObject
        {
            ["uuid"] = uuid.ToString("D"),
        };
        if (world.EntityIdentities.TryGet(uuid, out _))
        {
            item.CopyFrom(GameMcpEntityCatalog.Lookup(
                world.EntityIdentities, uuid, category.ExpectedNativeType));
        }

        // The category the world actually publishes this id in, not the one the catalog's runtime
        // type implies: they disagree exactly where a caller most needs the truth.
        item["category"] = category.Name;
        // Every detail page that can carry the game's own words for a thing carries them. The read
        // was gated on the evaluated-detail resolver, whose thirteen kinds are the set this build
        // evaluates predicates for — a shared entry point, not a rule about descriptions — so a
        // whole category could publish a detail page with the authored text one lookup away and
        // never say it. The category already declares the native type the text is read through.
        var description = GameMcpEntityIdentity.ReadDescription(world, uuid);
        if (description.Length == 0)
            description = GameMcpEntityIdentity.ReadDescription(uuid, category.ExpectedNativeType);
        if (description.Length > 0) item["description"] = description;

        // The words the game prints on this thing's type line, under the same name and in the same
        // spelling `world_search` prints them. One fact, one name, both verbs.
        //
        // This was the round's costliest miss. A glyph's detail block published state, discovery,
        // visibility and price and never said `keywords: Elemental` — the one fact that decides
        // which family the glyph is in and therefore which page it renders on — so a reader working
        // from the detail block concluded the read surface contradicted the screen, and held that
        // finding for two hours. The search row ten minutes earlier had carried the word plainly.
        // Where a category also publishes a richer relation block (`belongsTo` on spell recipes)
        // that block stays; this line rides beside it, because a reader should not have to know
        // which of three treatments a category happens to give one fact.
        var keywords = keywordIndex.Line(uuid);
        if (keywords.Length > 0) item["keywords"] = keywords;

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
        AddModifiedBy(item, world, uuid);
        return item;
    }

    /// <summary>
    /// What moves this thing, counted per kind of mover, on the thing's own answer.
    /// </summary>
    /// <remarks>
    /// The game stores an effect on its owner only, so until this a statistic named none of its
    /// sources and "what gives me a third Discovery Option" had no read that answered it. The count
    /// is the node naming its edge; <c>world_search</c> with <c>modifies</c> set to this id walks it,
    /// and both read <see cref="SourceKinds"/>, so a page saying three beside a walk returning three
    /// is one fact. Owned and not-yet-owned movers alike are counted: the walk's <c>state</c> filter
    /// is what splits them.
    /// </remarks>
    internal static void AddModifiedBy(JObject item, GameWorldState world, Guid uuid)
    {
        var sources = GameMcpEffectSourceIndex.Build(world).Sources(uuid);
        if (sources is null) return;
        var kinds = SourceKinds(world, sources);
        if (kinds.Count == 0) return;

        var modifiedBy = new JArray();
        foreach (var kind in kinds)
            modifiedBy.Add(new JObject { ["kind"] = kind.Key, ["count"] = kind.Value });
        item["modifiedBy"] = modifiedBy;
    }

    /// <summary>
    /// How many of these movers each searchable category answers for, walked in the order and with
    /// the one-entity-one-hit rule <c>world_search</c> walks them, so the count and the filtered page
    /// cannot disagree.
    /// </summary>
    internal static List<KeyValuePair<string, int>> SourceKinds(
        GameWorldState world,
        IReadOnlyDictionary<Guid, string> sources)
    {
        var kinds = new List<KeyValuePair<string, int>>();
        var seen = new HashSet<Guid>();
        for (var categoryIndex = 0;
             categoryIndex < GameMcpWorldCategories.Categories.Length;
             categoryIndex++)
        {
            var category = GameMcpWorldCategories.Categories[categoryIndex];
            if (!GameMcpWorldSearch.IsSearchable(category) ||
                !GameMcpWorldCategories.Availability(world, category).Available)
            {
                continue;
            }
            var found = 0;
            var count = category.Count(world);
            for (var rowIndex = 0; rowIndex < count; rowIndex++)
            {
                if (!category.TryIdentity(category.Row(world, rowIndex), out var identity)) continue;
                if (!sources.ContainsKey(identity) || !seen.Add(identity)) continue;
                found++;
            }

            if (found > 0) kinds.Add(new KeyValuePair<string, int>(category.Name, found));
        }

        return kinds;
    }

    /// <summary>
    /// The published table an id belongs to, without the caller naming one. What the world itself
    /// publishes decides it; the live catalog's runtime type answers for the categories this build
    /// evaluates no detail for.
    /// </summary>
    internal static bool TryEntityCategory(
        GameWorldState world,
        Guid uuid,
        out GameMcpEntityCapabilityDescriptor category)
    {
        if (GameMcpEntityIdentity.TryDescribePublishedEntity(
                world, uuid, out var published, out _, out _) &&
            GameMcpWorldCategories.TryCategory(published, out category, out _))
        {
            return true;
        }
        if (world.EntityIdentities.TryGet(uuid, out var identity) &&
            GameMcpEntityCapabilityMap.TryCategoryForNativeType(
                identity.RuntimeType, out var implied) &&
            GameMcpWorldCategories.TryCategory(implied, out category, out _) &&
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
        if (!GameMcpWorldCategories.TryCategory(categoryName, out var category, out var reason))
            return PostStateUnavailable("unknown_category", reason);
        var world = state.World.Snapshot;
        var count = category.Count(world);
        for (var index = 0; index < count; index++)
        {
            var row = category.Row(world, index);
            if (category.TryIdentity(row, out var identity) && identity == uuid)
                return ProjectRow(world, category, row);
        }
        return PostStateNotPublished("this " + category.Name + " entry");
    }

    internal static GameMcpValue ProjectRow(
        GameWorldState world,
        GameMcpEntityCapabilityDescriptor category,
        object row) =>
        WithGroupMembers(
            world,
            category,
            row,
            WithLevelEffects(
                world,
                category,
                row,
                WithOwnIdentity(category, category.Project(world, row))));

    /// <summary>
    /// What a stat group's bonus is distributed into, on the group's own answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node names its edge by carrying it, and this is the only edge there is: the game stores
    /// the relation as a list on the group and nothing on the far side names its group back, so a
    /// reader walks group → members and there is no reverse walk to offer. A group holds between one
    /// and thirteen references on the pinned build, which is smaller than the sentence that would
    /// point at them.
    /// </para>
    /// <para>
    /// Nothing is unfurled. A member is not an entity — it is a reference to one — so the row carries
    /// the target's identity for <c>world_get</c> to follow rather than a copy of the target, exactly
    /// as a glyph's factors carry the statistic they name.
    /// </para>
    /// </remarks>
    internal static GameMcpValue WithGroupMembers(
        GameWorldState world,
        GameMcpEntityCapabilityDescriptor category,
        object row,
        GameMcpValue projected)
    {
        if (row is not WorldAttributeGroup) return projected;
        if (!category.TryIdentity(row, out var groupId)) return projected;
        if (!WorldAttributeGroupMemberLookup.TryFindRange(
                world.AttributeGroupMembers, groupId, out var start, out var count))
        {
            return projected;
        }

        var members = new JArray();
        for (var index = 0; index < count; index++)
        {
            var member = world.AttributeGroupMembers[start + index];
            var entry = new JObject
            {
                ["modifiesId"] = member.TargetId.ToString("D"),
                ["property"] = member.Property,
                ["propertyIndex"] = member.PropertyIndex,
                ["ratio"] = member.Ratio,
                ["ratioExp"] = member.RatioExp,
                ["orderAdjust"] = member.OrderAdjust,
            };
            members.Add(entry);
        }

        if (projected is GameMcpProjectedDomainValue reflected)
            return reflected.With(new JObject { ["members"] = members }.Freeze());
        if (projected is not GameMcpObject frozen) return projected;
        var result = new JObject();
        result.CopyFrom(frozen);
        result["members"] = members;
        return result.Freeze();
    }

    /// <summary>
    /// What one more level of this thing buys, on the answer a reader already asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Six classes author per-level modifier tuples and they answer the same question on all six —
    /// "what do I get for the next level" — so the block rides every owner's own read rather than
    /// becoming a table of its own. There is no cross-owner page here on purpose: a single read says
    /// what the tooltip says, a list does not, and "what affects my X" is a search rather than a
    /// table.
    /// </para>
    /// <para>
    /// Attached here rather than inside each of the six projections, because two of the six are
    /// rendered from a declared field list and have no hand-written projection to add it to. The
    /// owner is the row's own identity in both shapes and the block is keyed on nothing else.
    /// </para>
    /// </remarks>
    internal static GameMcpValue WithLevelEffects(
        GameWorldState world,
        GameMcpEntityCapabilityDescriptor category,
        object row,
        GameMcpValue projected)
    {
        if (!category.TryIdentity(row, out var ownerId)) return projected;
        if (!WorldLevelEffectLookup.TryFindRange(
                world.LevelEffects, ownerId, out var start, out var count))
        {
            return projected;
        }

        var effects = new JArray();
        for (var index = 0; index < count; index++)
        {
            var effect = world.LevelEffects[start + index];
            var entry = new JObject();

            // Absent rather than blank where the game authors no property word: a number-variable
            // tuple has none, because the variable it names is the whole of what moves.
            if (effect.Property.Length > 0) entry["property"] = effect.Property;
            entry["modifiesId"] = effect.TargetId.ToString("D");
            entry["amount"] = GameMcpNativeVocabulary.ModifierMagnitude(
                effect.ModifierType, effect.Amount);
            entry["order"] = effect.Order;
            if (effect.When.Length > 0) entry["when"] = effect.When;
            effects.Add(entry);
        }

        var block = new JObject { ["levelEffects"] = effects };
        if (row is WorldAlchemyRecipe) block["levelEffectsAt"] = AlchemyLevelEffectsAt;
        if (projected is GameMcpProjectedDomainValue reflected)
            return reflected.With(block.Freeze());
        if (projected is not GameMcpObject frozen) return projected;
        var result = new JObject();
        result.CopyFrom(frozen);
        result.CopyFrom(block);
        return result.Freeze();
    }



}
#endif
