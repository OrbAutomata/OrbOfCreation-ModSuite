#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWorldSearch
{
    /// <summary>
    /// One search hit before it is projected: enough to sort the whole result set, and nothing else.
    /// </summary>
    private readonly struct GameMcpSearchHit
    {
        internal GameMcpSearchHit(
            GameMcpSearchTier tier,
            Guid identity,
            int category,
            int row,
            string matchedOn)
        {
            Tier = tier;
            Identity = identity;
            Category = category;
            Row = row;
            MatchedOn = matchedOn;
        }

        internal GameMcpSearchTier Tier { get; }
        internal Guid Identity { get; }
        internal int Category { get; }
        internal int Row { get; }

        /// <summary>The field the query hit, or the absence mark where no query was applied.</summary>
        internal string MatchedOn { get; }
    }

    internal static JObject Search(
        GameMcpFrameContext state,
        string query,
        int offset,
        int limit,
        string categoryName = "",
        string stateFilter = "",
        string runFilter = "",
        string keywordFilter = "",
        bool limitFromCaller = true,
        bool? discoveredFilter = null,
        string modifiesFilter = "")
    {
        if (!GameMcpWorldQuery.TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        var normalized = (query ?? string.Empty).Trim();
        if (offset < 0)
            return GameMcpWorldQuery.NotAvailable(publication, "invalid_offset", "offset must be zero or greater");
        if (limit <= 0 || limit > GameMcpWorldQuery.MaximumPageSize)
        {
            return GameMcpWorldQuery.NotAvailable(
                publication,
                "invalid_limit",
                "limit must be between 1 and " +
                GameMcpWorldQuery.MaximumPageSize.ToString(CultureInfo.InvariantCulture));
        }

        var scope = (categoryName ?? string.Empty).Trim();
        GameMcpWorldCategory? only = null;
        if (scope.Length > 0)
        {
            if (!GameMcpWorldCategories.TryCategory(scope, out var scoped, out var reason))
                return GameMcpWorldQuery.NoSuchCategory(publication, scope, reason);
            if (!IsSearchable(scoped))
            {
                return GameMcpWorldQuery.NotAvailable(
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
            return GameMcpWorldQuery.NotAvailable(
                publication,
                "invalid_state_filter",
                "state must be one of " + GameMcpListColumns.Locked + ", " +
                GameMcpListColumns.Available + ", " + GameMcpListColumns.Completed);
        }

        if (discoveredFilter is not null && only is not null && !GameMcpWorldQuery.SupportsDiscoveredFilter(only))
        {
            return GameMcpWorldQuery.NotAvailable(
                publication,
                "discovered_filter_out_of_scope",
                "the categories whose rows spell discovery in that word are " +
                string.Join(", ", GameMcpWorldQuery.DiscoverableCategories) + ", so it cannot narrow " + only.Name +
                "; the rest spell it as their state, which the state filter narrows");
        }

        var wantedRun = (runFilter ?? string.Empty).Trim();
        var wantedKeyword = (keywordFilter ?? string.Empty).Trim();
        if (wantedRun.Length > 0 && only is not null &&
            !string.Equals(only.Name, "challenges", StringComparison.Ordinal))
        {
            return GameMcpWorldQuery.NotAvailable(
                publication,
                "run_filter_out_of_scope",
                "run is a challenges column and no other category publishes one, so it cannot " +
                "narrow " + only.Name + "; drop the category or drop the run filter");
        }

        // A query is what a caller asks for by name; a filter is what they ask for by shape. One of
        // the two has to be there, and requiring both made a round invent eight filler queries — a
        // reach nobody could characterise, sitting under a count the whole sweep was judged on.
        var wantedTarget = (modifiesFilter ?? string.Empty).Trim();
        if (normalized.Length == 0 && scope.Length == 0 && wanted.Length == 0 &&
            wantedRun.Length == 0 && wantedKeyword.Length == 0 && discoveredFilter is null &&
            wantedTarget.Length == 0)
        {
            return GameMcpWorldQuery.NotAvailable(
                publication,
                "query_required",
                "name something to search for: a query, or a category, state, run, discovered, " +
                "keyword or modifies filter");
        }

        // The far side of the count a type's page prints, walked from the same index the count is
        // taken over. A word is a substring and a keyword is a node: a query for "Primal" reads the
        // structures spelled that and misses every Arcanist the parent type reaches, which is the
        // one edge on this surface a query could not walk at all.
        var world = publication.Snapshot;
        var keywords = GameMcpKeywordIndex.Build(world);
        HashSet<Guid>? worn = null;
        if (wantedKeyword.Length != 0)
        {
            // A caller reads the word off a row's keywords cell, so that is the word the filter
            // takes. An id is still accepted, because a members line prints one.
            if (!Guid.TryParse(wantedKeyword, out var keywordId))
            {
                if (!keywords.TryResolveWord(wantedKeyword, out keywordId, out var matches))
                {
                    return GameMcpWorldQuery.NotAvailable(
                        publication,
                        matches > 1 ? "ambiguous_handle" : "keyword_unknown",
                        matches > 1
                            ? "'" + wantedKeyword + "' is the printed word of " + matches +
                              " different type assets, so it names none of them; use the id a " +
                              "members line prints"
                            : "'" + wantedKeyword + "' is neither a published id nor a word any " +
                              "row's keywords cell prints; a row's keywords are the words this " +
                              "filter takes");
                }
            }

            // The guard is the members block's own: this answers for exactly the ids whose page
            // prints a count, so "the filter reaches it" and "the page counted it" are one fact
            // rather than two that could drift apart.
            worn = WorldKeywordModifierLookup.TryFind(world.KeywordModifiers, keywordId, out _, out _)
                ? WorldKeywordMembership
                    .Build(
                        world.EntityKeywords,
                        world.Research,
                        world.ConsumableTypes,
                        world.TypeSubtypes)
                    .Members(keywordId)
                : GameMcpKeywordIndex.SpellsOfType(world, keywordId);
            if (worn.Count == 0)
            {
                return GameMcpWorldQuery.NotAvailable(
                    publication,
                    "keyword_not_worn",
                    GameMcpEntityHandle.Name(keywordId, world.EntityIdentities) + " " +
                    GameMcpEntityHandle.Format(keywordId) + " is " +
                    KeywordScope(world, keywordId) + " and its page counts no members, so it " +
                    "cannot narrow anything; name the type asset a members line counted");
            }
        }

        // The far side of the modifiedBy count a target's page prints, read from the same index. A
        // query for the target's name reaches the same owners by their effect words, but also
        // every name the words happen to contain; this names the node.
        IReadOnlyDictionary<Guid, string>? sources = null;
        if (wantedTarget.Length != 0)
        {
            if (!TryResolveModifiesTarget(world, wantedTarget, out var targetId, out var refusal))
                return GameMcpWorldQuery.NotAvailable(publication, refusal.Key, refusal.Value);
            sources = GameMcpEffectSourceIndex.Build(world).Sources(targetId);
            if (sources is null)
            {
                return GameMcpWorldQuery.NotAvailable(
                    publication,
                    "modifies_nothing",
                    GameMcpEntityHandle.Name(targetId, world.EntityIdentities) + " " +
                    GameMcpEntityHandle.Format(targetId) + " is moved by no authored effect " +
                    "this world publishes, so its page counts no modifiedBy and there is nothing " +
                    "to narrow to");
            }
        }

        // Search is deliberately an entity-catalog surface. Composite diagnostic categories are
        // readable through world_list, where their full identity and localized partiality survive.
        // One entity is one match however many categories publish it: identity is deduplicated
        // before the sort, so a repeat can never eat a slot the caller paid for.
        var effects = GameMcpEffectWordIndex.Build(world);
        var hits = new List<GameMcpSearchHit>();
        var keywordHits = new List<KeyValuePair<string, int>>();
        var seen = new HashSet<Guid>();
        var unavailableCategories = new JArray();
        for (var categoryIndex = 0;
             categoryIndex < GameMcpWorldCategories.Categories.Length;
             categoryIndex++)
        {
            var category = GameMcpWorldCategories.Categories[categoryIndex];
            if (!IsSearchable(category)) continue;
            if (only is not null && !ReferenceEquals(category, only)) continue;
            var availability = GameMcpWorldCategories.Availability(world, category);
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
                if (worn is not null && !worn.Contains(identity)) continue;
                if (sources is not null && !sources.ContainsKey(identity)) continue;
                if (wanted.Length > 0 &&
                    !string.Equals(SearchLifecycle(world, row), wanted, StringComparison.Ordinal))
                {
                    continue;
                }
                if (wantedRun.Length > 0 &&
                    !string.Equals(SearchRun(row), wantedRun, StringComparison.Ordinal))
                {
                    continue;
                }
                if (discoveredFilter is { } wantedDiscovery &&
                    SearchDiscovery(row) != wantedDiscovery)
                {
                    continue;
                }
                var tier = GameMcpSearchTier.Name;
                var matchedOn = GameMcpListColumns.Absent;
                if (normalized.Length > 0 &&
                    !TryTier(
                        world, category, identity, keywords, effects, normalized, out tier,
                        out matchedOn))
                {
                    continue;
                }
                if (!seen.Add(identity)) continue;
                hits.Add(new GameMcpSearchHit(tier, identity, categoryIndex, rowIndex, matchedOn));
                if (normalized.Length > 0)
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
        var whole = !limitFromCaller && hits.Count <= GameMcpWorldQuery.WholeCategoryRows;
        var rows = new JArray();
        var estimatedBytes = 128;
        for (var index = offset; index < hits.Count; index++)
        {
            if (rows.Count >= limit && !whole) break;
            var hit = hits[index];
            var category = GameMcpWorldCategories.Categories[hit.Category];
            var row = category.Row(world, hit.Row);
            var projected = ProjectSearchMatch(
                category, hit.Identity, keywords.Line(hit.Identity), hit.MatchedOn,
                sources is null ? null : sources[hit.Identity]);
            var local = GameMcpWorldQuery.LocalizedRequirementImplications(
                world, new HashSet<Guid> { hit.Identity });
            var localOffers = GameMcpWorldQuery.LocalizedDiscoveryOfferImplications(
                world, new HashSet<Guid> { hit.Identity });
            var matchBytes = GameMcpWorldQuery.EstimateListRowBytes(world, category, row, projected);
            GameMcpValue match;
            // A search hit is a row of a page, so it wears what a list row wears.
            if (local.Count == 0 && localOffers.Count == 0)
            {
                match = projected;
            }
            else
            {
                match = GameMcpWorldQuery.WithIncompleteNote(projected, GameMcpWorldQuery.IncompleteRowNote(local.Count > 0));
                matchBytes = checked(matchBytes + 192);
            }
            if (rows.Count > 0 && !whole &&
                estimatedBytes + matchBytes > GameMcpWorldQuery.MaximumListResponseBytes)
            {
                break;
            }
            estimatedBytes += matchBytes;
            rows.Add(match);
        }

        var result = new JObject();
        result["total"] = hits.Count;
        if (unavailableCategories.Count > 0)
            result["unavailableCategories"] = unavailableCategories;
        var summary = KeywordHitSummary(keywordHits);
        if (summary.Length > 0) result["keywordHits"] = summary;

        // The published world is not everything this build loaded, and the one moment that
        // difference matters is the moment this page comes back empty: a caller who searched a word
        // and found nothing has a second question — is it in the game at all — and no line on this
        // surface said the question had an answer. Said only on the empty page, and said whichever
        // way it comes out, because "nothing loaded is called that" closes the question where
        // silence would send the caller off to ask it anyway.
        //
        // The line names no verb. It used to point at the page that listed the leftovers and count
        // this query against it; the world publishes those rows now, so the pointer would name a
        // verb this server no longer has and the count would be a number with nothing to spend it
        // on. What is left is the answer itself, in one sentence, with no noun declined against a
        // count.
        if (hits.Count == 0 && normalized.Length > 0)
        {
            result["unprojected"] =
                GameMcpEntityCatalog.AnswersOnlyAsInternalMachinery(
                    world.EntityIdentities, normalized)
                    // "No id this build loaded answers to this query" is false the moment ids do
                    // answer, and the caller's question is answered either way: the word names
                    // something in this build, and that something is machinery no reader can act on.
                    ? "what answers to this query in this build is internal machinery the world " +
                      "does not publish."
                    : "no id this build loaded answers to this query.";
        }
        if (rows.Count == 0)
        {
            result["columns"] = GameMcpEntityWireNormalizer.WireColumns(
                sources is null ? SearchColumns : SourceSearchColumns);
        }
        result["rows"] = rows;
        if (offset + rows.Count < hits.Count) result["nextOffset"] = offset + rows.Count;
        return result;
    }

    /// <summary>
    /// What the id a keyword filter named turned out to be, by the category that publishes it — the
    /// same way the run filter names the scope it could not narrow.
    /// </summary>
    /// <remarks>
    /// A spell type lands here rather than as an error: all twenty-two hold values instead of
    /// distributing, so no total indexes them and their pages print no members line. So does an id
    /// that is no type at all, and an id the world published no row for.
    /// </remarks>
    private static string KeywordScope(GameWorldState world, Guid uuid) =>
        GameMcpWorldQuery.TryEntityCategory(world, uuid, out var category)
            ? "published under " + category.Name
            : "published under no category this world holds";

    /// <summary>
    /// Whether search can address this category's rows at all: it indexes entities, and a composite
    /// diagnostic row has no identity of its own to return.
    /// </summary>
    internal static bool IsSearchable(GameMcpWorldCategory category) =>
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
    /// it, which is what the filter reaches: the three purchasables plus alchemy recipes, augment
    /// glyphs, rituals, plot nodes and challenges.
    /// </para>
    /// <para>
    /// A category with no lifecycle model still does not match a state filter, and is still not
    /// excluded from an unfiltered search — inventing a word here for a row whose page never speaks
    /// one is exactly what one vocabulary per fact forbids. <c>challenges</c> is now read, because
    /// it now has a lifecycle to read: its run words moved to <c>run</c>, which this never consults.
    /// </para>
    /// </remarks>
    private static string SearchLifecycle(GameWorldState world, object row) => row switch
    {
        WorldUpgrade upgrade => GameMcpWorldQuery.UpgradeState(world, in upgrade),
        WorldResearch research => GameMcpWorldQuery.ResearchLifecycle(in research),
        WorldStructure structure => GameMcpWorldQuery.StructureState(in structure),
        WorldAlchemyRecipe recipe => GameMcpWorldQuery.AlchemyRecipeState(in recipe),
        WorldGlyph glyph => GameMcpWorldQuery.GlyphState(in glyph),
        WorldRitual ritual => GameMcpWorldQuery.RitualState(in ritual),
        WorldPlotNode plot => GameMcpWorldQuery.PlotNodeState(in plot),
        WorldChallenge challenge => GameMcpWorldQuery.ChallengeLifecycle(in challenge),
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
        GameMcpEffectWordIndex effects,
        string query,
        out GameMcpSearchTier tier,
        out string matchedOn)
    {
        matchedOn = GameMcpEntityCatalog.MatchedIdentityField(
            world.EntityIdentities, identity, query);
        if (matchedOn.Length > 0)
        {
            tier = GameMcpSearchTier.Name;
            return true;
        }
        var words = keywords.Words(identity);
        for (var index = 0; index < words.Count; index++)
        {
            if (words[index].IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            tier = GameMcpSearchTier.Keyword;
            matchedOn = "keywords";
            return true;
        }
        if (category.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            tier = GameMcpSearchTier.Category;
            matchedOn = "category";
            return true;
        }
        if (category.RowTypeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            category.ExpectedNativeType.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            tier = GameMcpSearchTier.Category;
            matchedOn = "nativeType";
            return true;
        }

        // What the thing does, when nothing about what it is called said so. This is the answer to
        // "what affects my X": the query is matched against the property each authored effect moves
        // and the name of the entity it moves it on, so a reader who knows the effect and not the
        // name still finds it, and the row says that is why it is here.
        if (effects.Matches(identity, query))
        {
            tier = GameMcpSearchTier.Effect;
            matchedOn = "effects";
            return true;
        }
        tier = GameMcpSearchTier.Name;
        matchedOn = GameMcpListColumns.Absent;
        return false;
    }

    /// <summary>
    /// What this row's own attempt is doing, or nothing where its category runs nothing.
    /// </summary>
    /// <remarks>
    /// Challenges are the one category that publishes a second lifecycle column, and until now only
    /// the first of the two could be filtered. A round that wanted the rows reading
    /// <c>run: passed</c> had to page all 98 by hand — four calls with hand-computed offsets — to
    /// prove a negative. This reads the word the challenge's own list row says and derives none.
    /// </remarks>
    private static string SearchRun(object row) =>
        row is WorldChallenge challenge ? GameMcpWorldQuery.ChallengeRun(challenge.State) : string.Empty;

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

    private static GameMcpValue ProjectSearchMatch(
        GameMcpWorldCategory category,
        Guid identity,
        string keywords,
        string matchedOn,
        string? effect = null)
    {
        var match = new JObject
        {
            ["entityId"] = identity.ToString("D"),
            ["category"] = category.Name,
            ["keywords"] = keywords,
            ["matchedOn"] = matchedOn,
        };

        // What this row does to the thing the modifies filter named — the one column that filter
        // needs, because the row is only on the page for that reason.
        if (effect is not null) match["effect"] = effect;
        return match.Freeze();
    }

    /// <summary>
    /// Whether a row is discovered, for the categories that spell it in that word, and nothing for
    /// the rest — which is how a lifecycle word reads on a category with no lifecycle model.
    /// </summary>
    /// <remarks>
    /// A round needed one discovered-but-unequipped spell recipe out of sixty-five, and no listing
    /// answered either half: it guessed three names off their sound and paid a detail read to find
    /// out two of them were wrong. Categories that spell discovery as their lifecycle
    /// <c>state</c> — a ritual, a glyph and an alchemy recipe are all drawn on the member their
    /// screens read, which <em>is</em> <c>IsDiscovered()</c> — are narrowed by the <c>state</c>
    /// filter that already reaches them, not by a second word for one fact.
    /// </remarks>
    private static bool? SearchDiscovery(object row) => row switch
    {
        WorldSpellRecipe recipe => recipe.Discovered,
        WorldTimeRune rune => rune.Discovered,
        _ => null,
    };

    private static readonly string[] SearchColumns =
        { "entityId", "category", "keywords", "matchedOn" };

    private static readonly string[] SourceSearchColumns =
        { "entityId", "category", "keywords", "matchedOn", "effect" };

    /// <summary>
    /// The thing a <c>modifies</c> filter names: an id or a handle a row printed, or the name the
    /// screen prints. A name several ids answer to is taken only when exactly one of them is moved
    /// by anything, because that is the only one the filter could mean.
    /// </summary>
    private static bool TryResolveModifiesTarget(
        GameWorldState world,
        string text,
        out Guid targetId,
        out KeyValuePair<string, string> refusal)
    {
        refusal = default;
        var catalog = world.EntityIdentities;
        switch (GameMcpEntityHandle.Resolve(text, catalog, out targetId, out var candidates))
        {
            case GameMcpEntityHandle.ResolutionOutcome.Resolved:
                return true;
            case GameMcpEntityHandle.ResolutionOutcome.Ambiguous:
                refusal = new KeyValuePair<string, string>(
                    "ambiguous_handle",
                    "'" + text + "' is the start of " + candidates.Count + " ids; use more of " +
                    "the id a row printed");
                return false;
        }

        var named = new List<Guid>();
        if (catalog.IsBound)
        {
            var rows = catalog.Rows.AsSpan();
            for (var index = 0; index < rows.Length; index++)
            {
                if (string.Equals(rows[index].DisplayName, text, StringComparison.OrdinalIgnoreCase))
                    named.Add(rows[index].EntityId);
            }
        }

        if (named.Count > 1)
        {
            var index = GameMcpEffectSourceIndex.Build(world);
            named.RemoveAll(candidate => index.Sources(candidate) is null);
            if (named.Count > 1)
            {
                refusal = new KeyValuePair<string, string>(
                    "ambiguous_handle",
                    "'" + text + "' names " + named.Count + " things that are each moved by " +
                    "something; use the id of the one you mean");
                return false;
            }
        }

        if (named.Count == 1)
        {
            targetId = named[0];
            return true;
        }

        refusal = new KeyValuePair<string, string>(
            "modifies_unknown",
            "'" + text + "' is neither a published id nor a name the screen prints; modifies " +
            "takes the id or the name of the thing an effect moves");
        return false;
    }
}
#endif
