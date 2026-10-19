using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Why an Auto Harvest action ended the way it did, in the detail the worker needs to remember it.
/// </summary>
/// <remarks>
/// A contract code names a mismatch the game proved, and the worker holds it for the rest of the
/// lifecycle, for one pair or for both. Every other fault backs its pair off and is retried. Codes
/// are append-only.
/// </remarks>
internal static class AutoHarvestActionResultCodes
{
    public static ServiceActionResultCode ActionFamilyUnavailable => new(1024, "action_family_unavailable");

    /// <summary>This pair's authored content could not be bound or audited.</summary>
    public static ServiceActionResultCode PairContractUnavailable => new(1025, "pair_contract_unavailable");

    /// <summary>Something both pairs share could not be bound or audited.</summary>
    public static ServiceActionResultCode FeatureContractUnavailable => new(1026, "feature_contract_unavailable");

    /// <summary>This pair's mutation was attempted and the game did not do what it was asked.</summary>
    public static ServiceActionResultCode PairFaulted => new(1027, "pair_faulted");

    /// <summary>The fresh native prerequisite check returned false without a quantity mutation.</summary>
    public static ServiceActionResultCode NativePrerequisitesCurrentlyUnmet => new(1028, "native_prerequisites_currently_unmet");

    /// <summary>The exact prerequisite validation evidence could not be read safely.</summary>
    public static ServiceActionResultCode NativePrerequisiteValidationUnavailable => new(1029, "native_prerequisite_validation_unavailable");

    public static ServiceActionResultCode NativePairIdentityRevalidationRefused => new(1030, "native_rejected");

    public static ServiceActionResultCode NativePlotVisibilityRefused => new(1031, "native_rejected");

    public static ServiceActionResultCode NativeOfferedInstanceMembershipRefused => new(1032, "native_rejected");

    public static ServiceActionResultCode NativeActionRowVisibilityRefused => new(1033, "native_rejected");

    public static ServiceActionResultCode NativeHasEnoughForOneInstanceRefused => new(1034, "native_rejected");

    public static ServiceActionResultCode NativeMaximumRemainingInstancesRefused => new(1035, "native_rejected");
}
