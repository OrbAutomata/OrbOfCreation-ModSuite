#if SERVICE_CYCLE_PROFILE
using System;
using BepInEx.Configuration;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The one table that says where a setting lives on the wire and where it lives in its mod's own
/// config file, for the settings where those two are not the same address.
/// </summary>
/// <remarks>
/// <para>
/// Every other setting is addressed identically on both sides, and the wire address was simply the
/// BepInEx <c>ConfigDefinition</c>. One is not: <c>MentorConfig</c> binds Orb Mentor's breaker as
/// <c>[General] Mode</c> in the suite's one config file, so the raw definition put it in a
/// <c>General</c> section beside OrbAutomata's own <c>General/Enabled</c> — two features' settings
/// under one word, which no reader could attribute. Every other section on this surface names the
/// feature it configures; this makes that setting do the same.
/// </para>
/// <para>
/// The file does not move. Renaming the TOML section would relocate a player's existing
/// <c>[General] Mode</c> line and silently reset the breaker to its default, and
/// <c>MentorConfig</c>'s own dependency graph — what the in-game settings page draws from — is
/// bound to that spelling. The in-game page has called the entry Mentor all along
/// (<c>displaySection: "Mentor"</c>); it is the MCP surface that was publishing the raw
/// definition.
/// </para>
/// <para>
/// This is one table rather than a scatter of string tests on purpose. The schema that is
/// published, the write that resolves an address, every refusal sentence that names a setting, and
/// the name the wire no longer answers to all ask here, so the wire cannot end up spelling one
/// setting two ways.
/// </para>
/// </remarks>
internal static class GameMcpConfigurationAddress
{
    private static readonly (string FileSection, string FileKey, string Section, string Key)[] Moved =
    {
        ("General", "Mode", "Mentor", "Mode"),
    };

    /// <summary>The address the wire uses for the setting the config file holds at this address.</summary>
    internal static void OnTheWire(
        string fileSection,
        string fileKey,
        out string section,
        out string key)
    {
        for (var index = 0; index < Moved.Length; index++)
        {
            if (!string.Equals(Moved[index].FileSection, fileSection, StringComparison.Ordinal) ||
                !string.Equals(Moved[index].FileKey, fileKey, StringComparison.Ordinal))
                continue;
            section = Moved[index].Section;
            key = Moved[index].Key;
            return;
        }
        section = fileSection;
        key = fileKey;
    }

    /// <summary>The wire's own spelling of one entry, for a sentence that names the setting.</summary>
    internal static string OnTheWire(ConfigDefinition definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        OnTheWire(definition.Section, definition.Key, out var section, out var key);
        return section + "/" + key;
    }

    /// <summary>
    /// Whether this is an address the wire used to answer to, and the sentence that says so.
    /// </summary>
    /// <remarks>
    /// A moved setting's file address is exactly the name the wire published before it moved, so a
    /// caller holding the old spelling is told where the setting went rather than being told the
    /// suite has no such setting — which is true and useless.
    /// </remarks>
    internal static bool IsRetired(string section, string key, out string reason)
    {
        for (var index = 0; index < Moved.Length; index++)
        {
            if (!string.Equals(Moved[index].FileSection, section, StringComparison.Ordinal) ||
                !string.Equals(Moved[index].FileKey, key, StringComparison.Ordinal))
                continue;
            reason =
                "setting " + section + "/" + key + " is now addressed as " +
                Moved[index].Section + "/" + Moved[index].Key +
                ", under the mod it belongs to; the setting itself is unchanged";
            return true;
        }
        reason = string.Empty;
        return false;
    }
}
#endif
