#if SERVICE_CYCLE_PROFILE
using System;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>
/// MCP query projection over the Common live identity catalog. This owns no names and performs no
/// native read: every result comes from the immutable catalog pinned by the answering world.
/// </summary>
internal static class GameMcpEntityCatalog
{
    internal static JObject Search(
        EntityIdentityCatalogSnapshot catalog,
        string query,
        int offset,
        int limit)
    {
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return NotAvailable("query_required", "query must not be empty");
        if (offset < 0)
            return NotAvailable("invalid_offset", "offset must be zero or greater");
        if (limit <= 0 || limit > 200)
            return NotAvailable("invalid_limit", "limit must be between 1 and 200");
        if (!catalog.IsBound)
            return NotAvailable(
                "entity_catalog_unavailable",
                catalog.FailureReason.Length > 0
                    ? catalog.FailureReason
                    : "the live entity catalog has not bound in this playing lifecycle yet");

        var page = new JArray();
        var totalMatches = 0;
        var estimatedBytes = 128;
        var budgetReached = false;
        var rows = catalog.Rows.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            if (!Matches(in row, normalized)) continue;
            totalMatches++;
            if (totalMatches <= offset || page.Count >= limit || budgetReached) continue;
            var rowBytes = EstimateRowBytes(in row);
            if (page.Count > 0 &&
                estimatedBytes + rowBytes > GameMcpWorldQuery.MaximumListResponseBytes)
            {
                budgetReached = true;
                continue;
            }
            estimatedBytes += rowBytes;
            page.Add(Project(catalog, in row));
        }

        var result = new JObject
        {
            ["total"] = totalMatches,
            ["rows"] = page,
        };
        if (offset + page.Count < totalMatches) result["nextOffset"] = offset + page.Count;
        return result;
    }

    /// <summary>
    /// One id's identity block.
    /// </summary>
    /// <remarks>
    /// <paramref name="impliedNativeType"/> is the native type the caller's own category declares
    /// for every row it holds. Where the row's runtime type is that type, the category beside it
    /// already says it and the block does not say it twice — the map from category to native type
    /// is what <c>world_categories</c> publishes. The moment a row's runtime type is something its
    /// category does not declare, the implication is not one-to-one for that row and
    /// <c>nativeType</c> stays, which is the only case where it carries a fact the category cannot.
    /// A caller browsing the catalog names no category and is told both.
    /// </remarks>
    internal static JObject Lookup(
        EntityIdentityCatalogSnapshot catalog,
        Guid uuid,
        string impliedNativeType = "")
    {
        if (!catalog.IsBound)
            return NotAvailable(
                "entity_catalog_unavailable",
                catalog.FailureReason.Length > 0
                    ? catalog.FailureReason
                    : "the live entity catalog has not bound in this playing lifecycle yet");
        if (catalog.TryGet(uuid, out var row))
            return Project(catalog, in row, impliedNativeType);

        // The id is on the row already; a sentence that repeats it spends the caller's line on a
        // string it just sent.
        var unavailable = NotAvailable(
            "entity_name_unavailable",
            "the live entity catalog has no entry for that id");
        unavailable["uuid"] = uuid.ToString("D");
        return unavailable;
    }

    /// <summary>
    /// Whether this id's own identity answers the query: what the player calls it, what the asset is
    /// called, or the id itself.
    /// </summary>
    /// <remarks>
    /// The runtime type is deliberately not here. It is the kind of thing this is rather than which
    /// thing it is, and a surface that ranks matches must not rank <c>UpgradeSO</c> against two
    /// hundred rows as if each of them were named that.
    /// </remarks>
    internal static bool MatchesIdentity(
        EntityIdentityCatalogSnapshot catalog,
        Guid uuid,
        string query) =>
        catalog.TryGet(uuid, out var row)
            ? row.EntityId.ToString("D").IndexOf(
                  query ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0 ||
              row.AssetName.IndexOf(
                  query ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0 ||
              row.DisplayName.IndexOf(
                  query ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0
            : uuid.ToString("D").IndexOf(
                query ?? string.Empty,
                StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// Which of an entity's own identity fields the query hit, or nothing where none did.
    /// </summary>
    /// <remarks>
    /// The player's word first, then the asset's, then the id — so a row that matches on more than
    /// one names the one a reader would have meant. Saying only that the identity matched was not
    /// enough: three of these are different facts, and the field that matched is what tells a caller
    /// whether the hit is the thing they were looking for or a coincidence in a string they never
    /// see.
    /// </remarks>
    internal static string MatchedIdentityField(
        EntityIdentityCatalogSnapshot catalog,
        Guid uuid,
        string query)
    {
        var text = query ?? string.Empty;
        if (!catalog.TryGet(uuid, out var row))
        {
            return uuid.ToString("D").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                ? "id"
                : string.Empty;
        }
        if (row.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return "name";
        if (row.AssetName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
            return "internalName";
        return row.EntityId.ToString("D").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
            ? "id"
            : string.Empty;
    }

    private static bool Matches(in EntityIdentityName row, string query) =>
        row.EntityId.ToString("D").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
        row.RuntimeType.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
        row.AssetName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
        row.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private static int EstimateRowBytes(in EntityIdentityName row) =>
        checked(192 +
            Encoding.UTF8.GetByteCount(row.RuntimeType) +
            Encoding.UTF8.GetByteCount(row.AssetName) +
            Encoding.UTF8.GetByteCount(row.DisplayName));

    private static JObject Project(
        EntityIdentityCatalogSnapshot catalog,
        in EntityIdentityName row,
        string impliedNativeType = "")
    {
        var identity = EntityIdentityFormatter.Describe(row.EntityId, catalog);
        var result = new JObject
        {
            ["uuid"] = row.EntityId.ToString("D"),
        };
        if (!string.Equals(row.RuntimeType, impliedNativeType, StringComparison.Ordinal))
            result["nativeType"] = row.RuntimeType;
        // A row whose only label is the Unity asset id has no player-facing name, so it publishes
        // none: `name` is the word the game shows or it is absent, on this surface and on every
        // other. `nameSource: asset` was the flag that admitted the substitution one surface made
        // and the entity rows made silently; with the substitution gone there is nothing to flag.
        var named = identity.HasName &&
            identity.Source != EntityIdentityNameSource.LiveAssetName;
        if (named) result["name"] = identity.Name;
        if (row.AssetName.Length > 0 &&
            !(named && string.Equals(identity.Name, row.AssetName, StringComparison.Ordinal)))
            result["internalName"] = row.AssetName;
        if (GameMcpEntityCapabilityMap.TryCategoryForNativeType(
                row.RuntimeType,
                out var category))
            result["category"] = category;
        else result["category"] = "not-world-projected";

        // An id nobody can name says so in its name cell — `(unnamed 2c20e7)`, the one form this
        // surface has for it. A second block saying the same thing put a refusal class in a table
        // cell, and a table refuses nothing: the reader who has already read `(unnamed …)` learned
        // from `unavailable (ERR_UNAVAILABLE): …` only that the row would say it twice.
        return result;
    }

    private static JObject NotAvailable(string code, string reason) => new()
    {
        ["status"] = "not_available",
        ["code"] = code,
        ["reason"] = reason,
    };
}
#endif
