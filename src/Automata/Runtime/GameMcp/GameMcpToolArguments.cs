#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpToolArguments
{
    internal static void ValidateToolArguments(GameMcpToolDescriptor tool, JObject arguments)
    {
        var name = tool.Name;
        var schema = tool.Listing.InputSchema;
        var properties = schema["properties"] as JObject ?? new JObject();
        var errors = new List<string>();
        if (schema["required"] is JArray required)
        {
            foreach (var field in required.Values<string>())
            {
                if (field is not null && !arguments.ContainsKey(field))
                    errors.Add("required field '" + field + "' is missing");
            }
        }

        tool.Validate?.Invoke(arguments, errors);

        foreach (var supplied in arguments.Properties())
        {
            if (properties.ContainsKey(supplied.Name)) continue;
            errors.Add("field '" + supplied.Name + "' is not accepted by " + name);
        }

        AddModeRuleErrors(errors, schema, arguments);

        if (errors.Count > 0) throw GameMcpInvalidParamsException.Validation(errors);
    }

    /// <summary>
    /// Enforces the per-mode rules the tool publishes in its schema, so the contract a caller reads
    /// is the one the server holds it to. A field the mode forbids is refused rather than dropped.
    /// </summary>
    private static void AddModeRuleErrors(List<string> errors, JObject schema, JObject arguments)
    {
        if (schema["allOf"] is not JArray rules || arguments["mode"]?.Type != JTokenType.String) return;
        var mode = (string)arguments["mode"]!;
        var topLevel = schema["required"] as JArray;
        foreach (var rule in rules)
        {
            if ((string?)rule["if"]?["properties"]?["mode"]?["const"] != mode) continue;
            var then = rule["then"]!;
            foreach (var field in then["required"]?.Values<string>() ?? Enumerable.Empty<string>())
            {
                if (field is null || arguments.ContainsKey(field)) continue;
                if (topLevel?.Values<string>().Contains(field) == true) continue;
                errors.Add("required field '" + field + "' is missing for mode '" + mode + "'");
            }
            foreach (var any in then["not"]?["anyOf"] ?? new JArray())
            {
                var field = (string?)any["required"]?[0];
                if (field is not null && arguments.ContainsKey(field))
                    errors.Add("field '" + field + "' is not accepted for mode '" + mode + "'");
            }
        }
    }

    internal static JObject ModeSchema(JObject schema, params JObject[] rules)
    {
        schema["allOf"] = new JArray(rules);
        return schema;
    }

    internal static JObject ModeRule(
        string mode,
        string[]? required = null,
        string[]? forbidden = null)
    {
        var then = new JObject();
        if (required is { Length: > 0 }) then["required"] = new JArray(required);
        if (forbidden is { Length: > 0 })
        {
            var any = new JArray();
            for (var index = 0; index < forbidden.Length; index++)
                any.Add(new JObject { ["required"] = new JArray(forbidden[index]) });
            then["not"] = new JObject { ["anyOf"] = any };
        }
        return new JObject
        {
            ["if"] = new JObject
            {
                ["properties"] = new JObject
                {
                    ["mode"] = new JObject { ["const"] = mode },
                },
                ["required"] = new JArray("mode"),
            },
            ["then"] = then,
        };
    }

    internal static JObject ObjectSchema(JObject? properties = null, params string[] required)
    {
        var result = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties ?? new JObject(),
            ["additionalProperties"] = false,
        };
        if (required.Length > 0) result["required"] = new JArray(required);
        return result;
    }

    internal static JObject StringSchema(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description,
    };

    internal static JObject BooleanSchema(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description,
    };

    internal static JObject ArraySchema(JObject items, int minimum, int maximum) => new()
    {
        ["type"] = "array",
        ["items"] = items,
        ["minItems"] = minimum,
        ["maxItems"] = maximum,
    };

    /// <summary>
    /// A declared ceiling is published; an int.MaxValue placeholder is not. Publishing the
    /// placeholder put a number in the shape of a bound that no part of the game ever chose.
    /// </summary>
    internal static JObject DescribedSchema(JObject schema, string description)
    {
        schema["description"] = description;
        return schema;
    }

    internal static JObject IntegerSchema(int minimum, int maximum)
    {
        var schema = new JObject
        {
            ["type"] = "integer",
            ["minimum"] = minimum,
        };
        if (DeclaresCeiling(maximum)) schema["maximum"] = maximum;
        return schema;
    }

    internal static JObject EnumSchema(params string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JArray(values),
    };

    internal static JObject RequireObject(JObject source, string name) =>
        source[name] as JObject ??
        throw new GameMcpInvalidParamsException(name + " must be an object");

    internal static string RequireString(JObject source, string name)
    {
        if (source[name]?.Type != JTokenType.String)
            throw new GameMcpInvalidParamsException(name + " must be a string");
        var value = ((string?)source[name] ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new GameMcpInvalidParamsException(name + " must not be empty");
        return value;
    }

    internal static string OptionalString(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token)) return string.Empty;
        if (token.Type != JTokenType.String)
            throw new GameMcpInvalidParamsException(name + " must be a string");
        var value = ((string?)token ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new GameMcpInvalidParamsException(name + " must not be empty when supplied");
        return value;
    }

    private static string[] RequireStringArray(JObject source, string name, int maximum)
    {
        if (source[name] is not JArray array)
            throw new GameMcpInvalidParamsException(name + " must be an array");
        if (array.Count == 0 || array.Count > maximum)
            throw new GameMcpInvalidParamsException(
                name + " must contain between 1 and " + maximum + " entries");
        var result = new string[array.Count];
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index]?.Type != JTokenType.String)
                throw new GameMcpInvalidParamsException(
                    name + "[" + index + "] must be a string");
            result[index] = ((string?)array[index] ?? string.Empty).Trim();
            if (result[index].Length == 0)
                throw new GameMcpInvalidParamsException(
                    name + "[" + index + "] must not be empty");
        }
        return result;
    }

    /// <summary>
    /// The batch form of <see cref="ReadEntityId"/>. A caller pages a list, reads its handles, and
    /// hands them straight back; an entry the resolver cannot place is named where it stands rather
    /// than reaching the world reader as a UUID nothing published.
    /// </summary>
    internal static string[] ReadEntityIdArray(JObject source, string name, int maximum)
    {
        var text = RequireStringArray(source, name, maximum);
        var result = new string[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            result[index] = ReadEntityId(text[index], name + "[" + index + "]")
                .ToString("D");
        }
        return result;
    }

    internal const string GlyphLayoutShape =
        "glyphs takes a list of {uuid, count} objects, e.g. " +
        "[{\"uuid\": \"<augment glyph id>\", \"count\": 1}], or [] for no augments";

    internal static GameMcpUuidCount[] RequireGlyphLayout(JObject source)
    {
        try { return RequireUuidCountArray(source, "glyphs", 64); }
        catch (GameMcpInvalidParamsException exception)
        {
            throw new GameMcpInvalidParamsException(
                exception.Message + "; " + GlyphLayoutShape, exception.Code);
        }
    }

    private static GameMcpUuidCount[] RequireUuidCountArray(
        JObject source,
        string name,
        int maximum)
    {
        if (source[name] is not JArray values)
            throw new GameMcpInvalidParamsException(name + " must be an array");
        if (values.Count > maximum)
            throw new GameMcpInvalidParamsException(
                name + " accepts at most " + maximum + " rows");
        var result = new GameMcpUuidCount[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is not JObject row)
                throw new GameMcpInvalidParamsException(
                    name + "[" + index + "] must be an object");
            foreach (var property in row.Properties())
                if (property.Name is not "uuid" and not "count")
                    throw new GameMcpInvalidParamsException(
                        name + "[" + index + "]." + property.Name + " is unexpected");
            result[index] = new GameMcpUuidCount(
                RequireUuid(row, "uuid"),
                RequiredInt(row, "count", 1, int.MaxValue));
        }
        return result;
    }

    internal static string RequireRawString(JObject source, string name)
    {
        if (source[name]?.Type != JTokenType.String)
            throw new GameMcpInvalidParamsException(name + " must be a string");
        return (string?)source[name] ?? string.Empty;
    }

    internal static int OptionalInt(JObject source, string name, int fallback)
    {
        if (!source.TryGetValue(name, out var token)) return fallback;
        if (token.Type != JTokenType.Integer)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        try { return token.Value<int>(); }
        catch (Exception)
        {
            throw new GameMcpInvalidParamsException(name + " is outside the supported integer range");
        }
    }

    internal static int RequiredInt(
        JObject source,
        string name,
        int minimum,
        int maximum)
    {
        if (!source.TryGetValue(name, out var token) || token.Type != JTokenType.Integer)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        int value;
        try { value = token.Value<int>(); }
        catch (Exception)
        {
            throw new GameMcpInvalidParamsException(name + " is outside the supported integer range");
        }
        if (value < minimum || value > maximum)
        {
            // A floor this schema declares is a real bound. A ceiling of int.MaxValue is not one,
            // and printing it beside the floor published a number that is neither a native limit
            // nor a policy — 2147483647 where the game's own ceiling was 259. Where the schema
            // knows no ceiling it says so, and the game's refusal names the real one.
            throw new GameMcpInvalidParamsException(
                DeclaresCeiling(maximum)
                    ? name + " must be between " + minimum + " and " + maximum
                    : name + " must be " + minimum + " or greater; the game decides how high " +
                      "it may go and names that limit when it refuses");
        }
        return value;
    }

    private static bool DeclaresCeiling(int maximum) => maximum < int.MaxValue - 1;

    internal static int OptionalIntInRange(
        JObject source,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        if (!source.TryGetValue(name, out _)) return fallback;
        return RequiredInt(source, name, minimum, maximum);
    }

    internal static bool OptionalBool(JObject source, string name, bool fallback)
    {
        if (!source.TryGetValue(name, out var token)) return fallback;
        if (token.Type != JTokenType.Boolean)
            throw new GameMcpInvalidParamsException(name + " must be a boolean");
        return token.Value<bool>();
    }

    internal static bool RequireBool(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token))
            throw new GameMcpInvalidParamsException(name + " is required");
        if (token.Type != JTokenType.Boolean)
            throw new GameMcpInvalidParamsException(name + " must be a boolean");
        return token.Value<bool>();
    }

    internal static Guid RequireUuid(JObject source, string name)
    {
        var value = OptionalUuid(source, name);
        if (value == Guid.Empty)
            throw new GameMcpInvalidParamsException(
                name + " must be a whole canonical UUID or an id handle that names one published " +
                "entity");
        return value;
    }

    internal static Guid OptionalUuid(JObject source, string name)
    {
        if (!source.TryGetValue(name, out _)) return Guid.Empty;
        return ReadEntityId(RequireString(source, name), name);
    }

    /// <summary>
    /// One reader for every id argument: the full canonical UUID the suite calls identity, or the
    /// handle the wire hands out — any prefix that names exactly one published id.
    /// </summary>
    private static Guid ReadEntityId(string text, string name)
    {
        // The all-zero UUID is well formed and names nothing, and it used to collide with the
        // sentinel this reader uses for "the caller sent no id at all" — so a caller who sent a
        // perfectly shaped id was told their id was malformed and went off to debug a string that
        // was fine. What is wrong with it is that nothing carries it, which is a not-found.
        if (Guid.TryParseExact(text.Trim(), "D", out var canonical) &&
            canonical == Guid.Empty)
        {
            throw new GameMcpInvalidParamsException(
                "no entity in this build carries the id given for " + name +
                "; page world_categories for the category you meant, or check the id you copied",
                "unknown_uuid");
        }

        var outcome = GameMcpEntityHandle.Resolve(
            text, EntityIdentityCatalogPublication.Current, out var uuid, out var candidates);
        if (outcome == GameMcpEntityHandle.ResolutionOutcome.Ambiguous)
        {
            var catalog = EntityIdentityCatalogPublication.Current;
            var written = new System.Text.StringBuilder();
            for (var index = 0; index < candidates.Count; index++)
            {
                if (index > 0) written.Append(", ");
                written.Append(GameMcpEntityHandle.Name(candidates[index], catalog))
                    .Append(' ')
                    .Append(candidates[index].ToString("D"));
            }
            throw new GameMcpInvalidParamsException(
                "the id " + text + " given for " +
                name + " names more than one published entity — " + written +
                "; send more characters or the whole UUID",
                "ambiguous_handle");
        }

        // A handle stops resolving the moment a run ends, and answering "that is not an id" for one
        // this same run handed out taught a caller to throw away good ids after any teardown. The
        // whole UUID for the same entity already answered with the lifecycle fact; so does this.
        if (outcome == GameMcpEntityHandle.ResolutionOutcome.CatalogUnavailable)
        {
            throw new GameMcpInvalidParamsException(
                "No entity catalog is published, so no id handle resolves; " +
                "the whole UUID still reads, and handles resolve again once a save is loaded.",
                "entity_catalog_unavailable");
        }
        if (outcome == GameMcpEntityHandle.ResolutionOutcome.NotFound || uuid == Guid.Empty)
        {
            throw new GameMcpInvalidParamsException(
                name + " must be a whole canonical UUID or an id handle that names one published " +
                "entity",
                "invalid_uuid");
        }
        return uuid;
    }

    internal static string RequireOneOf(
        JObject source,
        string name,
        params string[] allowed)
    {
        var value = RequireString(source, name);
        for (var index = 0; index < allowed.Length; index++)
            if (string.Equals(value, allowed[index], StringComparison.Ordinal))
                return value;
        throw new GameMcpInvalidParamsException(
            name + " must be one of: " + string.Join(", ", allowed));
    }

    internal static string RequireSelector(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token))
            throw new GameMcpInvalidParamsException(name + " is required");
        if (token.Type == JTokenType.String)
        {
            var label = ((string?)token ?? string.Empty).Trim();
            if (label.Length == 0)
                throw new GameMcpInvalidParamsException(name + " must not be empty");
            return label;
        }
        throw new GameMcpInvalidParamsException(name + " must be an exact player-facing name");
    }
}
#endif
