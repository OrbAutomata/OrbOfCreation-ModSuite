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
            return result;
        }

        result = new JObject();
        for (var index = 0; index < source.Paths.Length; index++)
            CopyPath(complete, result, source.Paths[index]);
        if (!source.Addressable && result["uuid"] is null)
        {
            result["category"] = source.Category;
            result["addressable"] = false;
        }
        return result;
    }

    /// <summary>
    /// One declared column, filled from the row or filled with the word for having nothing to fill
    /// it with. The declaration is what the page's header promises, so skipping a path the row
    /// happened not to carry made the header a fact about the rows rather than about the category.
    /// </summary>
    private static void CopyPath(JObject source, JObject destination, string path)
    {
        var segments = path.Split('.');
        JToken? value = source;
        for (var index = 0; index < segments.Length && value is not null; index++)
            value = value[segments[index]];
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
        target[segments[segments.Length - 1]] = Declared(value);
    }

    /// <summary>
    /// The value a declared column carries. A member the game published nothing under says so, and
    /// so does one holding the zero identity — a handle that addresses nothing is not an entity,
    /// and dropping it would take the column with it on a page where no row has one.
    /// </summary>
    private static JToken Declared(JToken? value)
    {
        if (value is null) return new JValue(GameMcpListColumns.Unset);
        if (value is JValue { Type: JTokenType.String } text &&
            Guid.TryParseExact((string?)text, "D", out var uuid) && uuid == Guid.Empty)
        {
            return new JValue(GameMcpListColumns.Unset);
        }
        return value.DeepClone();
    }
}
#endif
