namespace OrbAutomata;

internal sealed record AutoBuyConfiguration
{
    internal AutoBuyOperationMode Mode { get; init; }
    internal AutoBuyAffordabilityMode StructureAffordability { get; init; }
    internal AutoBuyAffordabilityMode UpgradeAffordability { get; init; }
    internal bool IncludeStructures { get; init; }
    internal bool IncludeUpgrades { get; init; }
    internal bool AutoLevelSpells { get; init; }
    internal bool FollowStrategistGoal { get; init; }
    internal int LeaveQueueSlots { get; init; }
}
