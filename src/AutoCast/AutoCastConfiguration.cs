namespace OrbAutomata;

internal sealed record AutoCastConfiguration
{
    internal AutoCastOperationMode Mode { get; init; }
    internal string ToggleShortcut { get; init; } = string.Empty;
    internal float StartResourcePercent { get; init; }
    internal float ManualPauseSeconds { get; init; }
    internal bool FullCharge { get; init; }
    internal bool RitualAware { get; init; }
}
