#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbAutomata.GameMcp;

internal static class GameMcpScreenCatalogProjection
{
    /// <summary>
    /// The live screen rail, each screen saying whether the game has unlocked it.
    /// </summary>
    /// <remarks>
    /// A rail button exists for every screen from the first frame, so a catalog that reported only
    /// label and active told a young run it had eight screens while the game drew three — and
    /// <c>game_navigate</c> then offered the five it does not draw as candidates. <c>locked</c> is
    /// <c>ViewSO.IsAvailable()</c> negated: the same member the world publication reads for every
    /// view's <c>available</c>, so the catalog and the discovery previews cannot disagree.
    /// </remarks>
    internal static GameMcpValue ProjectGameMcpScreenCatalog(
        string scene,
        bool navigationAvailable,
        IReadOnlyList<(string Label, bool Active, bool Locked)> tabs,
        IReadOnlyList<(string Strip, string Label, bool Active)> subtabs)
    {
        var result = new GameMcpObjectBuilder
        {
            ["status"] = navigationAvailable ? "available" : "unavailable",
            ["scene"] = scene,
            ["navigationAvailable"] = navigationAvailable,
        };
        if (!navigationAvailable)
        {
            result["reasonCode"] = "navigation_unavailable";
            result["reason"] = "the game is not showing its screen tabs right now — this answers " +
                "only while a save is open";
            result["screens"] = new GameMcpArrayBuilder();
            return result.Freeze();
        }
        var projectedTabs = new GameMcpArrayBuilder();
        for (var index = 0; index < tabs.Count; index++)
        {
            var tab = tabs[index];
            var projectedTab = new GameMcpObjectBuilder
            {
                ["label"] = tab.Label,
                ["active"] = tab.Active,
                ["locked"] = tab.Locked,
            };
            if (!tab.Active || subtabs.Count == 0)
            {
                projectedTabs.Add(projectedTab);
                continue;
            }
            projectedTab["subtabStrips"] = ProjectGameMcpSubtabStrips(subtabs);
            projectedTabs.Add(projectedTab);
        }
        result["screens"] = projectedTabs;
        return result.Freeze();
    }

    internal static GameMcpValue ProjectGameMcpSubtabStrips(
        IEnumerable<(string Strip, string Label, bool Active)> subtabs)
    {
        var projectedStrips = new GameMcpArrayBuilder();
        var strips = subtabs.GroupBy(subtab => subtab.Strip, StringComparer.Ordinal);
        foreach (var strip in strips)
        {
            var labels = new GameMcpArrayBuilder();
            var projectedStrip = new GameMcpObjectBuilder();
            foreach (var subtab in strip)
            {
                labels.Add(subtab.Label);
                if (subtab.Active) projectedStrip["active"] = subtab.Label;
            }
            projectedStrip["labels"] = labels;
            projectedStrips.Add(projectedStrip);
        }
        return projectedStrips.Freeze();
    }
}
#endif
