namespace OrbAutomata;

/// <summary>
/// Holds Auto Buy back for the rest of one lifecycle after the game refused a purchase that every
/// term the suite can read says it must take. It never touches the player's configuration: the next
/// lifecycle boundary clears it, and the first cycle after that probes the game again.
/// </summary>
/// <remarks>Read and written on the Unity main thread only, like every other Auto Buy action fact.</remarks>
internal sealed class AutoBuyInvariantLatch
{
    internal bool IsTripped => Summary.Length != 0;

    /// <summary>What tripped it, for the status line and suite health; empty while clear.</summary>
    internal string Summary { get; private set; } = string.Empty;

    /// <summary>Changes on every trip and clear, so a status reader notices both.</summary>
    internal int Revision { get; private set; }

    internal bool TryTrip(string summary)
    {
        if (IsTripped) return false;
        Summary = string.IsNullOrWhiteSpace(summary)
            ? throw new System.ArgumentException("A tripped invariant names what it saw.", nameof(summary))
            : summary;
        Revision++;
        return true;
    }

    internal void Clear()
    {
        if (!IsTripped) return;
        Summary = string.Empty;
        Revision++;
    }
}
