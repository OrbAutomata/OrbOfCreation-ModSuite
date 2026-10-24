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
using static OrbAutomata.GameMcp.GameMcpCraftingProjection;
using static OrbAutomata.GameMcp.GameMcpDecisionReason;
using static OrbAutomata.GameMcp.GameMcpDiscoveryTreeOfferProjection;
using static OrbAutomata.GameMcp.GameMcpGlyphProjection;
using static OrbAutomata.GameMcp.GameMcpHarvestLifecycleProjection;
using static OrbAutomata.GameMcp.GameMcpLoadoutProjection;
using static OrbAutomata.GameMcp.GameMcpPurchaseProjection;
using static OrbAutomata.GameMcp.GameMcpResearchProjection;
using static OrbAutomata.GameMcp.GameMcpResourceProjection;
using static OrbAutomata.GameMcp.GameMcpRitualLifecycleProjection;
using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpScreenState;
using static OrbAutomata.GameMcp.GameMcpSpellLevelProjection;
using static OrbAutomata.GameMcp.GameMcpStructureLifecycleProjection;
using static OrbAutomata.GameMcp.GameMcpTargetingProjection;
using static OrbAutomata.GameMcp.GameMcpWorldRead;

using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWorldList
{
    internal const int DefaultPageSize = 50;

    /// <summary>How many target candidates a caller who named no limit is handed.</summary>
    internal const int TargetingCandidatePageSize = 25;
    internal const int MaximumPageSize = 200;

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

    internal static JObject ListRows(
        GameMcpFrameContext state,
        string categoryName,
        int offset,
        int limit,
        bool affordableOnly = false,
        bool limitFromCaller = true,
        bool? discovered = null)
    {
        if (!TryWorld(state, out var publication, out var unavailable))
            return unavailable;
        if (!GameMcpWorldCategories.TryCategory(categoryName, out var category, out var reason))
            return NoSuchCategory(publication, categoryName, reason);
        if (affordableOnly && !SupportsAffordableFilter(category))
        {
            return NotAvailable(
                publication,
                "filter_not_supported",
                "the affordable filter narrows a page by the price column its rows carry, and " +
                "rows in " + category.Name + " carry none; the categories whose rows carry one " +
                "are " + string.Join(", ", PricedCategories));
        }
        if (discovered is not null && !SupportsDiscoveredFilter(category))
        {
            return NotAvailable(
                publication,
                "filter_not_supported",
                "the discovered filter narrows a page by the discovered column its rows carry, " +
                "and rows in " + category.Name + " carry none; the categories whose rows carry " +
                "one are " + string.Join(", ", DiscoverableCategories));
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

        var availability = GameMcpWorldCategories.Availability(publication.Snapshot, category);
        var localizedRequirementCategory = !availability.Available &&
            category.LocalizesRequirementFailures &&
            TryLocalizedRequirementFailures(publication.Snapshot, out _);
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
        if (category.PageKind == GameMcpWorldPageKind.MasteryExperience)
            return MasteryExperienceSummary(publication, category, offset, limit);
        if (category.PageKind == GameMcpWorldPageKind.Targeting)
            return TargetingPage(publication, category, offset, limit, limitFromCaller);

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
            if (discovered is { } wanted && IsDiscoveredRow(category, row) != wanted) continue;

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
            rows.Add(local.Count == 0 && localOffers.Count == 0
                ? projected
                : WithIncompleteNote(projected, IncompleteRowNote(local.Count > 0)));
            estimatedBytes += rowBytes;
        }

        // One pagination rule: nextOffset present means more rows remain and names where to
        // resume. A page shorter than the limit with a nextOffset is the byte budget; a page
        // shorter than the limit without one is the end of the category.
        var result = new JObject();

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

    /// <summary>
    /// The pending target request, whose candidate list is the thing being paged.
    /// </summary>
    /// <remarks>
    /// One request is one row, so paging the rows could only ever hand back the same single row or
    /// nothing at all while the list a caller actually reads — 180 candidates, 8,216 bytes, the
    /// largest text payload of a live round — came back whole every time to settle one choice.
    /// <c>limit</c> and <c>offset</c> therefore reach the candidates, which is where the length is,
    /// and every candidate stays reachable by asking for the next offset.
    /// </remarks>
    internal static JObject TargetingPage(
        WorldPublication<GameWorldState> publication,
        GameMcpEntityCapabilityDescriptor category,
        int offset,
        int limit,
        bool limitFromCaller)
    {
        var world = publication.Snapshot;
        var page = limitFromCaller ? limit : TargetingCandidatePageSize;
        var count = category.Count(world);
        var rows = new JArray();
        for (var index = 0; index < count; index++)
        {
            if (category.Row(world, index) is not WorldTargetingRequest request) continue;
            // Past the end of the candidates is past the end of the page, the way it is in every
            // other category: a request whose candidates this offset has all gone by is not a row.
            if (offset > 0 && offset >= request.Candidates.Count) continue;
            rows.Add(ProjectListRow(world, category, request, offset, page));
        }

        var result = new JObject();
        if (rows.Count == 0) result["columns"] = ListColumns(category);
        result["rows"] = rows;
        // The outer page counts requests, which is what its rows are. How many candidates remain
        // and where to resume them belong to the candidate page, and are said there.
        result["total"] = count;
        return result;
    }

    internal static string[] ListColumns(GameMcpEntityCapabilityDescriptor category) =>
        GameMcpWireColumns.WireColumns(category.ListColumns);


    /// <summary>
    /// What a page row says when the world could not publish all of what gates its entity.
    /// </summary>
    /// <remarks>
    /// A row of a list stays a row. It used to be swapped for a refusal block — a status, a code
    /// that reads <c>ERR_</c> on the wire, a reason, the row itself demoted to <c>partialRow</c>
    /// and the whole leaf list inline — once for every entity an unmodelled condition gates, which
    /// on a mature save is most of page one. The row's own columns are what the page is for; one
    /// sentence says what is missing, and the read that holds the detail is <c>world_get</c>.
    /// </remarks>

    /// <summary>
    /// The note, on a hand-written projection or on a reflected one, without either losing what it
    /// already carries.
    /// </summary>
    internal static GameMcpValue ProjectListRow(
        GameWorldState world,
        GameMcpEntityCapabilityDescriptor category,
        object row,
        int nestedOffset = 0,
        int nestedLimit = int.MaxValue) =>
        WithOwnIdentity(
            category,
            Declared(
                category,
                category.ProjectList(world, row, nestedOffset, nestedLimit)));

    /// <summary>
    /// A hand-written projection answers to its category's declared column set. A category with no
    /// hand-written projection is already rendered from its declared field list, so it is total by
    /// construction and has nothing to check here.
    /// </summary>
    internal static GameMcpValue Declared(GameMcpEntityCapabilityDescriptor category, GameMcpValue projected)
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

    /// <summary>The categories whose rows publish a price, and therefore an affordability.</summary>
    internal static string[] PricedCategories => FilterCategories(priced: true);


    /// <summary>
    /// The categories whose rows spell discovery in that word. The rest spell it as their
    /// lifecycle <c>state</c> — a ritual, a glyph and an alchemy recipe are all drawn on the
    /// member their screens read, which <em>is</em> <c>IsDiscovered()</c> — so a filter naming
    /// them here would be a second grammar for a fact their own page already answers.
    /// </summary>
    internal static string[] DiscoverableCategories => FilterCategories(priced: false);

    private static string[] FilterCategories(bool priced)
    {
        var names = new List<string>();
        foreach (var category in GameMcpWorldCategories.Categories)
            if (priced ? category.Priced : category.Discoverable) names.Add(category.Name);
        return names.ToArray();
    }

    internal static bool SupportsDiscoveredFilter(GameMcpEntityCapabilityDescriptor category) =>
        category.Discoverable;


    /// <summary>Whether one row is discovered, read exactly as its own list row prints it.</summary>
    internal static bool IsDiscoveredRow(GameMcpEntityCapabilityDescriptor category, object row) =>
        category.IsDiscovered(row);

    internal static bool SupportsAffordableFilter(GameMcpEntityCapabilityDescriptor category) =>
        category.Priced;


    /// <summary>
    /// Whether one priced row can be bought right now, read exactly as its own list row reads it —
    /// an exhausted upgrade has no next level and so has no price to be short of.
    /// </summary>
    internal static bool IsAffordableRow(
        GameWorldState world, GameMcpEntityCapabilityDescriptor category, object row) =>
        category.IsAffordable(world, row);


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
    internal static int EstimateListRowBytes(
        GameWorldState world,
        GameMcpEntityCapabilityDescriptor category,
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
    internal const int NameFieldBytes = 10;

    /// <summary>What a canonical UUID gives back when the wire says it as a handle.</summary>
    internal const int HandleSavingBytes = 36 - GameMcpEntityHandle.Length;

    /// <summary>One large-magnitude value, which renders as a short scientific string.</summary>
    internal const int DomainValueBytes = 10;

    /// <summary>One path copied out of a reflected row: its key, its punctuation, and its value.</summary>
    internal const int ProjectedPathBytes = 48;

    /// <summary>A whole reflected row, whose shape is not known until the transport projects it.</summary>
    internal const int UnprojectedRowBytes = 512;

    internal static int MeasureDocumentBytes(GameMcpValue value)
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

    internal static int DefaultLimit => DefaultPageSize;
}
#endif
