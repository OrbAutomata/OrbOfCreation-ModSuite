using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class EquipmentLoadoutActionResultMapper
{
    internal static ServiceActionResult Map(in EquipmentLoadoutSubmission submission)
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

    internal static ServiceActionResultCode Code(EquipmentLoadoutPreflight preflight) => preflight switch
    {
        EquipmentLoadoutPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        EquipmentLoadoutPreflight.ContractUnavailable => EquipmentLoadoutActionResultCodes.ContractUnavailable,
        EquipmentLoadoutPreflight.WrongThread => EquipmentLoadoutActionResultCodes.WrongThread,
        EquipmentLoadoutPreflight.IdentityUnavailable => EquipmentLoadoutActionResultCodes.IdentityUnavailable,
        EquipmentLoadoutPreflight.NotCreated => EquipmentLoadoutActionResultCodes.NotCreated,
        EquipmentLoadoutPreflight.AlreadyInRequestedState => EquipmentLoadoutActionResultCodes.AlreadyInRequestedState,
        EquipmentLoadoutPreflight.LoadoutFull => EquipmentLoadoutActionResultCodes.LoadoutFull,
        EquipmentLoadoutPreflight.EquipmentTypeFull => EquipmentLoadoutActionResultCodes.EquipmentTypeFull,
        EquipmentLoadoutPreflight.UsageUnaffordable => EquipmentLoadoutActionResultCodes.UsageUnaffordable,
        EquipmentLoadoutPreflight.MultiBuyUnavailable => EquipmentLoadoutActionResultCodes.MultiBuyUnavailable,
        EquipmentLoadoutPreflight.MutationPermitUnavailable => EquipmentLoadoutActionResultCodes.MutationPermitUnavailable,
        EquipmentLoadoutPreflight.PostCommitFault => EquipmentLoadoutActionResultCodes.PostCommitFault,
        EquipmentLoadoutPreflight.VerificationFailed => EquipmentLoadoutActionResultCodes.VerificationFailed,
        EquipmentLoadoutPreflight.AmountUnavailable => EquipmentLoadoutActionResultCodes.AmountUnavailable,
        _ => EquipmentLoadoutActionResultCodes.ContractUnavailable,
    };
}
