#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpChargeProjection
{
    /// <summary>
    /// What one press was charged, read off the price the game named at that press.
    /// </summary>
    /// <remarks>
    /// A charge is never a difference of two balances. A Spark channel paying into the same window
    /// answered a 9 Spark mastery level as <c>before=101 after=125</c>, and a purchase landing
    /// between two readings made a 100 Spark level read as 499. The price is converted once into
    /// the figure the screen draws it in, against the world the press acted on.
    /// </remarks>
    internal static void Add(
        GameMcpObjectBuilder result,
        GameWorldState world,
        ReadOnlySpan<ResourceCharge> charges)
    {
        if (ResourceCharges.Nothing(charges))
        {
            result["free"] = true;
            return;
        }
        var charged = new GameMcpArrayBuilder();
        for (var index = 0; index < charges.Length; index++)
        {
            var charge = charges[index];
            charged.Add(new GameMcpObjectBuilder
            {
                ["resourceId"] = charge.ResourceId.ToString("D"),
                ["cost"] = new GameMcpDomainValue(
                    GameMcpWorldQuery.PlayerFacingCost(world, charge.ResourceId, charge.Amount)),
            });
        }
        result["charged"] = charged;
    }
}
#endif
