namespace OrbModding.Common;

/// <summary>
/// The one code and sentence for "there is no live run behind this read", wherever a surface has to
/// say it.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of reader ask. The published-world query answers from a captured world and knows the
/// lifecycle from the frame it was captured in; the game-math check reads the live game directly and
/// knows it from the monitor. Both are answering the same question about the same run, so both say
/// the same sentence — a caller comparing two tools sees one fact rather than two beliefs.
/// </para>
/// <para>
/// <see cref="GameLifecycleState.Playing"/> is not here on purpose. A playing run that still has
/// nothing to read is a publication fact, not a lifecycle one, and the surface that knows why owes
/// its own sentence rather than borrowing a lifecycle word that would be false.
/// </para>
/// </remarks>
internal static class GameLifecycleUnavailability
{
    /// <summary>
    /// Whether this lifecycle state is itself the answer, and what it says when it is.
    /// </summary>
    internal static bool TryDescribe(
        GameLifecycleState state,
        out string code,
        out string reason)
    {
        switch (state)
        {
            case GameLifecycleState.NoGame:
                code = "lifecycle_no_game";
                reason = "no save is loaded, so there is no world to read.";
                return true;
            case GameLifecycleState.Initializing:
                code = "lifecycle_initializing";
                reason = "the save is still loading, so no world has been collected yet.";
                return true;
            case GameLifecycleState.Resetting:
                code = "lifecycle_resetting";
                reason =
                    "a save load, reset, or new game plus is replacing the run, so the previous " +
                    "world was dropped and the next one has not been collected yet.";
                return true;
            case GameLifecycleState.SceneExit:
                code = "lifecycle_scene_exit";
                reason = "the play scene is unloading, so the world it was read from is gone.";
                return true;
            default:
                code = string.Empty;
                reason = string.Empty;
                return false;
        }
    }
}
