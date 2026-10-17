namespace OrbAutomata;

/// <summary>
/// Callers waiting on a world newer than the one they acted on, so collection reads at the fastest
/// cadence whatever the player's pace.
/// </summary>
/// <remarks>
/// Main thread only: the waiters open and close on Unity, and collection asks during the pump.
/// An automation's own action is deliberately not a waiter. Its postcondition is proved at the action
/// boundary, and the world gate already holds it until the next reading, so pulling that reading
/// forward would run every busy save at the fastest pace and leave the setting doing nothing.
/// </remarks>
internal sealed class AutomataWorldDemand
{
    private int _waiting;

    internal bool IsRequested => _waiting > 0;

    internal void Begin() => _waiting++;

    internal void End()
    {
        if (_waiting == 0)
            throw new System.InvalidOperationException("No world waiter is open.");
        _waiting--;
    }
}
