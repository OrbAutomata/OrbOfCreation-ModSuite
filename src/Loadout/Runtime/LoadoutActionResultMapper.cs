using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class LoadoutActionResultMapper
{
    internal static ServiceActionResult Map(in LoadoutSubmission submission)
    {
        if (submission.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified, submission.CallOutcome));
        var code = Code(submission.Preflight);
        return submission.CallOutcome.MutationAttempts == 0
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(code,
                ServiceNativeMutationEvidence.Observed(
                    submission.Outcome, submission.CallOutcome));
    }

    internal static ServiceActionResultCode Code(LoadoutPreflight preflight) =>
        preflight switch
        {
            LoadoutPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
            LoadoutPreflight.ContractUnavailable => LoadoutActionResultCodes.ContractUnavailable,
            LoadoutPreflight.WrongThread => LoadoutActionResultCodes.WrongThread,
            LoadoutPreflight.IdentityUnavailable => LoadoutActionResultCodes.IdentityUnavailable,
            LoadoutPreflight.WrongTargetType => LoadoutActionResultCodes.WrongTargetType,
            LoadoutPreflight.AlreadyInRequestedState => LoadoutActionResultCodes.AlreadyInRequestedState,
            LoadoutPreflight.SwitchBlocked => LoadoutActionResultCodes.SwitchBlocked,
            LoadoutPreflight.EntryUnavailable => LoadoutActionResultCodes.EntryUnavailable,
            LoadoutPreflight.SlotOutOfRange => LoadoutActionResultCodes.SlotOutOfRange,
            LoadoutPreflight.SlotEmpty => LoadoutActionResultCodes.SlotEmpty,
            LoadoutPreflight.SlotOccupied => LoadoutActionResultCodes.SlotOccupied,
            LoadoutPreflight.ActiveSectionEmpty => LoadoutActionResultCodes.ActiveSectionEmpty,
            LoadoutPreflight.NameOutOfRange => LoadoutActionResultCodes.NameOutOfRange,
            LoadoutPreflight.MutationPermitUnavailable => LoadoutActionResultCodes.MutationPermitUnavailable,
            LoadoutPreflight.PostCommitFault => LoadoutActionResultCodes.PostCommitFault,
            LoadoutPreflight.VerificationFailed => LoadoutActionResultCodes.VerificationFailed,
            _ => LoadoutActionResultCodes.ContractUnavailable,
        };
}
