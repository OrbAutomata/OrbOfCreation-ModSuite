namespace OrbAutomata;

internal sealed record AutoConceptConfiguration
{
    internal AutoConceptOperationMode Mode { get; init; }
    internal AutoConceptSlotManagementMode SlotManagement { get; init; }
    internal int TrainingPeriodSeconds { get; init; }
    internal float RateReservePercent { get; init; }
    internal float MinimumDrainRatio { get; init; }
}
