#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;


using static OrbAutomata.GameMcp.GameMcpPurchaseProjection;
using static OrbAutomata.GameMcp.GameMcpScreenState;

using static OrbAutomata.GameMcp.GameMcpRowKit;
using static OrbAutomata.GameMcp.GameMcpSpellWorkbenchProjection;

namespace OrbAutomata.GameMcp;

internal static class GameMcpUpgradeProjection
{

    /// <summary>
    /// An upgrade with no ceiling has no ceiling to report: the game marks that with a negative
    /// <c>maxLevel</c>, which becomes the word for having none rather than a plausible number.
    /// <c>world_get</c> shares the list's vocabulary, so it says the lifecycle in the same word the
    /// page does rather than in a second grammar of its own.
    /// </summary>
    internal static GameMcpValue ProjectUpgrade(GameWorldState world, in WorldUpgrade upgrade)
    {
        var result = new JObject
        {
            ["entityId"] = upgrade.EntityId.ToString("D"),
            ["category"] = "upgrades",
            ["screen"] = UpgradeScreen(world, upgrade.EntityId),
            ["state"] = UpgradeState(world, in upgrade),
            ["level"] = upgrade.Reading.Level,

            // Nothing developing is a fact about the upgrade, not a missing reading, so zero ships.
            ["queuedLevels"] = upgrade.Reading.QueuedLevels,
            ["maximum"] = UpgradeCeiling(in upgrade),
        };
        if (upgrade.IsDeveloping)
            result["developmentProgress"] = upgrade.DevelopmentProgress;
        return result.Freeze();
    }
    internal static GameMcpValue List(
        GameWorldState world,
        in WorldUpgrade upgrade,
        int nestedOffset,
        int nestedLimit)
    {
        var projected = new JObject
        {
            ["entityId"] = upgrade.EntityId.ToString("D"),
            ["level"] = upgrade.Reading.Level,
            ["queuedLevels"] = upgrade.Reading.QueuedLevels,
            ["screen"] = UpgradeScreen(world, upgrade.EntityId),
            ["state"] = UpgradeState(world, in upgrade),
            ["maximum"] = UpgradeCeiling(in upgrade),
            ["nextLevelRequirements"] = RequirementWord(
                WorldRequirementEvaluator.Evaluate(
                    world,
                    upgrade.EntityId,
                    WorldRequirementEvaluator.UpgradeCheckLevel(in upgrade))),
            ["affordable"] = UpgradeAffordability(world, in upgrade),
        };
        return projected.Freeze();
    }

}
#endif
