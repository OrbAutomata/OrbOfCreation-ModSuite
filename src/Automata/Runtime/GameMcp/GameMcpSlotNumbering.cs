#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

/// <summary>
/// The one place the suite's zero-based arrays meet the player's one-based counting.
/// </summary>
/// <remarks>
/// Every native list is indexed from zero and every screen in the game counts from one, so the wire
/// counts from one everywhere: the first spell slot is slot 1, the first snapshot is snapshot 1,
/// and the first loadout is loadout 1. Internal arrays are untouched — the conversion happens only
/// where an argument arrives and where a response is written, which is exactly here.
/// </remarks>
internal static class GameMcpSlotNumbering
{
    /// <summary>The number a response says for a zero-based position.</summary>
    internal static int Wire(int index) => index + 1;

    /// <summary>The array position an argument's slot number addresses.</summary>
    internal static int Index(int slot) => slot - 1;
}
#endif
