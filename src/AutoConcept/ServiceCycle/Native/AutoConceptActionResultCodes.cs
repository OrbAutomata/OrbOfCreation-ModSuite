using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>Why an Auto Concept action ended the way it did. Codes are append-only.</summary>
internal static class AutoConceptActionResultCodes
{
    public static ServiceActionResultCode ActionFamilyUnavailable => new(3328, "action_family_unavailable");
    public static ServiceActionResultCode RecipeIdentityChanged => new(3329, "recipe_identity_changed");
    public static ServiceActionResultCode AssignmentUnsettled => new(3330, "assignment_unsettled");
    public static ServiceActionResultCode OwnershipChanged => new(3331, "ownership_changed");
    public static ServiceActionResultCode SlotUnavailable => new(3332, "slot_unavailable");
    public static ServiceActionResultCode ProjectionRefused => new(3333, "projection_refused");
    public static ServiceActionResultCode MasteryLimitChanged => new(3334, "mastery_limit_changed");
    public static ServiceActionResultCode AmountUnavailable => new(3335, "amount_unavailable");
    public static ServiceActionResultCode ConceptNotDiscovered => new(3336, "not_discovered");
}
