using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Why an Auto Concept action ended the way it did. Codes are append-only.</summary>
internal static class AutoConceptActionResultCodes
{
    public static ServiceActionResultCode ActionFamilyUnavailable => new(3328);
    public static ServiceActionResultCode RecipeIdentityChanged => new(3329);
    public static ServiceActionResultCode AssignmentUnsettled => new(3330);
    public static ServiceActionResultCode OwnershipChanged => new(3331);
    public static ServiceActionResultCode SlotUnavailable => new(3332);
    public static ServiceActionResultCode ProjectionRefused => new(3333);
    public static ServiceActionResultCode MasteryLimitChanged => new(3334);
    public static ServiceActionResultCode AmountUnavailable => new(3335);
}
