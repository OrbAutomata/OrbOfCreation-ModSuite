#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The one wire spelling of an entity id, and the one reader of a caller's id argument.
/// </summary>
/// <remarks>
/// <para>
/// Identity is still the full canonical UUID everywhere inside the suite: this type changes what an
/// id looks like on the wire and what a caller may type, never what identity is. The full 36-char
/// form cost 17.4% of one live round's bytes for an address a caller used about once per hundred
/// emissions.
/// </para>
/// <para>
/// The length is a property of the id set rather than of any one id, and the suite pins the game
/// build, so the set is fixed at build time and so is the length: <see cref="Length"/> is the
/// shortest prefix that is unique across every published id in <c>data/entity-mappings.tsv</c>,
/// floored at six. There is no runtime recompute and no per-handle special case — every handle on
/// the wire is the same width, which is what makes the whole surface readable at a glance. A
/// portable test recomputes the count against that same file, so a build whose id set moved fails
/// the gate rather than shipping a colliding handle.
/// </para>
/// </remarks>
internal static class GameMcpEntityHandle
{
    /// <summary>
    /// Shortest prefix unique across all 2,818 published ids of the pinned build. Six is also the
    /// floor this surface will use whatever a future id set allows.
    /// </summary>
    internal const int Length = 6;

    /// <summary>The wire spelling of one id.</summary>
    internal static string Format(Guid uuid) => uuid.ToString("D").Substring(0, Length);

    /// <summary>
    /// The one name every surface says for an id: the player's own word for it, or a marked stand-in
    /// when the catalog has none.
    /// </summary>
    /// <remarks>
    /// A nameless id used to render as a bare id, which reads exactly like a named row whose name
    /// happens to be a hex string — so the one case where a caller must stop and look the thing up
    /// was the one case that announced nothing. <c>(unnamed 2c20e7)</c> says it out loud.
    /// </remarks>
    internal static string Name(Guid uuid, EntityIdentityCatalogSnapshot? catalog = null)
    {
        var identity = EntityIdentityFormatter.Describe(uuid, catalog);
        return identity.HasName ? identity.Name : Unnamed(uuid);
    }

    internal static string Unnamed(Guid uuid) => "(unnamed " + Format(uuid) + ")";

    internal enum ResolutionOutcome
    {
        Resolved = 0,
        NotFound = 1,
        Ambiguous = 2,
    }

    /// <summary>
    /// Reads a caller's id argument: a full canonical UUID, or any prefix of one that names exactly
    /// one published id. An ambiguous prefix answers with the ids it matched rather than guessing.
    /// </summary>
    internal static ResolutionOutcome Resolve(
        string text,
        EntityIdentityCatalogSnapshot catalog,
        out Guid uuid,
        out IReadOnlyList<Guid> candidates)
    {
        uuid = Guid.Empty;
        candidates = Array.Empty<Guid>();
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return ResolutionOutcome.NotFound;
        if (Guid.TryParseExact(trimmed, "D", out var exact))
        {
            uuid = exact;
            return ResolutionOutcome.Resolved;
        }
        if (catalog is null || !catalog.IsBound || !IsHexPrefix(trimmed))
            return ResolutionOutcome.NotFound;

        var matches = new List<Guid>();
        var rows = catalog.Rows.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            var candidate = rows[index].EntityId;
            if (!candidate.ToString("D").StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                continue;
            matches.Add(candidate);
            if (matches.Count > 8) break;
        }
        if (matches.Count == 0) return ResolutionOutcome.NotFound;
        if (matches.Count == 1)
        {
            uuid = matches[0];
            return ResolutionOutcome.Resolved;
        }
        candidates = matches;
        return ResolutionOutcome.Ambiguous;
    }

    private static bool IsHexPrefix(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var hex = (current >= '0' && current <= '9') ||
                (current >= 'a' && current <= 'f') ||
                (current >= 'A' && current <= 'F') ||
                current == '-';
            if (!hex) return false;
        }
        return true;
    }
}
#endif
