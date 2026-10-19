#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;

namespace OrbAutomata.GameMcp;

internal static class GameMcpConfigurationProjection
{
    /// <summary>
    /// The committed value of every writable setting, one setting per line, or of one section's.
    /// </summary>
    /// <remarks>
    /// What a setting means and which values it takes do not change between calls, so they are
    /// documentation, not an answer: the ordinary read is the values a caller came for, and
    /// <c>mode=describe</c> is where the type, the domain — a range or a list of names, said the
    /// same way for both — and the sentence live for whoever is deciding what to write.
    /// <para>
    /// The section is the grouping word every setting already wears in its own name, so
    /// <c>section=AutoBuy</c> is "everything Auto Buy has" and needs no second vocabulary. It
    /// narrows both modes, because the read whose answer is longest is the one worth narrowing. An
    /// answer that was not narrowed names the sections it holds, so the word is read off the
    /// surface rather than guessed at.
    /// </para>
    /// </remarks>
    internal static GameMcpValue ProjectGameMcpConfiguration(
        GameMcpFrameContext context,
        bool describe,
        string section = "")
    {
        if (!context.ConfigurationGeneration.IsValid)
        {
            return new GameMcpObjectBuilder
            {
                ["status"] = "not_available",
                ["code"] = "configuration_unpublished",
                ["reason"] = "no committed configuration has been published yet",
            }.Freeze();
        }
        var narrowed = section.Length > 0;
        if (narrowed &&
            !TryConfigurationSection(
                context.WritableConfiguration, section, out section, out var unknown))
        {
            return new GameMcpObjectBuilder
            {
                ["status"] = "not_available",
                ["code"] = "unknown_section",
                ["reason"] = unknown,
            }.Freeze();
        }
        var described = new GameMcpArrayBuilder();
        var values = new GameMcpObjectBuilder();
        for (var index = 0; index < context.WritableConfiguration.Length; index++)
        {
            var item = context.WritableConfiguration[index];
            if (narrowed && !string.Equals(item.Section, section, StringComparison.Ordinal))
                continue;
            var value = CanonicalConfigurationValue(
                GameMcpConfigurationSchema.SerializePublishedValue(
                    context.Configuration.Snapshot,
                    item.Section,
                    item.Key));
            if (!describe)
            {
                values[item.Section + "/" + item.Key] = value;
                continue;
            }
            var setting = new GameMcpObjectBuilder
            {
                ["setting"] = item.Section + "/" + item.Key,
                ["value"] = value,
                ["type"] = item.SettingType,
            };
            var domain = item.Constraint.Domain.Length > 0
                ? item.Constraint.Domain
                : PlainConfigurationDomain(item.Constraint.AcceptableValues);
            if (domain.Length > 0) setting["domain"] = domain;

            // The same two numbers a refused write hands back. A caller that has to parse a range
            // out of prose before it may write is a caller that will get the parse wrong once.
            GameMcpConfigurationValuePolicy.AddBound(setting, item.Constraint.Bound);
            setting["description"] = item.Description;
            described.Add(setting);
        }
        var result = new GameMcpObjectBuilder();
        if (!narrowed)
            result["sections"] = ConfigurationSectionNames(context.WritableConfiguration);
        if (describe)
        {
            if (described.Count > 0) result["settings"] = described;
            return result.Freeze();
        }
        result.CopyFrom(values);
        return result.Freeze();
    }

    /// <summary>
    /// The section a caller named, spelled the way every row spells it, or the sentence refusing it.
    /// </summary>
    /// <remarks>
    /// The refusal names the sections that exist rather than pointing at a call that would list
    /// them: there are few enough to fit on the line, and a caller who mistyped one should not have
    /// to spend a round trip learning what it should have been.
    /// </remarks>
    private static bool TryConfigurationSection(
        GameMcpWritableSettingDescriptor[] writable,
        string requested,
        out string canonical,
        out string reason)
    {
        for (var index = 0; index < writable.Length; index++)
        {
            if (!string.Equals(
                    writable[index].Section, requested, StringComparison.OrdinalIgnoreCase))
                continue;
            canonical = writable[index].Section;
            reason = string.Empty;
            return true;
        }
        canonical = string.Empty;
        reason = "unknown section '" + requested + "'; the sections are " +
            string.Join(", ", ConfigurationSectionNames(writable));
        return false;
    }

    /// <summary>Each section once, in the order the writable schema declares its settings.</summary>
    private static string[] ConfigurationSectionNames(
        GameMcpWritableSettingDescriptor[] writable)
    {
        var names = new List<string>();
        for (var index = 0; index < writable.Length; index++)
        {
            var section = writable[index].Section;
            if (!names.Contains(section)) names.Add(section);
        }
        return names.ToArray();
    }

    /// <summary>
    /// The published text of one setting, as the wire says it. How a value is spelled is decided
    /// where it becomes text, in <see cref="GameMcpConfigurationSchema.SerializePublishedValue"/>,
    /// so nothing here re-decides it by reading a type name back off the descriptor.
    /// </summary>
    private static object CanonicalConfigurationValue(string value)
    {
        // An allowlist stored as joined UUIDs printed 288 characters that resolved to nothing a
        // caller could use, on a surface that says every other id as a named handle. Written as the
        // list it is, each entry crosses the wire the way every other entity reference does.
        var entries = TryConfigurationIdentityList(value);
        if (entries is null) return value;
        var list = new GameMcpArrayBuilder();
        for (var index = 0; index < entries.Length; index++) list.Add(entries[index]);
        return list;
    }

    /// <summary>The whole UUIDs a joined setting holds, or nothing when it holds something else.</summary>
    private static string[]? TryConfigurationIdentityList(string value)
    {
        if (value.Length == 0) return null;
        var entries = value.Split(',');
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = entries[index].Trim();
            if (!Guid.TryParseExact(entries[index], "D", out _)) return null;
        }
        return entries;
    }

    private static string PlainConfigurationDomain(string value)
    {
        var result = (value ?? string.Empty).Trim();
        const string marker = "# Acceptable value range:";
        if (result.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            result = result.Substring(marker.Length).Trim();
        return result;
    }
}
#endif
