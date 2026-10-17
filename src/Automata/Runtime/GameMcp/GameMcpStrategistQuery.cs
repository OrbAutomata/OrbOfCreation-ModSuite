#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;

namespace OrbAutomata.GameMcp;

internal static class GameMcpStrategistQuery
{
    internal static GameMcpToolExecution Read(GameMcpFrameContext state, string section)
    {
        if (!GameMcpWorldQuery.TryWorld(state, out _, out var unavailable))
            return GameMcpToolExecution.Read(unavailable.Freeze());
        var runtime = state.Runtime!;
        var bulletin = runtime.StrategistBulletin;
        var fault = Fault(runtime.Services);
        if (fault is not null && bulletin is null)
            return GameMcpToolExecution.Text(fault + " — no bulletin yet");
        var text = section.Length == 0
            ? StrategistBulletinText.Summary(bulletin)
            : StrategistBulletinText.Section(bulletin, section);
        return GameMcpToolExecution.Text(fault is null
            ? text
            : fault + " — showing the last bulletin it published\n" + text);
    }

    private static string? Fault(AutomataServiceFrameFacts[] services)
    {
        var id = StrategistServicePolicies.ServiceId.Value;
        foreach (var service in services)
        {
            if (!string.Equals(service.ServiceId, id, StringComparison.Ordinal)) continue;
            var fault = service.Runner.Fault;
            return service.HasRunner && fault.IsValid
                ? "strategist faulted (" + fault.Category + ", " +
                    fault.OccurrenceCount.ToString(CultureInfo.InvariantCulture) + "× in a row)"
                : null;
        }
        throw new InvalidOperationException("The strategist is not registered.");
    }
}
#endif
