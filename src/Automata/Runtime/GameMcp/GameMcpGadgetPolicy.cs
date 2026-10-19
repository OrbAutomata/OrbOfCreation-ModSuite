#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbAutomata.GameMcp;

internal enum GameMcpTile
{
    None = 0,
    Plot = 1,
    Spell = 2,
}

/// <summary>Closed-world names for native probes whose implementations are fixed in the mod.</summary>
internal static class GameMcpGadgetPolicy
{
    /// <summary>
    /// The width every capture arrives at. A screenshot costs its reader whole 28-pixel patches, so
    /// pixels are the only lever that matters: 900 is the narrowest width at which every class of
    /// on-screen text stays readable through the suite's own resampler, and 896 is that width
    /// snapped down onto the patch grid at 32 patches across. It reads identically to 900 and costs
    /// one patch column less, which is why no caller is asked to pick a number instead.
    /// </summary>
    internal const int CaptureWidth = 896;

    internal static bool IsAllowlistedProbe(string probe) =>
        probe is "runtime" or "action_queue_room" or "navigation";

    internal static bool IsCurrentContentSubtabPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("Canvas[0]/ContentArea[", StringComparison.Ordinal);

    internal static GameMcpTile TileAt(string screen, string? subtab) =>
        string.Equals(screen, "World", StringComparison.Ordinal) &&
        string.Equals(subtab, "Agromancy", StringComparison.Ordinal)
            ? GameMcpTile.Plot
            : string.Equals(screen, "Magic", StringComparison.Ordinal) &&
              string.Equals(subtab, "Spells", StringComparison.Ordinal)
                ? GameMcpTile.Spell
                : GameMcpTile.None;

    /// <summary>Where the selectable tiles live, for the refusal that has to say so.</summary>
    internal const string TileDestinations =
        "Agromancy plots on World/Agromancy, spells on Magic/Spellbook/Spells";
}
#endif
