using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

/// <summary>
/// The five codes every lifecycle GameAction answers from the shared shell. The wire names are the
/// same in every feature; the numbers are each feature's own.
/// </summary>
internal readonly struct GameActionCodes
{
    internal GameActionCodes(
        ServiceActionResultCode wrongThread,
        ServiceActionResultCode contractUnavailable,
        ServiceActionResultCode mutationPermitUnavailable,
        ServiceActionResultCode postCommitFault,
        ServiceActionResultCode verificationFailed)
    {
        WrongThread = wrongThread;
        ContractUnavailable = contractUnavailable;
        MutationPermitUnavailable = mutationPermitUnavailable;
        PostCommitFault = postCommitFault;
        VerificationFailed = verificationFailed;
    }

    internal ServiceActionResultCode WrongThread { get; }
    internal ServiceActionResultCode ContractUnavailable { get; }
    internal ServiceActionResultCode MutationPermitUnavailable { get; }
    internal ServiceActionResultCode PostCommitFault { get; }
    internal ServiceActionResultCode VerificationFailed { get; }
}
