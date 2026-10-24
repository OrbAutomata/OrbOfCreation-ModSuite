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


using static OrbAutomata.GameMcp.GameMcpRowKit;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSpellCompositionProjection
{
    internal static GameMcpValue Project(in SpellCompositionSubmission submission, string dial)
    {
        if (submission.Outcome.Verified) return new JObject().Freeze();
        var result = new JObject();

        // The commit names which dial moved; so does the refusal. Two dials share one tool, and a
        // refusal that named neither left the caller to remember what it had asked for.
        if (dial.Length > 0) result["dial"] = dial;
        if (submission.Outcome.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "requested dial value";

        // A refusal whose sentence names a range carries that same range as numbers, read from the
        // native capture the sentence was written from.
        if (submission.Minimum >= 0 && submission.Maximum >= 0)
        {
            result["minimum"] = submission.Minimum;
            result["maximum"] = submission.Maximum;
        }
        return result.Freeze();
    }

    internal static GameMcpValue ProjectCastingDialDelta(
        GameMcpFrameContext state,
        GameMcpCommand command)
    {
        if (state.World is null)
            return PostStateUnavailable("world_not_published", state.RuntimeNotAvailableReason);
        var after = DialLevel(state.World.Snapshot.SpellWorkbench, command.Mode);
        var before = Before(command)?.SpellWorkbench is { } earlier
            ? DialLevel(earlier, command.Mode).Current
            : (int?)null;
        return new JObject
        {
            ["dial"] = command.PayloadKey,
            ["before"] = before,
            ["after"] = after.Current,
            // The floor of every dial is the constant 1 and lives in the tool documentation. The
            // ceiling is the one end of the range that moves, so it is the one end worth sending.
            ["maximum"] = after.Maximum,
        }.Freeze();
    }

    internal static (int Current, int Maximum) DialLevel(
        in WorldSpellWorkbench workbench,
        string mode) => mode switch
        {
            "set_output_level" => (workbench.OutputLevel, workbench.MaximumOutputLevel),
            "set_reserve_level" => (workbench.ReserveLevel, workbench.MaximumReserveLevel),
            "set_druidry_level" => (workbench.DruidryLevel, workbench.MaximumDruidryLevel),
            _ => (workbench.AlchemyLevel, workbench.MaximumAlchemyLevel),
        };

}
#endif
