#if SERVICE_CYCLE_PROFILE
using System.Text;

namespace OrbAutomata.GameMcp;

/// <summary>One player-facing text projection for every MCP response surface.</summary>
internal static class GameMcpTextFormatter
{
    internal static string Plain(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('<') < 0) return value ?? string.Empty;

        StringBuilder? result = null;
        var copiedThrough = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '<') continue;
            var end = value.IndexOf('>', index + 1);
            if (end < 0) break;
            if (!IsUnityMarkup(value.Substring(index + 1, end - index - 1))) continue;
            result ??= new StringBuilder(value.Length);
            result.Append(value, copiedThrough, index - copiedThrough);
            copiedThrough = end + 1;
            index = end;
        }
        if (result is null) return value;
        if (copiedThrough < value.Length)
            result.Append(value, copiedThrough, value.Length - copiedThrough);
        return result.ToString();
    }

    /// <summary>
    /// Whether a bracketed word is layout rather than something the player is meant to read.
    /// </summary>
    /// <remarks>
    /// The list is closed on purpose: prose that happens to hold an angle bracket has to survive,
    /// so a tag is stripped because it is known to be one. Which means the list has to hold every
    /// word the pinned build actually authors, and it did not — a census of every lowercase tag in
    /// <c>data/game-data.json</c> found eight, of which <c>lore</c> (332 uses), <c>emph2</c> (168),
    /// <c>negative</c> (4) and <c>positive</c> (2) were absent, so 506 authored spans shipped their
    /// markup to a caller reading what a tool advertises as plain screen text. The other four —
    /// <c>emph</c>, <c>deemph</c>, <c>warn</c>, <c>color</c> — were already here, as are the Unity
    /// built-ins the game does not currently use.
    /// </remarks>
    private static bool IsUnityMarkup(string tag)
    {
        var normalized = tag.Trim().TrimStart('/');
        if (normalized.Length == 0) return false;
        if (normalized[0] == '#') return true;
        var separator = normalized.IndexOfAny(new[] { '=', ' ' });
        var name = separator < 0 ? normalized : normalized.Substring(0, separator);
        return name is "color" or "emph" or "emph2" or "deemph" or "warn" or
            "lore" or "negative" or "positive" or
            "b" or "i" or "u" or "s" or "size" or "alpha" or "align" or
            "font" or "line-height" or "link" or "mark" or "material" or
            "nobr" or "space" or "sprite" or "style" or "voffset" or "width" or "br";
    }
}
#endif
