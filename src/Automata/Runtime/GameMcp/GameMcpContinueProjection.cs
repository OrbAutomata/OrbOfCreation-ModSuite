#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbAutomata.GameMcp;

/// <summary>
/// What a Continue press settled at: where the game is now, and — where that is still the scene the
/// call was made from — that the press itself landed.
/// </summary>
/// <remarks>
/// A load slower than the verb's wait answers with the scene the call was made from, which reads
/// byte-identically to "nothing happened". A round read exactly that, spent a health call chasing
/// it, and filed a wrong finding that a screenshot later disproved: the press had landed and the
/// screen was already black behind a loading spinner. Where the scene changed, the new scene is
/// the proof and no clause is needed; where it has not, the press is still a fact this verb owns,
/// because it invoked <c>SaveStateManager.StartGame</c> and that call returned.
/// </remarks>
internal static class GameMcpContinueProjection
{
    internal const string StartScene = "Start";

    /// <remarks>
    /// The press that landed carried a sentence about the suite not having read the game yet, which
    /// is true of the instant a Continue returns in and false of the instant a caller reads it —
    /// the load it just started is what makes the first read possible. <c>runtimeAvailable</c> is
    /// the fact; there is nothing to add to it beside a success.
    /// </remarks>
    internal static GameMcpValue Project(string sceneName, bool runtimeAvailable)
    {
        var details = new GameMcpObjectBuilder();
        if (string.Equals(sceneName, StartScene, StringComparison.Ordinal))
            details["pressed"] = "Continue landed; the scene has not changed yet";
        details["scene"] = sceneName;
        details["runtimeAvailable"] = runtimeAvailable;
        return details.Freeze();
    }
}
#endif
