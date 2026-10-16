#if SERVICE_CYCLE_PROFILE
using System;
using Newtonsoft.Json.Linq;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>HTTP-side wire encoding for immutable frame-operation results.</summary>
internal static class GameMcpDocumentJsonEncoder
{
    internal static JToken Encode(
        GameMcpValue value,
        EntityIdentityCatalogSnapshot catalog)
    {
        GameMcpFrameThreadBoundary.AssertTransportWorkAllowed("JSON encoding");
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        return GameMcpEntityWireNormalizer.Normalize(EncodeValue(value), catalog);
    }

    private static JToken EncodeValue(GameMcpValue value)
    {
        return value switch
        {
            GameMcpObject item => EncodeObject(item),
            GameMcpArray item => EncodeArray(item),
            GameMcpScalar item => new JValue(item.Value),
            GameMcpProjectedDomainValue item => EncodeProjection(item),
            GameMcpDomainValue item => GameMcpObjectProjector.Project(item.Value),
            GameMcpNull => JValue.CreateNull(),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static JObject EncodeObject(GameMcpObject source)
    {
        var result = new JObject();
        for (var index = 0; index < source.Properties.Count; index++)
        {
            var property = source.Properties[index];
            var encoded = EncodeValue(property.Value);
            // A written empty collection is evidence (for example, a cleared glyph layout or
            // an exhausted search). Preserve it; only an unwritten field means not applicable.
            if (encoded.Type == JTokenType.Null || encoded is JObject { Count: 0 })
            {
                continue;
            }
            result[property.Name] = encoded;
        }
        return result;
    }

    private static JArray EncodeArray(GameMcpArray source)
    {
        var result = new JArray();
        for (var index = 0; index < source.Items.Count; index++)
            result.Add(EncodeValue(source.Items[index]));
        return result;
    }

    private static JObject EncodeProjection(GameMcpProjectedDomainValue source)
    {
        var complete = GameMcpObjectProjector.Project(source.Value) as JObject ?? new JObject();
        JObject result;
        if (source.Paths.Length == 0)
        {
            result = complete;
            result["mcpCategory"] = source.Category;
            if (!source.Addressable && result["uuid"] is null) result["addressable"] = false;
            Attach(result, source.Attached);
            return result;
        }

        result = new JObject();
        for (var index = 0; index < source.Paths.Length; index++)
            CopyPath(complete, result, source.Paths[index], source.TableRow);
        if (!source.Addressable && result["uuid"] is null)
        {
            result["category"] = source.Category;
            result["addressable"] = false;
        }
        Attach(result, source.Attached);
        return result;
    }

    /// <summary>
    /// The producer's own blocks, beside the fields the declaration filled. A block never displaces
    /// a declared field: the declaration is the row's own answer, and this is what a related table
    /// says about it.
    /// </summary>
    private static void Attach(JObject result, GameMcpObject? attached)
    {
        if (attached is null) return;
        for (var index = 0; index < attached.Properties.Count; index++)
        {
            var property = attached.Properties[index];
            if (result[property.Name] is not null) continue;
            result[property.Name] = EncodeValue(property.Value);
        }
    }

    /// <summary>
    /// One declared path, filled from the row — or, in a table, filled with the word for having
    /// nothing to fill it with. The declaration is what the page's header promises, so skipping a
    /// path the row happened not to carry made the header a fact about the rows rather than about
    /// the category. A detail block promises no header, so it says nothing instead.
    /// </summary>
    private static void CopyPath(
        JObject source,
        JObject destination,
        string path,
        bool tableRow)
    {
        var segments = path.Split('.');
        JToken? value = source;
        for (var index = 0; index < segments.Length && value is not null; index++)
            value = value[segments[index]];
        var filled = Declared(value, tableRow);
        if (filled is null) return;
        var target = destination;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (target[segments[index]] is not JObject nested)
            {
                nested = new JObject();
                target[segments[index]] = nested;
            }
            target = nested;
        }
        target[segments[segments.Length - 1]] = filled;
    }

    /// <summary>
    /// The value a declared path carries, or nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In a table, a member the game published nothing under says so, and so does one holding the
    /// zero identity — a handle that addresses nothing is not an entity, and dropping it would take
    /// the column with it on a page where no row has one. Outside a table there is no column to
    /// take: silence is what "does not apply" reads as on every other block of the surface, and a
    /// detail block that spelled a gap the reader never asked about was the one place this surface
    /// answered a question nobody put. It also ends a split spelling of one fact — the wire
    /// normalizer already drops the zero identity from every projection that declares no paths.
    /// </para>
    /// <para>
    /// A member the game published as an empty string is one of those gaps and not a value: an
    /// effect block whose script names no effect type has no effect type, the same way a row with
    /// no such member has none. The page already reads both as <c>-</c> in a table, so the
    /// document now agrees with the page it renders into — and outside a table the empty string
    /// goes quiet with every other absence rather than being the one that still spoke.
    /// </para>
    /// </remarks>
    private static JToken? Declared(JToken? value, bool tableRow)
    {
        if (value is null) return tableRow ? new JValue(GameMcpListColumns.Absent) : null;
        if (value is JValue { Type: JTokenType.String } text)
        {
            var published = (string?)text ?? string.Empty;
            if (published.Length == 0) return tableRow ? value.DeepClone() : null;
            if (Guid.TryParseExact(published, "D", out var uuid) && uuid == Guid.Empty)
                return tableRow ? new JValue(GameMcpListColumns.Absent) : null;
        }
        return value.DeepClone();
    }
}
#endif
