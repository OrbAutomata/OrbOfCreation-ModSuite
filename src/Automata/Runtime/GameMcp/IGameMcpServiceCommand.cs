#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata.GameMcp;

/// <summary>One feature's typed command hook, registered once when the dispatcher is composed.</summary>
internal interface IGameMcpServiceCommand
{
    GameMcpCommandKind Kind { get; }
    ServiceId Service { get; }
    GameMcpCommandResult Execute(
        GameMcpCommand command, GameMcpCommandScope scope, in ServiceActionContext context);
}
#endif
