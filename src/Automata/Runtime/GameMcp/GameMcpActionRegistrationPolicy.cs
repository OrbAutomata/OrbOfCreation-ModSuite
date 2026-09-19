namespace OrbAutomata;

internal static class GameMcpActionRegistrationPolicy
{
    internal static bool ShouldCompose(bool runtimeActivationAllowed) =>
        runtimeActivationAllowed;
}
