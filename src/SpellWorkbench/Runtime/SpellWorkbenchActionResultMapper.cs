using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class SpellWorkbenchActionResultMapper
{
    internal static ServiceActionResult Map(in SpellWorkbenchSubmission submission)
    {
        if (submission.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, submission.CallOutcome));
        var code = Code(submission.Preflight);
        return submission.CallOutcome.MutationAttempts == 0
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(code,
                ServiceNativeMutationEvidence.Observed(submission.Outcome, submission.CallOutcome));
    }

    internal static ServiceActionResultCode Code(SpellWorkbenchPreflight preflight) => preflight switch
    {
        SpellWorkbenchPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        SpellWorkbenchPreflight.ContractUnavailable => SpellWorkbenchActionResultCodes.ContractUnavailable,
        SpellWorkbenchPreflight.WrongThread => SpellWorkbenchActionResultCodes.WrongThread,
        SpellWorkbenchPreflight.IdentityUnavailable => SpellWorkbenchActionResultCodes.IdentityUnavailable,
        SpellWorkbenchPreflight.SelectionUnavailable => SpellWorkbenchActionResultCodes.SelectionUnavailable,
        SpellWorkbenchPreflight.WrongSelection => SpellWorkbenchActionResultCodes.WrongSelection,
        SpellWorkbenchPreflight.AlreadyDiscovered => SpellWorkbenchActionResultCodes.AlreadyDiscovered,
        SpellWorkbenchPreflight.DiscoveryUnavailable => SpellWorkbenchActionResultCodes.DiscoveryUnavailable,
        SpellWorkbenchPreflight.RecipeUnavailable => SpellWorkbenchActionResultCodes.RecipeUnavailable,
        SpellWorkbenchPreflight.Unaffordable => SpellWorkbenchActionResultCodes.Unaffordable,
        SpellWorkbenchPreflight.LoadoutFull => SpellWorkbenchActionResultCodes.LoadoutFull,
        SpellWorkbenchPreflight.CompositionUnsupported => SpellWorkbenchActionResultCodes.CompositionUnsupported,
        SpellWorkbenchPreflight.MutationPermitUnavailable => SpellWorkbenchActionResultCodes.MutationPermitUnavailable,
        SpellWorkbenchPreflight.PostCommitFault => SpellWorkbenchActionResultCodes.PostCommitFault,
        SpellWorkbenchPreflight.VerificationFailed => SpellWorkbenchActionResultCodes.VerificationFailed,
        SpellWorkbenchPreflight.UsageRequirementsUnavailable => SpellWorkbenchActionResultCodes.UsageRequirementsUnavailable,
        SpellWorkbenchPreflight.UsageUnaffordable => SpellWorkbenchActionResultCodes.UsageUnaffordable,
        SpellWorkbenchPreflight.UniqueSpellConflict => SpellWorkbenchActionResultCodes.UniqueSpellConflict,
        SpellWorkbenchPreflight.GlyphRequirementsUnavailable => SpellWorkbenchActionResultCodes.GlyphRequirementsUnavailable,
        SpellWorkbenchPreflight.StagedWriteFailed => SpellWorkbenchActionResultCodes.StagedWriteFailed,
        SpellWorkbenchPreflight.AugmentSlotsExceeded => SpellWorkbenchActionResultCodes.AugmentSlotsExceeded,
        SpellWorkbenchPreflight.ScreenLocked => SpellWorkbenchActionResultCodes.ScreenLocked,
        _ => SpellWorkbenchActionResultCodes.ContractUnavailable,
    };
}
