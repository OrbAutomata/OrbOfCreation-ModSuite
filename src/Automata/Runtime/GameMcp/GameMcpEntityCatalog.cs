#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>
/// MCP query projection over the Common live identity catalog. This owns no names and performs no
/// native read: every result comes from the immutable catalog pinned by the answering world.
/// </summary>
internal static class GameMcpEntityCatalog
{
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
    /// A caller who names no category implies nothing, so the block keeps the type.
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

    /// <summary>
    /// Whether any id this build loaded and the published world carries no row for answers to this
    /// query — that is, whether the answer is the build's own internal machinery.
    /// </summary>
    /// <remarks>
    /// This is asked at one moment: when a search of the published world comes back empty. "I
    /// searched for that word and got nothing" is exactly when "is it in the game at all?" becomes
    /// the next question, and nothing else on the surface says the question has an answer.
    /// "Nothing this build loaded is called that" and "the only things called that are machinery"
    /// are different answers to it, and a caller who cannot tell them apart goes looking. No count
    /// rides with the verdict: there is no page left to call with it, so a number would be a fact
    /// nothing could be done with. An id the world does publish is neither answer, because the page
    /// that raised the question is the one that already looked for it.
    /// </remarks>
    internal static bool AnswersOnlyAsInternalMachinery(
        EntityIdentityCatalogSnapshot catalog,
        string query)
    {
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0 || !catalog.IsBound) return false;
        var rows = catalog.Rows.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            if (Matches(in row, normalized) &&
                GameMcpEntityCatalogScope.IsMachinery(row.RuntimeType))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Matches(in EntityIdentityName row, string query) =>
        row.EntityId.ToString("D").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
        row.RuntimeType.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
        row.AssetName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
        row.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private static JObject Project(
        EntityIdentityCatalogSnapshot catalog,
        in EntityIdentityName row,
        string impliedNativeType)
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
        // The block prints no `category` cell of its own; the word is read here because the
        // `world_get` block this projection sits inside prints one, and an asset id that is the name
        // plus that word says nothing twice. An id whose type no category claims finds none and is
        // left with the de-spaced rule alone.
        GameMcpEntityCapabilityMap.TryCategoryForNativeType(row.RuntimeType, out var category);
        if (row.AssetName.Length > 0 &&
            !(named && SaysNothingNew(identity.Name, row.AssetName, category)))
        {
            result["internalName"] = row.AssetName;
        }

        // An id nobody can name says so in its name cell — `(unnamed 2c20e7)`, the one form this
        // surface has for it. A second block saying the same thing put a refusal class in a table
        // cell, and a table refuses nothing: the reader who has already read `(unnamed …)` learned
        // from `unavailable (ERR_UNAVAILABLE): …` only that the row would say it twice.
        return result;
    }

    /// <summary>
    /// Whether the Unity asset id is the player's own word with its spaces and punctuation taken
    /// out — <c>Specialization: Storm</c> against <c>SpecializationStorm</c> — or that word plus
    /// the one the block's own <c>category</c> already states, and so says nothing the lines beside
    /// it have not already said.
    /// </summary>
    /// <remarks>
    /// Most assets are named that way, and the field was published on every one of them: a live
    /// round carried 143 of these and 110 were this, byte for byte derivable from the line above.
    /// The rule is stated in the verb's contract, so absence means "the de-spaced name" rather than
    /// "unknown", and the identifiers that genuinely differ — the camel-cased internals, the
    /// renamed assets — still ship, which is the whole reason the field exists.
    /// </remarks>
    private static bool SaysNothingNew(string name, string assetName, string category) =>
        MatchDeSpaced(name, assetName) == assetName.Length ||
        RestatesCategory(name, assetName, category);

    /// <summary>
    /// How much of the asset id the player's own word accounts for, or −1 where it does not lead it.
    /// </summary>
    private static int MatchDeSpaced(string name, string assetName)
    {
        var read = 0;
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (!char.IsLetterOrDigit(character)) continue;
            if (read >= assetName.Length || assetName[read] != character) return -1;
            read++;
        }
        return read;
    }

    /// <summary>
    /// Whether what the asset id adds to the name is only the word the block's own <c>category</c>
    /// already states — <c>Strength</c> plus <c>rituals</c> is the whole of <c>StrengthRitual</c>.
    /// </summary>
    /// <remarks>
    /// An id whose type no published category claims names none here and the rule cannot fire for
    /// it; where one is named it is the <c>world_get</c> block's own category line, right there.
    /// Six of one round's eleven `internalName` lines were this: `StrengthRitual`,
    /// `ArtistryResearch`, `MiningActionType`, `MiningHarvestAction`, `AlchemistStructures`,
    /// `PlantHarvestAction` — the name, then the category, on a block that prints the category one
    /// line down. Every word the id adds has to be one the category says, so an id that adds a fact
    /// — `SpellOutputLevel`, `ReserveLevel` — still ships whole, and one that does not lead with the
    /// name at all, like `PNACraggySpireMine`, never reaches this test.
    /// </remarks>
    private static bool RestatesCategory(string name, string assetName, string category)
    {
        var read = MatchDeSpaced(name, assetName);
        if (read <= 0 || read == assetName.Length) return false;
        var start = read;
        for (var index = read + 1; index <= assetName.Length; index++)
        {
            if (index != assetName.Length && !char.IsUpper(assetName[index])) continue;
            if (!Names(category, assetName.Substring(start, index - start))) return false;
            start = index;
        }
        return true;
    }

    /// <summary>Whether a category's own words already contain this one, singular or plural.</summary>
    private static bool Names(string category, string word)
    {
        var start = 0;
        for (var index = 0; index <= category.Length; index++)
        {
            if (index != category.Length && category[index] != '-') continue;
            if (string.Equals(
                    Singular(category.Substring(start, index - start)),
                    Singular(word),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            start = index + 1;
        }
        return false;
    }

    private static string Singular(string word) =>
        word.Length > 1 && (word[word.Length - 1] == 's' || word[word.Length - 1] == 'S')
            ? word.Substring(0, word.Length - 1)
            : word;

    private static JObject NotAvailable(string code, string reason) => new()
    {
        ["status"] = "not_available",
        ["code"] = code,
        ["reason"] = reason,
    };
}
#endif
