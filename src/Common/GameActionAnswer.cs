using System;

namespace OrbModding.Common;

/// <summary>
/// The five answers every feature's GameAction gives when it stops short of a committed mutation,
/// written once so that twenty-one features cannot say them twenty-one ways.
/// </summary>
/// <remarks>
/// <para>
/// None of it named anything in the game, and none of it named a next move — while the refusals
/// beside them, written by the same files, are player words. The split was never deliberate; it is
/// what happens when the same sentence is authored twenty-one times.
/// </para>
/// <para>
/// The screen each method takes is the game's own name for the page a caller should look at, as
/// <c>docs/game-systems/screens.md</c> spells it. It is the only per-feature part of these
/// sentences, which is why it is a parameter and the prose is not.
/// </para>
/// <para>
/// An answer that logs a cause hands back its sentence and the cause's reference apart, so a
/// failure repeating under a fresh reference still reads as a repeat.
/// </para>
/// </remarks>
internal static class GameActionAnswer
{
    /// <summary>
    /// A stop before anything was sent, because the facts the press needs would not read. Hand the
    /// exception in and it goes to the suite log instead, under a reference this sentence then ends
    /// with.
    /// </summary>
    internal static GameActionReason CouldNotRead(string screen, Exception? exception = null) =>
        GameActionFaultLog.Reason(
            "Nothing was sent to the game: the suite could not read what this press needs. Open " +
            screen + " and read it again.",
            exception,
            screen);

    /// <summary>
    /// The suite never attached to the screen this verb presses, so it will keep refusing until the
    /// run restarts. Standing and run-scoped, which is the fact a caller needs: retrying is futile
    /// and no other press on that screen will work either.
    /// </summary>
    /// <remarks>
    /// The detail behind it — which member of which type would not bind — is a maintainer's fact,
    /// not a player's, so it goes to the suite log under the reference the answer ends with, the way
    /// <see cref="CouldNotRead"/> keeps an exception's text.
    /// </remarks>
    internal static GameActionReason NotAttached(string screen, string bindingFailure) =>
        new("The suite could not attach to " + screen + " in this run, so it will refuse every press " +
            "there until the run restarts.",
            GameActionFaultLog.RecordDetail(bindingFailure, screen));

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
    /// The suite pressed the game and then pressed it back, and the game reads as it was. Said after
    /// the refusal so a caller knows a press happened and left nothing behind.
    /// </summary>
    internal static string Undone() =>
        "The suite pressed it back, and the game shows it as it was.";

    /// <summary>
    /// The suite pressed the game, pressed it back, and the game does not read as it was.
    /// </summary>
    internal static string NotUndone(string screen) =>
        "The suite pressed it back, but the game does not show it as it was. Open " + screen +
        " and check before retrying.";

    /// <summary>
    /// The game's own code threw while running the press. Hand the exception in and it goes to the
    /// suite log instead, under a reference this sentence then ends with.
    /// </summary>
    internal static GameActionReason GameErrored(string screen, Exception? exception = null, Exception? putBack = null) =>
        new("The game itself errored on this press, so nothing can be proven about whether it landed. " +
            "Check " + screen + " before retrying.",
            GameActionFaultLog.RecordReference(exception, screen, putBack));
}
