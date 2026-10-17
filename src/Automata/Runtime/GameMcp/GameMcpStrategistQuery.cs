#if SERVICE_CYCLE_PROFILE
namespace OrbAutomata.GameMcp;

internal static class GameMcpStrategistQuery
{
    internal static GameMcpToolExecution Read(GameMcpFrameContext state, string section)
    {
        if (!GameMcpWorldQuery.TryWorld(state, out _, out var unavailable))
            return GameMcpToolExecution.Read(unavailable.Freeze());
        var bulletin = state.Runtime!.Strategy.Bulletin;
        return GameMcpToolExecution.Text(section.Length == 0
            ? StrategistBulletinText.Summary(bulletin)
            : StrategistBulletinText.Section(bulletin, section));
    }
}
#endif
