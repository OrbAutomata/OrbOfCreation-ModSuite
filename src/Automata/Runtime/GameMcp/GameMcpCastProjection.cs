#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpCastProjection
{
    /// <summary>
    /// Which sentence a refused cast carries. The world's wins wherever it has one.
    /// </summary>
    /// <remarks>
    /// The boundary knows only that the game said no. The published slot knows how many charges
    /// this spell is short and when the next one lands, or which other spell is holding the caster.
    /// The boundary's string used to be taken unconditionally, so it clobbered the sentence
    /// <see cref="ExactGameMcpReason"/> had already composed seventy-nine lines earlier: a live
    /// round read "the game refused the cast on its own readiness terms" twenty-five times, never
    /// once with a number, while the very next <c>world_get</c> on the same slot printed "0 of 1
    /// charges, next in 5.66s". Both producers run here, in this order, and the live press has no
    /// second copy of the rule to drift from.
    /// </remarks>
    internal static string? CastRefusalReason(
        GameMcpCommand command,
        GameWorldState world,
        in ServiceActionResult result,
        bool boundaryVerified,
        string? boundaryReason)
    {
        var worldReason = ExactGameMcpReason(command, world, in result);
        return !boundaryVerified &&
            string.IsNullOrEmpty(worldReason) &&
            !string.IsNullOrEmpty(boundaryReason)
            ? boundaryReason
            : worldReason;
    }

    internal static string? ExactGameMcpReason(
        GameMcpCommand command,
        GameWorldState world,
        in ServiceActionResult result)
    {
        // The cast boundary asks the game's own CanCast() and binds no charge reader, so its
        // refusal could only report that the game had said no. The published slot holds the two
        // facts that say why, so the sentence is written where they are.
        if (command.Kind == GameMcpCommandKind.Cast &&
            result.Code == AutoCastActionResultCodes.SpellNotReady)
        {
            var sentence = GameMcpWorldQuery.CastNotReadyReason(
                world, command.Amount - 1, command.TargetId);
            return sentence.Length == 0 ? null : sentence;
        }

        // SpellManager.CanCastASpell() is activeSpells.Any(s => s.IsReadyingCast()) — one spell
        // mid-cast refuses every cast in the game. The boundary can only report that gate by name;
        // the published loadout holds the spell that is holding it, which is the one thing a caller
        // batching three casts can act on.
        if (command.Kind == GameMcpCommandKind.Cast &&
            result.Code == AutoCastActionResultCodes.NativeCasterBusy)
        {
            var sentence = GameMcpWorldQuery.CasterBusyReason(world, command.TargetId);
            return sentence.Length == 0 ? null : sentence;
        }
        if (command.Kind != GameMcpCommandKind.Purchase ||
            !string.Equals(command.Mode, "upgrade", StringComparison.Ordinal) ||
            result.Disposition != ServiceActionDisposition.Skipped ||
            !WorldLookup.TryFind(world.Upgrades, command.TargetId, out var upgrade) ||
            upgrade.Reading.CachedCostLevel == upgrade.Reading.Level)
        {
            return null;
        }

        return "This upgrade's live price is still refreshing; open its game screen and retry.";
    }
}
#endif
