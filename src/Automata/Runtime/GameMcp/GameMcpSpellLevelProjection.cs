#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellLevelProjection
{
    /// <summary>What a bought mastery level was charged, from the price its own press paid.</summary>
    internal static GameMcpValue? Project(in SpellLevelSubmission submission, GameWorldState world)
    {
        if (!submission.Outcome.Verified || submission.Charges.Length == 0) return null;
        var result = new GameMcpObjectBuilder();
        GameMcpChargeProjection.Add(result, world, submission.Charges);
        return result.Freeze();
    }
}
#endif
