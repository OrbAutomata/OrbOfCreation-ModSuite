namespace OrbModding.Common;

/// <summary>
/// The five answers every feature's GameAction gives when it stops short of a committed mutation,
/// written once so that twenty-one features cannot say them twenty-one ways.
/// </summary>
/// <remarks>
/// <para>
/// Each feature used to compose its own: thread ids, lifecycle epochs, "preflight", "observable",
/// "resolution", and the game's own .NET exception text all reached a caller as the explanation of
/// what had happened. None of it named anything in the game, and none of it named a next move —
/// while the refusals beside them, written by the same files, are player words. The split was never
/// deliberate; it is what happens when the same sentence is authored twenty-one times.
/// </para>
/// <para>
/// The screen each method takes is the game's own name for the page a caller should look at, as
/// <c>docs/game-systems/screens.md</c> spells it. It is the only per-feature part of these
/// sentences, which is why it is a parameter and the prose is not.
/// </para>
/// </remarks>
internal static class GameActionAnswer
{
    /// <summary>
    /// A stop before anything was sent, because the facts the press needs would not read. The
    /// game's own .NET exception text used to be the whole explanation here; it named a type and a
    /// field, and a caller could neither look it up nor act on it.
    /// </summary>
    internal static string CouldNotRead(string screen) =>
        "Nothing was sent to the game: the suite could not read what this press needs. Open " +
        screen + " and read it again.";

    /// <summary>
    /// The suite stopped its own call before the game was asked, and holds nothing further to say
    /// about it. Never blame the game here: the game was not consulted.
    /// </summary>
    internal static string SuiteStopped() =>
        "The suite stopped this before the game was asked, so nothing was applied. Nothing in the " +
        "game refused it.";

    /// <summary>
    /// The run was replaced between accepting the call and running it. Already the sentence
    /// <c>lifecycle_replaced</c> answers with at the wire's own boundary, said here in the same
    /// words so the two cannot drift.
    /// </summary>
    internal static string RunChanged() =>
        "The run changed while this was being sent — a save load, a reset, or a new game plus. " +
        "Nothing was applied; read the world again and retry.";

    /// <summary>
    /// The game handed back a different object for an id between the read and the press. The
    /// caller's move is the same every time: read it again.
    /// </summary>
    internal static string Replaced(string thing) =>
        "The game replaced this " + thing + " between reading it and acting on it; read it again " +
        "and retry.";

    /// <summary>
    /// The press was made and the change it asked for never appeared. It is the one outcome where
    /// the caller cannot be told whether it landed, so the sentence says exactly that rather than
    /// implying either half.
    /// </summary>
    internal static string ChangeNotSeen(string screen) =>
        "The game accepted the press but the change never showed up. Open " + screen + " and check " +
        "before retrying — it may or may not have landed.";

    /// <summary>
    /// The game's own code threw while running the press. The exception text used to ride on the
    /// wire; it named a .NET type and a field, which is not a fact about the game a caller can act
    /// on, and it sat where the answer to "did this land" belonged.
    /// </summary>
    internal static string GameErrored(string screen) =>
        "The game itself errored on this press, so nothing can be proven about whether it landed. " +
        "Check " + screen + " before retrying.";
}
